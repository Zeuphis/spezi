# Spezi 1.1.0

- Videos open in the Windows default player instead of an embedded player.
- Replay length is configured exclusively in Settings, from 15 seconds to 5 minutes in 15-second steps. The redesigned slider has larger minute markers and an accent-colored duration indicator.
- Selected-app audio can capture the current game, Discord, TeamSpeak and other running apps independently of whole-system audio. A microphone remains optional. App audio requires Windows build 20348 or newer.
- The app and installer are in English. All recording and audio options are configured in Settings; first launch opens Recording directly.
- The gallery has larger 16:9 previews, game badges, a compact search field, sorting and card actions. Generated titles are made readable without renaming existing files. Thumbnail loading remains bounded and limited to visible entries.
- A separate offline updater supports installed and portable copies, retains clips and preferences, blocks running-app updates and rejects downgrades.

Finish a recording and exit Spezi from the tray before running the installer or updater. For a new installation, use `installer/Spezi-Setup.exe`. For an existing copy, use `updater/Spezi-Updater.exe` and select its program folder if needed. The portable application is `dist/Spezi.exe`; keep the complete `dist` folder together.
