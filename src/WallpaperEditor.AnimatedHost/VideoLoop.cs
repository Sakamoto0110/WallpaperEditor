using System.Diagnostics;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace WallpaperEditor.AnimatedHost;

/// <summary>Loops one muted video, handing every decoded frame to the presenter (ADR 0006).</summary>
internal sealed class VideoLoop : IDisposable
{
    private static readonly TimeSpan ReportEvery = TimeSpan.FromSeconds(10);

    private readonly CompositionPresenter presenter;
    private readonly IDirect3DSurface surface;
    private readonly MediaPlayer player;
    private readonly MediaPlaybackItem item;
    private readonly Stopwatch sinceReport = Stopwatch.StartNew();
    private long frames;
    private long framesAtReport;
    private bool describedTrack;

    public VideoLoop(string file, CompositionPresenter presenter)
    {
        this.presenter = presenter;
        surface = presenter.CreateBackBufferSurface();

        item = new MediaPlaybackItem(MediaSource.CreateFromUri(new Uri(Path.GetFullPath(file))));
        player = new MediaPlayer
        {
            IsVideoFrameServerEnabled = true,
            IsLoopingEnabled = true,
            IsMuted = true,
            AutoPlay = false,
        };

        // Otherwise the video shows up in the volume flyout, as something the user is listening to.
        player.CommandManager.IsEnabled = false;
        player.VideoFrameAvailable += OnFrame;
        player.MediaFailed += (_, failure) => HostLog.Write($"media failed: {failure.Error} {failure.ErrorMessage} 0x{failure.ExtendedErrorCode?.HResult:X8}");
        player.Source = item;
    }

    public void Play()
    {
        player.Play();
        HostLog.Write("play");
    }

    public void Pause()
    {
        player.Pause();
        HostLog.Write("pause");
    }

    public void Dispose()
    {
        player.VideoFrameAvailable -= OnFrame;
        player.Dispose();
        (surface as IDisposable)?.Dispose();
    }

    private void OnFrame(MediaPlayer sender, object args)
    {
        try
        {
            sender.CopyFrameToVideoSurface(surface);
            presenter.Present();
            frames++;
            Report();
        }
        catch (Exception exception)
        {
            HostLog.Write($"frame failed: {exception.GetType().Name} 0x{exception.HResult:X8} {exception.Message}");
        }
    }

    private void Report()
    {
        if (!describedTrack && item.VideoTracks.Count > 0)
        {
            // The .mp4 is only a container: this is the codec that is really being decoded.
            var properties = item.VideoTracks[0].GetEncodingProperties();
            var session = player.PlaybackSession;
            HostLog.Write(
                $"video: {properties.Subtype} {session.NaturalVideoWidth}x{session.NaturalVideoHeight} " +
                $"{properties.FrameRate.Numerator}/{properties.FrameRate.Denominator} fps, {properties.Bitrate / 1_000_000.0:0.0} Mbps");
            describedTrack = true;
        }

        if (sinceReport.Elapsed < ReportEvery)
        {
            return;
        }

        var shown = frames - framesAtReport;
        HostLog.Write($"frames: {frames} total, {shown / sinceReport.Elapsed.TotalSeconds:0.0} fps");
        framesAtReport = frames;
        sinceReport.Restart();
    }
}
