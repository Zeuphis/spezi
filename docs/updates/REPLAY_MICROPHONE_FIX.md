# Replay background capture and centered microphone

Released in **1.1.1** after the user's Go. The portable EXE, installer and offline updater include these corrections.

## Behavior

- Automatic replay now controls starting only. Once replay is active, foreground game detection never stops it or clears its buffer. Desktop use, Alt+Tab, switching apps and closing a game leave replay running until the user turns it off or exits Spezi. The existing duration and memory limits still apply.
- A screen source captures everything shown on that display, including the desktop. An explicitly selected window still captures that window. Existing game names and app-specific audio selection continue to follow their existing rules.
- Microphone channels are averaged to mono before resampling and mixing, so both sides of the finished stereo track carry the same microphone signal. This also covers stereo audio interfaces with a microphone connected on only one side. App and system sources remain stereo. Existing microphone gain remains adjustable; no preferences are migrated or overwritten.
- Large microphone blocks no longer overflow while calculating the resampling buffer size.
- Settings explain the background replay and centered microphone behavior in English.

## Validation

- Release builds of the app and regression runner pass without warnings or errors. Test builds use the existing cached dependencies; the network package audit is disabled for these isolated builds.
- Real controller/encoder capture: automatic start, over ten seconds without a foreground game, repeated focus transitions, saving a playable replay with audio while on the desktop, preserving a running buffer when automatic starts are disabled, and explicit replay stop. Capture uses a private temporary output folder and unchanged real preferences.
- Microphone fixtures: left-only, right-only and dual-channel PCM8/16/24/32 and float at 44.1/48/96 kHz; mono and multichannel sources; WASAPI extensible float. Stereo app/system channel separation is preserved.
- Actual AAC/MP4 encode/decode: over 48,000 frames of a left-only input tone are audible with equal left and right RMS after microphone centering.
- Existing replay-duration tests pass for all 20 steps from 15 to 300 seconds, migration and live buffer trimming. Native settings UI checks also pass.

Reports are stored in `tests/results/replay-microphone-20261102`.
