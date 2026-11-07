<#
.SYNOPSIS
    Measures what changing a wallpaper costs: time, shell and compositor CPU, VRAM, memory and disk.

.DESCRIPTION
    Runs idle / swapping / idle phases while sampling once per second, and compares the phases. It
    changes the wallpaper of the portrait monitor only, repeatedly, and does not restore it: run
    `WallpaperEditor.Diagnostics snapshot` first, and `restore` afterwards.

    The machine should be otherwise quiet, since these counters cover everything running on it.

.EXAMPLE
    ./scripts/measure-swap.ps1 -WorkDir C:\temp\swap -Count 20
#>
param(
    [Parameter(Mandatory)] [string] $WorkDir,
    [int] $Count = 20,
    [int] $GapMs = 2500,
    [int] $IdleSeconds = 20
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$diag = Join-Path $repo 'tools\WallpaperEditor.Diagnostics\bin\Debug\net10.0-windows\WallpaperEditor.Diagnostics.exe'
$themes = "$env:APPDATA\Microsoft\Windows\Themes"

New-Item -ItemType Directory -Force $WorkDir | Out-Null
if (-not (Test-Path (Join-Path $WorkDir 'photo-a.png'))) { & $diag images $WorkDir | Out-Null }

$explorerPid = (Get-Process explorer | Select-Object -First 1).Id
$dwmPid = (Get-Process dwm | Select-Object -First 1).Id
$counters = @(
    '\GPU Adapter Memory(*)\Dedicated Usage',
    "\GPU Process Memory(pid_${explorerPid}_*)\Dedicated Usage",
    "\GPU Process Memory(pid_${dwmPid}_*)\Dedicated Usage",
    '\Process(explorer)\% Processor Time',
    '\Process(dwm)\% Processor Time'
)

function Read-Sample($phase) {
    $values = (Get-Counter -Counter $counters -ErrorAction SilentlyContinue).CounterSamples
    $sum = { param($pattern) ($values | Where-Object Path -Match $pattern | Measure-Object CookedValue -Sum).Sum }
    [pscustomobject]@{
        Phase = $phase
        AdapterMiB = (& $sum 'gpu adapter memory') / 1MB
        ExplorerGpuMiB = (& $sum "pid_${explorerPid}_") / 1MB
        DwmGpuMiB = (& $sum "pid_${dwmPid}_") / 1MB
        ExplorerCpu = (& $sum 'process\(explorer\)') / [Environment]::ProcessorCount
        DwmCpu = (& $sum 'process\(dwm\)') / [Environment]::ProcessorCount
        ExplorerPrivateMiB = (Get-Process -Id $explorerPid).PrivateMemorySize64 / 1MB
    }
}

function Get-ThemesMiB { ((Get-ChildItem $themes -Recurse -File -Force | Measure-Object Length -Sum).Sum) / 1MB }

$samples = [System.Collections.Generic.List[object]]::new()
$themesBefore = Get-ThemesMiB
$explorerCpuBefore = (Get-Process -Id $explorerPid).CPU

$deadline = (Get-Date).AddSeconds($IdleSeconds)
while ((Get-Date) -lt $deadline) { $samples.Add((Read-Sample 'before')) }
$explorerCpuIdle = (Get-Process -Id $explorerPid).CPU - $explorerCpuBefore
$idleElapsed = $IdleSeconds

$output = Join-Path $WorkDir 'bench-output.txt'
$swapStart = Get-Date
$explorerCpuSwapStart = (Get-Process -Id $explorerPid).CPU
$bench = Start-Process $diag -ArgumentList 'bench', "`"$WorkDir`"", $Count, $GapMs -NoNewWindow -PassThru -RedirectStandardOutput $output
while (-not $bench.HasExited) { $samples.Add((Read-Sample 'swapping')) }
$swapElapsed = ((Get-Date) - $swapStart).TotalSeconds
$explorerCpuSwap = (Get-Process -Id $explorerPid).CPU - $explorerCpuSwapStart

$deadline = (Get-Date).AddSeconds($IdleSeconds)
while ((Get-Date) -lt $deadline) { $samples.Add((Read-Sample 'after')) }

$samples | Export-Csv (Join-Path $WorkDir 'samples.csv') -NoTypeInformation

Get-Content $output
''
foreach ($phase in 'before', 'swapping', 'after') {
    $p = $samples | Where-Object Phase -eq $phase
    $avg = { param($name) ($p | Measure-Object $name -Average).Average }
    $max = { param($name) ($p | Measure-Object $name -Maximum).Maximum }
    '{0,-9} n={1,-3} adapterVRAM avg={2:F0} max={3:F0} MiB | explorerGPU={4:F1} dwmGPU={5:F1} MiB | explorerCPU avg={6:F2}% max={7:F2}% | dwmCPU avg={8:F2}% max={9:F2}% | explorerPrivate={10:F0} MiB' -f `
        $phase, $p.Count, (& $avg AdapterMiB), (& $max AdapterMiB), (& $avg ExplorerGpuMiB), (& $avg DwmGpuMiB), (& $avg ExplorerCpu), (& $max ExplorerCpu), (& $avg DwmCpu), (& $max DwmCpu), (& $avg ExplorerPrivateMiB)
}
''
$extraCpu = $explorerCpuSwap - ($explorerCpuIdle / $idleElapsed * $swapElapsed)
'explorer CPU time: idle {0:F2}s in {1}s, swapping {2:F2}s in {3:F0}s; about {4:F0} ms of CPU per swap above idle' -f $explorerCpuIdle, $idleElapsed, $explorerCpuSwap, $swapElapsed, ($extraCpu / $Count * 1000)
'themes folder: {0:F2} MiB before, {1:F2} MiB after' -f $themesBefore, (Get-ThemesMiB)
