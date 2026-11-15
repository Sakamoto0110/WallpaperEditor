<#
.SYNOPSIS
    Measures what a video wallpaper costs: VRAM by process, GPU engines, CPU and memory.

.DESCRIPTION
    Runs the phases of docs/05-experimento-animado.md, sampling about once per second: baseline without the
    host, warm-up (discarded), load, paused, and after closing. Writes every sample to a CSV, and prints the
    median and 95th percentile of each phase, plus the difference from the baseline.

    The defaults are the full protocol, which needs a freshly restarted, otherwise idle PC. Shorter phases on a
    PC in use still give useful per-process numbers, but not a trustworthy adapter total.

    It changes no wallpaper. It does leave the shell's WorkerW behind, until Explorer restarts.

.EXAMPLE
    ./scripts/measure-animated.ps1 -Video C:\temp\videos\R.mp4 -Monitor '\\?\DISPLAY#...' -OutDir C:\temp\results
#>
param(
    [Parameter(Mandatory)] [string] $Video,
    [Parameter(Mandatory)] [string] $OutDir,
    [string] $Monitor = 'portrait',
    [int] $BaselineSeconds = 300,
    [int] $WarmupSeconds = 60,
    [int] $LoadSeconds = 600,
    [int] $PauseSeconds = 120,
    [int] $AfterSeconds = 180
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$hostExe = Join-Path $repo 'src\WallpaperEditor.AnimatedHost\bin\Release\net10.0-windows10.0.19041.0\WallpaperEditor.AnimatedHost.exe'

New-Item -ItemType Directory -Force $OutDir | Out-Null
$name = [IO.Path]::GetFileNameWithoutExtension($Video)
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$log = Join-Path $OutDir "$name-$stamp.log"
$csv = Join-Path $OutDir "$name-$stamp.csv"
$control = "Local\WallpaperEditorMeasure$PID"

$dwmPid = (Get-Process dwm | Select-Object -First 1).Id
$explorerPid = (Get-Process explorer | Select-Object -First 1).Id
$script:hostProcess = $null
$script:lastCpu = @{}
$script:lastTime = Get-Date

function Get-CpuPercent($key, $process) {
    if (-not $process -or $process.HasExited) { return 0.0 }
    $total = $process.TotalProcessorTime.TotalSeconds
    $elapsed = ((Get-Date) - $script:lastTime).TotalSeconds
    $previous = $script:lastCpu[$key]
    $script:lastCpu[$key] = $total
    if ($null -eq $previous -or $elapsed -le 0) { return 0.0 }
    return 100 * ($total - $previous) / $elapsed / [Environment]::ProcessorCount
}

function Read-Sample($phase) {
    $hostPid = if ($script:hostProcess -and -not $script:hostProcess.HasExited) { $script:hostProcess.Id } else { 0 }
    $counters = @(
        '\GPU Adapter Memory(*)\Dedicated Usage',
        "\GPU Process Memory(pid_${dwmPid}_*)\Dedicated Usage",
        "\GPU Process Memory(pid_${explorerPid}_*)\Dedicated Usage"
    )
    if ($hostPid) {
        $counters += "\GPU Process Memory(pid_${hostPid}_*)\Dedicated Usage"
        $counters += "\GPU Process Memory(pid_${hostPid}_*)\Shared Usage"
        $counters += "\GPU Engine(pid_${hostPid}_*engtype_Video*)\Utilization Percentage"
        $counters += "\GPU Engine(pid_${hostPid}_*engtype_3D)\Utilization Percentage"
    }

    $values = (Get-Counter -Counter $counters -ErrorAction SilentlyContinue).CounterSamples
    $sum = { param($pattern) [double](($values | Where-Object Path -Match $pattern | Measure-Object CookedValue -Sum).Sum) }
    $hostProcess = $script:hostProcess

    $sample = [pscustomobject]@{
        Phase = $phase
        Time = (Get-Date).ToString('HH:mm:ss')
        AdapterMiB = (& $sum 'gpu adapter memory') / 1MB
        HostDedicatedMiB = if ($hostPid) { (& $sum "pid_${hostPid}_.*dedicated usage") / 1MB } else { 0 }
        HostSharedMiB = if ($hostPid) { (& $sum "pid_${hostPid}_.*shared usage") / 1MB } else { 0 }
        DwmMiB = (& $sum "pid_${dwmPid}_.*dedicated usage") / 1MB
        ExplorerMiB = (& $sum "pid_${explorerPid}_.*dedicated usage") / 1MB
        DecodePercent = if ($hostPid) { & $sum 'engtype_video' } else { 0 }
        ThreeDPercent = if ($hostPid) { & $sum 'engtype_3d' } else { 0 }
        HostCpuPercent = Get-CpuPercent 'host' $hostProcess
        DwmCpuPercent = Get-CpuPercent 'dwm' (Get-Process -Id $dwmPid)
        HostPrivateMiB = if ($hostPid) { $hostProcess.Refresh(); $hostProcess.PrivateMemorySize64 / 1MB } else { 0 }
    }
    $script:lastTime = Get-Date
    return $sample
}

function Measure-Phase($phase, $seconds) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) { $samples.Add((Read-Sample $phase)) }
}

