# macOS packaging

These scripts produce **unsigned** `.app` bundles and DMGs from the current MSBuild `Version`. That is the public release path.

```text
Release status: READY FOR UNSIGNED PUBLIC RELEASE
Code signing: Not provided
Apple notarization: Not provided
```

`./packaging/macos/package.sh` does not require `APPLE_SIGN_IDENTITY`, a Developer ID certificate, a notary profile, or any Apple credentials.

## Bundle identity

| Key | Value |
| --- | --- |
| CFBundleIdentifier | `io.github.nardo021.SteamDesktopAuthenticator` |
| CFBundleDisplayName | Steam Desktop Authenticator |
| CFBundleExecutable | `SDA.Desktop` |
| Version source | `Directory.Build.props` → `<SdaProductVersion>` |

Do not invent a company domain. The identifier follows the canonical GitHub remote `Nardo021/SteamDesktopAuthenticator-macOS`.

## Unsigned public / local build

```bash
./packaging/macos/package.sh
```

This writes:

- arm64 `.app` and DMG
- x64 `.app` and DMG
- `dist/SHA256SUMS.txt`

Or one architecture:

```bash
./packaging/macos/build-app.sh osx-arm64
./packaging/macos/build-dmg.sh osx-arm64
./packaging/macos/verify-release.sh osx-arm64 --unsigned
```

Unsigned verification checks bundle structure, Info.plist, architecture, and version. It reports:

```text
Signing: SKIPPED — unsigned release
Notarization: SKIPPED — unsigned release
```

It does not claim Gatekeeper or Apple verification.

## Optional future signing

`sign-app.sh`, `notarize.sh`, and `SDA.entitlements` are kept for a later signed release. They are **not** part of the unsigned public workflow.

```bash
export APPLE_SIGN_IDENTITY='Developer ID Application: Name (TEAMID)'
./packaging/macos/sign-app.sh osx-arm64
```

```bash
export APPLE_NOTARY_PROFILE='sda-notary'
./packaging/macos/notarize.sh osx-arm64
```

Do not store Apple passwords, `.p8` keys, or certificates in the repository.

## Entitlements

`SDA.entitlements` enables only `com.apple.security.cs.allow-jit` for the .NET runtime. App Sandbox is not enabled. Unsigned public releases do not apply this file because they are not signed.

## Environment overrides for tests

`SDA_DATA_DIRECTORY` redirects Application Support when launching the executable or using `open --env`. Do not point automated tests at real `maFiles`.
