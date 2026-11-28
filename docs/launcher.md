# Launcher

Players download only `Fetih.exe` (`/indir` → `/download/Fetih.exe`). It installs the game,
keeps it in line with the server's manifest and starts it.

- `src/Fetih.Patch`: manifest, publishing, the sync plan and downloads, `GameSetUp.ini`. Tested
  on any OS.
- `src/Fetih.Launcher`: the Windows exe. Win32 by P/Invoke, self-contained, trimmed, one file;
  builds on Linux and macOS. The window is the old Play screen (`Play/Main.bmp`, the pirate
  `MainS.bmp`, the Oyna button, the game's own minimize and close); the art comes from the old
  Turkish client (`--art`, default `_CLIENT/fo_3105`), not from git. DPI-aware: it scales the art
  itself.
- `tools/build.cs client` builds it into the client with `launcher.json` (`--url`, the
  production site). `patch` puts `Fetih.exe` in the manifest's
  `launcher` entry and copies it to `download/`.

## What it does

- First start, from anywhere: installs to `%LOCALAPPDATA%\Programs\Fetih`, adds desktop and
  Start menu shortcuts and an uninstall entry under HKCU, starts from there.
- Every start: `{url}/patch/manifest.json`, then self-update when its own entry
  differs (`Fetih.new.exe --replace <pid>`), then the files: sizes and times against
  `.launcher/state.json`, a hash only on a mismatch (all with "Dosyaları onar" or `--repair`).
  16 objects at a time, each streamed into a temp file, verified, renamed; a failure starts that
  file over (no resume: the biggest, `data.wdf`, is 176 MB compressed).
  Player-owned files (`PlayerOwnedFiles`) are fetched only when missing; files go only when the
  previous manifest had them. Then Oyna starts the manifest's `start` (`Conquer.exe`).
- First sync: `ini/GameSetUp.ini` gets high quality, as `play.exe` set it, and the screen's
    size as `Conquer.exe` sees it, full screen. After that only Ayarlar writes `[ScreenMode]`
  (record 4: [client-notes.md](client-notes.md)).
- News on the parchment: `/haberler?handler=launcher`.
- `Conquer.exe`'s own restart runs `Fetih.exe` (`client/patches.json`).

## Testing from a Windows VM

Build the client pointing at the server or a local test build, then open `/indir` in the VM:

```bash
dotnet run tools/build.cs all --ip <ip> --url https://<domain>
```
