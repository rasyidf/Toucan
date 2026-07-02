# Toucan Packaging

## Quick Start

### Self-contained executable (no MSIX)

```powershell
dotnet publish Toucan/Toucan.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist/
```

This produces a single `Toucan.exe` (~60-80MB) that runs without .NET installed.

### MSIX Package

Requires: [Windows 10 SDK](https://developer.microsoft.com/windows/downloads/windows-sdk/) (for `makeappx.exe`)

```powershell
.\packaging\Build-Msix.ps1
```

Options:
- `-SkipBuild` — skip the publish step (reuse existing output)
- `-Sign -CertPath path\to\cert.pfx -CertPassword pwd` — sign the MSIX
- `-Version 0.14.0.0` — override version

### Signing for sideloading

Generate a self-signed cert (one-time):
```powershell
New-SelfSignedCertificate -Type Custom -Subject "CN=rasyid.dev" `
    -KeyUsage DigitalSignature -FriendlyName "Toucan Dev" `
    -CertStoreLocation "Cert:\CurrentUser\My" `
    -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")
```

Export to PFX:
```powershell
$cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq "CN=rasyid.dev" }
Export-PfxCertificate -Cert $cert -FilePath packaging\dev-cert.pfx -Password (ConvertTo-SecureString "password" -Force -AsPlainText)
```

Then build signed:
```powershell
.\packaging\Build-Msix.ps1 -Sign -CertPath packaging\dev-cert.pfx -CertPassword password
```

### Installing unsigned MSIX (development)

1. Enable Developer Mode (Settings → For developers)
2. Double-click the `.msix` file
3. Click "Install"

## Assets

Place these in `packaging/Assets/` for proper store-quality icons:
- `StoreLogo.png` — 50x50
- `Square44x44Logo.png` — 44x44
- `Square150x150Logo.png` — 150x150
- `Wide310x150Logo.png` — 310x150

## CI/CD (GitHub Actions)

```yaml
- name: Publish
  run: dotnet publish Toucan/Toucan.csproj -c Release -r win-x64 --self-contained -o publish/

- name: Package MSIX
  run: .\packaging\Build-Msix.ps1 -SkipBuild -Version ${{ github.ref_name }}

- name: Upload artifact
  uses: actions/upload-artifact@v4
  with:
    name: Toucan-${{ github.ref_name }}-x64.msix
    path: dist/*.msix
```
