#!/usr/bin/env python3
import hashlib, json, os, subprocess, sys

D = os.environ.get("STEALTHD", "StealthCoind")
MAGIC = "58535457"
VER = 1
PREFIX = "OP_RETURN "


def rpc(*args):
    out = subprocess.run([D] + [str(a) for a in args], capture_output=True, text=True)
    if out.returncode != 0:
        raise RuntimeError(out.stderr.strip() or out.stdout.strip())
    return out.stdout


def rpcjson(*args):
    return json.loads(rpc(*args))


FLAG_CONTESTED = 0x01
FLAG_BOT = 0x02


def describe(flags):
    clean = (flags & FLAG_CONTESTED) and not (flags & FLAG_BOT)
    parts = ["every match human against human" if clean
             else "not all matches are contested"]
    if flags & FLAG_BOT:
        parts.append("at least one has a bot")
    return ", ".join(parts)


def decode(rec):
    raw = bytes.fromhex(rec)
    if len(raw) != 40 or raw[:4] != b"XSTW":
        return None
    flags = raw[39]
    return {
        "version": raw[4],
        "root": raw[5:37].hex(),
        "count": int.from_bytes(raw[37:39], "big"),
        "flags": flags,
        "contested": bool(flags & FLAG_CONTESTED),
        "bot": bool(flags & FLAG_BOT),
        "means": describe(flags),
    }


def pushes(tx):
    found = []
    for out in tx.get("vout", []):
        asm = out.get("scriptPubKey", {}).get("asm", "")
        if not asm.startswith(PREFIX):
            continue
        for token in asm[len(PREFIX):].split(" "):
            if token:
                found.append(token)
    return found


def anchors_in(txid):
    tx = rpcjson("getrawtransaction", txid, 1)
    out = []
    for push in pushes(tx):
        rec = decode(push)
        if rec:
            rec["confirmations"] = tx.get("confirmations", 0)
            rec["blocktime"] = tx.get("blocktime", 0)
            out.append(rec)
    return out


def leaf(digest_hex):
    return hashlib.sha256(b"\x00" + bytes.fromhex(digest_hex)).digest()


def node(left, right):
    return hashlib.sha256(b"\x01" + left + right).digest()


def walk(digest_hex, proof):
    cur = leaf(digest_hex)
    if not proof:
        return cur.hex()
    for step in proof.split(";"):
        if not step:
            continue
        side, sib = step.split(":", 1)
        sib = bytes.fromhex(sib)
        cur = node(sib, cur) if side == "l" else node(cur, sib)
    return cur.hex()


def read_receipt(path):
    digest = os.path.basename(path).rsplit(".", 1)[0].lower()
    txid = proof = ""
    with open(path, encoding="utf-8") as fh:
        for line in fh:
            line = line.rstrip("\n")
            if line.startswith("txid="):
                txid = line[5:].strip()
            elif line.startswith("proof="):
                proof = line[6:].strip()
    return digest, txid, proof


def cmd_verify(path):
    digest, txid, proof = read_receipt(path)
    print("receipt  %s" % digest)

    if not txid:
        print("VERDICT  not anchored - the receipt carries no txid")
        return 2

    print("txid     %s" % txid)

    found = anchors_in(txid)
    if not found:
        print("VERDICT  FAILED - that transaction carries no XSTW anchor")
        return 1

    rec = found[0]
    got = walk(digest, proof)

    print("root     %s   (on chain, %d receipt(s), %d confirmation(s))"
          % (rec["root"], rec["count"], rec["confirmations"]))
    print("batch    %s" % rec["means"])
    print("proof    %s" % (got))

    if got != rec["root"]:
        print("VERDICT  FAILED - the proof does not reconstruct the anchored root")
        return 1

    print("VERDICT  ANCHORED - this receipt is committed to by that transaction")
    return 0


def cmd_dir(folder):
    bad = 0
    names = sorted(n for n in os.listdir(folder) if n.endswith(".txt"))
    for name in names:
        digest, txid, proof = read_receipt(os.path.join(folder, name))
        if not txid:
            print("  %s  not anchored" % digest[:16])
            continue
        try:
            found = anchors_in(txid)
            ok = found and walk(digest, proof) == found[0]["root"]
        except Exception as e:
            print("  %s  error %s" % (digest[:16], e))
            bad += 1
            continue
        print("  %s  %s  %s" % (digest[:16], "ANCHORED" if ok else "FAILED  ", txid[:16]))
        if not ok:
            bad += 1
    print("\n%d receipt(s), %d problem(s)" % (len(names), bad))
    return 1 if bad else 0


def cmd_scan(start, count):
    height = int(start)
    for i in range(int(count)):
        try:
            h = rpc("getblockhash", height + i).strip()
            block = rpcjson("getblock", h, "true")
        except Exception as e:
            print("stopped at %d: %s" % (height + i, e), file=sys.stderr)
            return 1
        for txid in block.get("tx", []):
            try:
                for rec in anchors_in(txid):
                    print("%d  %s  root %s  count %d"
                          % (height + i, txid, rec["root"], rec["count"]))
            except Exception:
                continue
    return 0


def usage():
    print("""anchor.py - read Witness anchors off the Stealth chain

  verify <receipt.txt>        check one receipt against the chain
  dir <receipts-folder>       check every receipt in a folder
  tx <txid>                   decode the anchor in a transaction
  decode <80-hex>             decode a raw record
  scan <height> <blocks>      walk blocks looking for anchors (slow)

STEALTHD overrides the daemon binary (default StealthCoind).""")
    return 2


if __name__ == "__main__":
    a = sys.argv[1:]
    if not a:
        sys.exit(usage())
    elif a[0] == "verify":
        sys.exit(cmd_verify(a[1]))
    elif a[0] == "dir":
        sys.exit(cmd_dir(a[1]))
    elif a[0] == "tx":
        for r in anchors_in(a[1]) or []:
            print(json.dumps(r, indent=2))
        sys.exit(0)
    elif a[0] == "decode":
        print(json.dumps(decode(a[1]), indent=2))
        sys.exit(0)
    elif a[0] == "scan":
        sys.exit(cmd_scan(a[1], a[2] if len(a) > 2 else 100))
    else:
        sys.exit(usage())
