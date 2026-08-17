[CmdletBinding()]
param(
    [string] $ArchivePath,
    [string] $WorkingDirectory,
    [string] $ExpectedVersion = '0.0.0'
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($ArchivePath)) {
    $ArchivePath = Join-Path $PSScriptRoot "artifacts\PrivateType-$ExpectedVersion-win-x64.zip"
}

if ([string]::IsNullOrWhiteSpace($WorkingDirectory)) {
    $WorkingDirectory = Join-Path ([System.IO.Path]::GetTempPath()) "private-type-release-smoke-$([Guid]::NewGuid().ToString('N'))"
}

function Assert-ReleaseFile {
    param([Parameter(Mandatory)] [string] $Path)

    if (!(Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Portable release is missing required file: $Path"
    }
}

if (!(Test-Path -LiteralPath $ArchivePath -PathType Leaf)) {
    throw "Portable release archive was not found: $ArchivePath"
}

if (Test-Path -LiteralPath $WorkingDirectory) {
    throw "Smoke-test directory already exists and will not be overwritten: $WorkingDirectory"
}

$engineFiles = @(
    'nemo-speech.exe',
    'nemo_speech_asr.dll',
    'nemo_speech_asr_c.dll',
    'ggml.dll',
    'ggml-base.dll',
    'ggml-cpu.dll',
    'abseil_dll.dll',
    'libprotobuf.dll'
)

$noticeFiles = @(
    'LICENSE',
    'licenses\THIRD-PARTY-NOTICES.txt',
    'licenses\PrivateType-MIT.txt',
    'licenses\NeMo-Speech.cpp-APACHE-2.0.txt',
    'licenses\NeMo-Speech.cpp-NOTICE.txt',
    'licenses\NeMo-Speech.cpp-THIRD-PARTY-NOTICES.md',
    'licenses\ggml-MIT.txt',
    'licenses\cpp-httplib-MIT.txt',
    'licenses\SentencePiece-APACHE-2.0.txt',
    'licenses\Protobuf-BSD-3-Clause.txt',
    'licenses\Abseil-APACHE-2.0.txt',
    'licenses\utf8-range-MIT.txt',
    'licenses\NAudio-MIT.txt',
    'licenses\dotnet-LICENSE.txt',
    'licenses\dotnet-THIRD-PARTY-NOTICES.txt'
)

try {
    Expand-Archive -LiteralPath $ArchivePath -DestinationPath $WorkingDirectory -ErrorAction Stop
    $releaseDirectory = Join-Path $WorkingDirectory "PrivateType $ExpectedVersion"

    $launcherPath = Join-Path $releaseDirectory 'PrivateType.exe'
    $startHerePath = Join-Path $releaseDirectory 'README - Start here.txt'
    $executablePath = Join-Path $releaseDirectory 'app\PrivateType.exe'
    Assert-ReleaseFile $launcherPath
    Assert-ReleaseFile $startHerePath
    Assert-ReleaseFile $executablePath
    $unexpectedTopLevelEntries = Get-ChildItem -LiteralPath $releaseDirectory | Where-Object Name -NotIn @('PrivateType.exe', 'README - Start here.txt', 'app')
    if ($unexpectedTopLevelEntries) {
        throw "Portable release has unexpected top-level entries: $($unexpectedTopLevelEntries.Name -join ', ')"
    }
    if (![string]::IsNullOrWhiteSpace($ExpectedVersion)) {
        $expected = [Version]::new()
        $actual = [Version]::new()
        if (![Version]::TryParse($ExpectedVersion, [ref] $expected)) {
            throw "Expected version must be numeric, for example 1.2.3: $ExpectedVersion"
        }
        $expected = [Version]::new($expected.Major, $expected.Minor, [Math]::Max(0, $expected.Build), [Math]::Max(0, $expected.Revision))
        foreach ($versionedExecutable in @($launcherPath, $executablePath)) {
            $actualText = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($versionedExecutable).FileVersion
            if (![Version]::TryParse($actualText, [ref] $actual) -or $actual -ne $expected) {
                throw "Portable executable version mismatch for $versionedExecutable. Expected $expected; actual $actualText."
            }
        }
    }
    foreach ($engineFile in $engineFiles) {
        Assert-ReleaseFile (Join-Path $releaseDirectory "app\engine\bin\$engineFile")
    }
    foreach ($noticeFile in $noticeFiles) {
        Assert-ReleaseFile (Join-Path $releaseDirectory "app\$noticeFile")
    }

    if (Test-Path -LiteralPath (Join-Path $releaseDirectory 'app\models')) {
        throw 'Portable archive must not contain a downloaded model.'
    }

    $relocatedRoot = Join-Path $WorkingDirectory 'relocated'
    $relocatedDirectory = Join-Path $relocatedRoot "PrivateType $ExpectedVersion"
    New-Item -ItemType Directory -Path $relocatedRoot | Out-Null
    Move-Item -LiteralPath $releaseDirectory -Destination $relocatedRoot

    Assert-ReleaseFile (Join-Path $relocatedDirectory 'PrivateType.exe')
    Assert-ReleaseFile (Join-Path $relocatedDirectory 'app\PrivateType.exe')
    Assert-ReleaseFile (Join-Path $relocatedDirectory 'app\engine\bin\nemo-speech.exe')
    if (Test-Path -LiteralPath (Join-Path $relocatedDirectory 'app\models')) {
        throw 'Relocated portable archive unexpectedly contains a downloaded model.'
    }

    Write-Host "PASS: clean unpack and whole-folder relocation validated: $relocatedDirectory"
}
finally {
    if (Test-Path -LiteralPath $WorkingDirectory) {
        Remove-Item -LiteralPath $WorkingDirectory -Recurse -Force
    }
}
