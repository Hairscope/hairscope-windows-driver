# Refreshes the installed HairscopeAgent service with the latest publish output.
# Run this in an ELEVATED PowerShell (Administrator).
#
#   powershell -ExecutionPolicy Bypass -File update-service.ps1
#
# It stops the service, copies the freshly published files over the installed
# location, and starts the service again.

$ErrorActionPreference = 'Stop'
$service = 'HairscopeAgent'
$target  = Join-Path $env:ProgramFiles 'Hairscope\Agent'
$source  = Join-Path $PSScriptRoot '..\src\HairscopeAgent\publish'

if (-not (Test-Path $source)) {
    Write-Error "Publish output not found at $source. Run: dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true -o publish"
    exit 1
}
if (-not (Test-Path $target)) {
    Write-Error "Service not installed at $target. Run the installer first (HairscopeAgentSetup)."
    exit 1
}

Write-Host "Stopping $service ..."
& sc.exe stop $service | Out-Null
Start-Sleep -Seconds 2

Write-Host "Copying files to $target ..."
Get-ChildItem $source -Recurse -File | Where-Object { $_.Extension -ne '.pdb' } | ForEach-Object {
    $rel = $_.FullName.Substring((Resolve-Path $source).Path.Length).TrimStart('\')
    $dst = Join-Path $target $rel
    New-Item -ItemType Directory -Force -Path (Split-Path $dst) | Out-Null
    Copy-Item $_.FullName $dst -Force
}

Write-Host "Starting $service ..."
& sc.exe start $service | Out-Null
Start-Sleep -Seconds 1
& sc.exe query $service
Write-Host "Done. The service is running the latest build."
