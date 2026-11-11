<#
.SYNOPSIS
  Builds the HydrogenXML self-contained publish, a portable zip and the Windows installer.
.EXAMPLE
  .\build-installer.ps1              # version taken from the app csproj
  .\build-installer.ps1 -Version 1.2.0
#>
param(
	[string]$Version,
	[string]$Runtime = 'win-x64'
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$csproj = Join-Path $root 'src\HydrogenXML\HydrogenXML.csproj'
if (-not $Version) { $Version = ([xml](Get-Content $csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1 }

$publish = Join-Path $root 'publish'
$out = Join-Path $root 'artifacts'
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
New-Item -ItemType Directory -Force $out | Out-Null

Write-Host "Publishing HydrogenXML $Version ($Runtime)..."
dotnet publish $csproj -c Release -r $Runtime --self-contained -p:Version=$Version -o $publish
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

$zip = Join-Path $out "HydrogenXML-$Version-win-x64-portable.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $zip -CompressionLevel Optimal
Write-Host "Portable zip: $zip"

$iscc = @(
	(Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source,
	"$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
	"${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
	"$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 not found. Install it with: winget install JRSoftware.InnoSetup' }

& $iscc "/DAppVersion=$Version" (Join-Path $root 'installer\HydrogenXML.iss')
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup failed' }
Write-Host "Installer: $(Join-Path $out "HydrogenXML-Setup-$Version.exe")"
