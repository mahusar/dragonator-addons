#!/usr/bin/env python3
"""A Dragonator bot.

It dials in to a server, proves who it is, plays its turns and signs the match
receipt with its own key. Run it and it stays connected, taking one match after
another.

    pip install cryptography
    python3 dragon_bot.py --host 127.0.0.1 --port 6000

To reach a server on an onion address, send it through Tor:

    python3 dragon_bot.py --host <server>.onion --port 6000 --tor
"""

import argparse
import json
import os
import socket
import sys

try:
    from cryptography.hazmat.primitives import serialization
    from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PrivateKey
except ImportError:
    sys.exit("This bot needs the cryptography package. Install it with:  pip install cryptography")

PROTOCOL = 1
AUTH_TAG = "dragonator-bot-auth-1"


def load_key(path):
    if os.path.exists(path):
        with open(path, "rb") as handle:
            seed = handle.read()

        if len(seed) != 32:
            sys.exit("%s is %d bytes, not 32. Move it aside and a new key will be made." % (path, len(seed)))

        return Ed25519PrivateKey.from_private_bytes(seed)

    key = Ed25519PrivateKey.generate()
    seed = key.private_bytes(serialization.Encoding.Raw,
                             serialization.PrivateFormat.Raw,
                             serialization.NoEncryption())

    with open(path, "wb") as handle:
        handle.write(seed)

    try:
        os.chmod(path, 0o600)
    except OSError:
        pass

    print("made a new key at %s - keep it, it is who your bot is" % path)
    return key


def public_hex(key):
    raw = key.public_key().public_bytes(serialization.Encoding.Raw, serialization.PublicFormat.Raw)
    return raw.hex()


def recvn(sock, count):
    data = b""

    while len(data) < count:
        chunk = sock.recv(count - len(data))
        if not chunk:
            raise IOError("the connection closed during the proxy handshake")
        data += chunk

    return data


def socks5_connect(sock, host, port):
    sock.sendall(b"\x05\x01\x00")

    if recvn(sock, 2) != b"\x05\x00":
        raise IOError("the Tor proxy refused the handshake")

    try:
        target = host.encode("ascii")
    except UnicodeEncodeError:
        raise IOError("the host must be an ascii name or an onion address")

    if len(target) > 255:
        raise IOError("that host name is too long for SOCKS5")

    sock.sendall(b"\x05\x01\x00\x03" + bytes([len(target)]) + target + port.to_bytes(2, "big"))

    reply = recvn(sock, 4)

    if reply[1] != 0:
        raise IOError("the Tor proxy could not reach %s:%d (code %d)" % (host, port, reply[1]))

    kind = reply[3]

    if kind == 1:
        recvn(sock, 4)
    elif kind == 3:
        recvn(sock, recvn(sock, 1)[0])
    elif kind == 4:
        recvn(sock, 16)

    recvn(sock, 2)


def send(stream, line):
    stream.write(line + "\n")
    stream.flush()


def read(stream):
    line = stream.readline()

    if not line:
        raise IOError("the server closed the connection")

    return line.strip()


def decide(board):
    if not board.get("yourTurn"):
        return "end"

    swing = finisher(board)
    if swing:
        return swing

    spell = choose_spell(board)
    if spell:
        return spell

    play = choose_creature(board)
    if play:
        return play

    attack = choose_attack(board)
    if attack:
        return attack

    return "end"


def choose_spell(board):
    mana = board.get("you", {}).get("mana", 0)

    best = None
    best_score = 0

    for card in board.get("hand", []):
        if card.get("kind") != "spell":
            continue

        cost = card.get("cost", 0)
        if cost > mana:
            continue

        if card.get("destroys"):
            move, score = removal(board, card, cost)
        elif card.get("healthChange", 0) < 0:
            move, score = damage(board, card, cost)
        elif card.get("strengthChange", 0) > 0:
            move, score = buff(board, card, cost)
        elif card.get("healthChange", 0) > 0:
            move, score = repair(board, card, cost)
        elif card.get("cardDraw", 0) > 0:
            move, score = refill(board, card, cost, mana)
        else:
            move, score = None, 0

        if move is None or score <= best_score:
            continue

        best, best_score = move, score

    return best


