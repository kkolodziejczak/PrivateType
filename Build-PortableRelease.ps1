[CmdletBinding()]
param(
    [string] $OutputDirectory,
    [string] $Version = '0.0.0'
)

$ErrorActionPreference = 'Stop'

$parsedVersion = [Version]::new()
if (![Version]::TryParse($Version, [ref] $parsedVersion)) {
    throw "Release version must be numeric, for example 1.2.3: $Version"
}
$numericVersion = '{0}.{1}.{2}.{3}' -f $parsedVersion.Major, $parsedVersion.Minor, [Math]::Max(0, $parsedVersion.Build), [Math]::Max(0, $parsedVersion.Revision)

function Copy-ReleaseFile {
    param(
        [Parameter(Mandatory)] [string] $SourcePath,
        [Parameter(Mandatory)] [string] $DestinationPath
    )

    if (!(Test-Path -LiteralPath $SourcePath -PathType Leaf)) {
        throw "Required release notice is missing: $SourcePath"
    }

    $destinationDirectory = Split-Path -Parent $DestinationPath
    New-Item -ItemType Directory -Force -Path $destinationDirectory | Out-Null
    Copy-Item -LiteralPath $SourcePath -Destination $DestinationPath
}

function Add-ReleaseNotices {
    param(
        [Parameter(Mandatory)] [string] $PublishDirectory,
        [Parameter(Mandatory)] [string] $EngineLicenses
    )

    $licensesDirectory = Join-Path $PublishDirectory 'licenses'
    $naudioPackageRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }
    $dotnetRoot = Split-Path -Parent (Get-Command dotnet -ErrorAction Stop).Source

    Copy-ReleaseFile (Join-Path $PSScriptRoot 'LICENSE') (Join-Path $PublishDirectory 'LICENSE')
    Copy-ReleaseFile (Join-Path $PSScriptRoot 'LICENSE') (Join-Path $licensesDirectory 'PrivateType-MIT.txt')
    # NVIDIA's release ships the license of every component inside its binaries; keep them as published.
    foreach ($file in $requiredEngineNotices) {
        Copy-ReleaseFile (Join-Path $EngineLicenses $file) (Join-Path $licensesDirectory "NeMo-Speech.cpp\$file")
    }
    Copy-ReleaseFile (Join-Path $naudioPackageRoot 'naudio\2.2.1\license.txt') (Join-Path $licensesDirectory 'NAudio-MIT.txt')
    Copy-ReleaseFile (Join-Path $dotnetRoot 'LICENSE.txt') (Join-Path $licensesDirectory 'dotnet-LICENSE.txt')
    Copy-ReleaseFile (Join-Path $dotnetRoot 'ThirdPartyNotices.txt') (Join-Path $licensesDirectory 'dotnet-THIRD-PARTY-NOTICES.txt')

    @'
PrivateType third-party notices
================================

PrivateType source and maintainer-owned assets are available under the MIT License
in PrivateType-MIT.txt. Components included in this portable release remain under
their own terms. This folder contains the full texts and notices used for the
shipped build:

* NeMo-Speech.cpp 0.2.0, NVIDIA's unmodified Windows x64 Vulkan release
  (nemo-speech-0.2.0-windows-x86_64-vulkan.zip, SHA-256
  edf15a04ba98740aa0ab75ae4681ac7ad0a488b22b35ce2cbebd10590bb1c1e1):
  Apache-2.0 license, NVIDIA NOTICE, and the licenses NVIDIA ships for ggml,
  llama.cpp, cpp-httplib, miniaudio, Abseil, Protocol Buffers, SentencePiece,
  and utf8-range, in the NeMo-Speech.cpp folder. The Microsoft Visual C++
  runtime libraries in engine\bin are redistributed as part of that release.
* NAudio 2.2.1: MIT.
* The spoken cue recordings were generated with Kokoro-82M by hexgrad
  (Apache-2.0, https://huggingface.co/hexgrad/Kokoro-82M); no model is included.
* Self-contained .NET runtime: .NET Library License and accompanying notices.

The NVIDIA speech models are not included in this ZIP. They are downloaded
separately by the user and are governed by their own terms: OpenMDW-1.1
(Nemotron) and CC-BY-4.0 (Parakeet).
'@ | Set-Content -LiteralPath (Join-Path $licensesDirectory 'THIRD-PARTY-NOTICES.txt') -Encoding utf8
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $PSScriptRoot "artifacts\PrivateType-$Version-win-x64"
}

$appProject = Join-Path $PSScriptRoot 'src\PrivateType.App\PrivateType.App.csproj'
$launcherProject = Join-Path $PSScriptRoot 'src\PrivateType.Launcher\PrivateType.Launcher.csproj'
# Run Get-NemoSpeechRuntime.ps1 first; it verifies and unpacks NVIDIA's pinned release here.
$runtimeRoot = Join-Path $PSScriptRoot '.engine\runtime'
$runtimeSource = Join-Path $runtimeRoot 'bin'
$engineLicenses = Join-Path $runtimeRoot 'share\licenses\nemo-speech'
# The whole bin folder is shipped; these are the files the app cannot run without.
$requiredEngineFiles = @(
    'nemo-speech.exe',
    'nemo_speech_asr.dll',
    'ggml.dll',
    'ggml-base.dll',
    'ggml-cpu.dll',
    'ggml-vulkan.dll'
)
$requiredEngineNotices = @(
    'LICENSE',
    'NOTICE',
    'THIRD_PARTY_NOTICES.md',
    'third_party\cpp-httplib\LICENSE',
    'third_party\ggml\LICENSE',
    'third_party\llama.cpp\LICENSE',
    'third_party\miniaudio\LICENSE',
    'third_party\vcpkg\abseil\LICENSE',
    'third_party\vcpkg\protobuf\LICENSE',
    'third_party\vcpkg\sentencepiece\LICENSE',
    'third_party\vcpkg\utf8-range\LICENSE'
)

