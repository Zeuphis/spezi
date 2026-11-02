# Spezi

Spezi is a local Windows app for gameplay clips and full sessions. Recording stays on this PC. The core features do not need an account or an internet connection.

Clips and sessions are H.264 video with one AAC audio track. Spezi prefers a hardware encoder (NVIDIA, AMD, then Intel). A software encoder exists only if you turn it on in Settings, because it uses much more CPU.

## Build and test

- `dotnet build Spezi.sln -c Release` and `dotnet test tests/Spezi.Tests` (hardware-free unit tests, also run in CI).
- `dotnet run --project tests/Spezi.RecordingTests.csproj -c Release` runs the encoder/audio regression suite on a real PC.
- App self-tests (`--self-test` etc.) are compiled only in Debug or with `-p:SpeziSelfTests=true`.

## Install

1. Run `installer/Spezi-Setup.exe` to install Spezi for your Windows user account. Administrator privileges and a separate .NET installation are not required.
2. Choose an installation folder and optionally create a desktop shortcut. Launch Spezi from the Start menu or the shortcut.
3. To uninstall, use Windows Settings → Installed apps → Spezi. Clips and preferences are preserved.

The `dist` folder remains a self-contained portable build: copy it anywhere and run `Spezi.exe`. See [installer/README.md](installer/README.md) for rebuilding the installer.

## Update to 1.1.1

Finish any recording and exit Spezi from its system tray menu. Run `updater/Spezi-Updater.exe`. It detects an installed Spezi folder automatically; for a portable copy, select the existing folder containing `Spezi.exe`. The update preserves clips and preferences, runs offline and refuses to replace a newer version. Use `Spezi-Setup.exe` for a new installation. See [updater/README.md](updater/README.md) and [the changelog](CHANGELOG.md).

Spezi opens directly on the Recording page, including on first launch. Configure the save folder, quality, hotkeys and audio in **Settings**. There is no separate recording setup wizard during installation or first launch. Existing preferences are preserved. The app and installer use English. Closing the window leaves Spezi running in the system tray. Use the tray menu to exit.

To start Spezi with Windows, enable that option in Settings. The startup entry launches `Spezi.exe --background`.

## Record

- **Replay** keeps the last 15 seconds to 5 minutes in a memory buffer of already compressed video. Choose the duration with the slider in 15-second steps, exclusively in Settings. A clear scale below the track uses larger full-minute markers. The selected point and exact duration badge follow the app accent color. The clip hotkey writes that window to disk and the background recording continues. Videos start on a keyframe, so a saved clip can be slightly shorter than the selected window.
- **Record** starts a continuous session. Press the same hotkey again to finish and save it.
- Replay and a session share one capture and one encoder.
- Automatic replay starts when a game is detected. Once enabled, replay continues until you turn it off or exit Spezi. Switching to the desktop, another app, or closing the game does not clear the buffer. Screen capture includes the desktop and other apps on the selected display; an explicitly selected window remains a window-only source.
- Pick a screen or a window in Settings. A running recording keeps its source until you stop it.
- There is no live preview. That is intentional, so the app does less work while you play.

Default hotkeys:

- Save clip: Ctrl+Shift+F8
- Start or stop a session: Ctrl+Shift+F9

Page Up and Page Down can also be assigned, including without modifiers. Spezi pauses its existing shortcuts while a hotkey field is being edited. For a Page key already registered by another app, an alternative listener handles the selected shortcut. Other conflicting combinations are reported. The two hotkeys cannot be the same combination.

## Audio

Choose **All system audio** to record selected playback devices, including game and voice apps that use those devices. Choose **Selected apps only** to record only the checked applications: the current game, Discord, TeamSpeak, or other running apps. Windows process loopback records each selected process tree across playback devices; selecting a parent and its child does not duplicate their audio. App-specific capture requires Windows build 20348 or newer (Windows 11 on normal desktop installations). Unsupported systems continue to offer whole-device system audio; app-only recording never falls back to recording other applications.

You can mix multiple selected applications and adjust their recording volume independently. Selections are saved by app identity, so a checked app is automatically attached when it starts or restarts. The current game follows the selected game window, or a detected foreground game when recording a display, and stays selected when you briefly switch to Spezi. A microphone remains optional and independent. Audio sources apply to the next recording; recording volume changes apply immediately. The audio test updates its sources when you change selections. Spezi mixes recorded sources into one AAC track.

Use the level meters and **Test audio** in Settings to check the mix. If a device is unplugged, Spezi reports it and keeps recording the devices that remain. Changing the Windows default device does not drop a device you already selected.

Microphone input is downmixed to mono and placed equally in the left and right channels, including interfaces that expose a single connected microphone as one side of a stereo device. App and system sources retain their stereo channels. Adjust microphone volume in Settings if needed.

## Gallery and settings

The gallery uses video cards with large 16:9 previews, game badges, duration overlays, readable titles, dates and file sizes. A compact search field filters by filename or game, and a small sort control switches between newest and oldest. The actions menu on each card contains playback, rename, open folder and delete. Automatically generated filenames get readable display titles; custom names and files on disk are preserved. Playback from the gallery and recent clips opens the Windows default app for `.mp4`, such as Media Player or VLC. Spezi has no built-in video player. Only visible entries load compact thumbnails, cached under `%LocalAppData%\Spezi\thumbs`. A bounded background decoder also works during recording. Clips are organized in game folders.

Settings cover the folder, replay length, hotkeys, resolution, frame rate, bitrate, the three quality profiles (Efficient, Balanced, High quality), audio devices and individual applications, startup, and automatic replay when a fullscreen or borderless game is in front. Settings are stored in `%AppData%\Spezi\settings.json`. Existing replay preferences in minutes migrate to seconds automatically. Clips without a detected game go into **General**. An existing **Allgemein** folder is reused and shown as General in the gallery. Existing filenames are not renamed; new sessions and recoveries use Recording and Recovered in their filenames.

Spezi warns when the destination drive has less than 8 GB free and refuses to start a recording below 1 GB. A session is stored in short segments first, then joined into one file, so a crash keeps everything except the segment that was still open. The next launch offers to recover those parts.

## Build from source

Requires the .NET 9 SDK and a 64-bit version of Windows 10 or 11.

```text
dotnet build Spezi.sln -c Release
dotnet publish src/Spezi.App/Spezi.App.csproj -c Release -r win-x64 --self-contained true -o dist
```

`Spezi.exe --self-test` records a few seconds from the primary display and writes a short report to `%TEMP%\SpeziSelfTest\result.txt`.

The log file is `%LocalAppData%\Spezi\spezi.log`.
