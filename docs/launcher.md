# Launcher

Players download only `Fetih.exe` (`/indir` → `/download/Fetih.exe`). It installs the game,
keeps it in line with the server's manifest and starts it.

- `src/Fetih.Patch`: manifest, publishing, the sync plan and downloads, `GameSetUp.ini`. Tested
  on any OS.
- `src/Fetih.Launcher`: the Windows exe. Win32 by P/Invoke, self-contained, trimmed, one file;
  builds on Linux and macOS. The window is the old Play screen; its art comes from the old
  Turkish client (`--art`, default `_CLIENT/tr/app`), not from git.
- `tools/build.cs client` builds it into the client with `launcher.json` (`--url`, the
  production site for both envs; channel `prod`). `patch` puts `Fetih.exe` in the manifest's
  `launcher` entry and copies it to `download/`.

## What it does

- First start, from anywhere: installs to `%LOCALAPPDATA%\Programs\Fetih`, adds desktop and
  Start menu shortcuts and an uninstall entry under HKCU, starts from there.
- Every start: `{url}/patch/<channel>/manifest.json`, then self-update when its own entry
  differs (`Fetih.new.exe --replace <pid>`), then the files: sizes and times against
  `.launcher/state.json`, a hash only on a mismatch (all with "Dosyaları onar" or `--repair`).
  Player-owned files (`PlayerOwnedFiles`) are fetched only when missing; files go only when the
  previous manifest had them. Then Oyna starts the manifest's `start` (`Conquer.exe`).
- First sync: `ini/GameSetUp.ini` gets high quality, as `play.exe` set it, and the screen's
  size full screen. After that only Ayarlar writes `[ScreenMode]`.
- `--channel test` (staff: a shortcut to `Fetih.exe --channel test`) syncs the same folder to
  `/patch/test/` for that run; the next normal start syncs back.
- News on the parchment: `/haberler?handler=launcher`.
- `Conquer.exe`'s own restart runs `Fetih.exe` (`client/patches.json`).

## Testing from a Windows VM

Everything runs on the Mac; the VM reaches it at `172.16.60.1`. The dev proxy
(`deploy/Caddyfile.dev`, port 8080) serves the web, `/patch/` and `/download/Fetih.exe`.

```bash
dotnet run tools/build.cs all --ip 172.16.60.1 --url http://172.16.60.1:8080
podman compose -f deploy/compose.yaml up -d dev-proxy
cd src/Fetih.Web && dotnet run                                  # and Account, Game as usual
```

In the VM open `http://172.16.60.1:8080/indir`.

- A change on the server: edit a file in `_CLIENT/fetih`, then `dotnet run tools/build.cs patch`.
- The test channel: build a second client and patch, then point the proxy at it:
  `--out _CLIENT/fetih-test --patch _CLIENT/patch-test` with the same `--ip` and `--url`, and
  `PATCH_TEST_DIR=../_CLIENT/patch-test` in `deploy/.env`.
