# Pending update: redesigned Gallery

Source changes only. Build the app EXE and installer after the user's explicit Go.

- Compact search with inline placeholder and clear action; searches filenames and game folders. Sort and folder controls replace large toolbar buttons.
- Video cards with 16:9 covers, game badges, duration overlays and accessible playback buttons. Dates and file sizes sit below readable titles.
- Automatically generated names are shortened for display. Custom titles and actual filenames remain unchanged.
- One small menu per clip offers playback in the Windows default app, rename, open folder and delete. Existing deletion confirmation remains.
- Clip/game counts and separate empty-library and no-results states. Layout works at the app's minimum width and with other accent colors.
- Recycling virtualization and pixel scrolling remain enabled. The existing 320-pixel thumbnail cache is unchanged; hidden cards release their images and returning to a clip reuses the cached preview.

Validation: development build without an EXE app host passed with zero warnings and errors. Real WPF checks passed for actual MP4 previews and duration, custom titles, game search, sorting, clear search, popup action data contexts, narrow layout and empty states. With 24 test clips, the 600×620 test window loaded two thumbnails in about 310 ms and released hidden previews while scrolling. These figures are a local test result, not a general performance guarantee. Preview images use copies of a test recording in fixture game folders; no user files or preferences were changed.
