# Swapper add-on

Lets players buy XST from you with Monero.

One direction only: **players send XMR, you send XST.** You are selling your own
XST. Betting, payouts and refunds are untouched and stay XST-only.

Installing and building the add-on is in the [README](../README.md). Everything
here is the Monero side.

## You need

- A Dragonator server with `rpc.conf` and a funded Stealth wallet.
- **Spare XST**, beyond what any match owes. You are the seller; with no
  inventory there is nothing to sell.
- Linux with Tor running on `127.0.0.1:9050`.
- About 30 minutes, mostly waiting for the first Monero sync.

## 1. Install

Put `Swapper.dll` in the `Addons` folder and restart.

If you ever ran it as `MoneroSwapper.dll`, **delete that file.** Both would load,
both claim the same settings, and which wins depends on file sort order.

## 2. Open the swap port

The swapper answers on its own port, not the game's. Add to `torrc`, **in the same
block as your existing `HiddenServicePort` lines** - they attach to the
`HiddenServiceDir` above them:

```
HiddenServicePort 5556 127.0.0.1:5556
```

Reload Tor (`sudo systemctl reload tor`). Check with
`sudo -u debian-tor tor --verify-config -f /etc/tor/torrc`.

Players reach the desk at the same .onion on 5556, and Dragonator tells them the
port, so you never publish it. Use `-swapport <n>` if 5556 is taken and match the
`torrc` line.

If the port cannot be opened, Dragonator advertises `swap=off` rather than a rate
it cannot serve - a missing `torrc` line shows as "no swapping" in the banner, not
as a broken button.

## 3. Monero binaries

```
wget https://downloads.getmonero.org/cli/linux64 -O monero-linux-x64.tar.bz2
tar -xjf monero-linux-x64.tar.bz2
```

They are **not** on your PATH. Call them with `./` from inside the folder.

## 4. Create the wallet

Offline - it needs no daemon, and this avoids `--proxy` argument errors entirely:

```
cd monero-x86_64-linux-gnu-v0.18.5.1
./monero-wallet-cli --generate-new-wallet ~/swapper --offline
```

- Write the 25-word seed on paper. Losing it loses every XMR sent to you.
- Note the restore height. If it says `0`, fix it once you know the chain height
  from step 5, or the first sync scans the whole chain.
- **Leave with `exit`, never Ctrl+Z.** Ctrl+Z suspends the process and keeps the
  wallet file locked, which later blocks the RPC with
  `"swapper.keys" is opened by another wallet program`.

## 5. Find a Tor node

You do not run a Monero node. You sync against someone else's over Tor.

Get four or five .onion addresses from https://monero.fail with the Tor filter.
**Ignore its red/green flags** - it probes over clearnet and reports false
failures for onion nodes. Test them yourself:

```
for n in node1.onion node2.onion node3.onion; do
  printf '%-60s ' "$n"
  curl -s -m 120 --socks5-hostname 127.0.0.1:9050 http://$n:18081/get_info \
    | grep -oE '"height": *[0-9]+' || echo FAIL
done
```

**Try both 18081 and 18089.** Many nodes serve restricted RPC on 18089 only, and
testing just 18081 makes a working node look dead.

Keep two that report `"status": "OK"`, `"nettype": "mainnet"`,
`"synchronized": true`, and heights that agree. Two matters: a single node can
withhold a transaction, and a hidden payment looks exactly like one that never
arrived. A stagenet node would accept you happily and your real XMR would never
appear.

## 6. Password file

Must have **no trailing newline**:

```
printf '%s' 'your-wallet-password' > ~/.monero-pass
chmod 600 ~/.monero-pass
wc -c ~/.monero-pass
```

`wc -c` must equal your password's exact length. Use `printf`, never `echo` -
`echo` appends a newline that becomes part of the password.

## 7. Start the wallet RPC

```
cd monero-x86_64-linux-gnu-v0.18.5.1
./monero-wallet-rpc --wallet-file ~/swapper --password-file ~/.monero-pass \
  --daemon-address YOURNODE.onion:18089 \
  --proxy 127.0.0.1:9050 --untrusted-daemon \
  --rpc-bind-ip 127.0.0.1 --rpc-bind-port 18083 --disable-rpc-login \
  --log-file ~/monero-wallet-rpc.log
```

`--daemon-address` must be a **bare** `host.onion:port` - no `http://`, no
trailing slash. Anything else gives a misleading error demanding
`--daemon-ssl-allow-any-cert`. Run it under `tmux` or systemd so it survives your
SSH session.

**The first start takes a long time and looks frozen.** The RPC binds its port
only after the initial refresh, so there is nothing to query until then. ~23,000
blocks took about 27 minutes over Tor and peaked near 1.2 GB RAM. It is working
if CPU time climbs and a Tor circuit is open:

```
ps aux | grep monero-wallet-rpc | grep -v grep
ss -tnp | grep 9050
```

A single `no_connection_to_daemon` is survivable - it retries. Do not restart on
the first one. You are ready at:

```
Binding on 127.0.0.1 (IPv4):18083
Starting wallet RPC server
```

Later restarts are fast.

## 8. Check it works

```
R(){ curl -s http://127.0.0.1:18083/json_rpc -H 'Content-Type: application/json' -d "$1"; echo; }

R '{"jsonrpc":"2.0","id":"0","method":"get_height"}'
R '{"jsonrpc":"2.0","id":"0","method":"create_address","params":{"account_index":0,"label":"test"}}'
```

`get_height` should match your node. `create_address` returns an address starting
with `8` - subaddresses start with 8, your main address with 4.

