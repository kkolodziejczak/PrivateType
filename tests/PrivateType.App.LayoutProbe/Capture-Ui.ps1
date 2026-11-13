# Builds a checkout's layout probe and draws every window as <screen>/<state>-dark.png into the output folder, for design review.
# Usage: Capture-Ui.ps1 <checkout> <output folder>   builds, draws, and deletes what it built.
#        Capture-Ui.ps1 <checkout>                   only builds, and keeps the build for the capture of the same commit.
# A design climb calls it the second way before a capture waits for its turn, so a compile error shows in seconds and
# the turn itself is spent drawing. It launches nothing but the probe, which draws offscreen and exits, and no build
# server or MSBuild node outlives it. The engine fetch is skipped: drawing needs no engine.
param(
    [Parameter(Mandatory)][string]$Checkout,
    [string]$Output
)
$ErrorActionPreference = 'Stop'
$project = Join-Path $Checkout 'tests\PrivateType.App.LayoutProbe\PrivateType.App.LayoutProbe.csproj'
$temp = [IO.Path]::GetTempPath()

# A build is kept under the tree of the commit it was made from, and only for a checkout with nothing uncommitted.
$uncommitted = git -C $Checkout status --porcelain
$tree = if ($LASTEXITCODE -eq 0 -and -not $uncommitted) { git -C $Checkout rev-parse 'HEAD^{tree}' }
$kept = if ($tree) { Join-Path $temp "privatetype-ui-built-$tree" }
# A build nobody drew from, such as one whose capture was stopped while it waited.
Get-ChildItem -Path $temp -Directory -Filter 'privatetype-ui-built-*' |
    Where-Object { $_.LastWriteTime -lt (Get-Date).AddHours(-1) } |
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

$build = Join-Path $temp ("privatetype-capture-" + [Guid]::NewGuid().ToString('N'))
$env:PRIVATETYPE_ENGINE_ROOT = Join-Path $build 'no-engine'
try {
    # Taken by renaming, so two captures of one commit never draw from the same folder.
    if ($Output -and $kept -and (Test-Path $kept)) { Move-Item $kept $build -ErrorAction SilentlyContinue }
    if (-not (Test-Path $build)) {
        # A full build into a private folder: no stale XAML, and overlapping builds never share an output.
        dotnet build $project -c Debug --no-incremental -nologo -v quiet -clp:ErrorsOnly --artifacts-path $build -nodeReuse:false -p:UseSharedCompilation=false
        if ($LASTEXITCODE -ne 0) { throw "The build failed." }
    }
    if (-not $Output) {
        if ($kept) {
            Remove-Item -Recurse -Force $kept -ErrorAction SilentlyContinue
            Move-Item $build $kept -ErrorAction SilentlyContinue
        }
        return
    }

    $probe = Get-ChildItem -Path "$build\bin" -Recurse -Filter 'PrivateType.App.LayoutProbe.exe' | Select-Object -First 1
    if (-not $probe) { throw "The build wrote no probe." }
    New-Item -ItemType Directory -Force $Output | Out-Null
    $process = Start-Process -FilePath $probe.FullName -ArgumentList '--capture', "`"$Output`"" -NoNewWindow -PassThru
    $null = $process.Handle  # keeps the exit code readable after the process ends
    if (-not $process.WaitForExit(180000)) {
        taskkill /PID $process.Id /T /F | Out-Null
        throw "The probe took longer than three minutes."
    }
    if ($process.ExitCode -ne 0) { throw "The probe failed with exit code $($process.ExitCode)." }
}
finally {
    Remove-Item -Recurse -Force $build -ErrorAction SilentlyContinue
}
