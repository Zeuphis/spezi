# Spezi offline updater

`Spezi-Updater.exe` contains the complete **1.1.1** application payload. It updates existing installed and portable copies without downloading anything.

1. Finish any recording and choose **Exit** from the Spezi system tray menu. Closing the window alone leaves Spezi running.
2. Run `Spezi-Updater.exe`.
3. Confirm the existing program folder. Installed copies are detected automatically. For portable copies, select the folder containing `Spezi.exe`, `Spezi.dll` and `Spezi.runtimeconfig.json`. Do not select your clips folder.
4. Click **Update** and optionally launch Spezi afterwards.

Your clips, settings, audio selections, accent color and hotkeys are preserved. Installed copies keep their existing Windows uninstall entry and shortcuts. Portable copies stay portable: the updater does not add an uninstaller or Start menu shortcuts. An empty folder and a newer executable are rejected. Updating to the same version is allowed for repair. No account, separate .NET installation or administrator privileges are required for normal per-user installations.

This is a manual offline updater. Automatic update checks and an online distribution service are not included. The package is not signed with a custom code-signing certificate. `Spezi-Updater.exe.sha256` provides its SHA-256 checksum.

## Build

Publish the full self-contained application into `dist` first (or run `installer/Build-Installer.ps1`), then build this package with the same payload:

```powershell
.\updater\Build-Updater.ps1 -CompilerPath 'C:\Path\To\Inno Setup 7\ISCC.exe'
```

Requires Inno Setup 7.1 or later. The updater includes the shared installer script; keep its stable AppId for future updates. The build reads the version directly from `dist/Spezi.exe` and writes the checksum alongside the output. Test packages compiled with `/DTestBuild` use a separate uninstall identity and shortcuts.
