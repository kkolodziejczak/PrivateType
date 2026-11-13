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

# The tree of the checkout's commit, or nothing when the folder isn't a repository's own or holds uncommitted work.
function Get-CommittedTree {
    $uncommitted = git -C $Checkout status --porcelain
    if ($LASTEXITCODE -ne 0 -or $uncommitted) { return }
    if (git -C $Checkout rev-parse --show-prefix) { return }  # a folder inside a repository is not that repository's commit
    git -C $Checkout rev-parse 'HEAD^{tree}'
}

# A build is kept under the tree of the commit it was made from.
$tree = Get-CommittedTree
$kept = if ($tree) { Join-Path $temp "privatetype-ui-built-$tree" }
# Builds nobody used up: one whose capture never came, or one whose script was stopped before it could delete it.
Get-ChildItem -Path $temp -Directory -Filter 'privatetype-*' |
    Where-Object { $_.Name -match '^privatetype-(ui-built-[0-9a-f]{40,64}|capture-[0-9a-f]{32})$' -and $_.LastWriteTime -lt (Get-Date).AddHours(-1) } |
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

$build = Join-Path $temp ("privatetype-capture-" + [Guid]::NewGuid().ToString('N'))
$env:PRIVATETYPE_ENGINE_ROOT = Join-Path $build 'no-engine'
try {
    # Taken by renaming, so two captures of one commit never draw from the same folder. Directory.Move fails where
    # Move-Item would put one folder inside another that is already there.
    if ($Output -and $kept) { try { [IO.Directory]::Move($kept, $build) } catch { } }
    if (-not (Test-Path $build)) {
        # A full build into a private folder: no stale XAML, and overlapping builds never share an output.
        dotnet build $project -c Debug --no-incremental -nologo -v quiet -clp:ErrorsOnly --artifacts-path $build -nodeReuse:false -p:UseSharedCompilation=false
        if ($LASTEXITCODE -ne 0) { throw "The build failed." }
    }
    if (-not $Output) {
        # Kept only if the checkout is still the commit the build began from, and no build of that commit is kept already.
        if ($kept -and (Get-CommittedTree) -eq $tree) { try { [IO.Directory]::Move($build, $kept) } catch { } }
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
