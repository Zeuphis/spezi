# Changelog

All notable changes to Spezi. The version is defined once in `Directory.Build.props`.

## 1.3.1

- Calmer replay card: while replay is on it simply shows "Last <length> ready to save", without a ticking buffer counter or filling indicator.
- Sharper text on the Save clip and Record buttons and their hotkey badges: the glow no longer blurs the content, and text uses display rendering with pixel snapping.

## 1.3.0

- New Recording page design: the upper half is one big replay power card. Click anywhere on it to turn replay on or off. It shows the state, how much is ready to save, buffer and quality chips, and glows in your accent color while replay is on.
- Save clip and Record sit side by side with hotkey badges. Save clip is disabled with "Turn on replay first" while replay is off; Record turns red with a live timer while recording.
- Recent clips are shown as a grid of up to 8 thumbnails (two rows) with duration badges, plus an empty state.
- Settings: sections are collapsible cards that start collapsed; the "Changes are saved automatically" note is now a subtle line under the title.
- The main window is moved back on screen if it was shown or restored partly off-screen.
- Removed the Test audio button from the audio settings; level meters stay.

## 1.2.0

- Recording page redesign: replay status card with a pulse while active and chips for buffer length and quality; a stronger Save clip button with a clear hotkey badge and an in-button hint when replay is off; a red Start recording button with a live timer; a yellow warning row with a Change hotkeys link when both hotkeys are the same; compact audio chips with a microphone level meter; recent clips as larger tiles with duration badges and an empty state.
- Fixed an error dialog when closing the main window. The notification window is recreated when needed, and notification failures are only logged.
- Updater: when Spezi was not installed with Setup, the updater checks the default install folder and the portable `dist` folder, then lets you pick the folder that contains Spezi.exe instead of aborting.
- Startup update check: once per start and in the background, Spezi checks GitHub Releases. If a newer version exists, a small banner offers Update or Later. Updates are downloaded over HTTPS and run only after the SHA-256 checksum matches. You can turn this off in Settings with Check for updates on startup.
- Lower idle use: hardware encoder detection runs in a short-lived helper process, so GPU driver threads no longer stay in Spezi (settings open: about 45 to 18 threads and about 20 MB less memory). Status timers and level meters pause while the window is minimized.
- Internal: the Notifications settings section uses MVVM (CommunityToolkit.Mvvm) with unit tests; tests no longer write to the user's log.

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
