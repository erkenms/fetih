# Login Sequence

Conquer Online's login sequence is split between two stages: account and game. This describes patch 5619.

## Account Authentication Stage

First, the game client connects to the Account server and the server sends a seed using a MsgEncryptCode message. Older clients used it to encrypt the password with RC5; from patch 5532 the password never leaves the client, so the seed goes unused. The client sends MsgAccountSRP6 with the account name and realm name.

The account server answers with an SRP6 challenge (MsgLoginChallengeS): the server's public key and the account's salt. The client proves it knows the password with MsgLoginProofC, which holds its public key and a proof. The server checks the proof against the verifier stored for the account; the password itself is never stored or sent. An account without a verifier (`SrpSalt` and `SrpVerifier` in the account table) can't log in. The verifier can't be computed in SQL, so passwords are set by a tool that uses `Srp6.ComputeVerifier`.

After the account server verifies the proof, it gets a single-use authentication token from the game server and sends it to the client in a MsgConnectEx message, which also contains the game server's IP address and port. The game client then disconnects and reconnects to that game server.

## Game Login Stage

On client connect to the game server, the server sends a DH key exchange request message. This message contains the server's public key and other identifiers used to set up encryption and a shared private key between the client and server. Traffic is encrypted with CAST5: first with a fixed key, then with a key made from the shared secret. After the client responds with its public key and the private key can be computed, the client then sends MsgConnect. This message contains the authentication token generated in the last stage. The game server uses this token with the player's IP address to verify the login attempt. If the login is valid, then the game server will attempt to load the player's character.

If the character doesn't exist, then a MsgTalk message will be sent containing the text "NEW_ROLE" on the Login channel. This indicates that the client should open the character creation screen. After the player selects a class, character name, and body size, the game client will send the server a MsgRegister message to register the new player. If the name of the character has been taken, then an error will be sent back using MsgTalk on the Register channel. If the character was created successfully, then the server will send MsgTalk with "ANSWER_OK" on the Register channel. The client may then send MsgConnect again on the same connection to enter the game with the new character.

If the character already exists, then "ANSWER_OK" will be sent instead on the Login channel, followed by a MsgUserInfo message containing the player's character details. The client then responds by asking for the character's map and coordinates using a MsgAction message. Once the server responds to this packet, the client will send a chain of MsgAction messages responsible for loading friends, items, PK mode, offline whispers, etc.

The details of each 5619 message, and how each was established, are in [Patch5619.md](Patch5619.md).
