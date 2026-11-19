# Patch 5619 protocol

Packet layouts are in the comments of each packet class. This page covers the flow and
what isn't obvious from any one file.

## Where facts come from

Every layout in code says how it was established:

- **from client binary**: read from the 5619 `Conquer.exe` by disassembly.
- **seen / confirmed in VM**: observed with the real client.

## Login

Account server (TQCipher):

1. Server sends MsgEncryptCode (a seed the 5619 client no longer uses).
2. Client sends MsgAccountSRP6 (account name and realm, no password).
3. Server sends MsgLoginChallengeS; client answers with MsgLoginProofC.
4. Server checks the proof against the stored verifier, gets a one-time token from the
   game server over RPC, and sends MsgConnectEx with the token and the game server's
   address. There is no server proof.

Game server (CAST5 CFB64):

1. DH key exchange (MsgHandshake), then the key switches to one made from the shared
   secret.
2. Client sends MsgConnect with the token. No character: server says `NEW_ROLE`,
   client sends MsgRegister, then MsgConnect again on the same connection.
3. Server sends `ANSWER_OK` and MsgUserInfo. Client sends MsgAction steps 74 (position),
   75–78, 96, 97, each after the server answers the previous one.
4. Client sends MsgAction 251 (LoginComplete, not answered). The character is put on
   the map and exchanged with everyone in view.

## Traps

- The SRP6 password step is TQ's own: x = SHA1(salt ‖ 0000 ‖ MD5(password ‖ fixed
  suffix)); numbers are little-endian. The client hashes 32 bytes of S even when S is
  shorter, so about 1 real login in 256 fails with the right password.
- The CAST5 starting key is `BC234xs45nme7HU9`; CSV3's current key belongs to a later
  patch.
- A DMap's base grid blocks most raised ground. Bridges, platforms and stairs come from
  terrain objects placing scene files over it, counted backwards from the object's
  cell. Without them a quarter of cells real players jump to look blocked.
- GameMap.dat lists map 1067 twice; the first entry wins.
- MsgPlayer: a byte at 225 must be 0 or the client ignores the position.