# Pending Spezi update: English interface and replay slider

These changes are in the source only. The portable application and installer have not been rebuilt. Build the update only after the user's explicit Go.

- Redesigned replay slider exclusively in Settings; Recording keeps only replay on/off and the clip/recording controls: separate readable scale, larger minute dots, compact accent-colored duration badge and thumb, 15 seconds to 5 minutes in 15-second steps.
- English navigation, settings, gallery, prompts, notifications, recording errors, display labels and hotkey names. English app culture keeps numeric and date formatting consistent on German Windows installations. Windows-owned device names, window titles and native dialogs follow the operating system.
- English installer source, with installation location and optional shortcut only. Recording and audio are configured inside Settings.
- Removed the first-run configuration wizard. Spezi opens directly on Recording and no longer resets the quality profile merely by constructing the main window.
- Existing audio, hotkey and video settings remain intact. Old German recording filenames are still recognized by the library. Existing fallback folders and unfinished recovery destinations remain compatible; new recording names use English.

Validation: isolated development builds (without an EXE app host) passed with zero warnings and errors; real WPF tests passed for all 20 slider values, snapping, accent changes, minimum window width, app-audio selection and first launch. Existing settings, library, replay-duration and recovery checks passed. The installer source was inspected; its executable will be compiled after Go.
