<#
.SYNOPSIS
    Measures how long the app takes to open and how much memory it uses.

.DESCRIPTION
    Opens and closes the app several times. It only reads monitors: nothing is applied. The first run is
    discarded by default, since it pays for loading files from disk that later runs find in the OS cache,
    and is reported separately because that is what opening the app after a while feels like.

.EXAMPLE
    ./scripts/measure-startup.ps1 -Exe .\publish\WallpaperEditor.App.exe -Runs 8
#>
param(
    [Parameter(Mandatory)] [string] $Exe,
    [int] $Runs = 8
)

$ErrorActionPreference = 'Stop'

$results = foreach ($i in 1..$Runs) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $process = Start-Process $Exe -PassThru
    while ($process.MainWindowHandle -eq 0 -and $clock.ElapsedMilliseconds -lt 20000) {
        Start-Sleep -Milliseconds 5
        $process.Refresh()
    }

    $windowMs = $clock.ElapsedMilliseconds

    # Usable: the window has drained its queue, including the monitor list filled in when it loaded.
    # Asking UI Automation instead would disturb the measurement, and was seen to hang.
    [void]$process.WaitForInputIdle(5000)
    $usableMs = $clock.ElapsedMilliseconds

    Start-Sleep -Milliseconds 1500
    $process.Refresh()
    $workingSet = $process.WorkingSet64 / 1MB
    $private = $process.PrivateMemorySize64 / 1MB

    [void]$process.CloseMainWindow()
    if (-not $process.WaitForExit(5000)) { throw 'The app did not close.' }

    [pscustomobject]@{ WindowMs = $windowMs; UsableMs = $usableMs; WorkingSetMiB = $workingSet; PrivateMiB = $private }
}

$warm = $results | Select-Object -Skip 1
$median = { param($values) $s = @($values | Sort-Object); $s[[int][math]::Floor($s.Count / 2)] }
'first run: window {0} ms, usable {1} ms' -f $results[0].WindowMs, $results[0].UsableMs
'later runs (median of {0}): window {1} ms, usable {2} ms; working set {3:F0} MiB, private {4:F0} MiB' -f `
    @($warm).Count, (& $median $warm.WindowMs), (& $median $warm.UsableMs), (& $median $warm.WorkingSetMiB), (& $median $warm.PrivateMiB)
'processes left after closing: {0}' -f @(Get-Process ([IO.Path]::GetFileNameWithoutExtension($Exe)) -ErrorAction SilentlyContinue).Count
