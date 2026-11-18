# Comet

You can find setup instructions in the [main branch's readme](https://gitlab.com/spirited/comet/-/blob/main/Readme.md).

This document outlines key features of the project and gives an overview of the solution.

## Solution Overview

Comet is made up of multiple server, library, and test projects.

Server projects:

* [Comet.Account](src/Comet.Account/): Authenticates and redirects players to a game server.
* [Comet.Game](src/Comet.Game/): Serves authenticated players in a game world (multiple can be set up).

Library projects:

* [Comet.Client](src/Comet.Client/): A headless 5619 game client for integration tests and bots.
* [Comet.Core](src/Comet.Core/): Contains language or system additions that are not game centric.
* [Comet.Network](src/Comet.Network/): Contains base networking classes for creating networked actors.
* [Comet.Shared](src/Comet.Shared/): Contains shared message classes for RPC between the servers.

Test projects:

* [Comet.Account.Tests](tests/Comet.Account.Tests/): Unit tests for validating basic account server functionality.
* [Comet.Core.Tests](tests/Comet.Core.Tests/): Unit tests for validating core functionality.
* [Comet.Game.Tests](tests/Comet.Game.Tests/): Unit tests for game server packets and rules.
* [Comet.Integration.Tests](tests/Comet.Integration.Tests/): Runs both servers against throwaway databases and plays through them with Comet.Client (see [doc/Testing.md](doc/Testing.md)).
* [Comet.Network.Tests](tests/Comet.Network.Tests/): Unit tests for validating network and base server functionality.

## Container Files

In the top directory, you can find files for containers:

* [compose.yaml](compose.yaml): Runs local MariaDB container.

## Map Data

The game server reads map data from a copy of the 5619 client: `ini/GameMap.dat` for
the list of maps, and the `.7z` (or `.DMap`) files under `map/map` for each map's cells.
Set `Maps:ClientDirectory` in `Comet.Game.config` to that folder; the default,
`../../../_CLIENT/5619`, is a `_CLIENT` folder next to the `comet` folder (it isn't
committed). A map is loaded the first time it's used, then kept in memory.
