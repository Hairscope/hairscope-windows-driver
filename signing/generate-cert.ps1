<#
.SYNOPSIS
    Generates a self-signed code signing certificate for local development/testing.
    Run as Administrator.

.DESCRIPTION
    Creates a self-signed certificate with code signing EKU (1.3.6.1.5.5.7.3.3),
    exports to PFX (with password) and CER (public key).
    Stores certificate in CurrentUser\My (personal store).

.NOTES
    ⚠️  SELF-SIGNED CERTIFICATES ARE NOT TRUSTED BY WINDOWS SMARTSCREEN
    ⚠️  Users will see "Unknown Publisher" warnings
    ⚠️  For PRODUCTION distribution, use SignPath.io with a trusted certificate

    This is ONLY for:
    - Local development testing
    - CI/CD pipeline testing
    - Internal distribution where you control all target machines

.PARAMETER Subject
    Certificate subject (default: "CN=Hairscope Agent")

.PARAMETER FriendlyName
    Friendly name in cert store (default: "Hairscope Agent Code Signing (Dev)")

.PARAMETER PfxPath
    Output path for PFX file (default: .\hairscope-dev-code-sign.pfx)

.PARAMETER CerPath
    Output path for CER file (default: .\hairscope-dev-code-sign.cer)

.PARAMETER Password
    PFX password (default: "hairscope-dev-123" - CHANGE THIS!)

.PARAMETER ValidYears
    Certificate validity in years (default: 2)

.EXAMPLE
    # Run as Administrator
    .\generate-cert.ps1

.EXAMPLE
    .\generate-cert.ps1 -Password "my-secure-password" -ValidYears 3
#>

param(
    [string]$Subject = "CN=Hairscope Agent",
    [string]$FriendlyName = "Hairscope Agent Code Signing (Dev)",
    [string]$PfxPath = ".\hairscope-dev-code-sign.pfx",
    [string]$CerPath = ".\hairscope-dev-code-sign.cer",
    [string]$Password = "hairscope-dev-123",
    [int]$ValidYears = 2
)

$ErrorActionPreference = "Stop"

Write-Host "╔══════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║  Hairscope Agent - Self-Signed Certificate Generator         ║" -ForegroundColor Cyan
Write-Host "║  ⚠️  FOR DEVELOPMENT/TESTING ONLY - NOT FOR PRODUCTION      ║" -ForegroundColor Yellow
Write-Host "╚══════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan

# Check if running as admin
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Warning "Not running as Administrator. Certificate creation may fail."
    Write-Host "Please re-run PowerShell as Administrator." -ForegroundColor Yellow
}

Write-Host "`nGenerating self-signed code signing certificate..." -ForegroundColor Cyan
Write-Host "Subject: $Subject" -ForegroundColor Gray
Write-Host "Friendly Name: $FriendlyName" -ForegroundColor Gray
Write-Host "Validity: $ValidYears years" -ForegroundColor Gray

# Create the certificate with Code Signing EKU
$cert = New-SelfSignedCertificate `
    -Subject $Subject `
    -FriendlyName $FriendlyName `
    -CertStoreLocation "Cert:\CurrentUser\My" `
    -KeyUsage "DigitalSignature" `
    -KeyAlgorithm "RSA" `
    -KeyLength 2048 `
    -Provider "Microsoft Enhanced Cryptographic Provider v1.0" `
    -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3") `  # Code Signing EKU (OID)
    -NotAfter (Get-Date).AddYears($ValidYears) `
    -HashAlgorithm "SHA256"

if (-not $cert) {
    Write-Error "Failed to create certificate"
    exit 1
}

Write-Host "`n✓ Certificate created successfully!" -ForegroundColor Green
Write-Host "  Thumbprint: $($cert.Thumbprint)" -ForegroundColor Gray
Write-Host "  Expires: $($cert.NotAfter)" -ForegroundColor Gray
Write-Host "  Has Private Key: $($cert.HasPrivateKey)" -ForegroundColor Gray

# Export PFX (with private key)
Write-Host "`nExporting PFX (with private key)..." -ForegroundColor Cyan
$securePwd = ConvertTo-SecureString -String $Password -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath $PfxPath -Password $securePwd -Force
Write-Host "✓ PFX exported to: $(Resolve-Path $PfxPath)" -ForegroundColor Green

# Export CER (public key only)
Write-Host "`nExporting CER (public key)..." -ForegroundColor Cyan
Export-Certificate -Cert $cert -FilePath $CerPath -Force
Write-Host "✓ CER exported to: $(Resolve-Path $CerPath)" -ForegroundColor Green

# Instructions
Write-Host "`n" + "="*60 -ForegroundColor Cyan
Write-Host "NEXT STEPS" -ForegroundColor Cyan
Write-Host "="*60 -ForegroundColor Cyan

Write-Host "`n1. SIGN LOCALLY (for testing):" -ForegroundColor Yellow
Write-Host "   .\sign-local.ps1 -PfxPath $PfxPath -Password `"$Password`"" -ForegroundColor Gray

Write-Host "`n2. TRUST LOCALLY (removes 'Unknown Publisher' on THIS machine only):" -ForegroundColor Yellow
Write-Host "   # Run as Administrator:" -ForegroundColor Gray
Write-Host "   Import-Certificate -FilePath $(Resolve-Path $CerPath) -CertStoreLocation Cert:\LocalMachine\TrustedPublisher" -ForegroundColor Gray
Write-Host "   Import-Certificate -FilePath $(Resolve-Path $CerPath) -CertStoreLocation Cert:\LocalMachine\Root" -ForegroundColor Gray

Write-Host "`n3. FOR PRODUCTION (trusted 'Hairscope' publisher):" -ForegroundColor Green
Write-Host "   • Open source the project (OSI-approved license)" -ForegroundColor Gray
Write-Host "   • Apply at https://signpath.io (free for open source)" -ForegroundColor Gray
Write-Host "   • Follow SIGNPATH_SETUP.md in this repo" -ForegroundColor Gray

Write-Host "`n4. SECURITY:" -ForegroundColor Red
Write-Host "   • KEEP $PfxPath SECURE - contains private key!" -ForegroundColor Red
Write-Host "   • NEVER commit PFX files to git" -ForegroundColor Red
Write-Host "   • Use strong password in production" -ForegroundColor Red
Write-Host "   • Store password in GitHub Secrets / Azure Key Vault for CI" -ForegroundColor Gray

Write-Host "`nCertificate Details:" -ForegroundColor Cyan
$cert | Select-Object Subject, FriendlyName, Thumbprint, NotBefore, NotAfter, HasPrivateKey, SignatureAlgorithm | Format-List