def removal(board, card, cost):
    best, score = None, 0

    for enemy in board.get("enemyField", []):
        if not enemy.get("targetable") or enemy.get("shield"):
            continue
        if walled(board) and not enemy.get("taunt"):
            continue
        if not reaches(card, enemy):
            continue

        weight = body(enemy)
        if weight < 12:
            continue

        worth_it = 40 + weight - cost * 2
        if worth_it <= score:
            continue

        score, best = worth_it, aimed(card, enemy)

    return best, score


def damage(board, card, cost):
    hit = -card.get("healthChange", 0)
    if hit <= 0:
        return None, 0

    if not card.get("targeted"):
        affects = card.get("affects")
        if affects not in ("enemies", "random"):
            return None, 0

        touched = 0
        kills = 0

        for enemy in board.get("enemyField", []):
            if enemy.get("health", 0) <= 0 or not reaches(card, enemy):
                continue
            touched += 1
            if enemy.get("health", 0) <= hit and not enemy.get("shield"):
                kills += 1

        if touched == 0:
            return None, 0

        if affects == "random":
            bolts = max(1, card.get("bolts", 1))
            score = bolts * hit * 2 + (10 if kills else 0) - cost
            return ("cast %d" % card["index"], score) if score > 0 else (None, 0)

        if kills == 0 and touched < 2:
            return None, 0

        return "cast %d" % card["index"], kills * 20 + touched * 4 - cost

    best, score = None, 0

    for enemy in board.get("enemyField", []):
        if not enemy.get("targetable") or enemy.get("shield"):
            continue
        if walled(board) and not enemy.get("taunt"):
            continue

        if not reaches(card, enemy):
            continue

        health = enemy.get("health", 0)
        if health <= 0 or health > hit:
            continue

        worth_it = 20 + body(enemy) - cost
        if worth_it <= score:
            continue

        score, best = worth_it, aimed(card, enemy)

    return best, score


def buff(board, card, cost):
    lift = card.get("strengthChange", 0)

    if not card.get("targeted"):
        wide = len([c for c in board.get("yourField", [])
                    if c.get("health", 0) > 0 and reaches(card, c)])
        if wide < 3:
            return None, 0

        score = wide * lift * 3 - cost
        return ("cast %d" % card["index"], score) if score > 0 else (None, 0)

    best, score = None, 0

    for mine in board.get("yourField", []):
        if mine.get("health", 0) <= 0 or not reaches(card, mine):
            continue

        worth_it = lift * 3 + body(mine) // 4 - cost
        if worth_it <= score:
            continue

        score, best = worth_it, aimed(card, mine)

    return best, score


def repair(board, card, cost):
    heal = card.get("healthChange", 0)
    if heal <= 0:
        return None, 0

    best, score = None, 0

    for mine in board.get("yourField", []):
        health = mine.get("health", 0)
        printed = mine.get("maxHealth", 0)
        if health <= 0 or printed <= 0 or not reaches(card, mine):
            continue

        used = min(heal, printed - health)
        if used < 3:
            continue

        worth_it = used * 2 - cost
        if worth_it <= score:
            continue

        score, best = worth_it, aimed(card, mine)

    return best, score


def refill(board, card, cost, mana):
    drawn = card.get("cardDraw", 0)
    held = len(board.get("hand", []))

    if drawn <= 0 or held >= 7:
        return None, 0
    if held > 2 and affordable(board, mana - cost):
        return None, 0

    score = 4 + drawn * 2 - cost

    return ("cast %d" % card["index"], score) if score > 0 else (None, 0)


def reaches(spell, creature):
    only = spell.get("onlyTribe", "")
    if not only:
        return True

    tribe = creature.get("tribe", "")

    return tribe == only or tribe == "all"


def affordable(board, mana):
    for card in board.get("hand", []):
        if card.get("kind") != "creature":
            continue
        if card.get("cost", 0) <= mana:
            return True

    return False


def aimed(card, target):
    return "cast %d %d" % (card["index"], target["netId"])


def ready(board):
    live = []

    for card in board.get("yourField", []):
        if card.get("waitTurn", 0) > 0 or card.get("attacked"):
            continue
        if card.get("strength", 0) <= 0 or card.get("health", 0) <= 0:
            continue
        live.append(card)

    return live


