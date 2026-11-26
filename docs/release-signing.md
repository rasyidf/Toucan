---
title: "Release signing"
status: active
updated: 2026-11-26
summary: "What the maintainer supplies so release builds are signed and notarized on macOS, how to store it as GitHub secrets, and why Windows ships unsigned for 1.0."
---
# Release signing

The release workflow (`.github/workflows/release.yml`) signs and notarizes the macOS app when the secrets below exist. A final release (a tag without `-`, such as `v1.0.0`) fails without them; a release candidate (`v1.0.0-rc.1`) builds ad-hoc signed packages instead.

## macOS: what to supply

1. **Apple Developer Program membership** (USD 99 per year), for the account that will publish Toucan.
2. **A Developer ID Application certificate.** In Xcode → Settings → Accounts → Manage Certificates, add a *Developer ID Application* certificate, then export it from Keychain Access with its private key as a `.p12` file protected by a password.
3. **An App Store Connect API key** for notarization: App Store Connect → Users and Access → Integrations → App Store Connect API → generate a key with *Developer* access. Download the `.p8` file (it can be downloaded once) and note its Key ID and the Issuer ID.

Store them as repository secrets (GitHub → Settings → Secrets and variables → Actions), for example with the GitHub CLI:

```bash
base64 -i DeveloperID.p12 | gh secret set MACOS_CERTIFICATE_P12
```

```bash
gh secret set MACOS_CERTIFICATE_PASSWORD
```

```bash
gh secret set MACOS_SIGN_IDENTITY --body "Developer ID Application: Your Name (TEAMID1234)"
```

```bash
base64 -i AuthKey_ABC123.p8 | gh secret set APPLE_API_KEY_P8
```

```bash
gh secret set APPLE_API_KEY_ID --body ABC123
```

```bash
gh secret set APPLE_API_ISSUER --body 00000000-0000-0000-0000-000000000000
```

The workflow imports the certificate into a throwaway keychain, stores the API key as a `notarytool` profile, runs `packaging/build-macos-app.sh` with `SIGN_IDENTITY` and `NOTARY_PROFILE`, checks the result with `spctl --assess` and `stapler validate`, and deletes the keychain. Nothing is written to the repository.

A local signed build works the same way: `SIGN_IDENTITY="Developer ID Application: …" NOTARY_PROFILE=<profile> bash packaging/build-macos-app.sh build/macos`, after `xcrun notarytool store-credentials <profile> --key … --key-id … --issuer …`.

## Windows

Toucan 1.0 ships unsigned (decided 2026-11-26): the installer and the portable ZIP. Windows SmartScreen asks once before the first start; [install.md](install.md) shows the steps. Signing can be added later in the `package-windows` job with Azure Trusted Signing or a code-signing certificate, without changing the packages.

## Linux

Packages are not signed. Their SHA-256 checksums are in `SHA256SUMS.txt` on each release.
