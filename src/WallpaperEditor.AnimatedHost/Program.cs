using System.Globalization;
using WallpaperEditor.AnimatedHost;
using WallpaperEditor.Core.Monitors;
using WallpaperEditor.Platform.Windows;

// Experimental, and separate from the app on purpose: see docs/adr/0002-divisao-do-projeto.md.
// One process plays one monitor. Closing it brings the static wallpaper back, since nothing here changes it.
return Run(args);

static int Run(string[] args)
{
    HostOptions options;
    try
    {
        options = HostOptions.Parse(args);
    }
    catch (Exception exception) when (exception is ArgumentException or FormatException)
    {
        Console.Error.WriteLine(exception.Message);
        Console.Error.WriteLine(HostOptions.Usage);
        return 2;
    }

    HostLog.Open(options.Log);
    try
    {
        if (options.MakeVideo is not null)
        {
            TestVideo.Make(options).GetAwaiter().GetResult();
            return 0;
        }

        Play(options);
        return 0;
    }
    catch (Exception exception)
    {
        HostLog.Write($"failed: {exception}");
        return 1;
    }
}

static void Play(HostOptions options)
{
    var monitors = new DesktopWallpaperService();
    var monitor = FindMonitor(monitors, options.Monitor!)
        ?? throw new InvalidOperationException($"Monitor '{options.Monitor}' is not active.");
    HostLog.Write($"host started: pid {Environment.ProcessId}, {monitor.Name} {monitor.Bounds}");

    using var presenter = new CompositionPresenter(monitor.Bounds.Width, monitor.Bounds.Height);
    using var video = options.Video is not null ? new VideoLoop(options.Video, presenter) : null;
    using var window = new DesktopWindow(() => FindMonitor(monitors, monitor.Id.Value));

    window.Attached += presenter.Show;
    window.Detached += presenter.Hide;

    if (options.Solid is not null)
    {
        var color = uint.Parse(options.Solid, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        presenter.Fill((byte)(color >> 16), (byte)(color >> 8), (byte)color);
    }

    using var control = options.Control is not null ? new ControlEvents(options.Control, window.Stop, video) : null;
    using var deadline = options.MaxSeconds is { } seconds
        ? new Timer(_ => window.Stop(), null, TimeSpan.FromSeconds(seconds), Timeout.InfiniteTimeSpan)
        : null;

    video?.Play();
    window.Run();
    HostLog.Write("host stopping");
}

static MonitorInfo? FindMonitor(IMonitorService monitors, string wanted) =>
    monitors.GetMonitors().FirstOrDefault(m =>
        wanted.Equals("portrait", StringComparison.OrdinalIgnoreCase) ? m.IsPortrait : m.Id.Value == wanted);

/// <summary>The whole protocol with the outside: named events to stop, pause and resume.</summary>
internal sealed class ControlEvents : IDisposable
{
    private readonly EventWaitHandle stop;
    private readonly EventWaitHandle pause;
    private readonly EventWaitHandle resume;
    private readonly Thread thread;

    public ControlEvents(string name, Action onStop, VideoLoop? video)
    {
        stop = new EventWaitHandle(false, EventResetMode.AutoReset, $"{name}.stop");
        pause = new EventWaitHandle(false, EventResetMode.AutoReset, $"{name}.pause");
        resume = new EventWaitHandle(false, EventResetMode.AutoReset, $"{name}.resume");
        thread = new Thread(() =>
        {
            while (true)
            {
                switch (WaitHandle.WaitAny([stop, pause, resume]))
                {
                    case 0:
                        onStop();
                        return;
                    case 1:
                        video?.Pause();
                        break;
                    default:
                        video?.Play();
                        break;
                }
            }
        })
        { IsBackground = true, Name = "control events" };
        thread.Start();
    }

    public void Dispose()
    {
        // The background thread ends with the process, and keeps the handles until then.
    }
}
