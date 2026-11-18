using WallpaperEditor.Core.Wallpapers;

namespace WallpaperEditor.App.ViewModels;

/// <summary>One press of an orientation button, as seen by the user.</summary>
public enum OrientationStep
{
    RotateLeft,
    RotateRight,
    Rotate180,
    MirrorHorizontal,
    MirrorVertical,
}

public static class OrientationSteps
{
    public static ImageOrientation Apply(this ImageOrientation orientation, OrientationStep step) => step switch
    {
        OrientationStep.RotateLeft => orientation.Rotated(-90),
        OrientationStep.RotateRight => orientation.Rotated(90),
        OrientationStep.Rotate180 => orientation.Rotated(180),
        OrientationStep.MirrorHorizontal => orientation.MirroredHorizontally(),
        OrientationStep.MirrorVertical => orientation.MirroredVertically(),
        _ => orientation,
    };

    /// <summary>A short description for under the buttons, or null when the image is as it came.</summary>
    public static string? Describe(ImageOrientation orientation)
    {
        if (orientation.IsNone)
        {
            return null;
        }

        var parts = new List<string>();
        if (orientation.Degrees != 0)
        {
            parts.Add($"girada {orientation.Degrees}° no sentido horário");
        }

        if (orientation.FlipHorizontal && orientation.FlipVertical)
        {
            parts.Add("espelhada nos dois sentidos");
        }
        else if (orientation.FlipHorizontal)
        {
            parts.Add("espelhada na horizontal");
        }
        else if (orientation.FlipVertical)
        {
            parts.Add("espelhada na vertical");
        }

        return "Imagem " + string.Join(" e ", parts) + ".";
    }
}
