# Writing a Dragonator bot

A bot is a program that **dials in** to a server. You need no hosting, no public
address, no port forwarding and no hidden service - your bot connects out, the
same direction a game client does.

```
pip install cryptography
python3 tools/dragon_bot.py --host 127.0.0.1 --port 6000
```

That is a complete, working entrant. Everything below is what it does.

## The key is who you are

Your bot signs in with an **Ed25519 key** it generates once and keeps. Over Tor
every connection looks like it came from 127.0.0.1, so there is no address to
recognise you by. The key is your queue place, your name on the board, your
ranking and your signature on the match receipt. Lose it and you are a new
entrant; share it and someone else is you.

## Signing in

Every line is UTF-8 and ends with `\n`.

```
you -> HELLO|1|<your public key, 64 hex>|<name>
srv -> CHALLENGE|<the server's public key>|<32 random bytes, hex>
you -> PROOF|<signature, 128 hex>
srv -> WELCOME|<your place in the queue>|<the name it will show>
```

What you sign is this exact string, built from what the server just sent plus
your own key:

```
dragonator-bot-auth-1|<server key>|<nonce>|<your public key>
```

Sign the **UTF-8 bytes** of that string. The nonce is fresh for every
connection, so a signature cannot be replayed.

If anything is wrong the server answers `DENIED|<reason>` and closes. The
reasons are plain: a protocol it does not speak, a key that is not 64 hex
characters, a signature that does not verify, a key already connected, or a
full queue.

**Checking the server back is optional but wise.** The `CHALLENGE` carries the
server's own public key. If the operator publishes it next to their onion
address, compare it and refuse to play anywhere else - otherwise a relay could
pass your challenge along and play as you somewhere you never agreed to.

## Waiting

After `WELCOME` you wait. A server is **one table**, so a competition plays
matches one after another and your turn comes round.

While you wait the server sends `PING` every so often and expects `PONG` back.
Answer it or you are dropped from the queue. Send nothing else while waiting -
the server is not listening for it.

```
srv -> PING
you -> PONG
```

## Playing

```
srv -> SEATED|<1 or 2>
```

From then on:

1. The server sends **one line** of JSON describing the board.
2. You send back **one line** naming a single move.
3. Repeat until your turn ends.

One move per exchange, never a whole turn at once. The server applies your
move, then sends the board again. Your bot asks, it does not act.

### The three moves

```
play <handIndex>                       put a creature from your hand onto the field
attack <attackerNetId> <targetNetId>   attack with one of your creatures
end                                    finish your turn
```

Nothing else is accepted. Anything the server cannot read counts as a refused
move.

`handIndex` is the `index` field of a card in your hand. `netId` values come
from the board, never invent one.

### The board

```json
{
  "protocol": 1,
  "turn": 6,
  "yourTurn": true,
  "secondsLeft": 42.5,
  "you": {
    "netId": 6, "name": "dragon_bot",
    "health": 30, "mana": 3,
    "handCount": 8, "deckCount": 31, "fieldCount": 2,
    "taunt": 1, "targetable": true
  },
  "opponent": {
    "netId": 5, "name": "other_bot",
    "health": 27, "mana": 4,
    "handCount": 10, "deckCount": 29, "fieldCount": 0,
    "taunt": 0, "targetable": true
  },
  "hand": [
    { "index": 0, "cardId": "03", "name": "03_Obi", "cost": 2,
      "kind": "creature", "strength": 3, "health": 5,
      "charge": false, "taunt": true,
      "lifesteal": false, "shield": false,
      "deathrattle": false, "deathrattleDamage": 0 }
  ],
  "yourField": [
    { "netId": 7, "cardId": "03", "name": "03_Obi",
      "strength": 3, "health": 5, "waitTurn": 0,
      "attacked": false, "taunt": true,
      "lifesteal": false, "shield": false,
      "deathrattle": false, "deathrattleDamage": 0, "targetable": true }
  ],
  "enemyField": []
}
```

### What you can and cannot see

You get your **own** hand in full. You get only a **count** of the opponent's
hand and deck, never the cards. This is deliberate and it is enforced by the
server, not by good manners: the document is built by the game itself and it is
the only thing your bot ever receives.

### Fields

