# Hairscope Agent - Code Signing

This folder contains scripts and configuration for code signing the Hairscope Agent.

## Files

| File | Purpose |
|------|---------|
| `generate-cert.ps1` | Generate self-signed certificate for **local development only** |
| `sign-local.ps1` | Sign artifacts locally using a PFX certificate |
| `signpath.yml` | SignPath.io configuration for automated signing |
| `SIGNPATH_SETUP.md` | Complete guide for SignPath.io setup (production) |

## Quick Start - Local Development

```powershell
# 1. Generate self-signed cert (run as Administrator)
cd signing
.\generate-cert.ps1

# 2. Build the project
cd ..\src\HairscopeAgent
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish

# 3. Sign locally
cd ..\..\signing
.\sign-local.ps1 -PfxPath .\hairscope-dev-code-sign.pfx -Password "hairscope-dev-123"
```

⚠️ **Self-signed certificates show "Unknown Publisher" warnings** - only for local testing.

## Production Signing - SignPath.io (Free for Open Source)

For a **trusted "Hairscope" publisher** with no warnings:

1. **Open source the project** with an OSI-approved license (MIT, Apache-2.0)
2. **Apply at [signpath.io](https://signpath.io)** - free for open source
3. **Follow `SIGNPATH_SETUP.md`** for complete configuration
4. **Push a version tag** to trigger automated signing:
   ```bash
   git tag v1.0.8
   git push origin v1.0.8
   ```

The GitHub Actions workflow (`.github/workflows/signpath-release.yml`) will:
- Build & publish the executable
- Build the Inno Setup installer
- Sign both via SignPath.io
- Create a GitHub Release with signed artifacts

## Certificate Management

### Never commit these files:
- `*.pfx` - Contains private key!
- `*.p12` - Contains private key!
- `*.cer` - Public cert (OK to share, but not needed in repo)

### For CI/CD:
- Store PFX password in **GitHub Secrets** (`CERT_PASSWORD`)
- SignPath.io token in **GitHub Secrets** (`SIGNPATH_TOKEN`)

## Verification

```powershell
# Verify signed executable
signtool verify /pa /v src/HairscopeAgent/publish/HairscopeAgent.exe

# Verify signed installer
signtool verify /pa /v installer/Output/HairscopeAgentSetup-*.exe
```

## Timestamp Server

All signing uses RFC3161 timestamping via DigiCert:
- URL: `http://timestamp.digicert.com`
- Algorithm: SHA256

This ensures signatures remain valid after certificate expiration.