Send a little XMR to it and watch it arrive:

```
R '{"jsonrpc":"2.0","id":"0","method":"get_transfers","params":{"in":true,"pool":true}}'
```

`"pool": true` shows the payment before it confirms. Once you see the transfer
with its `subaddr_index`, the Monero side is proven.

## 9. Set your rate

Dragonator asks at startup, and the answer reaches players on the connect screen.

**Manual** - you type it, it stays until you change it:

```
XST paid per 1 XMR ('off' for no swapping) [no swapping]: 12000
```

Full control, no dependency on any outside site; you carry the risk of selling at
a stale price. **Sanity-check against market before entering it.** Market is
around 12,100 XST per XMR at the time of writing. A rate of 250,000 would pay
roughly twenty times what the XMR is worth and drain your wallet in a few swaps.

**Automatic** - computed from live prices, refreshed every 120 seconds:

```
XST per XMR = (XMR_usd / XST_usd) x (1 - swapmargin)
```

- **XMR**: CoinGecko and Kraken, cross-checked. More than 3% apart and the rate is
  refused; when they agree the **lower** is used, so an error always errs toward
  paying out less.
- **XST**: NoFinex `https://xapi.finexbit.com/v1/market` - read the `XST_USDT`
  entry's `"price"`, which must also be `"active": true`. Use `/market`, not
  `/ticker`; the ticker endpoint returns a PHP error. NoFinex bans IPs polling
  faster than twice a second.

**Never use CoinGecko for the XST price.** `ids=stealthcoin` still returns a
number, but it is a frozen delisted entry - measured 2026-08-04, last updated
2024-05-09, about 2.2 years stale. It looks live and is not. Freshness-check any
second XST source before trusting it.

Price requests go through Tor on `127.0.0.1:9050`, so price sites never learn your
clearnet IP. `-swapdirect` bypasses Tor if an exit node is blocked. **TLS is
validated strictly either way, and over Tor that is not optional** - the exit node
sees your traffic and could otherwise forge a price and drain your wallet.

`swapmargin` is your spread. Without one you sell at exactly market and lose to
volatility during the confirmation wait.

You can switch back to manual whenever you like.

## 10. Safety settings

| Setting | Protects against |
|---|---|
| `swapreserve` | XST held back so a swap can never leave a match winner unpaid. Set above your largest possible pot. |
| `swapmax` | Caps XST paid in one swap, bounding the loss if a price is ever wrong. |
| `swapmin` | Rejects dust. Many tiny payments make fat, expensive transactions. |
| `swapconf` | XMR confirmations before you send. 2 to 4 is defensible; each is about 2 minutes. |

Automatic mode also enforces these itself, none configurable: it **refuses rather
than guesses** (any failed check sets the rate to `off` and issues no new address
- it never falls back to the last known rate), rejects **stale** prices (CoinGecko
XMR must be under 30 minutes old, any rate expires 10 minutes after it is
computed), rejects XMR sources **more than 3% apart**, rejects **jumps** over 25%
from the last accepted rate, and rejects a **dead market** (XST inactive, or no
trade for 48 hours).

The jump bound carries most of the weight, because **XST cannot be
cross-checked.** It trades on essentially one venue with thin depth (24h volume
around 28,600 USDT), so its price is cheap to move, and the one apparent second
source is stale by years. Since the rate is `XMR_usd / XST_usd`, pushing XST
*down* pushes your payout *up*: an attacker depresses XST, swaps at the inflated
rate, and you cover the difference. Your protection is the 25% bound, `swapmax`
and `swapreserve` - not a cross-check. **Set `swapmax` if you run automatic.**

## 11. Files, and stuck swaps

Two append-only files beside `rpc.conf`:

- **`swaps.txt`** - one line per deposit address issued: when, subaddress index,
  XMR address, the player's XST address, **the rate locked at that moment**, and
  confirmations required. The locked rate is what gets paid, however far the market
  has moved since.
- **`credits.txt`** - one line per event on an incoming payment, keyed on
  `txid:subaddress`. A payment is only ever acted on once, because the key is
  checked before anything is sent.

| State | Meaning |
|---|---|
| `paid` | Done. The last field is the Stealth transaction id. |
| `held` | Refused, will not retry: below `swapmin`, over `swapmax`, or no `swaps.txt` entry for that subaddress. **Needs you.** |
| `claimed` | A send started and never confirmed - the process died in between. **Check the wallet by hand before doing anything else.** |

`claimed` is never retried automatically. The swapper cannot tell whether the send
went out, and retrying might pay twice. Look up the player's XST address in your
wallet, see whether it was paid, and settle it yourself.

A payment that merely cannot be covered right now - because `swapreserve` would be
breached - gets **no** `credits.txt` entry and is retried every minute, so topping
up the wallet clears it.

The banner reports anything outstanding:

```
   swapper   12000 XST per XMR (fixed)  -  2 swaps need attention in credits.txt
```

## Operating notes

- **Your wallet is shared.** The same Stealth wallet pays match winners and sells
  XST to swappers. `swapreserve` keeps them from colliding.
- **Keep the Monero wallet RPC running.** If it stops, swapping stops; the game is
  unaffected.
- **Back up `swapper.keys` and the seed.** Losing them loses your XMR.
- **Move to a view-only wallet before this holds real value.** The swapper never
  spends XMR, so it does not need your spend key. Generate offline, restore on the
  server with `--generate-from-view-key`, and sweep manually from cold storage.
  `create_address` and `get_transfers` behave identically, so nothing above
  changes - and if the server is compromised the XMR stays untouchable.