def walled(board):
    return board.get("opponent", {}).get("taunt", 0) > 0


def guards(board):
    return [card for card in board.get("enemyField", [])
            if card.get("taunt") and card.get("targetable") and card.get("health", 0) > 0]


def chargers(board):
    mana = board.get("you", {}).get("mana", 0)

    rush = [card for card in board.get("hand", [])
            if card.get("kind") == "creature" and card.get("charge")]

    rush.sort(key=lambda card: card.get("cost", 0))

    afford = []

    for card in rush:
        cost = card.get("cost", 0)
        if cost > mana:
            continue
        mana -= cost
        afford.append(card)

    return afford


def finisher(board):
    if walled(board):
        return None

    opponent = board.get("opponent") or {}
    if not opponent.get("targetable"):
        return None

    face = opponent.get("health", 0)
    mine = ready(board)
    reach = sum(card.get("strength", 0) for card in mine)

    if mine and reach >= face:
        return "attack %d %d" % (mine[0]["netId"], opponent["netId"])

    rush = chargers(board)

    if rush and reach + sum(card.get("strength", 0) for card in rush) >= face:
        return "play %d" % rush[0]["index"]

    return None


def choose_creature(board):
    mana = board.get("you", {}).get("mana", 0)
    hurt = board.get("you", {}).get("health", 30) <= 12
    behind = len(board.get("enemyField", [])) > len(board.get("yourField", []))

    best = None
    best_score = None

    for card in board.get("hand", []):
        if card.get("kind") != "creature":
            continue

        cost = card.get("cost", 0)
        if cost > mana:
            continue

        score = cost * 2 + card.get("strength", 0) + card.get("health", 0)

        if card.get("shield"):
            score += 4
        if card.get("taunt") and (hurt or behind):
            score += 6
        if card.get("lifesteal") and hurt:
            score += 4
        if card.get("charge"):
            score += 2
        if card.get("deathrattle"):
            score += card.get("deathrattleDamage", 0) + 2

        score += cry_worth(board, card)

        if best_score is None or score > best_score:
            best_score = score
            best = card

    return None if best is None else "play %d" % best["index"]


def cry_worth(board, card):
    cry = card.get("battlecry")
    if not cry:
        return 0

    worth = cry.get("cardDraw", 0) * 4

    hit = -cry.get("healthChange", 0)

    if hit > 0:
        reach = 0
        kills = 0

        for enemy in board.get("enemyField", []):
            if enemy.get("health", 0) <= 0:
                continue
            reach += 1
            if enemy.get("health", 0) <= hit and not enemy.get("shield"):
                kills += 1

        if reach > 0:
            if cry.get("affects") == "enemies":
                landings = reach
            else:
                landings = max(1, cry.get("bolts", 1))

            worth += hit * landings + kills * 6

    lift = cry.get("strengthChange", 0)
    if lift > 0:
        worth += lift * 2

    return worth


def legal_targets(board):
    if walled(board):
        return guards(board)

    live = [card for card in board.get("enemyField", [])
            if card.get("targetable") and card.get("health", 0) > 0]

    opponent = board.get("opponent") or {}

    if opponent.get("targetable"):
        live.append(opponent)

    return live


def choose_attack(board):
    mine = ready(board)
    targets = legal_targets(board)

    if not mine or not targets:
        return None

    best = None
    best_score = 0

    for attacker in mine:
        for target in targets:
            score = worth(board, attacker, target)

            if score > best_score:
                best_score = score
                best = (attacker, target)

    if best is None:
        return None

    return "attack %d %d" % (best[0]["netId"], best[1]["netId"])


def worth(board, attacker, target):
    hit = attacker.get("strength", 0)
    mine = board.get("you", {}).get("health", 30)
    opponent = board.get("opponent") or {}

    starving = attacker.get("lifesteal") and mine <= 15

    if target.get("netId") == opponent.get("netId"):
        return hit * 3 + (hit if starving else 0)

    if target.get("shield"):
        return max(1, 20 - hit * 2)

    kills = 0 < target.get("health", 0) <= hit
    dies = target.get("strength", 0) >= attacker.get("health", 0) and not attacker.get("shield")

    rattle = target.get("deathrattleDamage", 0) * 2 if target.get("deathrattle") else 0

    if kills and not dies:
        return max(1, 60 + body(target) - rattle)
    if kills and dies:
        return max(1, 25 + body(target) - body(attacker) - rattle)
    if dies:
        return 0

    return max(1, hit - 2 + (hit if starving else 0))