| field | meaning |
|---|---|
| `protocol` | format version, currently `1`. Check it if you care about future changes. |
| `turn` | turn number, counting both players |
| `yourTurn` | if false, answer `end` |
| `secondsLeft` | time left on the turn clock, `0` if the server has no clock |
| `taunt` | how many taunt creatures that side has on the field |
| `targetable` | whether that seat or creature may be attacked at all |

Cards in `hand` always carry `index`, `cost` and `kind`. `kind` is `creature`,
`spell` or `other`. Creatures also carry `strength`, `health`, `charge` and
`taunt`. Spells carry `targeted`, `affects`, `healthChange`, `strengthChange` and
`cardDraw` - see **Casting a spell** below.

Cards on a field carry `netId`, `strength`, `health`, `waitTurn`, `attacked`,
`taunt`, `lifesteal`, `shield`, `deathrattle`, `deathrattleDamage` and
`targetable`.

### Keywords

| keyword | what it does |
|---|---|
| `taunt` | while any of a side's creatures has it, that side can only be attacked through them |
| `charge` | can attack the turn it is played, instead of waiting |
| `lifesteal` | damage this creature deals also heals its owner, up to their starting health |
| `shield` | the next damage this creature would take is ignored, and the shield is then spent |
| `deathrattle` | when this creature dies it deals `deathrattleDamage` to one enemy creature, picked at random |

`deathrattleDamage` is how much that death deals, and it is `0` on a creature
that has no deathrattle. The target is chosen by the server from the dying
creature's enemies, drawn from the match seed both seats committed to before the
first card was dealt, so it is not the operator's choice and it reproduces
exactly in the match replay.

On a card in hand these describe what it *will* have. On the board, **`shield` is live state** - once it absorbs a hit it becomes `false`, so a shielded creature is only safe once. A shielded creature that absorbs an attack takes no damage, and an attacker with `lifesteal` heals nothing from that hit, because no damage was dealt.

### Playing the keywords well

A bot that reads only `strength` and `health` will lose to one that reads the
rest. Five things are worth building in, and `tools/dragon_bot.py` does all of
them if you want the code.

**`shield` breaks the obvious kill test.** `health <= strength` is not a kill
when `shield` is true - the hit is ignored whole, whether it was for 1 or for
9. So spend your *weakest* ready creature on the shield and keep the big one
for the hit that follows. Sending your 7-strength creature in first throws six
damage away.

**`charge` is reach you already have.** Add up the `strength` of every ready
creature on your field, then add the `charge` creatures in hand you can still
pay for. If that total is at least the opponent's `health` and their `taunt`
count is 0, the match is over this turn - play the charge and swing. A bot that
only counts the board misses this every time.

**`lifesteal` is worth most when you are low.** The same attack is a different
move at 8 health than at 30. Weight it, do not just take it.

**`taunt` is a body you want on your own side too.** Play it when the opponent
has more creatures than you or your health is getting short - it buys the turn
your other creatures need.

**`deathrattle` makes a kill cost something.** The creature you kill hits one of
*your* creatures back for `deathrattleDamage` on the way out, and you do not get
to choose which one. So a killable creature with a deathrattle is worth less to
kill than the same body without one, and the cheapest answer is often to leave it
alone and hit something else. Two things follow. Only the *kill* sets it off, so
breaking a shield, or trading in without finishing the creature, is free. And the
damage lands on your own field, so when you attack with your only creature you
know exactly where it will go - check that your attacker lives through both the
trade and the death before you swing.

Keywords also make a creature worth more than its numbers when you decide what
to trade. A 3/3 with `taunt` and `lifesteal` is a better kill than a 4/4 with
neither.

### The rules the server will hold you to

A move is refused if it breaks any of these, so check them yourself and save a
turn:

- It is not your turn.
- You cannot afford the card, or the hand index does not exist, or the card is
  not a creature.
- The attacker is not yours, is dead, has `waitTurn` above 0, or has already
  attacked this turn.
- The target is dead or `targetable` is false.
- **Taunt:** if the opponent's `taunt` count is above 0, you may only attack a
  creature whose `taunt` is true. Attacking anything else is refused.

A newly played creature has `waitTurn` 1 and cannot attack until your next turn,
unless it has `charge`.

## Casting a spell

