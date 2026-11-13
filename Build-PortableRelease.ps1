[CmdletBinding()]
param(
    [string] $OutputDirectory,
    [string] $Version = '0.0.0',
    # Leaves the folder unzipped, so its executables can be signed before the ZIP and its hash are made.
    [switch] $SkipArchive
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

# Wraps a notice at 80 columns; a bullet's later lines hang under its text.
function Format-NoticeLines {
    param(
        [Parameter(Mandatory)] [string] $Text,
        [string] $Indent = '  '
    )

    $lines = [System.Collections.Generic.List[string]]::new()
    $line = ''
    foreach ($word in $Text -split ' ') {
        if ($line -and ($line.Length + 1 + $word.Length) -gt 80) {
            $lines.Add($line)
            $line = "$Indent$word"
        } elseif ($line) {
            $line += " $word"
        } else {
            $line = $word
        }
    }
    if ($line) { $lines.Add($line) }
    $lines
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
    Copy-ReleaseFile $loaderLicense (Join-Path $licensesDirectory 'VulkanRT-License.txt')
    Copy-ReleaseFile (Join-Path $naudioPackageRoot 'naudio\2.2.1\license.txt') (Join-Path $licensesDirectory 'NAudio-MIT.txt')
    Copy-ReleaseFile (Join-Path $dotnetRoot 'LICENSE.txt') (Join-Path $licensesDirectory 'dotnet-LICENSE.txt')
    Copy-ReleaseFile (Join-Path $dotnetRoot 'ThirdPartyNotices.txt') (Join-Path $licensesDirectory 'dotnet-THIRD-PARTY-NOTICES.txt')

    # The components are the app's own Credits.json, which the Licenses window shows, so the two can't drift.
    $credits = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'src\PrivateType.App\Credits.json') -Raw -Encoding utf8 | ConvertFrom-Json
    $components = foreach ($component in $credits.components) {
        $entry = "* $($component.name)"
        if ($component.maker) { $entry += " $($component.maker)" }
        $entry += ", $($component.release): $($component.license). $($component.role)"
        if ($component.includes) { $entry += " $($component.includes)" }
        if ($component.texts) { $entry += " License texts: $($component.texts)." }
        Format-NoticeLines $entry
    }
    $models = @($credits.models | ForEach-Object { "$($_.license) ($($_.name))" })
    $modelTerms = if ($models.Count -gt 1) { ($models[0..($models.Count - 2)] -join ', ') + " and $($models[-1])" } else { $models -join '' }
    $notices = @(
        'PrivateType third-party notices'
        '================================'
        ''
        'PrivateType source and maintainer-owned assets are available under the MIT License'
        'in PrivateType-MIT.txt. Components included in this portable release remain under'
        'their own terms. This folder contains the full texts and notices used for the'
        'shipped build:'
        ''
    ) + $components + @('') + (Format-NoticeLines -Indent '' "The NVIDIA speech models are not included in this ZIP. They are downloaded separately by the user and are governed by their own terms: $modelTerms.")
    $notices -join [Environment]::NewLine | Set-Content -LiteralPath (Join-Path $licensesDirectory 'THIRD-PARTY-NOTICES.txt') -Encoding utf8
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $PSScriptRoot "artifacts\PrivateType-$Version-win-x64"
}

$appProject = Join-Path $PSScriptRoot 'src\PrivateType.App\PrivateType.App.csproj'
$launcherProject = Join-Path $PSScriptRoot 'src\PrivateType.Launcher\PrivateType.Launcher.csproj'
# Run Get-NemoSpeechRuntime.ps1 first; it verifies and unpacks NVIDIA's pinned release and the Vulkan loader here.
$runtimeRoot = Join-Path $PSScriptRoot '.engine\runtime'
$runtimeSource = Join-Path $runtimeRoot 'bin'
$engineLicenses = Join-Path $runtimeRoot 'share\licenses\nemo-speech'
$loaderLicense = Join-Path $runtimeRoot 'share\licenses\vulkan-runtime\VulkanRT-License.txt'
# The whole bin folder is shipped; these are the files the app cannot run without.
$requiredEngineFiles = @(
    'nemo-speech.exe',
    'nemo_speech_asr.dll',
    'ggml.dll',
    'ggml-base.dll',
    'ggml-cpu.dll',
    'ggml-vulkan.dll',
    'vulkan-1.dll'
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
    $folderBytes = (Get-ChildItem -LiteralPath $publishDirectory -Recurse -File | Measure-Object -Property Length -Sum).Sum
    Write-Host "Folder bytes: $folderBytes"
    if (!$SkipArchive) {
        $archiveCreated = $true
        Compress-Archive -LiteralPath $publishDirectory -DestinationPath $archivePath -CompressionLevel Optimal
        Write-Host "ZIP bytes: $((Get-Item -LiteralPath $archivePath).Length)"
    }
    Write-Host "SUCCESS: $publishDirectory"
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
