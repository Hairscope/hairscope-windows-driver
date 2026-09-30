# SignPath.io Setup Guide for Hairscope Agent

This guide walks through configuring **free code signing via SignPath.io** for the Hairscope Agent project.

## Prerequisites

- ✅ Project is **open source** (OSI-approved license - MIT, Apache-2.0, etc.)
- ✅ GitHub repository with Actions enabled
- ✅ SignPath.io account (free for open source)

---

## Step 1: Create SignPath.io Account

1. Go to [signpath.io](https://signpath.io/) and sign up
2. Choose **"Free for Open Source"** plan
3. Verify your email

---

## Step 2: Create a Project in SignPath.io

1. Click **"New Project"**
2. **Project Name**: `Hairscope Agent`
3. **Project Slug**: `hairscope-agent` (must match `signpath.yml`)
4. **Repository**: Connect your GitHub repo
5. **License**: Select your OSI-approved license (MIT, Apache-2.0, etc.)

---

## Step 3: Configure Code Signing Certificate

### Option A: SignPath Managed Certificate (Recommended - Free)

1. In your project, go to **Certificates** → **New Certificate**
2. Choose **"SignPath Managed Certificate"**
3. **Subject Name**: `Hairscope`
4. **Certificate Type**: `Code Signing`
5. **Validity**: 3 years (auto-renewed)
6. Click **Create** - SignPath handles generation, storage, and renewal

### Option B: Bring Your Own Certificate

If you already have an EV certificate:
1. Choose **"External Certificate"**
2. Upload PFX + password
3. Subject must be `Hairscope`

---

## Step 4: Configure Signing Policies

The `signpath.yml` in this repo defines two policies:

| Policy | Trigger | Use Case |
|--------|---------|----------|
| `release-signing` | Git tag `v*` | Production releases |
| `prerelease-signing` | Git tag `v*-*` (beta, rc) | Pre-release testing |

**In SignPath.io UI:**
1. Go to **Policies** → **New Policy**
2. **Name**: `release-signing`
3. **Artifacts**: Select `HairscopeAgent.exe` and `HairscopeAgentSetup.exe`
4. **Conditions**: Add `Git Tag` condition with pattern `v*`
5. **Certificate**: Select your code signing certificate
6. **Timestamp**: Enable RFC3161, URL: `http://timestamp.digicert.com`
7. Repeat for `prerelease-signing` with pattern `v*-*`

---

## Step 5: Generate SignPath Token

1. In SignPath.io, go to **Account** → **API Tokens**
2. Click **New Token**
3. **Name**: `GitHub Actions`
4. **Scopes**: `signing:write`, `artifacts:read`
5. **Copy the token** (shown only once!)

---

## Step 6: Add GitHub Secrets

In your GitHub repository:
1. Go to **Settings** → **Secrets and variables** → **Actions**
2. **New repository secret**:
   - **Name**: `SIGNPATH_TOKEN`
   - **Value**: Paste the token from Step 5

---

## Step 7: Verify Workflow Permissions

In GitHub repository settings:
1. **Settings** → **Actions** → **General**
2. **Workflow permissions**: Select **"Read and write permissions"**
3. Check **"Allow GitHub Actions to create and approve pull requests"**

---

## Step 8: Test the Pipeline

### Create a test release tag:
```bash
git tag v1.0.0-test
git push origin v1.0.0-test
```

### Or trigger manually:
1. Go to **Actions** → **Build, Sign & Release**
2. Click **Run workflow** → **Run workflow**

---

## Step 9: Verify Signed Artifacts

After workflow completes:

1. Go to **Releases** in GitHub
2. Download `HairscopeAgent.exe` and `HairscopeAgentSetup-*.exe`
3. Verify signatures:

```powershell
# Verify executable
signtool verify /pa /v HairscopeAgent.exe

# Verify installer
signtool verify /pa /v HairscopeAgentSetup-1.0.0-test.exe

# Check certificate details
signtool verify /pa /v /ph HairscopeAgent.exe
```

Expected output:
```
Verifying: HairscopeAgent.exe
Signature Index: 0 (Primary Signature)
Signing Certificate Chain:
    Issued to: Hairscope
    Issued by: SignPath.io Code Signing CA
    ...
Timestamp: [RFC3161 timestamp]
Successfully verified: HairscopeAgent.exe
```

---

## Step 10: Production Release

```bash
# Update version in csproj and .iss
# Commit changes
git commit -am "Release v1.0.8"

# Create and push tag
git tag v1.0.8
git push origin v1.0.8
```

This triggers the full pipeline:
1. ✅ Build & publish
2. ✅ Build installer
3. ✅ Sign via SignPath.io
4. ✅ Create GitHub Release with signed artifacts

---

## Troubleshooting

### "Certificate not found" error
- Ensure certificate subject is exactly `Hairscope`
- Check certificate is **Code Signing** type (not SSL/TLS)

### "Policy not found" error
- Policy name in `signpath.yml` must match SignPath.io exactly
- Check `signpath-project` slug matches

### "Permission denied" on release
- Verify `SIGNPATH_TOKEN` secret is set
- Check workflow permissions (Step 7)

### Signature verification fails
- Ensure timestamp URL is accessible: `http://timestamp.digicert.com`
- Check certificate hasn't expired

---

## Local Development Signing (Optional)

For local testing without SignPath.io:

```powershell
# Generate self-signed cert (run once)
.\signing\generate-cert.ps1

# Sign locally
.\signing\sign-local.ps1 -PfxPath .\signing\hairscope-code-sign.pfx -Password "your-password"
```

**Note**: Self-signed certs show "Unknown Publisher" - only for local testing.

---

## Security Best Practices

1. **Never commit** certificates or private keys to git
2. **Rotate tokens** periodically in SignPath.io
3. **Monitor** signing activity in SignPath.io dashboard
4. **Use branch protection** on `main` branch
5. **Require signed commits** for releases

---

## Resources

- [SignPath.io Documentation](https://docs.signpath.io/)
- [SignPath GitHub Action](https://github.com/signpath/signpath-action)
- [Code Signing Best Practices](https://docs.signpath.io/best-practices)
- [RFC3161 Timestamping](https://docs.signpath.io/timestamping)

---

## Support

- SignPath.io: support@signpath.io
- GitHub Issues: For workflow problems
- This repo: Check `signpath.yml` and `.github/workflows/signpath-release.yml`