[CmdletBinding()]
param(
    [string] $ArchivePath,
    [string] $WorkingDirectory
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($ArchivePath)) {
    $ArchivePath = Join-Path $PSScriptRoot 'artifacts\PrivateType-win-x64.zip'
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

try {
    Expand-Archive -LiteralPath $ArchivePath -DestinationPath $WorkingDirectory -ErrorAction Stop
    $releaseDirectory = Join-Path $WorkingDirectory 'PrivateType'

    Assert-ReleaseFile (Join-Path $releaseDirectory 'PrivateType.exe')
    foreach ($engineFile in $engineFiles) {
        Assert-ReleaseFile (Join-Path $releaseDirectory "engine\bin\$engineFile")
    }

    if (Test-Path -LiteralPath (Join-Path $releaseDirectory 'models')) {
        throw 'Portable archive must not contain a downloaded model.'
    }

    $relocatedRoot = Join-Path $WorkingDirectory 'relocated'
    $relocatedDirectory = Join-Path $relocatedRoot 'PrivateType'
    New-Item -ItemType Directory -Path $relocatedRoot | Out-Null
    Move-Item -LiteralPath $releaseDirectory -Destination $relocatedRoot

    Assert-ReleaseFile (Join-Path $relocatedDirectory 'PrivateType.exe')
    Assert-ReleaseFile (Join-Path $relocatedDirectory 'engine\bin\nemo-speech.exe')
    if (Test-Path -LiteralPath (Join-Path $relocatedDirectory 'models')) {
        throw 'Relocated portable archive unexpectedly contains a downloaded model.'
    }

    Write-Host "PASS: clean unpack and whole-folder relocation validated: $relocatedDirectory"
}
finally {
    if (Test-Path -LiteralPath $WorkingDirectory) {
        Remove-Item -LiteralPath $WorkingDirectory -Recurse -Force
    }
}
