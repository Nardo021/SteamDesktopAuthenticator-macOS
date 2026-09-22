# Phase 9 original-vs-macOS parity audit

Date: 2026-09-22

Compared original WinForms application under `Steam Desktop Authenticator/` against the Avalonia macOS port in `src/`.

Classification is exactly one of:

- `PORTED`
- `PLATFORM-ADAPTED`
- `INTENTIONALLY-DEFERRED`
- `OBSOLETE-FOR-MACOS`
- `RELEASE-BLOCKING-GAP`

A difference is release-blocking only when it affects account recovery safety, maFile compatibility, enrollment, deactivation, login/session correctness, confirmation correctness, encryption correctness, app lifecycle, startup, the basic original workflow, or distribution viability. Cosmetic differences are not blockers.

## Decision log

### Single instance

Original `Program.PriorProcess()` looks for another process with the same name and `MainModule.FileName`, shows a MessageBox, and returns.

The macOS port now uses a named `Mutex` (`SteamDesktopAuthenticator-macOS`) in `Program.Main` **before** `AppStartup` and Avalonia start. A second process exits with code 0 and does not load Manifest mutation services, timers, or the status-bar icon.

Foreground activation of the first instance is not implemented. Sockets/IPC were not added solely for that. The original MessageBox is omitted because a WinExe `.app` has no useful console and a dialog would require initializing Avalonia.

Status: `PLATFORM-ADAPTED` (implemented; silent second-instance exit).

### Unsupported-software warning

Original `Program.cs` shows a blocking MessageBox on every start stating the software is no longer supported and will never receive updates.

This repository is an actively maintained macOS fork (`origin` = `Nardo021/SteamDesktopAuthenticator-macOS`). Repeating “this software will never receive updates” would be false. A blocking every-launch warning is not preserved.

Safety text lives in `README.md` (unofficial tool, backup `maFiles`, encryption key, account risk). That is intentional.

Status: `INTENTIONALLY-DEFERRED` for the blocking dialog; README carries the safety notice.

### WelcomeForm

Original WelcomeForm appears only when `FirstRun && Entries.Count == 0`. It offers “just start” or import an entire previous SDA install folder (`maFiles` or a folder containing `manifest.json`).

The macOS MainWindow already exposes **Setup New Account**, **Import Account**, and **Open maFiles Folder**. A redundant welcome window was not added.

`Manifest.FirstRun` is still serialized for compatibility and is not used as a UI gate.

Status: `PLATFORM-ADAPTED`.

### Version

Original UI shows `v{Application.ProductVersion}` from `AssemblyVersion` `1.0.15`.

This fork’s first packaged release is **1.0.0**, owned by `Directory.Build.props` `<SdaProductVersion>`. Assembly, Info.plist, DMG filenames, and the MainWindow label all read that source. It is not Windows SDA 1.0.15.

Status: `PLATFORM-ADAPTED`.

### Updater

Original `MainForm.checkForUpdates()` queries `Jessecar96/SteamDesktopAuthenticator` and opens that repository’s first release asset.

That URL must not be used here. `git remote -v` shows a clear canonical GitHub remote:

```text
origin  https://github.com/Nardo021/SteamDesktopAuthenticator-macOS.git
```

A minimal checker now queries **this** repository’s latest GitHub release, compares semantic versions, and can open this repository’s release page. It does not download, patch, or mount a DMG.

Status: `PLATFORM-ADAPTED`.

## Audit table

### Startup

| Feature | Classification | Notes |
| --- | --- | --- |
| single instance | PLATFORM-ADAPTED | Named Mutex; second process exits before Avalonia; no IPC foreground |
| command line `-k` | PORTED | Same flag and Manifest unlock path |
| command line `-s` | PLATFORM-ADAPTED | Hidden-to-tray instead of WinForms minimize-to-tray |
| manifest loading | PORTED | Same `manifest.json` schema and `GetManifest` |
| manifest corruption handling | PORTED | Reset / encrypted-maFile failure paths preserved in Core |
| first-run behavior | PLATFORM-ADAPTED | Empty MainWindow with Setup/Import; no WelcomeForm |
| unsupported-software warning | INTENTIONALLY-DEFERRED | Blocking “never receive updates” would be false for this fork |
| startup version display | PLATFORM-ADAPTED | `v{SdaProductVersion}` from MSBuild, not Windows 1.0.15 |

### Main window

| Feature | Classification | Notes |
| --- | --- | --- |
| account list | PORTED | |
| Manifest ordering | PORTED | Manifest entry order is authoritative |
| account search | PORTED | |
| regex search | PORTED | `~` prefix after plain prefix match |
| Control/Command reorder | PLATFORM-ADAPTED | Command on macOS, Control still accepted |
| Steam Guard code | PORTED | SteamAuth `GenerateSteamGuardCodeForTime` |
| countdown | PORTED | |
| copy | PORTED | Clipboard + `pbcopy` fallback while hidden |
| View Confirmations | PORTED | |
| Setup New Account | PORTED | |
| Manage Encryption | PORTED | |

### File / account menus

