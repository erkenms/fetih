# Patch 5619 Protocol Notes

How each part of the 5619 protocol in Comet was established. Every packet layout in the
code is marked the same way:

* **5619: from client binary**: read from the 5619 `Conquer.exe` (static disassembly
  of the code that builds or reads the message).
* **5619: from reference**: taken from an older open-source 5619 server (CSV3). That
  server was later updated for newer patches, so its layouts were checked against the
  client where possible.
* **5619: confirmed in VM**: seen working with the real client.

The first VM test (log in, create a Pirate, reach the map, log in again) confirmed
everything below except MsgEncryptCode's role, MsgItem, and the MsgUserInfo fields
marked as guesses.

## Account server

| Message | Type | Source | Layout |
|---|---|---|---|
| MsgEncryptCode | 1059 | reference | Seed at 4. Still sent, unused by the SRP6 login. |
| MsgAccountSRP6 | 1124 | confirmed in VM | 240 bytes. Account name at 8 (128), realm at 136 (16). No password. |
| MsgLoginChallengeS | 1213 | confirmed in VM | 520 bytes. Four fields, each a length byte and a 128 byte slot: B at 4, g at 133, N at 262, salt at 391. The client reads only B and the salt. |
| MsgLoginProofC | 1214 | confirmed in VM | 262 bytes. A at 4, M1 at 133, same field format. |
| MsgConnectEx | 1055 | confirmed in VM | Accept, 88 bytes: token low half at 4 (zero means reject), high half at 8, port at 12, IP at 20 (32), second port at 52, second IP at 56. The client pings both addresses and uses the faster. Reject: 0 at 4, code at 8. |

The account socket cipher is unchanged (TQCipher). There is no server proof (M2): after
MsgLoginProofC the client waits for MsgConnectEx.

### SRP6

The client's scheme is the World of Warcraft style SRP6 with TQ's password step. See
`Srp6.cs` for the formulas and `tests/Comet.Network.Tests/Security/Vectors/srp6_client.py`
for an independent model of the client used to make the test vectors.

* N = `894B645E89E1535BBDAD5B8B290650530801B18EBFBF5E8FAB3C82872A3E9BB7`, g = 7, k = 3.
* x = SHA1(salt || 00000000 || MD5(password || `A3 AC A1 A3` "fdjf,jkgfkl")). The
  account name is not part of x.
* Numbers are little-endian byte arrays without padding, both in hashes and on the wire.
* The client reads 32 bytes of S even when S is shorter, so about 1 login in 256 fails
  with the right password. The server logs this at debug level.

## Game server

| Part | Source | Notes |
|---|---|---|
| Cipher | confirmed in VM | CAST5 in CFB64 mode with a zero IV in both directions. Starting key `BC234xs45nme7HU9`. The current reference uses `C238xs65pjy7HU9Q`, which belongs to a later patch. |
| Key after DH | confirmed in VM | s1 = hex MD5 of the shared secret up to its first zero byte; key = s1 + hex MD5(s1 + s1). The client passes all 64 characters to CAST_set_key, which uses 16. IVs reset to zero. |
| Handshake | confirmed in VM | Same layout as 5187, with zero IVs. |
| Footer | client binary | `TQServer` / `TQClient`, as before. |

| Message | Type | Source | Layout |
|---|---|---|---|
| MsgConnect | 1052 | confirmed in VM | 28 bytes, same offsets as 5187: the two MsgConnectEx halves at 4 and 8. After creating a character the client sends it again on the same connection. |
| MsgRegister | 1001 | confirmed in VM | 124 bytes: zero at 4, name at 24, body at 72, class at 74, the MsgConnect token at 76, MAC text at 80. |
| MsgTalk | 1004 | confirmed in VM | Same as 5187; strings at 24. |
| MsgUserInfo | 1006 | confirmed in VM, except guesses | Mesh at 10 (a ushort was added at 8), stats from 52, level 68, class 69, strings at 110. The positions of previous class (70), ancestor class (71) and the VIP level are guesses or left out. |
| MsgAction | 10010 | confirmed in VM | 38 bytes: the 5187 layout plus a ushort at 36. Login actions are 74 to 78, 97 and 132 (LoginComplete), as in 5187 (from client binary). The client also sends action 251 while logging in; its purpose isn't known. |
| MsgItem | 1009 | client binary | 88 bytes; first fields as in 5187. |

New classes in the client data: Monk (60) and Pirate (70). Creating a Pirate was
confirmed in VM.
