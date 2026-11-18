using WallpaperEditor.Core.Monitors;
using WallpaperEditor.Core.Wallpapers;

namespace WallpaperEditor.Core.Tests;

public sealed class ImageOrientationTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(90, 90)]
    [InlineData(450, 90)]
    [InlineData(-90, 270)]
    [InlineData(135, 90)]
    public void Degrees_is_always_a_quarter_turn_between_0_and_270(int rotation, int expected) =>
        Assert.Equal(expected, new ImageOrientation(rotation).Degrees);

    [Fact]
    public void Four_quarter_turns_bring_the_image_back()
    {
        var orientation = ImageOrientation.None;
        for (var i = 0; i < 4; i++)
        {
            orientation = orientation.Rotated(90);
        }

        Assert.True(orientation.IsNone);
    }

    [Fact]
    public void Turning_left_is_the_same_as_three_turns_right() =>
        Assert.Equal(ImageOrientation.None.Rotated(90).Rotated(90).Rotated(90), ImageOrientation.None.Rotated(-90));

    [Fact]
    public void A_quarter_turn_exchanges_width_and_height()
    {
        var size = new PixelSize(2560, 1080);

        Assert.Equal(new PixelSize(1080, 2560), new ImageOrientation(90).Apply(size));
        Assert.Equal(size, new ImageOrientation(180).Apply(size));
        Assert.Equal(size, new ImageOrientation(0, FlipHorizontal: true).Apply(size));
    }

    [Fact]
    public void Behind_one_mirror_a_turn_right_is_stored_as_a_turn_left()
    {
        // The picture must turn the way the button says, so the stored rotation goes the other way.
        var mirrored = ImageOrientation.None.MirroredHorizontally();

        Assert.Equal(270, mirrored.Rotated(90).Degrees);
        Assert.Equal(90, mirrored.MirroredVertically().Rotated(90).Degrees);
    }

    [Fact]
    public void Mirroring_twice_undoes_it() =>
        Assert.True(ImageOrientation.None.MirroredHorizontally().MirroredHorizontally().IsNone);
}
