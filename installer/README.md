# Spezi installer

`Spezi-Setup.exe` installs Spezi for the current Windows account. The default installation folder is `%LocalAppData%\Programs\Spezi`; you can choose a different location.

- English setup wizard with a dark theme and Spezi icon.
- Windows 10 build 19041 or later and Windows 11, x64. ARM and 32-bit Windows are not supported.
- No administrator privileges, account or download is needed. The .NET runtime is included.
- Start menu entry, optional desktop shortcut and optional launch after installation.
- Recording, audio sources, microphone, quality and hotkeys are configured in the app's **Settings**. First launch opens Recording directly.
- Uninstall through Windows Settings → Installed apps → Spezi.
- Finish any recording and exit Spezi from the system tray before installing or uninstalling. Setup never ends a recording automatically.
- Existing settings, clips, thumbnails and recovery data are preserved. Uninstall removes a startup entry only if it belongs to this installation.
- The installer includes the current self-contained `dist` payload, excluding source, test recordings, preferences and debug symbols. It does not create another dist folder.

The current release is **1.1.1**. The installer and offline updater use the same published application payload. To update an existing installation or portable program folder, use `updater/Spezi-Updater.exe`; see [the updater instructions](../updater/README.md).

Spezi Setup is not signed with a custom code-signing certificate. `Spezi-Setup.exe.sha256` contains its SHA-256 checksum.

## Build

Requires the .NET 9 SDK and [Inno Setup 7.1 or later](https://jrsoftware.org/isdl.php). The official portable compiler is also supported.

```powershell
.\installer\Build-Installer.ps1 -CompilerPath 'C:\Path\To\Inno Setup 7\ISCC.exe'
```

The script publishes Spezi into `dist` and replaces `installer\Spezi-Setup.exe`. Use `-SkipPublish` only if `dist` already contains the matching current application. The installer version comes from the published Spezi.exe. Keep the stable AppId in `Spezi.iss` for future updates.

A test build (`ISCC /DTestBuild ...`) uses a separate uninstall identity and shortcuts, so installation tests cannot overwrite the user's Spezi installation.
