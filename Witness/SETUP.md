# Witness add-on

Keeps a signed receipt of every match, and optionally writes proof of it to the
Stealth chain.

A receipt is the match itself: who played, when, the settled shuffle, the result,
and a signature from **each player**, made with keys the server does not have. So
a result cannot be invented or altered afterwards, not even by the operator.

The chain part adds one thing the receipt cannot give itself: **time**. An anchor
proves the receipt existed by a certain block and has not changed since. This is
the same approach as Certificate Transparency (RFC 6962) and OpenTimestamps.

Three jobs, three places:

| What | Where | What it proves |
|---|---|---|
| the match | `receipts/<digest>.txt` | what happened |
| honesty | the player signatures inside it | both clients checked the deal and agreed |
| time | a 40 byte anchor on the chain | it existed by then, unchanged |

## You need

- Dragonator built with **add-on API 5** or newer. Older ones refuse to load this
  and say so at startup.
- For **storing receipts only**: nothing else. No daemon, no wallet, no balance.
- For **anchoring**: `StealthCoind` running and synced with a working `rpc.conf`,
  and at least `0.01` XST in the wallet.

**Anchoring costs you nothing.** The write is feeless - it rides on feework, so
it is paid in CPU rather than coin. And the `0.01` XST goes to an address from
your own `getnewaddress`, so it is not spent, only moved back into your own
wallet at a new address.

You still need that `0.01` to be **available**, because the transaction has to be
built out of something, and it must clear the dust threshold - `0.0001` was tried
first and the network rejected it.

## Install

Drop `Witness.dll` into the `Addons` folder and start the server. It asks:

```
anchor signed match receipts on the chain, so anyone can check a match
really happened (y/n)
```

Answer `n` (the default) and receipts are still written to disk, just not
anchored. That needs no wallet at all and is the right starting point.

Answer `y` and receipts are also batched onto the chain.

## What it writes

Receipts land in the `receipts` folder beside `rpc.conf`, in the path the banner
prints as `data`:

```
~/.config/unity3d/StealthDragons/StealthDragons/receipts/<digest>.txt
```

One file per match, named by its SHA-256 digest. Inside is the canonical receipt,
then a `signatures=` line, and after anchoring a line naming the txid and the
Merkle path.

**Only fully signed receipts are kept.** If a match ends without every player
signing, it is dropped with `a receipt arrived that is not signed by every
player - ignored`. A half-signed receipt proves nothing, so it is not stored.

## How anchoring batches

Receipts are not written to the chain one at a time. Up to **16** share a single
anchor through a Merkle root, and each receipt keeps its own proof path, so any
one of them still verifies on its own.

A batch is written when either happens first:

- 16 receipts are waiting, or
- **10 minutes** have passed since the last write.

It also flushes on a clean shutdown, and anything still unanchored is re-queued
the next time the server starts. So a restart does not lose receipts.

Only a 40 byte record goes on the chain, never the match. Nobody can read the
chain and list what a player has played.

The record carries one flags byte saying whether every match in the batch was
human against human, and whether any of them involved a bot.

**If the write fails** - daemon down, no balance, RPC refused - the batch is put
back and retried on the next flush. Nothing is lost, and the failure is logged:

```
[Witness] could not anchor 3 receipt(s) (...) - they stay queued
```

## Serving receipts

A full receipt is served on port 5555:

```
GET_RECEIPT|<digest>
```

Same port as `GET_SERVERINFO`, so it is already reachable over your existing
hidden service with no torrc change.

## Checking a receipt yourself

`tools/anchor.py` reads anchors back off the chain. It needs `StealthCoind` on
your PATH, or set `STEALTHD` to point at it.

```
python3 anchor.py verify <receipt.txt>       check one receipt against the chain
python3 anchor.py dir <receipts-folder>      check every receipt in a folder
python3 anchor.py tx <txid>                  decode the anchor in a transaction
python3 anchor.py decode <80-hex>            decode a raw record
python3 anchor.py scan <height> <blocks>     walk blocks looking for anchors
```

`verify` recomputes the receipt's digest, walks its stored Merkle path to the
root, and confirms that root is the one written in the named transaction. That
check is independent of this server: anyone holding the receipt file can run it.

## Costs

None, in coin. The write is feeless and the `0.01` XST returns to your own
wallet, so anchoring does not drain a balance no matter how many matches you
run. One anchor covers up to 16 matches.

What it does need is `0.01` XST sitting available at the moment of each write. If
you also run `Bet` on the same server, that is the same wallet payouts come from,
so a wallet emptied by payouts can stall anchoring until something lands in it.
Failed writes re-queue, so nothing is lost when it happens.

## Turning it off

Delete `Witness.dll` and restart, or answer `n` at setup to keep receipts
locally without touching the chain. Receipts already written stay on disk and
stay verifiable.
