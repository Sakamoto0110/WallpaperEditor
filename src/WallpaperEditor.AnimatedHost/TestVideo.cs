using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;

namespace WallpaperEditor.AnimatedHost;

/// <summary>
/// Generates the videos for the experiment, with the encoders Windows already has, so that measuring
/// needs nothing installed.
/// </summary>
/// <remarks>
/// Every video comes from the same deterministic source, which makes scenarios comparable: a moving
/// gradient with noise, which is hard enough to compress for the bitrate to matter, and a bar that makes
/// stutter visible.
/// </remarks>
internal static class TestVideo
{
    private const int NoiseSide = 2048;

    public static async Task Make(HostOptions options)
    {
        var (width, height, fps) = (options.Width, options.Height, options.Fps);
        var total = options.Fps * options.Seconds;

        var input = VideoEncodingProperties.CreateUncompressed(MediaEncodingSubtypes.Bgra8, (uint)width, (uint)height);
        input.FrameRate.Numerator = (uint)fps;
        input.FrameRate.Denominator = 1;

        var noise = MakeNoise();
        var gradient = MakeGradient();
        var frameLength = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / fps);
        var next = 0;

        var source = new MediaStreamSource(new VideoStreamDescriptor(input))
        {
            BufferTime = TimeSpan.Zero,
            Duration = TimeSpan.FromSeconds(options.Seconds),
        };
        source.SampleRequested += (_, request) =>
        {
            if (next >= total)
            {
                request.Request.Sample = null;
                return;
            }

            var pixels = new byte[width * height * 4];
            DrawFrame(MemoryMarshal.Cast<byte, uint>(pixels.AsSpan()), width, height, next, noise, gradient);
            var sample = MediaStreamSample.CreateFromBuffer(pixels.AsBuffer(), TimeSpan.FromTicks(next * frameLength.Ticks));
            sample.Duration = frameLength;
            sample.KeyFrame = true;
            request.Request.Sample = sample;
            next++;
        };

        var profile = options.Codec.ToUpperInvariant() switch
        {
            "H264" => MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p),
            "HEVC" => MediaEncodingProfile.CreateHevc(VideoEncodingQuality.HD1080p),
            _ => throw new ArgumentException($"Codec '{options.Codec}' is not one of h264, hevc."),
        };
        profile.Audio = null;
        profile.Video.Width = (uint)width;
        profile.Video.Height = (uint)height;
        profile.Video.Bitrate = (uint)options.Mbps * 1_000_000;
        profile.Video.FrameRate.Numerator = (uint)fps;
        profile.Video.FrameRate.Denominator = 1;

        var output = Path.GetFullPath(options.MakeVideo!);
        using var file = File.Create(output);
        using var stream = file.AsRandomAccessStream();

        var transcoder = new MediaTranscoder { HardwareAccelerationEnabled = true };
        var prepared = await transcoder.PrepareMediaStreamSourceTranscodeAsync(source, stream, profile);
        if (!prepared.CanTranscode)
        {
            throw new InvalidOperationException($"Cannot encode this: {prepared.FailureReason}.");
        }

        await prepared.TranscodeAsync();
        await stream.FlushAsync();
        HostLog.Write($"{output}: {width}x{height} {fps} fps {options.Mbps} Mbps {options.Codec}, {total} frames, {file.Length / 1024.0 / 1024.0:0.0} MiB");
    }

    private static uint[] MakeNoise()
    {
        var random = new Random(20261107);
        var noise = new uint[NoiseSide * NoiseSide];
        for (var i = 0; i < noise.Length; i++)
        {
            noise[i] = (uint)random.Next() & 0x001F1F1F;
        }

        return noise;
    }

    private static uint[] MakeGradient()
    {
        var gradient = new uint[1024];
        for (var i = 0; i < gradient.Length; i++)
        {
            var angle = i * Math.Tau / gradient.Length;
            var red = (uint)(110 + (90 * Math.Sin(angle)));
            var green = (uint)(110 + (90 * Math.Sin(angle + 2.1)));
            var blue = (uint)(110 + (90 * Math.Sin(angle + 4.2)));
            gradient[i] = 0xFF000000 | (red << 16) | (green << 8) | blue;
        }

        return gradient;
    }

    private static void DrawFrame(Span<uint> pixels, int width, int height, int frame, uint[] noise, uint[] gradient)
    {
        var shift = frame * 5;
        var noiseX = frame * 37;
        var noiseY = frame * 23;
        for (var y = 0; y < height; y++)
        {
            var row = pixels.Slice(y * width, width);
            var noiseRow = ((y + noiseY) & (NoiseSide - 1)) * NoiseSide;
            for (var x = 0; x < width; x++)
            {
                row[x] = gradient[(x + y + shift) & 1023] ^ noise[noiseRow + ((x + noiseX) & (NoiseSide - 1))];
            }
        }

        var bar = frame * 8 % width;
        for (var y = 0; y < height; y++)
        {
            pixels.Slice((y * width) + bar, Math.Min(16, width - bar)).Fill(0xFFFFFFFF);
        }
    }
}
