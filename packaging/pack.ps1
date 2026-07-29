<#
    Build BoxingSim as an MSIX package for the Microsoft Store.

    The Store is the answer to a problem this project could not otherwise solve: an unsigned executable is
    met by SmartScreen, and on a clean Windows 11 install with Smart App Control it can be refused outright.
    Microsoft signs Store packages, so neither applies. Individual developer registration is free.

    Usage:
        .\packaging\pack.ps1                     # build an unsigned .msix, ready for Partner Center
        .\packaging\pack.ps1 -SelfSign           # also sign it locally, so it can be installed and tested here

    A Store submission wants the package UNSIGNED — Microsoft applies its own signature. -SelfSign exists
    only so the package can be installed on this machine to check it actually works before submitting,
    which is the whole lesson of the v0.5.1 release.
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SelfSign,
    [string]$OutDir = "$PSScriptRoot\..\out-msix"
)

$ErrorActionPreference = "Stop"
$repo = Resolve-Path "$PSScriptRoot\.."
$staging = Join-Path $OutDir "staging"

Write-Host "Publishing $Configuration / $Runtime ..." -ForegroundColor Cyan
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
New-Item -ItemType Directory -Force $staging | Out-Null

# Self-contained, but NOT single-file: an MSIX is already one file to the user, and bundling inside the
# bundle only makes the package bigger and the extraction slower.
& dotnet publish "$repo\src\BoxingSim.Desktop" -c $Configuration -r $Runtime --self-contained true `
    -p:PublishSingleFile=false -o $staging --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

# The manifest and the tile assets travel with it.
Copy-Item "$PSScriptRoot\AppxManifest.xml" $staging -Force
$assets = Join-Path $staging "assets\store"
New-Item -ItemType Directory -Force $assets | Out-Null
Copy-Item "$repo\src\BoxingSim.Desktop\assets\store\*.png" $assets -Force

# makeappx and signtool come from the SDK build-tools NuGet package rather than a multi-gigabyte SDK install.
$tools = Get-ChildItem "$env:USERPROFILE\.nuget\packages\microsoft.windows.sdk.buildtools" -Directory -ErrorAction SilentlyContinue |
         Sort-Object Name -Descending | Select-Object -First 1
if (-not $tools) { throw "Microsoft.Windows.SDK.BuildTools not restored. See packaging/README.md." }
$bin = Get-ChildItem "$($tools.FullName)\bin" -Directory | Sort-Object Name -Descending | Select-Object -First 1
$makeappx = Join-Path $bin.FullName "x64\makeappx.exe"
$signtool = Join-Path $bin.FullName "x64\signtool.exe"

$msix = Join-Path $OutDir "BoxingSim.msix"
if (Test-Path $msix) { Remove-Item $msix -Force }

Write-Host "Packing ..." -ForegroundColor Cyan
& $makeappx pack /d $staging /p $msix /o
if ($LASTEXITCODE -ne 0) { throw "makeappx failed" }

if ($SelfSign) {
    # A throwaway certificate so the package can be installed HERE for testing. Its subject must match the
    # manifest's Publisher exactly or Windows refuses the install with a signature mismatch.
    $manifest = [xml](Get-Content "$PSScriptRoot\AppxManifest.xml")
    $publisher = $manifest.Package.Identity.Publisher
    Write-Host "Self-signing as $publisher (test only) ..." -ForegroundColor Cyan

    $cert = New-SelfSignedCertificate -Type Custom -Subject $publisher `
        -KeyUsage DigitalSignature -FriendlyName "BoxingSim test signing" `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")
    $pwd = ConvertTo-SecureString -String "boxingsim-test" -Force -AsPlainText
    $pfx = Join-Path $OutDir "test.pfx"
    Export-PfxCertificate -cert "Cert:\CurrentUser\My\$($cert.Thumbprint)" -FilePath $pfx -Password $pwd | Out-Null
    & $signtool sign /fd SHA256 /a /f $pfx /p "boxingsim-test" $msix
    if ($LASTEXITCODE -ne 0) { throw "signing failed" }
    Write-Host "To trust it locally: Import-Certificate -FilePath (exported .cer) -CertStoreLocation Cert:\LocalMachine\TrustedPeople" -ForegroundColor DarkGray
}

$size = [math]::Round((Get-Item $msix).Length / 1MB, 1)
Write-Host ""
Write-Host "$msix  ($size MB)" -ForegroundColor Green