| Feature | Classification | Notes |
| --- | --- | --- |
| Import Account | PORTED | Per-maFile + adjacent manifest for encrypted files |
| Settings | PORTED | Periodic check / auto-confirm / check all accounts |
| Quit | PLATFORM-ADAPTED | File, status-bar, red close, and Command-Q all explicit-shutdown |
| Login Again | PORTED | |
| Force Session Refresh | PORTED | |
| Remove from Manifest | PORTED | |
| Deactivate Authenticator | PORTED | |

### Login

| Feature | Classification | Notes |
| --- | --- | --- |
| credentials | PORTED | |
| device code | PORTED | |
| email code | PORTED | |
| session persistence | PORTED | Session saved back into the maFile |
| Import login | PORTED | |
| Login Again | PORTED | |

### Enrollment

| Feature | Classification | Notes |
| --- | --- | --- |
| phone | PORTED | |
| email confirmation | PORTED | |
| initial maFile save | PORTED | |
| revocation code | PORTED | |
| revocation confirmation | PORTED | |
| SMS | PORTED | |
| finalization | PORTED | |
| FullyEnrolled | PORTED | |

### Confirmations

| Feature | Classification | Notes |
| --- | --- | --- |
| manual list | PLATFORM-ADAPTED | Native list instead of `ConfirmationFormWeb` IE host |
| Accept | PORTED | |
| Deny | PORTED | |
| Refresh | PORTED | |
| icon | PORTED | |
| background polling | PORTED | |
| popup | PLATFORM-ADAPTED | Avalonia popup instead of `TradePopupForm` |
| auto-confirm market | PORTED | |
| auto-confirm trades | PORTED | |
| check all accounts | PORTED | |

### Encryption

| Feature | Classification | Notes |
| --- | --- | --- |
| unlock | PORTED | |
| enable | PORTED | |
| change key | PORTED | |
| remove encryption | PORTED | |
| legacy encrypted maFile | PORTED | FileEncryptor PBKDF2 50k HMAC-SHA1 AES-256-CBC |
| CLI key | PORTED | `-k` |

### Tray / lifecycle

| Feature | Classification | Notes |
| --- | --- | --- |
| minimize | PLATFORM-ADAPTED | Minimize hides to status bar |
| hide | PORTED | |
| restore | PORTED | Same MainWindow instance |
| tray account selection | PORTED | NativeMenu checkbox items |
| tray copy | PORTED | Works while MainWindow is hidden |
| tray confirmations | PORTED | |
| tray quit | PORTED | |
| silent startup | PLATFORM-ADAPTED | `-s` starts hidden-to-tray |

### Other

| Feature | Classification | Notes |
| --- | --- | --- |
| update checker | PLATFORM-ADAPTED | This repository only; no binary replace |
| WelcomeForm | PLATFORM-ADAPTED | Entry points exist on MainWindow |
| single-instance behavior | PLATFORM-ADAPTED | See decision log |
| version label | PLATFORM-ADAPTED | Central MSBuild version |
| unsupported-software warning | INTENTIONALLY-DEFERRED | See decision log |
| Squirrel / ClickOnce Windows updater | OBSOLETE-FOR-MACOS | Windows packaging only |
| `notifyIcon.Clicked` | OBSOLETE-FOR-MACOS | macOS NativeMenu has no TrayIcon.Clicked |
| embedded IE confirmation browser | OBSOLETE-FOR-MACOS | Replaced by native confirmation UI |
| App Sandbox / Mac App Store | INTENTIONALLY-DEFERRED | Direct distribution needs user-selected maFiles |
| Keychain migration | INTENTIONALLY-DEFERRED | Explicitly out of scope |
| Native AOT / trimming | INTENTIONALLY-DEFERRED | Reflection/serialization risk |
| Universal binary | INTENTIONALLY-DEFERRED | Separate arm64 and x64 artifacts |
| Hide Dock icon while in status bar | INTENTIONALLY-DEFERRED | Phase 8 kept the Dock icon |
| Bring first instance to foreground | INTENTIONALLY-DEFERRED | Would require IPC; Mutex already stops two writers |
| WelcomeForm whole-folder copy wizard | PLATFORM-ADAPTED | Covered by Import Account + Open maFiles Folder |

## Remaining gaps

No `RELEASE-BLOCKING-GAP` items remain after Phase 9 single-instance and updater retargeting.

Non-blocking leftovers:

1. Second instance does not activate the first window.
2. No blocking unsupported-software dialog (intentional).
3. No dedicated WelcomeForm (intentional).
4. Dock icon stays visible when hidden to the status bar.
5. Confirmation UI is native, not an embedded web view.
6. v1.0.0 is intentionally unsigned and unnotarized. That is documented, not a release-blocking gap. See `docs/release-status.md`.

## Counts

| Label | Count |
| --- | --- |
| PORTED | 48 |
| PLATFORM-ADAPTED | 18 |
| INTENTIONALLY-DEFERRED | 8 |
| OBSOLETE-FOR-MACOS | 3 |
| RELEASE-BLOCKING-GAP | 0 |
