# Registry add-on

Lets players find other Dragonators.

Normally every Dragonator is an island - you can only reach one if someone hands
you its .onion address. This add-on reads server listings off the Stealth chain,
so a player who knows **one** Dragonator can ask it who else is out there.

The chain only supplies addresses. Whether a server is actually up is decided by
the player's client, which contacts each one over Tor and shows the ones that
answer. Dead listings never reply and drop off the list.

## You need

- `StealthCoind` running and synced, with a working `rpc.conf`.
- Dragonator built with **add-on API 4** or newer. Older ones refuse to load this
  and say so at startup.

No balance is needed. This add-on only reads.

## Install

Drop `Registry.dll` into the `Addons` folder and start the server. It asks:

```
read the public server list from the chain, so players who reach this server
find the others (y/n)
```

Answer `y`. Answer `n` - or choose the free-server option at the top of setup -
and it stays off.

## What happens

It walks the chain in the background from block **35472734**, the first block
known to hold a listing. Catching up takes a few minutes, once. The banner shows
progress:

```
server list   2 servers, 4,100 blocks behind
```

Findings are saved to `registry.txt` beside your other data, so a restart carries
on instead of rescanning.

Players get the list by asking your server `GET_SERVERS` on port 5555 - the same
port as `GET_SERVERINFO`, so there is nothing new to open in Tor. Scanning runs on
its own thread and never slows a match.

## Getting your own server listed

The add-on reads the list; it does not publish your address to it. You do that
once, by hand, with `tools/registry.py`.

```
python3 tools/registry.py write <your-XST-address> <your-onion> 5555
```

That prints a `sendtoaddress` command - it does not run it. Check it, then run it.
The listing rides along as an `OP_RETURN` output, so **send to an address you own**;
the coin comes straight back to you and the destination is never read by anything.

Once it confirms, every Registry add-on picks it up on its next sweep. There is
nothing to renew and nothing to take down - the same command with a new onion
replaces the old entry.

The script also does the pieces on their own, which is what you want when
something looks wrong:

```
python3 tools/registry.py encode <onion> [port]     the 80-hex record
python3 tools/registry.py decode <80-hex>           back to an onion
python3 tools/registry.py list <address>            listings paid to one address
```

`list` is a check, not how the add-on works - it uses the daemon's address index,
which needs `exploreapi=1` and a one-off `StealthCoind -reindexexplore=1`. The
add-on itself walks blocks and needs neither.

Your server still hands the list to players either way - publishing only affects
whether other people's servers show yours.

## Two things worth knowing

**Listings cost no coin.** Stealth's feework pays the fee with CPU instead. That
also makes junk listings free to create, so ranking them by something scarce is a
job still to be done. Today the list is too small for it to matter.

**Publishing costs privacy.** A listing is a public transaction from whichever
address paid for it, tying that address to your onion. If that matters, register
from a separate, freshly funded address rather than your main one.
