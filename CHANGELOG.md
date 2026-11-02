# Changelog

All notable changes to Spezi. The version is defined once in `Directory.Build.props`.

## 1.1.2

- Settings: the Audio & microphone section is reorganized into two groups, App & system audio and Microphone, each with its own switch. Every source is one compact row with checkbox, name, volume slider and level meter; the long explanation moved into an info tooltip.
- Settings: a new custom color picker in Spezi's style replaces the Windows color dialog: color wheel, hue/saturation/brightness sliders, a validated #RRGGBB field and a before/after preview.
- Settings: the replay slider shows the estimated maximum RAM use; a missing hardware encoder is clearly highlighted.
- Settings: the app version and a GitHub link are shown at the bottom.
- Maximized windows keep Recording, the tabs and the footer centered at a readable width instead of stretching across the screen.
- Fixed the hotkey conflict hint being shown twice.
- Internal: UI threading and error handling hardening, asynchronous gallery scanning, xUnit tests, GitHub Actions CI and tag-based release workflow (installer and updater attached to the GitHub Release), optional code signing, automatic SHA-256 verification, central version in `Directory.Build.props`.

## 1.1.1

- Fixed automatic replay stopping and clearing the buffer after leaving a game. Automatic replay now controls starting only. Once enabled, replay continues through Alt+Tab, desktop use, switching apps and closing a game until it is turned off or Spezi exits. Screen capture includes everything visible on the selected display; an explicitly selected window remains a window source. Replay duration and memory limits still apply.
- Fixed microphone input being audible only on the left or right when an audio interface exposes a single microphone as a stereo device. Microphone channels are mixed to centered mono before encoding. Game, application and system audio retain their stereo channels.
- Fixed resampling-buffer size overflow for larger microphone packets. Existing settings and saved clips are preserved.

For an existing installed or portable copy, finish any recording, exit Spezi from the tray and run `updater/Spezi-Updater.exe`. Select the existing program folder if needed. Use `installer/Spezi-Setup.exe` for a new installation. Keep the complete `dist` folder together when using the portable app.

## 1.1.0

- Videos open in the Windows default player instead of an embedded player.
- Replay length is configured exclusively in Settings, from 15 seconds to 5 minutes in 15-second steps. The redesigned slider has larger minute markers and an accent-colored duration indicator.
- Selected-app audio can capture the current game, Discord, TeamSpeak and other running apps independently of whole-system audio. A microphone remains optional. App audio requires Windows build 20348 or newer.
- The app and installer are in English. All recording and audio options are configured in Settings; first launch opens Recording directly.
- The gallery has larger 16:9 previews, game badges, a compact search field, sorting and card actions. Generated titles are made readable without renaming existing files. Thumbnail loading remains bounded and limited to visible entries.
- A separate offline updater supports installed and portable copies, retains clips and preferences, blocks running-app updates and rejects downgrades.

Finish a recording and exit Spezi from the tray before running the installer or updater. For a new installation, use `installer/Spezi-Setup.exe`. For an existing copy, use `updater/Spezi-Updater.exe` and select its program folder if needed. The portable application is `dist/Spezi.exe`; keep the complete `dist` folder together.

## Detailed update notes

- [ENGLISH_UI_UPDATE](docs/updates/ENGLISH_UI_UPDATE.md)
- [EXTERNAL_PLAYER_UPDATE](docs/updates/EXTERNAL_PLAYER_UPDATE.md)
- [GALLERY_UPDATE](docs/updates/GALLERY_UPDATE.md)
- [HOTKEY_UPDATE](docs/updates/HOTKEY_UPDATE.md)
- [MEDIA_FIX](docs/updates/MEDIA_FIX.md)
- [PERFORMANCE_UPDATE](docs/updates/PERFORMANCE_UPDATE.md)
- [PERSONALIZATION_UPDATE](docs/updates/PERSONALIZATION_UPDATE.md)
- [RECORDING_FIX](docs/updates/RECORDING_FIX.md)
- [REPLAY_APP_AUDIO_UPDATE](docs/updates/REPLAY_APP_AUDIO_UPDATE.md)
- [REPLAY_MICROPHONE_FIX](docs/updates/REPLAY_MICROPHONE_FIX.md)
- [SETTINGS_UPDATE](docs/updates/SETTINGS_UPDATE.md)
- [TOAST_UPDATE](docs/updates/TOAST_UPDATE.md)
