<#
.SYNOPSIS
    Signs Hairscope Agent artifacts locally using a PFX certificate.
    For development/testing only - use SignPath.io for production releases.

.DESCRIPTION
    Signs the published executable and Inno Setup installer with a code signing certificate.
    Requires a PFX file with private key (self-signed for dev, or EV cert for production).

.PARAMETER PfxPath
    Path to the .pfx certificate file (with private key)

.PARAMETER Password
    Password for the PFX file

.PARAMETER TimestampUrl
    RFC3161 timestamp server URL (default: DigiCert)

.PARAMETER ArtifactsDir
    Directory containing artifacts to sign (default: publish output)

.EXAMPLE
    .\sign-local.ps1 -PfxPath .\signing\hairscope-code-sign.pfx -Password "mypassword"

.EXAMPLE
    .\sign-local.ps1 -PfxPath "C:\Certs\hairscope-ev.pfx" -Password $env:CERT_PASSWORD -TimestampUrl "http://timestamp.sectigo.com"
#>

param(
    [Parameter(Mandatory=$true)]
    [string]$PfxPath,

    [Parameter(Mandatory=$true)]
    [string]$Password,

    [string]$TimestampUrl = "http://timestamp.digicert.com",

    [string]$ArtifactsDir = "src/HairscopeAgent/publish",

    [string]$InstallerDir = "installer/Output"
)

$ErrorActionPreference = "Stop"

# Check for signtool
$signtoolPaths = @(
    "C:\Program Files (x86)\Windows Kits\10\bin\*\x64\signtool.exe",
    "C:\Program Files\Windows Kits\10\bin\*\x64\signtool.exe",
    "C:\Program Files (x86)\Windows Kits\10\App Certification Kit\signtool.exe"
)

$signtool = $null
foreach ($path in $signtoolPaths) {
    $resolved = Resolve-Path $path -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($resolved) {
        $signtool = $resolved.Path
        break
    }
}

if (-not $signtool) {
    Write-Error "signtool.exe not found. Install Windows SDK (https://developer.microsoft.com/windows/downloads/windows-sdk/)"
    exit 1
}

Write-Host "Using signtool: $signtool" -ForegroundColor Cyan

# Verify PFX exists
if (-not (Test-Path $PfxPath)) {
    Write-Error "PFX file not found: $PfxPath"
    exit 1
}

# Find executable to sign
$exePath = Join-Path $ArtifactsDir "HairscopeAgent.exe"
if (-not (Test-Path $exePath)) {
    Write-Error "Executable not found: $exePath. Run 'dotnet publish' first."
    exit 1
}

# Find installer to sign
$installerFiles = Get-ChildItem $InstallerDir -Filter "HairscopeAgentSetup-*.exe" | Sort-Object LastWriteTime -Descending
if ($installerFiles.Count -eq 0) {
    Write-Warning "No installer found in $InstallerDir. Skipping installer signing."
    $installerPath = $null
} else {
    $installerPath = $installerFiles[0].FullName
}

# Signing parameters
$signParams = @(
    "sign"
    "/fd", "sha256"
    "/tr", $TimestampUrl
    "/td", "sha256"
    "/a"
    "/f", $PfxPath
    "/p", $Password
    "/v"  # Verbose
)

Write-Host "`n=== Signing HairscopeAgent.exe ===" -ForegroundColor Cyan
$exeArgs = $signParams + @($exePath)
& $signtool @exeArgs
if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to sign executable"
    exit 1
}
Write-Host "✓ Executable signed successfully" -ForegroundColor Green

if ($installerPath) {
    Write-Host "`n=== Signing Installer ===" -ForegroundColor Cyan
    $instArgs = $signParams + @($installerPath)
    & $signtool @instArgs
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to sign installer"
        exit 1
    }
    Write-Host "✓ Installer signed successfully" -ForegroundColor Green
}

# Verify signatures
Write-Host "`n=== Verifying Signatures ===" -ForegroundColor Cyan

Write-Host "Verifying executable..." -ForegroundColor Gray
& $signtool verify /pa /v $exePath
if ($LASTEXITCODE -ne 0) {
    Write-Warning "Executable verification failed"
} else {
    Write-Host "✓ Executable verification passed" -ForegroundColor Green
}

if ($installerPath) {
    Write-Host "Verifying installer..." -ForegroundColor Gray
    & $signtool verify /pa /v $installerPath
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "Installer verification failed"
    } else {
        Write-Host "✓ Installer verification passed" -ForegroundColor Green
    }
}

Write-Host "`n=== All Done ===" -ForegroundColor Green
Write-Host "Signed artifacts:" -ForegroundColor Cyan
Write-Host "  $exePath" -ForegroundColor Gray
if ($installerPath) { Write-Host "  $installerPath" -ForegroundColor Gray }