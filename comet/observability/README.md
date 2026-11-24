# Logs and metrics

`podman compose -f comet/compose.yaml up -d` starts these next to MariaDB, all on 127.0.0.1:

| Service | Port | What |
|---|---|---|
| Grafana | 3000 | Dashboards; viewers need no login, `admin`/`admin` to edit |
| Loki | 3100 | Logs from `comet/src/Comet.*/logs/*.jsonl`, 30 days |
| Prometheus | 9090 | Scrapes the game's `/metrics` (`Metrics` in `Comet.Game.config`, port 9818), 30 days |
| Alloy | — | Tails the log files into Loki |

Loki labels are only `server`, `level` and `event`. Everything else (`characterId`,
`accountId`, `packetName`...) is filtered after `| json`. Packet trace lines are the
`packet.in` and `packet.out` events; leave them out with `event!~"packet\\.(in|out)"`. A
selector needs one matcher that can't match empty, so add `server=~".+"` to negative-only ones.

Links the web can build:

- Player: `http://localhost:3000/d/comet-player/player?var-characterId=1000000&var-accountId=1&from=now-24h&to=now`
  (incoming packets carry only `accountId`, so pass both)
- Packets: `http://localhost:3000/d/comet-packets/packets?var-accountId=1&var-packetName=MsgItem`
- Overview: `http://localhost:3000/d/comet-overview/overview`; a single panel embeds with
  `/d-solo/comet-overview/overview?panelId=<id>`

The dashboards are provisioned from `grafana/dashboards/*.json`. Edit them in Grafana, then
export the JSON over the file.
