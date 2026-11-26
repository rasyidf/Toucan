---
title: "Installing Toucan"
status: active
updated: 2026-11-26
summary: "Install, upgrade and uninstall Toucan on Windows, macOS and Linux; what stays when you upgrade or remove it; SmartScreen, Gatekeeper and the Linux Secret Service."
---
# Installing Toucan

Download from the [releases page](https://github.com/rasyidf/Toucan/releases). Each release lists `SHA256SUMS.txt`; check a download with `shasum -a 256 -c SHA256SUMS.txt --ignore-missing` (macOS, Linux) or `Get-FileHash` (Windows).

## Windows

- **Installer** (`ToucanSetup-<version>.exe`): installs for the current user without administrator rights, adds a Start menu entry, and optionally a desktop icon, the `.tproj` file association and an *Open with Toucan* folder entry.
- **Portable** (`Toucan-<version>-portable-x64.zip`): unzip anywhere and run `Toucan.exe`.

Toucan 1.0 is not signed on Windows, so SmartScreen may say *Windows protected your PC* the first time. Choose **More info → Run anyway**. This is asked once per version.

**Upgrade:** run the new installer; it replaces the old version in place. **Uninstall:** Settings → Apps → Toucan → Uninstall.

## macOS

Open the `.dmg` for your Mac (`osx-arm64` for Apple silicon, `osx-x64` for Intel) and drag Toucan to Applications. Release builds are signed with a Developer ID and notarized, so Gatekeeper opens them without a warning. A release candidate may be ad-hoc signed: if macOS says it cannot verify the app, open it once, then use **System Settings → Privacy & Security → Open Anyway**.

**Upgrade:** replace the app in Applications. **Uninstall:** move Toucan to the Bin.

## Linux

| Package | For | Install |
|---|---|---|
| `toucan_<version>_amd64.deb`, `_arm64.deb` | Debian, Ubuntu and derivatives | `sudo apt install ./toucan_<version>_amd64.deb` |
| `toucan-<version>-1.x86_64.rpm`, `.aarch64.rpm` | Fedora, openSUSE, RHEL and derivatives | `sudo dnf install ./toucan-<version>-1.x86_64.rpm` |
| `Toucan-<version>-linux-x64.tar.gz`, `-linux-arm64.tar.gz` | any distribution | unpack, run `./Toucan` |

The packages install the app in `/opt/toucan`, a `toucan` command, a desktop entry and the `.tproj` file type. They need the ICU and fontconfig libraries, which most desktops have.

**Saved sign-in tokens and the Secret Service.** Toucan keeps the key that encrypts saved API keys and tokens in the Secret Service (GNOME Keyring or KWallet) through `secret-tool`. The packages recommend it; install it yourself if your system skipped recommendations: `sudo apt install libsecret-tools` (Debian, Ubuntu) or `sudo dnf install libsecret` (Fedora). Without a Secret Service, Toucan keeps the key in a file in your Toucan folder, readable only by you. A locked keyring makes saved secrets read as missing until you unlock it; Toucan never creates a second key. Set `TOUCAN_SECRET_KEY_STORE=file` to keep the key in the file deliberately.

**Upgrade:** install the newer package the same way. **Uninstall:** `sudo apt remove toucan` or `sudo dnf remove toucan`; for the tarball, delete the folder.

## What stays when you upgrade or uninstall

Uninstalling removes the program only. These stay, so a later install picks them up:

| What | Where |
|---|---|
| Settings and recent projects | `Documents/Toucan/` |
| Accounts, saved API keys and tokens, the secret key file | `%APPDATA%\Toucan` (Windows), `~/.config/Toucan` (macOS, Linux) |
| The key that encrypts saved secrets | Windows: DPAPI; macOS: login Keychain; Linux: Secret Service (or the key file above) |
| Your projects | wherever they are; Toucan's per-project data is in each project's `.toucan` folder |

Delete those folders by hand to remove everything. Opening a project in an older Toucan is covered in [compatibility](compatibility.md).

## Updates

Toucan checks for a newer release when it starts and tells you; it does not download or install anything. Settings → About sets the channel (Stable or Preview) and turns the check off.