def body(card):
    score = card.get("strength", 0) + card.get("health", 0)

    if card.get("taunt"):
        score += 3
    if card.get("lifesteal"):
        score += 3
    if card.get("shield"):
        score += 3

    return score


def handshake(stream, key, mine, name):
    send(stream, "HELLO|%d|%s|%s" % (PROTOCOL, mine, name))

    line = read(stream)
    bits = line.split("|")

    if bits[0].upper() == "DENIED":
        sys.exit("the server turned this bot away - %s" % (bits[1] if len(bits) > 1 else line))

    if bits[0].upper() != "CHALLENGE" or len(bits) < 3:
        sys.exit("the server did not send a challenge, it sent: %s" % line)

    server, nonce = bits[1], bits[2]
    message = "%s|%s|%s|%s" % (AUTH_TAG, server, nonce, mine)

    send(stream, "PROOF|%s" % key.sign(message.encode("utf-8")).hex())

    line = read(stream)
    bits = line.split("|")

    if bits[0].upper() == "DENIED":
        sys.exit("the server turned this bot away - %s" % (bits[1] if len(bits) > 1 else line))

    if bits[0].upper() != "WELCOME":
        sys.exit("the server did not welcome this bot, it said: %s" % line)

    print("connected. this server is %s" % (server[:16] if server else "unidentified"))
    print("waiting for a seat (%s in the queue)" % (bits[1] if len(bits) > 1 else "?"))

    return server


def play(stream, key):
    while True:
        line = read(stream)

        if not line:
            continue

        if line.startswith("{"):
            try:
                board = json.loads(line)
            except ValueError:
                send(stream, "end")
                continue

            send(stream, decide(board))
            continue

        head = line.split("|")[0].upper()
        rest = line.split("|")[1:]

        if head == "PING":
            send(stream, "PONG")
        elif head == "SEATED":
            print("seated as player %s" % (rest[0] if rest else "?"))
        elif head == "SIGN":
            digest = rest[0] if rest else ""
            send(stream, key.sign(bytes.fromhex(digest)).hex())
            print("signed the match receipt %s" % digest[:16])
        elif head == "FINISHED":
            print("match over - %s" % (rest[0] if rest else "unknown"))
            print("waiting for another seat")
        elif head == "DENIED":
            sys.exit("the server dropped this bot - %s" % (rest[0] if rest else line))
        else:
            print("ignoring an unknown line: %s" % line)


def main():
    parser = argparse.ArgumentParser(description="A Dragonator bot that dials in to a server.")
    parser.add_argument("--host", default="127.0.0.1", help="the server address, an onion address with --tor")
    parser.add_argument("--port", type=int, default=6000, help="the port the server takes bots on")
    parser.add_argument("--name", default="dragon_bot", help="the name shown on the seat and in the receipt")
    parser.add_argument("--key", default="bot.key", help="the file holding this bot's identity")
    parser.add_argument("--tor", action="store_true", help="reach the server through Tor")
    parser.add_argument("--tor-host", default="127.0.0.1", help="the Tor SOCKS5 address")
    parser.add_argument("--tor-port", type=int, default=9050, help="the Tor SOCKS5 port")

    args = parser.parse_args()

    key = load_key(args.key)
    mine = public_hex(key)

    print("this bot is %s" % mine)

    if args.tor:
        sock = socket.create_connection((args.tor_host, args.tor_port), timeout=60)
        socks5_connect(sock, args.host, args.port)
    else:
        sock = socket.create_connection((args.host, args.port), timeout=60)

    sock.settimeout(None)
    stream = sock.makefile("rw", encoding="utf-8", newline="\n")

    handshake(stream, key, mine, args.name)

    try:
        play(stream, key)
    except IOError as problem:
        sys.exit("the link to the server ended - %s" % problem)


if __name__ == "__main__":
    main()
