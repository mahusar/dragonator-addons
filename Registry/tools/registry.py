#!/usr/bin/env python3
import base64, hashlib, json, os, subprocess, sys, secrets

D = os.environ.get("STEALTHD", "StealthCoind")
MAGIC = "58535444"
VER = 1


def rpc(*args):
    out = subprocess.run([D] + [str(a) for a in args], capture_output=True, text=True)
    if out.returncode != 0:
        raise RuntimeError(out.stderr.strip() or out.stdout.strip())
    return out.stdout


def rpcjson(*args):
    return json.loads(rpc(*args))


def onion_to_pub(onion):
    onion = onion.strip().lower().replace(".onion", "")
    return base64.b32decode(onion.upper())[:32]


def pub_to_onion(pub):
    chk = hashlib.sha3_256(b".onion checksum" + pub + bytes([3])).digest()[:2]
    return base64.b32encode(pub + chk + bytes([3])).decode().lower() + ".onion"


def encode(onion, port, flags=0):
    pub = onion_to_pub(onion)
    return MAGIC + "%02x" % VER + pub.hex() + "%04x" % port + "%02x" % flags


def decode(rec):
    raw = bytes.fromhex(rec)
    if len(raw) != 40 or raw[:4] != b"XSTD":
        return None
    return {
        "version": raw[4],
        "onion": pub_to_onion(raw[5:37]),
        "port": int.from_bytes(raw[37:39], "big"),
        "flags": raw[39],
    }


def records_of(tx):
    found = []
    for out in tx.get("vout", []):
        spk = out.get("scriptPubKey", {})
        hexs = spk.get("hex", "")
        if hexs.startswith("6a"):
            payload = hexs[4:] if hexs[2:4] == "28" else hexs[2:]
            if payload.startswith(MAGIC):
                r = decode(payload[:80])
                if r:
                    found.append(r)
    return found


def txids_in(blob):
    seen, out = set(), []

    def walk(node, key=None):
        if isinstance(node, dict):
            for k, v in node.items():
                walk(v, k)
        elif isinstance(node, list):
            for v in node:
                walk(v, key)
        elif isinstance(node, str):
            if len(node) == 64 and all(c in "0123456789abcdef" for c in node.lower()):
                if key in (None, "txid", "tx", "hash") and node not in seen:
                    seen.add(node)
                    out.append(node)

    walk(blob)
    return out


def cmd_list(addr, pages=10, per=100):
    listings = {}
    for page in range(1, pages + 1):
        blob = None
        for attempt in (("true",), ()):
            try:
                blob = rpcjson("getaddresstxspg", addr, page, per, *attempt)
                break
            except RuntimeError as e:
                last = e
        if blob is None:
            print("getaddresstxspg failed:", last, file=sys.stderr)
            return

        ids = txids_in(blob)
        if not ids:
            break

        for txid in ids:
            try:
                tx = rpcjson("getrawtransaction", txid, 1)
            except RuntimeError:
                continue
            for r in records_of(tx):
                listings.setdefault(r["onion"], dict(r, txid=txid))

        if len(ids) < per:
            break

    print("%d listing(s) on %s\n" % (len(listings), addr))
    for r in listings.values():
        print("  %s:%d  v%d flags=%d  %s" % (r["onion"], r["port"], r["version"], r["flags"], r["txid"][:16]))


def usage():
    print("""registry.py — read and write Dragonator server listings

  list    <registry-address>        every listing found on that address
  encode  <onion> [port]            record hex
  decode  <80-hex>                  record back to an onion
  write   <registry-address> <onion> [port] [amount]
                                    print the sendtoaddress command (does not run it)
  gen                               a random well-formed test onion""")


if __name__ == "__main__":
    a = sys.argv[1:]
    if not a:
        usage()
    elif a[0] == "list":
        cmd_list(a[1])
    elif a[0] == "encode":
        print(encode(a[1], int(a[2]) if len(a) > 2 else 5555))
    elif a[0] == "decode":
        print(decode(a[1]))
    elif a[0] == "write":
        rec = encode(a[2], int(a[3]) if len(a) > 3 else 5555)
        amt = a[4] if len(a) > 4 else "0.01"
        print('%s sendtoaddress %s %s "" "" true \'["%s"]\'' % (D, a[1], amt, rec))
    elif a[0] == "gen":
        print(pub_to_onion(secrets.token_bytes(32)))
    else:
        usage()
