# Spezi 1.1.1

- Fixed automatic replay stopping and clearing the buffer after leaving a game. Automatic replay now controls starting only. Once enabled, replay continues through Alt+Tab, desktop use, switching apps and closing a game until it is turned off or Spezi exits. Screen capture includes everything visible on the selected display; an explicitly selected window remains a window source. Replay duration and memory limits still apply.
- Fixed microphone input being audible only on the left or right when an audio interface exposes a single microphone as a stereo device. Microphone channels are mixed to centered mono before encoding. Game, application and system audio retain their stereo channels.
- Fixed resampling-buffer size overflow for larger microphone packets. Existing settings and saved clips are preserved.

For an existing installed or portable copy, finish any recording, exit Spezi from the tray and run `updater/Spezi-Updater.exe`. Select the existing program folder if needed. Use `installer/Spezi-Setup.exe` for a new installation. Keep the complete `dist` folder together when using the portable app.
