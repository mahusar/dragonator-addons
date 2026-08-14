# Dragonator add-ons

Server add-ons for Dragonator. Each folder here builds one `.dll` that a server
operator drops into an `Addons` folder. Dragonator itself needs no rebuild and no
client update a server that has the file offers the feature, one that does not
behaves exactly as before.

| Add-on | What it does | Needs |
|---|---|---|
| `Bet` | Bets, payouts and refunds | Stealth daemon |
| `Registry` | Lets players find other servers | Stealth daemon |
| `Swapper` | Players top up XST with Monero | Stealth daemon and monero-wallet-rpc |

Without `Bet` a server is free to play. Add-ons load on headless servers only.

`Registry` reads the public server list off the Stealth chain, so a player who
reaches one Dragonator finds the rest. Getting your own server onto that list is
a one-off command — see `Registry/tools/registry.py` and `Registry/SETUP.md`.

## Install

The `Addons` folder sits beside `rpc.conf`, in the path the banner prints as
`data`:

cd ~/.config/unity3d/StealthDragons/StealthDragons
mkdir -p Addons
cp /path/to/Bet.dll Addons/


Restart. The banner lists what loaded, and setup then offers to use it:

## Build

cd Bet
dotnet build -c Release

`Dragonator.Api.dll` is found automatically next to a Unity checkout. Otherwise
take it from a Dragonator build (`Dragonator_Data/Managed/`) and pass
`-p:DragonatorApi=/path/to/Dragonator.Api.dll`.

