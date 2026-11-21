#!/usr/bin/env python3
"""Model of the 5619 client's SRP6 login, written from its disassembly.

Generates the known vectors in Srp6Tests.cs independently of the C# server code.
Run with: python3 -I srp6_client.py

Big numbers follow the client's BigNumber class: byte arrays are little-endian and
exactly as long as the number (no padding), and bytes are read back as little-endian.
"""
import hashlib

N = int("894B645E89E1535BBDAD5B8B290650530801B18EBFBF5E8FAB3C82872A3E9BB7", 16)
G = 7
K_MULT = 3
PASSWORD_SALT = b"\xa3\xac\xa1\xa3fdjf,jkgfkl"   # "%s£¬¡£fdjf,jkgfkl" in GBK


def le(n):
    """BigNumber -> bytes: minimal little-endian."""
    return n.to_bytes((n.bit_length() + 7) // 8, "little")


def bn(b):
    """bytes -> BigNumber: little-endian."""
    return int.from_bytes(b, "little")


def sha1(*parts):
    h = hashlib.sha1()
    for p in parts:
        h.update(p)
    return h.digest()


def password_block(password):
    """MD5(password + salt) as hex, loaded as a big number, written little-endian into a
    zeroed 20 byte buffer, then the whole buffer reversed."""
    digest_hex = hashlib.md5(password + PASSWORD_SALT).hexdigest()
    buf = bytearray(20)
    data = le(int(digest_hex, 16))
    buf[:len(data)] = data
    return bytes(reversed(buf))


def client_proof(username, password, salt_le, b_le, a):
    s = bn(salt_le)
    B = bn(b_le)
    x = bn(sha1(le(s), password_block(password)))
    A = pow(G, a, N)
    u = bn(sha1(le(A), le(B)))
    S = pow((B - K_MULT * pow(G, x, N)) % N, a + u * x, N)

    # The client copies 32 bytes from S's array (short arrays read past the end; the
    # vectors here use a 32 byte S).
    t = le(S)
    assert len(t) == 32, "pick another a: S is shorter than 32 bytes"
    even, odd = sha1(t[0::2]), sha1(t[1::2])
    key = bytes(v for pair in zip(even, odd) for v in pair)

    hn, hg = sha1(le(N)), sha1(le(G))
    xor = bn(bytes(p ^ q for p, q in zip(hn, hg)))
    m1 = bn(sha1(le(xor), le(bn(sha1(username))), le(s), le(A), le(B), le(bn(key))))
    return le(A), le(m1), key


def server_challenge(password, salt_le, b):
    x = bn(sha1(le(bn(salt_le)), password_block(password)))
    v = pow(G, x, N)
    return le(v), le((K_MULT * v + pow(G, b, N)) % N)


def session_secret(password, salt_le, b_le, a):
    """S as the client computes it, to find inputs whose S is shorter than 32 bytes."""
    x = bn(sha1(le(bn(salt_le)), password_block(password)))
    A = pow(G, a, N)
    u = bn(sha1(le(A), b_le))
    return le(A), le(pow((bn(b_le) - K_MULT * pow(G, x, N)) % N, a + u * x, N))


def print_vectors(title, username, password, salt, b, a):
    v, B = server_challenge(password, salt, b)
    A, M1, key = client_proof(username, password, salt, B, a)
    print(title)
    print("  PasswordBlock =", password_block(password).hex().upper())
    print("  Salt          =", salt.hex().upper())
    print("  Verifier      =", v.hex().upper())
    print("  ServerPrivate =", format(b, "X"))
    print("  B             =", B.hex().upper())
    print("  A             =", A.hex().upper())
    print("  M1            =", M1.hex().upper())
    print("  K             =", key.hex().upper())
    return B


if __name__ == "__main__":
    username, password = b"tester", b"secret123"
    print_vectors("Srp6Tests", username, password, bytes(range(0x41, 0x41 + 32)),
                  int("1f2e3d4c5b6a79880123456789abcdef00112233445566778899aabbccddeeff", 16),
                  int("0badc0ffee0ddf00d5eed5eed5eed5eed5eed5eed5eed5eed5eed5eed5eed5e", 16))

    # The salt's, B's and K's top bytes are zero, so each is hashed one byte short
    salt = bytes(range(0x41, 0x41 + 31)) + b"\0"
    B = print_vectors("Srp6Tests, leading zeros", username, password, salt,
                      int("1f2e3d4c5b6a79880123456789abcdef00112233445566778899aabbccddf009", 16),
                      int("0badc0ffee0ddf00d5eed5eed5eed5eed5eed5eed5eed5eed5eed5eed5eef8c", 16))

    A, S = session_secret(password, salt, B,
                          int("0badc0ffee0ddf00d5eed5eed5eed5eed5eed5eed5eed5eed5eed5eed5eedec", 16))
    print("Srp6Tests, short session secret (same salt and b)")
    print("  A             =", A.hex().upper())
    print("  S             =", S.hex().upper(), f"({len(S)} bytes)")
