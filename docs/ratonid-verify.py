import hashlib, hmac, sys

ALPHABET = "0123456789ABCDEFGHJKMNPQRSTVWXYZ"
WORD = "ratonfun"


def encode(data, chars):
    out, buffer, bits = [], 0, 0
    for b in data:
        buffer = (buffer << 8) | b
        bits += 8
        while bits >= 5 and len(out) < chars:
            out.append(ALPHABET[(buffer >> (bits - 5)) & 31])
            bits -= 5
        buffer &= (1 << bits) - 1
    if bits > 0 and len(out) < chars:
        out.append(ALPHABET[(buffer << (5 - bits)) & 31])
    return "".join(out)


def verify(raton_id):
    parts = raton_id.strip().split("-")
    if len(parts) != 9 or parts[0] != "RTN1":
        return False
    data = ""
    for i, part in enumerate(parts[1:]):
        if len(part) != 6 or part[0] != WORD[i] or any(c not in ALPHABET for c in part[1:]):
            return False
        data += part[1:]
    random_part, tag = data[:26], data[26:]
    key = hashlib.sha256(b"raton.fun/sillet/id/v1").digest()
    mac = hmac.new(key, (WORD + "|1|" + random_part).encode(), hashlib.sha256).digest()
    return encode(mac[:9], 14) == tag


if __name__ == "__main__":
    for value in sys.argv[1:]:
        print(value, "REAL" if verify(value) else "FALSO")
