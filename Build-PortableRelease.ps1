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
        [Parameter(Mandatory)] [string] $EngineRoot,
        [Parameter(Mandatory)] [string] $VcpkgRoot
    )

    $licensesDirectory = Join-Path $PublishDirectory 'licenses'
    $naudioPackageRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }
    $dotnetRoot = Split-Path -Parent (Get-Command dotnet -ErrorAction Stop).Source

    Copy-ReleaseFile (Join-Path $PSScriptRoot 'LICENSE') (Join-Path $PublishDirectory 'LICENSE')
    Copy-ReleaseFile (Join-Path $PSScriptRoot 'LICENSE') (Join-Path $licensesDirectory 'PrivateType-MIT.txt')
    Copy-ReleaseFile (Join-Path $EngineRoot 'LICENSE') (Join-Path $licensesDirectory 'NeMo-Speech.cpp-APACHE-2.0.txt')
    Copy-ReleaseFile (Join-Path $EngineRoot 'NOTICE') (Join-Path $licensesDirectory 'NeMo-Speech.cpp-NOTICE.txt')
    Copy-ReleaseFile (Join-Path $EngineRoot 'THIRD_PARTY_NOTICES.md') (Join-Path $licensesDirectory 'NeMo-Speech.cpp-THIRD-PARTY-NOTICES.md')
    Copy-ReleaseFile (Join-Path $EngineRoot 'ggml\LICENSE') (Join-Path $licensesDirectory 'ggml-MIT.txt')
    Copy-ReleaseFile (Join-Path $EngineRoot 'third_party\cpp-httplib\LICENSE') (Join-Path $licensesDirectory 'cpp-httplib-MIT.txt')
    Copy-ReleaseFile (Join-Path $VcpkgRoot 'installed\x64-windows\share\sentencepiece\copyright') (Join-Path $licensesDirectory 'SentencePiece-APACHE-2.0.txt')
    Copy-ReleaseFile (Join-Path $VcpkgRoot 'installed\x64-windows\share\protobuf\copyright') (Join-Path $licensesDirectory 'Protobuf-BSD-3-Clause.txt')
    Copy-ReleaseFile (Join-Path $VcpkgRoot 'installed\x64-windows\share\abseil\copyright') (Join-Path $licensesDirectory 'Abseil-APACHE-2.0.txt')
    Copy-ReleaseFile (Join-Path $VcpkgRoot 'installed\x64-windows\share\utf8-range\copyright') (Join-Path $licensesDirectory 'utf8-range-MIT.txt')
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

* NeMo-Speech.cpp (pinned commit 1118951337094db3b362fbf1b27e871696f10590):
  Apache-2.0 license, NVIDIA NOTICE, and upstream third-party notices.
* ggml and cpp-httplib: MIT.
* SentencePiece and Abseil: Apache-2.0.
* Protocol Buffers: BSD 3-Clause.
* utf8-range and NAudio 2.2.1: MIT.
* Self-contained .NET runtime: .NET Library License and accompanying notices.

PrivateType applies a Windows linker patch to the pinned NeMo-Speech.cpp source;
the patch is available in the source repository at
patches/nemo-speech-windows-sentencepiece-absl.patch.

The NVIDIA Nemotron model is not included in this ZIP. It is downloaded separately
by the user and is governed by its own OpenMDW-1.1 terms.
'@ | Set-Content -LiteralPath (Join-Path $licensesDirectory 'THIRD-PARTY-NOTICES.txt') -Encoding utf8
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $PSScriptRoot 'artifacts\PrivateType-win-x64'
}

$appProject = Join-Path $PSScriptRoot 'src\PrivateType.App\PrivateType.App.csproj'
$engineRoot = Join-Path $PSScriptRoot '.engine\NeMo-Speech.cpp'
$vcpkgRoot = Join-Path $PSScriptRoot '.engine\vcpkg'
$runtimeSource = Join-Path $PSScriptRoot '.engine\build-cpu-realtime-manual\bin'
$requiredEngineFiles = @(
    'nemo-speech.exe',
    'nemo_speech_asr.dll',
    'nemo_speech_asr_c.dll',
    'ggml.dll',
    'ggml-base.dll',
    'ggml-cpu.dll',
    'abseil_dll.dll',
    'libprotobuf.dll'
)

if (Test-Path -LiteralPath $OutputDirectory) {
    throw "Release output already exists and will not be overwritten: $OutputDirectory"
}

foreach ($file in $requiredEngineFiles) {
    if (!(Test-Path -LiteralPath (Join-Path $runtimeSource $file))) {
        throw "The verified local engine runtime is incomplete; missing: $file"
    }
}

$publishDirectory = Join-Path $OutputDirectory 'PrivateType'
$engineDestination = Join-Path $publishDirectory 'engine\bin'
$archivePath = "$OutputDirectory.zip"
$outputCreated = $false

try {
    New-Item -ItemType Directory -Force -Path $publishDirectory | Out-Null
    $outputCreated = $true
    & dotnet publish $appProject --configuration Release --runtime win-x64 --self-contained true --output $publishDirectory -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false "-p:Version=$numericVersion" "-p:FileVersion=$numericVersion" "-p:AssemblyVersion=$numericVersion"
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed (exit code $LASTEXITCODE)."
    }

    New-Item -ItemType Directory -Force -Path $engineDestination | Out-Null
    foreach ($file in $requiredEngineFiles) {
        Copy-Item -LiteralPath (Join-Path $runtimeSource $file) -Destination $engineDestination
    }
    Add-ReleaseNotices -PublishDirectory $publishDirectory -EngineRoot $engineRoot -VcpkgRoot $vcpkgRoot

    if (!(Test-Path -LiteralPath (Join-Path $publishDirectory 'PrivateType.exe'))) {
        throw 'Portable app executable was not published.'
    }
    if (!(Test-Path -LiteralPath (Join-Path $engineDestination 'nemo-speech.exe'))) {
        throw 'Portable engine executable was not copied.'
    }
    if (Test-Path -LiteralPath (Join-Path $publishDirectory 'models')) {
        throw 'Portable release must not include a downloaded model.'
    }
    if (Test-Path -LiteralPath $archivePath) {
        throw "Archive already exists and will not be overwritten: $archivePath"
    }

    Compress-Archive -LiteralPath $publishDirectory -DestinationPath $archivePath -CompressionLevel Optimal
    $folderBytes = (Get-ChildItem -LiteralPath $publishDirectory -Recurse -File | Measure-Object -Property Length -Sum).Sum
    $archiveBytes = (Get-Item -LiteralPath $archivePath).Length
    Write-Host "SUCCESS: $publishDirectory"
    Write-Host "Folder bytes: $folderBytes"
    Write-Host "ZIP bytes: $archiveBytes"
}
catch {
    if (Test-Path -LiteralPath $archivePath) {
        Remove-Item -LiteralPath $archivePath -Force
    }
    if ($outputCreated -and (Test-Path -LiteralPath $OutputDirectory)) {
        Remove-Item -LiteralPath $OutputDirectory -Recurse -Force
    }
    throw
}
