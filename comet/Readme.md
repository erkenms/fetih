# Comet

```bash
podman compose up -d
cd src/Comet.Account
dotnet run -- create-account me@example.com secret12
dotnet run
cd ../Comet.Game && dotnet run
```
