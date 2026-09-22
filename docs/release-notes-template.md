# Steam Desktop Authenticator for macOS — Release Notes

Fill this template when cutting a GitHub Release. Do not invent a version that is not in `Directory.Build.props`.

## Version

- Product version:
- Source: `Directory.Build.props` `<SdaProductVersion>`
- Git commit:

## Architecture

- [ ] arm64 (Apple Silicon)
- [ ] x64 (Intel)

Ship separate DMGs. This port does not produce a Universal binary.

## macOS requirement

- Minimum: macOS 12.0 (`LSMinimumSystemVersion`)
- .NET runtime: bundled (self-contained). Users do not install .NET.

## Changes

-

## Known limitations

- Developer ID signing / notarization status:
- Intel verification level (built / Rosetta / physical Intel):
- Single-instance second process exits without bringing the first window forward (no IPC).
- Dock icon remains visible while hidden to the status bar.
-

## Security notes

- Back up `maFiles` and the encryption passkey before upgrading.
- Never share `shared_secret`, `identity_secret`, session tokens, or the encryption passkey.
- Download only from this repository's GitHub Releases.
- This tool stores authenticator secrets on disk. A compromised Mac can use them.

## Upgrade instructions

1. Quit Steam Desktop Authenticator completely.
2. Back up `~/Library/Application Support/Steam Desktop Authenticator`.
3. Replace `Steam Desktop Authenticator.app` with the new copy from the DMG.
4. Open the new app and confirm accounts and Steam Guard codes still match.

Windows users: see `docs/macos-migration.md`. Do not re-enroll unless import fails.

## Checksums

Copy the contents of `dist/SHA256SUMS.txt` here after `packaging/macos/write-checksums.sh`.

```text
```

## Upstream attribution

This macOS port is derived from [Jessecar96/SteamDesktopAuthenticator](https://github.com/Jessecar96/SteamDesktopAuthenticator) and uses [geel9/SteamAuth](https://github.com/geel9/SteamAuth). The original authors do not maintain this macOS repository.
