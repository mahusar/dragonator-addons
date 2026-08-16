# Dragonator add-ons

Server add-ons for Dragonator. Each folder here builds one `.dll` that a server
operator drops into an `Addons` folder. Dragonator itself needs no rebuild and no
client update a server that has the file offers the feature, one that does not
behaves exactly as before.

| Add-on | What it does | Needs |
|---|---|---|
| `Registry` | Lets players find other servers | Stealth daemon |
| `Witness` | Publishes proof that a match really happened | Stealth daemon |
| `Bet` | Bets, payouts and refunds | Stealth daemon |
| `Swapper` | Players top up XST with Monero | Stealth daemon and monero-wallet-rpc |

`Registry` reads the public server list off the Stealth chain, so a player who
reaches one Dragonator finds the rest. Getting your own server onto that list is
a one-off command - see `Registry/tools/registry.py` and `Registry/SETUP.md`.

`Witness` keeps a receipt of every match, signed by both players with keys the
server does not have, and anchors them on the Stealth chain - so a result cannot
be invented, altered or back-dated. Anchoring is off until the operator turns it
on.

Three jobs, three places:

| What | Where it lives | What it proves |
|---|---|---|
| The match itself | `receipts/<digest>.txt` on the server | what happened |
| Honesty | the player signatures inside the receipt | both clients checked the deal and agreed |
| Time | a 40 byte anchor on the chain | it existed by then, and has not changed since |

Only a hash goes on the chain, never the match, so nobody can list what a player
has played. Up to 16 receipts share one anchor through a Merkle root and each
keeps its own proof path, so any single receipt still checks out on its own. The
anchor also carries one byte saying whether every match in the batch was human
against human, and whether any of them had a bot.

Full receipts are served as `GET_RECEIPT|<digest>` on port 5555. Read anchors
back with `Witness/tools/anchor.py`:

    python3 anchor.py dir ~/.config/unity3d/StealthDragons/StealthDragons/receipts/
    python3 anchor.py verify <receipt.txt>

Same approach as RFC 6962 Certificate Transparency and OpenTimestamps.

Without `Bet` a server is free to play. Add-ons load on headless servers only.

## Build

    bash tools/build.sh

Pick from the menu, or name what you want:

    bash tools/build.sh all
    bash tools/build.sh witness registry
    bash tools/build.sh --clean all

Every add-on lands in `builds/` next to this file, one `.dll` each, ready to
copy to a server.

Needs the .NET SDK. `sudo apt install dotnet-sdk-8.0`.

It also needs `Dragonator.Api.dll`, which ships inside every Dragonator build at
`dragonator_Data/Managed/`.

## Install

The `Addons` folder sits beside `rpc.conf`, in the path the banner prints as
`data`:

    cd ~/.config/unity3d/StealthDragons/StealthDragons
    mkdir -p Addons
    cp /path/to/builds/Registry.dll Addons/

Or send them straight there when building:

    bash tools/build.sh --out ~/.config/unity3d/StealthDragons/StealthDragons/Addons all

Restart. The banner lists what loaded, and setup then offers to use it. Delete a
`.dll` to uninstall it, or start with `-noaddons` to load none.

## Licence

Copyright (C) 2026 Martin Husar

This program is free software: you can redistribute it and/or modify it under
the terms of the GNU Affero General Public License as published by the Free
Software Foundation, either version 3 of the Licence, or (at your option) any
later version. See `LICENSE` for the full text.

The author provides this software only and does not operate any Dragonator
server, betting service or swap service.

