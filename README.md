<h1 align="center">
  <img src="icon.png" height="64" width="64" />
  <br/>
  Steam Desktop Authenticator
</h1>
<p align="center">
  A desktop implementation of Steam's mobile authenticator app.<br/>
  This repository is a <b>macOS port</b> of the original Windows application.<br/>
  <sup><b>We are not affiliated with Steam or Valve in any way.</b></sup>
</p>

This repository is a community macOS port of [Jessecar96/SteamDesktopAuthenticator](https://github.com/Jessecar96/SteamDesktopAuthenticator). It keeps the original maFile format, encryption, and SteamAuth authenticator logic. The original authors do **not** maintain this macOS port.

Authenticator work uses [geel9/SteamAuth](https://github.com/geel9/SteamAuth).

**This macOS build is currently unsigned and not notarized by Apple.**

Because the application does not currently use an Apple Developer ID certificate, macOS may require manual approval before first launch. That is expected. It is not a crash, and it is not Apple saying the app is trustworthy.

```text
Release status: READY FOR UNSIGNED PUBLIC RELEASE
Code signing: Not provided
Apple notarization: Not provided
Gatekeeper manual approval may be required on first launch
```

**WARNING:** Only download releases from the official repository: [https://github.com/Nardo021/SteamDesktopAuthenticator-macOS](https://github.com/Nardo021/SteamDesktopAuthenticator-macOS). Fake copies exist that steal Steam accounts. Do not use mirrors.

**REMEMBER:** Always back up your `maFiles` directory and encryption passkey. If you lose the files or the key and you did not save your revocation code, you can lose access to the Steam account.

**Using this application stores authenticator secrets on the Mac.** If the machine is compromised, those secrets can be used. Prefer Steam's official mobile authenticator when you can.

If you lost `maFiles` or the encryption key, use [Steam two-factor management](https://store.steampowered.com/twofactor/manage) with the revocation code you wrote down when the authenticator was added.

## What this repository is

- A native macOS Avalonia/.NET port of Steam Desktop Authenticator
- Compatible with original SDA `.maFile` files and `manifest.json`
- Distributed as self-contained `arm64` and `x64` `.app` / DMG artifacts

It is not a rewrite of the account model, not a cloud sync product, and not the original Windows release channel.

## Installing on macOS

Download the DMG from this repository's [GitHub Releases](https://github.com/Nardo021/SteamDesktopAuthenticator-macOS/releases) page. Choose the architecture that matches the Mac:

| Build | Use on |
| --- | --- |
| `arm64` (`Steam-Desktop-Authenticator-macOS-*-arm64.dmg`) | Apple Silicon — M1, M2, M3, M4, and later Apple Silicon Macs |
| `x64` (`Steam-Desktop-Authenticator-macOS-*-x64.dmg`) | Intel Macs |

Apple Silicon users should use the **arm64** build. The Intel `x64` build is not the normal choice on Apple Silicon.

You may verify the published SHA-256 checksums in `SHA256SUMS.txt` on the same release page against the DMG you downloaded.

1. Download the correct DMG from the official repository.
2. Open the DMG.
3. Drag **Steam Desktop Authenticator** into **Applications**.
4. Open the app from Applications.
5. macOS may block it because this release is **unsigned** and **not notarized**.
6. If macOS blocks the app:
   1. Open **System Settings**.
   2. Open **Privacy & Security**.
   3. Find the message that Steam Desktop Authenticator was blocked.
   4. Choose **Open Anyway**.
   5. Confirm **Open**.
7. After that approval, macOS should remember it for this app.

The release is unsigned and unnotarized, so macOS cannot verify the developer identity. Only continue if you downloaded the app from the official repository and trust this project.

These **Open Anyway** steps apply to **this** official release. Do not treat them as a reason to approve other unsigned software.

Do not disable Gatekeeper globally. Do not turn off macOS security to install this app.

## Existing Windows SDA users

**BACK UP your original `maFiles` folder first.** Keep that backup until a Steam Guard code on macOS matches the Windows copy.

Then use one of:

- **Open maFiles Folder** — point the app at a copied `maFiles` directory
- **Import Account** — add a single `.maFile`

Encrypted source imports require the **source** `manifest.json` next to those files. Do not re-enroll Steam Guard unless import fails. See [docs/macos-migration.md](docs/macos-migration.md).

## macOS support

| Item | Value |
| --- | --- |
| Minimum OS | macOS 12.0 |
| Architectures | Apple Silicon (`arm64`) and Intel (`x64`) |
| Runtime | Self-contained Release builds include .NET. Users do not install a runtime. |
| Status bar | NativeMenu Restore / Accounts / View Confirmations / Copy Steam Guard / Quit |
| Default data | `~/Library/Application Support/Steam Desktop Authenticator/` |

Hide (yellow minimize or Hide) keeps the status-bar menu available. Restore returns the same MainWindow. The Dock icon remains visible.

## Build requirements

- .NET SDK 10
- macOS with Xcode command-line tools for `.app` / DMG packaging
- Git checkout **with the SteamAuth submodule**

```bash
git clone --recurse-submodules https://github.com/Nardo021/SteamDesktopAuthenticator-macOS.git
```

## Development run

```bash
dotnet run --project src/SDA.Desktop/SDA.Desktop.csproj
```

## Release build

Unsigned packaging does not require Apple credentials:

```bash
./packaging/macos/package.sh
```

This publishes self-contained `osx-arm64` and `osx-x64` builds, wraps each in `Steam Desktop Authenticator.app`, and writes architecture-specific DMGs plus `dist/SHA256SUMS.txt`.

Manual publish:

```bash
dotnet publish src/SDA.Desktop/SDA.Desktop.csproj -c Release -r osx-arm64 --self-contained true -p:UseAppHost=true
dotnet publish src/SDA.Desktop/SDA.Desktop.csproj -c Release -r osx-x64 --self-contained true -p:UseAppHost=true
```

Developer ID signing and notarization scripts exist in `packaging/macos/` for a possible later release. They are optional and are **not** used for unsigned public releases.

## Where maFiles are stored

Default:

```text
~/Library/Application Support/Steam Desktop Authenticator/maFiles
```

`settings.json` next to that folder records the current maFiles directory. Use **Open maFiles Folder** to point at an existing directory.

## Command line options

```
-k [encryption key]
  Unlock encrypted maFiles at startup
-s
  Start hidden to the status bar
```

## Security

- Only download from [https://github.com/Nardo021/SteamDesktopAuthenticator-macOS](https://github.com/Nardo021/SteamDesktopAuthenticator-macOS).
- Verify release checksums when they are published beside the DMGs.
- Back up `maFiles` before every upgrade or import.
- Do not commit real `.maFile` files, certificates, or Apple notary credentials.
- The MIT License from the original project is retained in `LICENSE`.

## Documentation

- [Release status](docs/release-status.md)
- [v1.0.1 release notes](docs/releases/v1.0.1.md)
- [v1.0.0 release notes](docs/releases/v1.0.0.md)
- [Parity audit](docs/parity-audit.md)
- [Windows → macOS migration](docs/macos-migration.md)
- [Release notes template](docs/release-notes-template.md)
- [macOS packaging](packaging/macos/README.md)

## License

MIT. Copyright (c) 2015 Jesse Cardone and contributors. See `LICENSE`.
