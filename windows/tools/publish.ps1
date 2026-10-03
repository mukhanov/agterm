# Publish the agterm Windows app as a self-contained folder + zip.
# Usage: powershell -File windows\tools\publish.ps1 [version]
param([string]$Version = "0.1.0")

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "dist"

& dotnet publish (Join-Path $root "src\Agterm.Windows\Agterm.Windows.csproj") `
    -c Release -r win-x64 --self-contained true `
    -p:WindowsAppSDKSelfContained=true -p:WindowsPackageType=None `
    -o $out
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$zip = Join-Path $root "..\agterm-windows-$Version.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path (Join-Path $out "*") -DestinationPath $zip
Write-Host "published: $out"
Write-Host "zip: $zip"