function Get-Percentile($values, $fraction) {
    $sorted = @($values | Sort-Object)
    if ($sorted.Count -eq 0) { return 0 }
    return $sorted[[math]::Min($sorted.Count - 1, [int][math]::Floor($sorted.Count * $fraction))]
}

function Send-Control($suffix) {
    $event = [Threading.EventWaitHandle]::OpenExisting("$control.$suffix")
    [void]$event.Set()
    $event.Dispose()
}

$samples = [System.Collections.Generic.List[object]]::new()

Measure-Phase 'baseline' $BaselineSeconds

$script:hostProcess = Start-Process $hostExe -PassThru -ArgumentList @(
    '--monitor', "`"$Monitor`"", '--video', "`"$Video`"", '--control', $control, '--log', "`"$log`"")
Start-Sleep -Seconds 1
if ($script:hostProcess.HasExited) { throw "The host failed. See $log." }

Measure-Phase 'warmup' $WarmupSeconds
Measure-Phase 'load' $LoadSeconds
Send-Control 'pause'
Measure-Phase 'paused' $PauseSeconds
Send-Control 'resume'
Send-Control 'stop'
$script:hostProcess | Wait-Process -Timeout 15
$script:hostProcess = $null
Measure-Phase 'after' $AfterSeconds

$samples | Export-Csv -Path $csv -NoTypeInformation -Encoding utf8

$metrics = 'AdapterMiB', 'HostDedicatedMiB', 'HostSharedMiB', 'DwmMiB', 'ExplorerMiB', 'DecodePercent', 'ThreeDPercent', 'HostCpuPercent', 'DwmCpuPercent', 'HostPrivateMiB'
$summary = foreach ($metric in $metrics) {
    $row = [ordered]@{ Metric = $metric }
    foreach ($phase in 'baseline', 'load', 'paused', 'after') {
        $values = @($samples | Where-Object Phase -EQ $phase | ForEach-Object { $_.$metric })
        $row[$phase] = '{0:0.0}' -f (Get-Percentile $values 0.5)
        if ($phase -eq 'load') { $row['loadP95'] = '{0:0.0}' -f (Get-Percentile $values 0.95) }
    }
    [pscustomobject]$row
}

"Scenario $name, $($samples.Count) samples: $csv"
$summary | Format-Table -AutoSize | Out-String
$median = { param($phase, $metric) Get-Percentile @($samples | Where-Object Phase -EQ $phase | ForEach-Object { $_.$metric }) 0.5 }
$adapterDelta = (& $median 'load' 'AdapterMiB') - (& $median 'baseline' 'AdapterMiB')
$dwmDelta = (& $median 'load' 'DwmMiB') - (& $median 'baseline' 'DwmMiB')
$hostMiB = & $median 'load' 'HostDedicatedMiB'

# Do not add the processes up: dwm also counts the swap chain it composes, which the adapter holds only once.
'Adapter {0:+0.0;-0.0} MiB (host {1:0.0}, dwm {2:+0.0;-0.0})' -f $adapterDelta, $hostMiB, $dwmDelta
Get-Content $log | Select-Object -Last 4