$publishDirectory = Join-Path $OutputDirectory "PrivateType $Version"
$appPublishDirectory = Join-Path $publishDirectory 'app'
$launcherPublishDirectory = Join-Path $OutputDirectory '.launcher-publish'
$engineDestination = Join-Path $appPublishDirectory 'engine\bin'
$archivePath = "$OutputDirectory.zip"

if (Test-Path -LiteralPath $OutputDirectory) {
    throw "Release output already exists and will not be overwritten: $OutputDirectory"
}
if (Test-Path -LiteralPath $archivePath) {
    throw "Archive already exists and will not be overwritten: $archivePath"
}

foreach ($file in $requiredEngineFiles) {
    if (!(Test-Path -LiteralPath (Join-Path $runtimeSource $file))) {
        throw "The verified local engine runtime is incomplete; missing: $file"
    }
}

$outputCreated = $false
$archiveCreated = $false

try {
    New-Item -ItemType Directory -Force -Path $appPublishDirectory | Out-Null
    $outputCreated = $true
    & dotnet publish $appProject --configuration Release --runtime win-x64 --self-contained true --output $appPublishDirectory -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false "-p:Version=$numericVersion" "-p:FileVersion=$numericVersion" "-p:AssemblyVersion=$numericVersion"
    if ($LASTEXITCODE -ne 0) {
        throw "PrivateType application publish failed (exit code $LASTEXITCODE)."
    }

    & dotnet publish $launcherProject --configuration Release --runtime win-x64 --self-contained true --output $launcherPublishDirectory -p:DebugType=None -p:DebugSymbols=false "-p:Version=$numericVersion" "-p:FileVersion=$numericVersion" "-p:AssemblyVersion=$numericVersion"
    if ($LASTEXITCODE -ne 0) {
        throw "PrivateType launcher publish failed (exit code $LASTEXITCODE)."
    }
    Copy-Item -LiteralPath (Join-Path $launcherPublishDirectory 'PrivateType.exe') -Destination (Join-Path $publishDirectory 'PrivateType.exe')
    Remove-Item -LiteralPath $launcherPublishDirectory -Recurse -Force

    New-Item -ItemType Directory -Force -Path $engineDestination | Out-Null
    Copy-Item -Path (Join-Path $runtimeSource '*') -Destination $engineDestination
    Add-ReleaseNotices -PublishDirectory $appPublishDirectory -EngineLicenses $engineLicenses

    @"
PrivateType $Version
===================

Double-click PrivateType.exe to start the application.

Keep this complete folder together. The app folder contains the local runtime,
speech engine, portable settings, and open-source license notices. By default,
the verified model is shared in the current Windows user's PrivateType cache;
create app\models before first launch to opt into portable-local model storage.

PrivateType performs speech recognition locally and does not retain audio or
dictated text. The speech model is downloaded and verified on first launch.

Project and release information:
https://github.com/kkolodziejczak/PrivateType
"@ | Set-Content -LiteralPath (Join-Path $publishDirectory 'README - Start here.txt') -Encoding utf8

    if (!(Test-Path -LiteralPath (Join-Path $publishDirectory 'PrivateType.exe'))) {
        throw 'Portable launcher executable was not published.'
    }
    if (!(Test-Path -LiteralPath (Join-Path $appPublishDirectory 'PrivateType.exe'))) {
        throw 'Portable application executable was not published.'
    }
    if (!(Test-Path -LiteralPath (Join-Path $engineDestination 'nemo-speech.exe'))) {
        throw 'Portable engine executable was not copied.'
    }
    if (Test-Path -LiteralPath (Join-Path $appPublishDirectory 'models')) {
        throw 'Portable release must not include a downloaded model.'
    }
    $archiveCreated = $true
    Compress-Archive -LiteralPath $publishDirectory -DestinationPath $archivePath -CompressionLevel Optimal
    $folderBytes = (Get-ChildItem -LiteralPath $publishDirectory -Recurse -File | Measure-Object -Property Length -Sum).Sum
    $archiveBytes = (Get-Item -LiteralPath $archivePath).Length
    Write-Host "SUCCESS: $publishDirectory"
    Write-Host "Folder bytes: $folderBytes"
    Write-Host "ZIP bytes: $archiveBytes"
}
catch {
    if ($archiveCreated -and (Test-Path -LiteralPath $archivePath)) {
        Remove-Item -LiteralPath $archivePath -Force
    }
    if ($outputCreated -and (Test-Path -LiteralPath $OutputDirectory)) {
        Remove-Item -LiteralPath $OutputDirectory -Recurse -Force
    }
    throw
}