A spell is played with `cast`, not `play`. `play` is refused for a spell and
`cast` is refused for a creature.

```
cast <handIndex>              an untargeted spell
cast <handIndex> <netId>      a spell that names one creature
```

The card in hand tells you which form to send:

| field | meaning |
|---|---|
| `targeted` | if true you MUST name a creature, and only a creature - never a seat |
| `affects` | who an untargeted spell reaches: `enemies`, `friendlies`, `random` or `owner` |
| `healthChange` | negative is damage, positive is healing |
| `strengthChange` | permanent change to a creature's strength |
| `cardDraw` | cards the caster draws |

```json
{ "index": 3, "cardId": "21", "name": "21_Scorch", "cost": 1,
  "kind": "spell", "targeted": true, "affects": "enemies",
  "healthChange": -2, "strengthChange": 0, "cardDraw": 0 }
```

### The rules the server holds you to

- **Taunt applies to a targeted spell exactly as it applies to an attack.** If the
  opponent's `taunt` count is above 0, a harmful targeted spell may only name a
  creature whose `taunt` is true.
- **An untargeted spell ignores taunt**, because it chooses nothing. That is what
  makes an area spell the answer to a taunt wall.
- A harmful spell cannot be aimed at your own creature, and a helpful one cannot
  reach the opponent's.
- **A shield absorbs spell damage** the same way it absorbs an attack, and is then
  spent. `health <= -healthChange` is not a kill when `shield` is true.
- `affects: "random"` draws its target from the sealed match seed, so the pick is
  reproducible from the replay and is not the operator's choice.

Spells never reach a seat, only creatures, so there is no burn-the-face plan to
build. Removal is for the board.

## Signing the match receipt

When the match ends the server asks every seat to sign what happened:

```
srv -> SIGN|<64 hex, the receipt digest>
you -> <signature, 128 hex>
```

**Decode the hex to 32 raw bytes and sign those bytes** - not the hex text.

This is the part that makes a result yours rather than the operator's word. The
receipt names both seats, both keys and the winner; your signature on it is
proof you played that match and agreed the outcome. A server that holds every
key can invent any ladder it likes. One that has to ask you cannot.

Signing is not compulsory. Refuse and the match still counts - the receipt just
records your seat as unsigned, which is a claim nobody can check.

## Between matches

```
srv -> FINISHED|<win, loss or draw>
```

You stay connected and go back into the queue for the next match. Keep
answering `PING` and wait for the next `SEATED`.

## Timing and failure

- You get a few seconds per move. Miss it and your turn is abandoned.
- Three abandoned turns and your seat stops being played for the rest of the match.
- Three refused moves in a row end your turn.
- If you crash or drop the connection, your seat passes its turns and your key
  leaves the queue. Reconnect and you are back in.

None of this can crash the server or the other bot. The worst a broken bot does
is pass its turns and lose.

## A complete bot

```python
import json, socket
from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PrivateKey

key = Ed25519PrivateKey.generate()          # save this to a file in a real bot
mine = key.public_key().public_bytes_raw().hex()

sock = socket.create_connection(("127.0.0.1", 6000))
io = sock.makefile("rw", encoding="utf-8", newline="\n")

def say(line):
    io.write(line + "\n")
    io.flush()

say("HELLO|1|%s|minimal" % mine)

server, nonce = io.readline().strip().split("|")[1:3]
say("PROOF|" + key.sign(("dragonator-bot-auth-1|%s|%s|%s" % (server, nonce, mine)).encode()).hex())
io.readline()

for line in io:
    line = line.strip()
    if line.startswith("{"):
        board = json.loads(line)
        move = "end"
        if board["yourTurn"]:
            for card in board["hand"]:
                if card["kind"] == "creature" and card["cost"] <= board["you"]["mana"]:
                    move = "play %d" % card["index"]
                    break
        say(move)
    elif line == "PING":
        say("PONG")
    elif line.startswith("SIGN|"):
        say(key.sign(bytes.fromhex(line.split("|")[1])).hex())
```

That signs in, plays legally and signs its receipts. It never attacks, so it
loses, but it is a real entrant and it is the right place to start.
`tools/dragon_bot.py` is the same thing with an attack policy, a saved key file
and Tor support.
