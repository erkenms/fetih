# 5619 client captures

Packet traces recorded from the real 5619 client in a VM (`packet.in` and `packet.out`
events, one JSON object per line). The replay tests in Comet.Game.Tests and
Comet.Account.Tests check every packet the client sent here against the servers'
handlers. See "Captures of the real client" in `doc/Testing.md` for how to record one.

Use test accounts only: captures contain account names.
