using WallpaperEditor.Core.Monitors;
using WallpaperEditor.Core.Wallpapers;

namespace WallpaperEditor.Core.Tests;

public class FitCalculatorTests
{
    private static readonly PixelSize Vertical = new(1080, 1920);
    private static readonly PixelSize Ultrawide = new(2560, 1080);

    [Fact]
    public void Fill_covers_the_monitor_and_crops_the_excess()
    {
        var p = FitCalculator.Compute(new PixelSize(6000, 3375), Vertical, FitMode.Fill);

        Assert.Equal(1920, p.Height, 3);
        Assert.Equal(3413.333, p.Width, 3);
        Assert.Equal(0, p.Y, 3);
        Assert.True(p.X < 0, "The sides should be cropped evenly.");
        Assert.Equal((1080 - p.Width) / 2, p.X, 3);
    }

    [Fact]
    public void Fit_shows_the_whole_image_with_bars()
    {
        var p = FitCalculator.Compute(new PixelSize(6000, 3375), Vertical, FitMode.Fit);

        Assert.Equal(1080, p.Width, 3);
        Assert.Equal(607.5, p.Height, 3);
        Assert.Equal(0, p.X, 3);
        Assert.Equal(656.25, p.Y, 3);
    }

    [Fact]
    public void Center_keeps_the_original_size()
    {
        var p = FitCalculator.Compute(new PixelSize(800, 600), Ultrawide, FitMode.Center);

        Assert.Equal(new Placement(880, 240, 800, 600), p);
    }

    [Fact]
    public void Center_crops_an_image_larger_than_the_monitor()
    {
        var p = FitCalculator.Compute(new PixelSize(4000, 1080), Ultrawide, FitMode.Center);

        Assert.Equal(new Placement(-720, 0, 4000, 1080), p);
    }

    [Fact]
    public void Center_stays_on_whole_pixels_when_the_difference_is_odd()
    {
        var p = FitCalculator.Compute(new PixelSize(801, 601), Ultrawide, FitMode.Center);

        Assert.Equal(new Placement(879, 239, 801, 601), p);
    }

    [Fact]
    public void Stretch_always_matches_the_monitor_exactly()
    {
        var p = FitCalculator.Compute(new PixelSize(10, 10), Ultrawide, FitMode.Stretch);

        Assert.Equal(new Placement(0, 0, 2560, 1080), p);
    }

    [Theory]
    [InlineData(FitMode.Fill)]
    [InlineData(FitMode.Fit)]
    [InlineData(FitMode.Center)]
    [InlineData(FitMode.Stretch)]
    public void Every_mode_agrees_when_the_image_already_has_the_monitor_size(FitMode mode)
    {
        // This is what makes the app independent of the global Windows setting: see ADR 0003.
        var p = FitCalculator.Compute(Ultrawide, Ultrawide, mode);

        Assert.Equal(new Placement(0, 0, 2560, 1080), p);
    }

    [Fact]
    public void An_image_without_pixels_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FitCalculator.Compute(new PixelSize(0, 10), Ultrawide, FitMode.Fill));
    }
}
