<#
.SYNOPSIS
    Generates the videos for the animated experiment, with the encoders Windows already has.

.DESCRIPTION
    Every video comes from the same deterministic source, built into AnimatedHost, so scenarios are
    comparable and nothing needs to be installed. AV1 is missing (scenario C2 of docs/05): Windows has no
    encoding profile for it in the API version this project targets.

    The V scenarios are variations played on the portrait monitor, which is the one tests may use freely.

.EXAMPLE
    ./scripts/make-test-videos.ps1 -OutDir C:\temp\videos -Only M2,VB1
#>
param(
    [Parameter(Mandatory)] [string] $OutDir,
    [string[]] $Only,
    [int] $Seconds = 30
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$hostExe = Join-Path $repo 'src\WallpaperEditor.AnimatedHost\bin\Release\net10.0-windows10.0.19041.0\WallpaperEditor.AnimatedHost.exe'

$scenarios = @(
    @{ Name = 'R';   Size = '2560x1080'; Fps = 30; Mbps = 10; Codec = 'h264' }
    @{ Name = 'C1';  Size = '2560x1080'; Fps = 30; Mbps = 10; Codec = 'hevc' }
    @{ Name = 'F1';  Size = '2560x1080'; Fps = 60; Mbps = 20; Codec = 'h264' }
    @{ Name = 'B1';  Size = '2560x1080'; Fps = 30; Mbps = 40; Codec = 'h264' }
    @{ Name = 'M1';  Size = '1920x1080'; Fps = 30; Mbps = 10; Codec = 'h264' }
    @{ Name = 'M2';  Size = '1080x1920'; Fps = 30; Mbps = 10; Codec = 'h264' }
    @{ Name = 'S1';  Size = '3840x1620'; Fps = 30; Mbps = 25; Codec = 'h264' }
    @{ Name = 'VC1'; Size = '1080x1920'; Fps = 30; Mbps = 10; Codec = 'hevc' }
    @{ Name = 'VF1'; Size = '1080x1920'; Fps = 60; Mbps = 20; Codec = 'h264' }
    @{ Name = 'VB1'; Size = '1080x1920'; Fps = 30; Mbps = 40; Codec = 'h264' }
)

New-Item -ItemType Directory -Force $OutDir | Out-Null
foreach ($scenario in $scenarios) {
    if ($Only -and $scenario.Name -notin $Only) { continue }
    $file = Join-Path $OutDir "$($scenario.Name).mp4"
    $process = Start-Process $hostExe -Wait -PassThru -ArgumentList @(
        '--make-video', "`"$file`"", '--size', $scenario.Size, '--fps', $scenario.Fps,
        '--mbps', $scenario.Mbps, '--seconds', $Seconds, '--codec', $scenario.Codec)
    if ($process.ExitCode -ne 0) { throw "Could not generate $($scenario.Name)." }
    $mib = (Get-Item $file).Length / 1MB
    "{0}: {1} {2} fps {3} Mbps {4}, {5:0.0} MiB" -f $scenario.Name, $scenario.Size, $scenario.Fps, $scenario.Mbps, $scenario.Codec, $mib
}
