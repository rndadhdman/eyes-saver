<#
.SYNOPSIS
  Builds every Eyes Saver package into .\dist.

.DESCRIPTION
  Windows:  EyesSaver-<v>-win-x64.msi              per-machine installer (WiX 5)
            EyesSaver-<v>-win-x64-portable.zip     single exe + data folder
  Linux:    eyessaver_<v>_amd64.deb                Debian/Ubuntu package
            EyesSaver-<v>-x86_64.flatpak           Flatpak bundle (runtime from Flathub)
            EyesSaver-<v>-linux-x64-portable.tar.gz

  Needs the .NET 10 SDK. The Linux packages are built in Docker containers (Docker Desktop with
  Linux containers); the Flatpak container runs privileged because flatpak-builder uses
  bubblewrap. Flathub runtimes are cached in the Docker volume "eyessaver-flatpak".

.EXAMPLE
  .\build.ps1
  .\build.ps1 -Version 2.1.1 -SkipFlatpak
#>
param(
    [string]$Version,
    [switch]$SkipTests,
    [switch]$SkipMsi,
    [switch]$SkipLinux,
    [switch]$SkipFlatpak
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$artifacts = Join-Path $root 'artifacts'
$dist = Join-Path $root 'dist'

function Invoke-Native {
    param([string]$What, [scriptblock]$Command)
    Write-Host "==> $What" -ForegroundColor Cyan
    # Windows PowerShell turns native stderr into errors when output is redirected; rely on exit codes instead.
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & $Command } finally { $ErrorActionPreference = $previous }
    if ($LASTEXITCODE -ne 0) { throw "$What failed (exit code $LASTEXITCODE)" }
}

if (-not $Version) {
    [xml]$project = Get-Content (Join-Path $root 'src\EyesSaver.App\EyesSaver.App.csproj')
    $Version = @($project.Project.PropertyGroup.Version | Where-Object { $_ })[0]
}
Write-Host "Eyes Saver $Version" -ForegroundColor Green

Remove-Item -Recurse -Force $dist -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $dist | Out-Null

# ---------------------------------------------------------------- tests
if (-not $SkipTests) {
    Invoke-Native 'Tests' { dotnet run --project (Join-Path $root 'tests\EyesSaver.Tests') -c Release }
}

# ---------------------------------------------------------------- publish
$winPublish = Join-Path $artifacts 'publish\win-x64'
$linuxPublish = Join-Path $artifacts 'publish\linux-x64'
Remove-Item -Recurse -Force $winPublish, $linuxPublish -ErrorAction SilentlyContinue
Invoke-Native 'Publish win-x64' { dotnet publish (Join-Path $root 'src\EyesSaver.App') -c Release -r win-x64 -o $winPublish "-p:Version=$Version" -nologo }
Invoke-Native 'Publish linux-x64' { dotnet publish (Join-Path $root 'src\EyesSaver.App') -c Release -r linux-x64 -o $linuxPublish "-p:Version=$Version" -nologo }

# ---------------------------------------------------------------- Windows portable
Write-Host '==> Windows portable zip' -ForegroundColor Cyan
$portableRoot = Join-Path $artifacts 'stage\win-portable'
$portable = Join-Path $portableRoot "EyesSaver-$Version"
Remove-Item -Recurse -Force $portableRoot -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force (Join-Path $portable 'data') | Out-Null
Copy-Item (Join-Path $winPublish 'EyesSaver.exe') $portable
Copy-Item (Join-Path $root 'packaging\windows\README-portable.txt') (Join-Path $portable 'README.txt')
Set-Content -Encoding utf8 (Join-Path $portable 'data\README.txt') @(
    'Eyes Saver keeps its settings in this folder because the folder exists.',
    'Delete it to keep settings in %LOCALAPPDATA%\EyesSaver instead.')
Compress-Archive -Path $portable -DestinationPath (Join-Path $dist "EyesSaver-$Version-win-x64-portable.zip") -CompressionLevel Optimal

# ---------------------------------------------------------------- MSI
if (-not $SkipMsi) {
    $wixproj = Join-Path $root 'packaging\windows\EyesSaver.Installer.wixproj'
    Invoke-Native 'MSI' { dotnet build $wixproj -c Release "-p:ProductVersion=$Version" "-p:PublishDir=$winPublish" -nologo }
    Get-ChildItem (Join-Path $root 'packaging\windows\bin') -Recurse -Filter "EyesSaver-$Version-win-x64.msi" |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1 |
        Copy-Item -Destination $dist
}

# ---------------------------------------------------------------- Linux (Docker)
if (-not $SkipLinux) {
    # Docker Desktop only sees shared drives, so stage everything under %TEMP%.
    $stage = Join-Path ([IO.Path]::GetTempPath()) 'eyessaver-pkg'
    Remove-Item -Recurse -Force $stage -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force (Join-Path $stage 'out') | Out-Null
    Copy-Item -Recurse $linuxPublish (Join-Path $stage 'app')
    Copy-Item -Recurse (Join-Path $root 'packaging\icons') (Join-Path $stage 'icons')
    Copy-Item -Recurse (Join-Path $root 'packaging\linux') (Join-Path $stage 'linux')
    Copy-Item -Recurse (Join-Path $root 'packaging\flatpak') (Join-Path $stage 'flatpak')

    # Scripts and desktop files must have Unix line endings.
    Get-ChildItem $stage -Recurse -File -Include *.sh, *.yml, *.desktop, *.xml, *.txt | ForEach-Object {
        $text = [IO.File]::ReadAllText($_.FullName) -replace "`r`n", "`n"
        [IO.File]::WriteAllText($_.FullName, $text, (New-Object Text.UTF8Encoding $false))
    }

    Invoke-Native '.deb and portable tarball' {
        docker run --rm -v "${stage}:/pkg" debian:bookworm-slim bash /pkg/linux/build-linux.sh $Version
    }

    if (-not $SkipFlatpak) {
        Invoke-Native 'Flatpak builder image' { docker build -q -t eyessaver-flatpak-builder (Join-Path $root 'packaging\flatpak') }
        Invoke-Native 'Flatpak bundle' {
            docker run --rm --privileged -v eyessaver-flatpak:/root/.local/share/flatpak -v "${stage}:/pkg" `
                eyessaver-flatpak-builder bash /pkg/flatpak/build-flatpak.sh $Version
        }
    }

    Copy-Item (Join-Path $stage 'out\*') $dist
}

# ---------------------------------------------------------------- checksums
$sums = Get-ChildItem $dist -File | Where-Object Name -ne 'SHA256SUMS.txt' | Sort-Object Name | ForEach-Object {
    '{0}  {1}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name
}
[IO.File]::WriteAllLines((Join-Path $dist 'SHA256SUMS.txt'), [string[]]$sums)

Write-Host ''
Get-ChildItem $dist | Format-Table Name, @{ n = 'Size (MB)'; e = { [math]::Round($_.Length / 1MB, 1) } } -AutoSize
