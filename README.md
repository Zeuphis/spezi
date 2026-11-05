# Spezi

Spezi is a lightweight Windows app for saving gameplay clips and recording full sessions. Everything stays on your PC: no account, no cloud, and the core features work offline.

Video is H.264 with one AAC audio track. Spezi prefers a hardware encoder (NVIDIA, AMD, then Intel); software encoding is available only if you turn it on, because it uses much more CPU.

## Features

- **Replay buffer** – keeps the last 15 seconds to 5 minutes (15-second steps) as already compressed video in memory. While replay is on, the Recording page simply shows *Last X ready to save*. Press the clip hotkey to write that window to disk; replay keeps running.
- **Recording** – start and stop a continuous session with a hotkey or the Record button, with a live timer. Replay and a session share one capture and one encoder.
- **Automatic replay** – optionally starts replay as soon as Spezi launches (e.g. with Windows), with or without a game, and keeps it running until you turn it off.
- **Gallery** – the latest clip is shown large at the top, game filter chips (*All* plus one per game with counts), a 3-column grid with time, game name and duration, and pages of 12 clips. Search by filename or game, sort newest/oldest. Click a tile to play it in your default video player; right-click for Open, Rename, Open folder and Delete.
- **Audio** – all system audio or only selected apps (e.g. the game and Discord; app-only capture needs Windows build 20348+), plus an optional microphone. Per-source volume and level meters. The microphone is mono by default (centered in both channels); turn **Mono** off to keep its channels as captured.
- **Game folders** – clips are sorted into one folder per detected game; clips without a game go into *General*.
- **Tray** – closing the window keeps Spezi running in the system tray (Open Spezi / Exit). Optional start with Windows.
- **Updates** – an optional check for a new GitHub release once per start, and a *Check for updates* link at the bottom of Settings. Updates are downloaded over HTTPS and run only after the SHA-256 checksum matches.
- **Safe recording** – warns below 8 GB free space and refuses to start below 1 GB. Sessions are written in segments, so after a crash the next launch can recover them.

### Resource efficiency

Spezi is built to stay out of the way while you play:

- No live preview.
- Thumbnails are decoded at display size, cached on disk (`%LocalAppData%\Spezi\thumbs`) and only loaded for the visible gallery page.
- Game detection (for clip names and folders) reacts to foreground-window changes and only checks while replay or a recording runs.
- Timers and animations pause while their page is hidden, the window is minimized or Spezi is in the tray; memory is trimmed once when hidden to the tray and nothing is recording.
- Hardware encoder detection runs in a short-lived helper process, so GPU driver threads don't stay loaded.

## Download and install

Download `Spezi-Setup.exe` from the [latest release](https://github.com/Zeuphis/spezi/releases/latest).

- Windows 10 (build 19041+) or Windows 11, x64. The .NET runtime is included; no administrator rights needed.
- Installs per user (default `%LocalAppData%\Programs\Spezi`), with a Start menu entry and an optional desktop shortcut.
- To update an existing installation you can also use `Spezi-Updater.exe` from the release. Each file has a `.sha256` checksum next to it.
- Uninstall via Windows Settings → Installed apps → Spezi. Clips and settings are kept.

The installer is not signed with a code-signing certificate, so Windows SmartScreen may ask for confirmation.

## Usage

1. Open Spezi and turn on **Replay** on the Recording page (or enable automatic replay in Settings).
2. Play. Press **Ctrl+Shift+F8** to save the last moments as a clip.
3. Press **Ctrl+Shift+F9** to start or stop a full recording.
4. Open the **Gallery** to watch, rename or delete clips.

Hotkeys can be changed in Settings (Page Up / Page Down are also supported). The two hotkeys must be different.

## Settings

Changes are saved automatically to `%AppData%\Spezi\settings.json`.

- **Appearance** – accent color.
- **Replay** – buffer length.
- **Storage & source** – clip folder, screen or window to capture.
- **Video & quality** – Efficient (720p · 30 FPS), Balanced (1080p · 60 FPS) or Quality (up to 1440p · 60 FPS), resolution, frame rate, cursor capture, optional software encoding.
- **Hotkeys** – save clip and start/stop recording.
- **Audio & microphone** – system or selected-app audio, devices, volumes, microphone and Mono.
- **Notifications** – toasts for starts, stops and saved clips, with display duration.
- **Background** – start with Windows, start replay automatically, check for updates on startup.

The log file is `%LocalAppData%\Spezi\spezi.log`.

## Build from source

Requires the .NET 9 SDK and 64-bit Windows 10 or 11 (WPF).

```text
dotnet build Spezi.sln -c Release
dotnet test tests/Spezi.Tests
dotnet publish src/Spezi.App/Spezi.App.csproj -c Release -r win-x64 --self-contained true -o dist
```

- `dotnet run --project tests/Spezi.RecordingTests.csproj -c Release` runs the encoder/audio regression suite on a real PC.
- App self-tests (`--self-test`) are compiled only in Debug or with `-p:SpeziSelfTests=true`.
- See [installer/README.md](installer/README.md) for building the installer. The version is defined once in `Directory.Build.props`; changes are listed in [CHANGELOG.md](CHANGELOG.md).
