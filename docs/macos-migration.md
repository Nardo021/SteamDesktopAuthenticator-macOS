# Migrating from Windows Steam Desktop Authenticator

This document is for existing Windows SDA users moving `maFiles` to the macOS port. Do not re-enroll the authenticator unless the import fails.

## 1. BACK UP your original maFiles folder first

Copy the entire Windows `maFiles` folder (including `manifest.json` and every `.maFile`) to a safe location. Keep that backup until the macOS account list works and a Steam Guard code matches the Windows copy.

If encryption is enabled, also keep the encryption passkey. Losing both the backup and the passkey can lock you out of Steam.

## 2. Install and open the macOS port

Install `Steam Desktop Authenticator.app` from the official [GitHub Releases](https://github.com/Nardo021/SteamDesktopAuthenticator-macOS/releases) page. Use the `arm64` DMG on Apple Silicon and the `x64` DMG on Intel Macs. A .NET runtime install is not required.

This release is unsigned and not notarized. If macOS blocks the first launch, use **System Settings → Privacy & Security → Open Anyway** for this official download. Do not disable Gatekeeper globally. Details are in the README installing section.

Default storage on macOS is:

```text
~/Library/Application Support/Steam Desktop Authenticator/maFiles
```

`settings.json` in the same Application Support folder records the current `maFiles` directory.

## 3. Import existing files

Preferred paths, in order:

1. **Import Account** for each `.maFile` you want to add.
2. **Open maFiles Folder** if you already copied a complete `maFiles` directory and want the app to use that folder.
3. Copy a backup `maFiles` directory into the default Application Support location, then restart.

Do not delete the Windows backup until a displayed Steam Guard code matches the Windows SDA code for the same account and time window.

## 4. Encrypted source imports

Encrypted `.maFile` import requires the **source** `manifest.json` that sits next to those files. That file holds the salt/IV entries SDA needs to decrypt with your passkey.

If the current macOS Manifest is already encrypted, decrypt it before importing additional files, then re-enable encryption.

## 5. After import

- Confirm the account appears in the list.
- Confirm a 5-character Steam Guard code appears and copies.
- Confirm **View Confirmations** works after login/session refresh if you use trades or market listings.
- Write down the revocation code if you still have it. The macOS port does not invent a new one.

## 6. Do not re-enroll unless necessary

Compatibility with original SDA `maFiles` and encryption is a core goal. Re-enrolling replaces the authenticator on the Steam account and can invalidate the old `maFile`.
