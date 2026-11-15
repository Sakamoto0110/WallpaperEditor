namespace WallpaperEditor.AnimatedHost;

/// <summary>What to show, and where. See <see cref="Usage"/>.</summary>
internal sealed record HostOptions
{
    public const string Usage = """
        WallpaperEditor.AnimatedHost: experimental video wallpaper, behind the desktop icons.

        Play:
          --monitor <device path | portrait>   the monitor to cover
          --video <file.mp4>                   video to loop, or
          --solid <RRGGBB>                     a solid color, to test the attachment by itself
          --control <name>                     named events <name>.stop, <name>.pause, <name>.resume
          --max-seconds <n>                    exit by itself after n seconds
          --log <file>                         log file

        Generate a test video:
          --make-video <file.mp4> --size <WxH> --fps <n> --mbps <n> --seconds <n> --codec <h264|hevc>
        """;

    public string? Monitor { get; private init; }

    public string? Video { get; private init; }

    public string? Solid { get; private init; }

    public string? Control { get; private init; }

    public int? MaxSeconds { get; private init; }

    public string? Log { get; private init; }

    public string? MakeVideo { get; private init; }

    public int Width { get; private init; }

    public int Height { get; private init; }

    public int Fps { get; private init; } = 30;

    public int Mbps { get; private init; } = 10;

    public int Seconds { get; private init; } = 30;

    public string Codec { get; private init; } = "h264";

    public static HostOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length || !args[i].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Expected --name value, near '{args[i]}'.");
            }

            values[args[i][2..]] = args[i + 1];
        }

        var size = Get("size")?.Split('x');
        var options = new HostOptions
        {
            Monitor = Get("monitor"),
            Video = Get("video"),
            Solid = Get("solid"),
            Control = Get("control"),
            MaxSeconds = GetInt("max-seconds"),
            Log = Get("log"),
            MakeVideo = Get("make-video"),
            Width = size is { Length: 2 } ? int.Parse(size[0]) : 0,
            Height = size is { Length: 2 } ? int.Parse(size[1]) : 0,
            Fps = GetInt("fps") ?? 30,
            Mbps = GetInt("mbps") ?? 10,
            Seconds = GetInt("seconds") ?? 30,
            Codec = Get("codec") ?? "h264",
        };

        if (options.MakeVideo is not null)
        {
            return options.Width > 0 ? options : throw new ArgumentException("--make-video needs --size WxH.");
        }

        if (options.Monitor is null || (options.Video is null) == (options.Solid is null))
        {
            throw new ArgumentException("Needs --monitor, and either --video or --solid.");
        }

        return options;

        string? Get(string name) => values.GetValueOrDefault(name);

        int? GetInt(string name) => Get(name) is { } text ? int.Parse(text) : null;
    }
}
