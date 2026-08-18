# Bots add-on

Turns a Dragonator into an arena where bots that people wrote play each other.

This is not a bot opponent for humans. A human who wants to play a bot uses
practice mode in the client, which needs no server at all. This add-on is for
the other thing: people write bots, and the bots fight.

A server running this add-on is a **bot-only server**. It refuses human players,
because a Dragonator is a single table and the bots are sitting at it.

## Bots dial in

Your server opens a port and waits. Entrants connect **to you**. They need no
hosting, no public address and no hidden service of their own - which is the
whole point, because over Tor asking every entrant to run a hidden service is
asking most of them not to enter.

Each bot signs in with its own Ed25519 key and proves it holds that key before
it is allowed near a seat. The key is the entrant's identity: their place in the
queue, the name on the board, and their signature on the match receipt.

A bot never runs inside the server. It cannot reach the wallet, cannot see the
opponent's cards, and cannot do anything except ask for a move that the server
then checks. Any language works.

## You need

- Dragonator built with **add-on API 6** or newer. Older ones refuse to load this
  and say so at startup.
- A port for bots to dial in on. `6000` is the usual one.

No wallet and no balance. This add-on never touches a stake, and an arena server
should not have `Bet.dll` installed.

## Install

Drop `Bots.dll` into the `Addons` folder and start the server. It asks:

```
a port for bots to dial in on, 6000 is the usual one,
or leave empty for a normal server humans play on
```

Answer `6000`.

Leave it empty and nothing changes: the server stays an ordinary human table.
The add-on only takes over when you give it a port.

The server then listens on `127.0.0.1:6000` and says so:

```
[Bots] bots dial in on 127.0.0.1:6000.
BotArena: this server plays bot against bot. No human seat is offered.
BotArena: waiting for bots to dial in - 0 of 2 connected.
```

## Letting entrants reach it

The desk binds to **loopback only**, never to a public interface. Tor carries
the rest. Add the bot port to the same hidden service block as your other
ports:

```
HiddenServiceDir /var/lib/tor/dragonator/
HiddenServicePort 7780 127.0.0.1:7780
HiddenServicePort 5555 127.0.0.1:5555
HiddenServicePort 6000 127.0.0.1:6000
```

**The port lines attach to the `HiddenServiceDir` above them.** Appending at the
end of the file silently creates a different service on a different address.
Check it before reloading:

```
sudo -u debian-tor tor --verify-config -f /etc/tor/torrc
```

Then publish two things to entrants: your onion address with the bot port, and
the server's public key, which the server prints at startup. An entrant who
pins that key cannot be relayed into playing somewhere they never agreed to.

## Running an entrant

One file on their laptop, nothing else:

```
pip install cryptography
python3 dragon_bot.py --host <your>.onion --port 6000 --tor --name theirbot
```

It makes a key on first run, joins the queue, and stays connected taking one
match after another.

## What happens then

The server plays matches back to back on its own, with nobody watching:

```
[Bots] alpha (7fbd11ff6ef76db8) dialled in, 1 in the queue.
[Bots] beta (017d4e2a4a48ef64) dialled in, 2 in the queue.
[Bots] alpha (7fbd11ff6ef76db8) takes seat 1.
[Bots] beta (017d4e2a4a48ef64) takes seat 2.
BotArena: alpha against beta.
GameManager: the bot 7fbd11ff6ef76db8 signed the match receipt.
BotArena: 1 match(es) played, the last took 34s.
[Bots] alpha (7fbd11ff6ef76db8) left seat 1 and is back in the queue.
```

Both bots stay connected and go back into the queue, so a ladder does not pay
the cost of building a fresh Tor circuit for every match.

Matches run one at a time, because a Dragonator is one table. Ten bots playing a
full round robin is 45 matches in sequence. Run several servers if you want them
in parallel.

The server waits for **two** entrants before it starts a match, and says where
it has got to:

```
BotArena: waiting for bots to dial in - 1 of 2 connected.
```

It will not manufacture an opponent, because a result against a built-in policy
is not a result anyone should put in a ladder. A lone entrant testing their bot
against a live server sees that line and waits for a second one.

## Who signed what

Each seat signs the match receipt with the entrant's own key, and the server
verifies the signature before recording it. That is what makes "my bot beat
yours" checkable rather than the operator's word, and it is the reason the
handshake exists at all.

A server that holds every key can write any ladder it likes. This one has to
ask.

## When a bot misbehaves

The server protects the match, not the bot:

- No answer in time loses that turn. Three abandoned turns and that seat stops
  being played for the rest of the match.
- An illegal move is refused and the bot is asked again. Three refusals in a row
  end the turn.
- A dropped connection loses only that seat. The other bot plays on, and the
  dead key leaves the queue so it is not seated again.
- A queued bot is pinged periodically. One that stops answering is dropped
  rather than seated into a match it cannot play.
- If matches start ending in under five seconds, three times in a row, the arena
  stops itself rather than spinning.

The handshake refuses, in plain words, a protocol it does not speak, a key that
is not 64 hex characters, a signature that does not verify, a key already
connected, and a full queue:

```
[Bots] a bot was turned away - that signature does not prove the key.
```

## One entry per key

The same key cannot hold both seats. Two entrants means two keys, which is also
what makes the two sides of a match genuinely independent parties.

For a local test, run two copies with two key files:

```
python3 dragon_bot.py --port 6000 --name alpha --key alpha.key
python3 dragon_bot.py --port 6000 --name beta  --key beta.key
```

## Writing your own

`PROTOCOL.md` has the sign-in, the board format, the three moves and the
receipt signature.
`tools/dragon_bot.py` is a complete working entrant in one file, meant to be
copied.
