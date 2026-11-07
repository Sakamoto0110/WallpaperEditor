using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WallpaperEditor.Core.Monitors;
using WallpaperEditor.Core.Rendering;
using WallpaperEditor.Core.Wallpapers;

namespace WallpaperEditor.Rendering;

/// <summary>Draws wallpapers with WPF, without a window. See ADR 0005.</summary>
public sealed class WpfWallpaperRenderer : IWallpaperRenderer
{
    public Task RenderToFileAsync(StaticImageRequest request, string outputPath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        return RunOnStaThread(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var bitmap = Render(request);
                cancellationToken.ThrowIfCancellationRequested();
                WritePng(bitmap, outputPath);
                return true;
            });
    }

    /// <summary>
    /// The same drawing as the final image, at a smaller size, so that what the user sees is what gets
    /// published. The result is frozen and can be used from any thread.
    /// </summary>
    public Task<BitmapSource> RenderPreviewAsync(StaticImageRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return RunOnStaThread(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var bitmap = Render(request);
                bitmap.Freeze();
                return bitmap;
            });
    }

    public Task RenderDocumentToFileAsync(DocumentRenderRequest request, string outputPath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        return RunOnStaThread(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var bitmap = Render(request);
                cancellationToken.ThrowIfCancellationRequested();
                WritePng(bitmap, outputPath);
                return true;
            });
    }

    /// <summary>The same drawing as the final image, at a smaller size. Frozen, for use from any thread.</summary>
    public Task<BitmapSource> RenderDocumentPreviewAsync(DocumentRenderRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return RunOnStaThread(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var bitmap = Render(request with { SkipUnreadableAssets = true });
                bitmap.Freeze();
                return bitmap;
            });
    }

    private static BitmapSource Render(DocumentRenderRequest request)
    {
        var images = new DocumentImages(request.ResolveAsset);
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        TextOptions.SetTextFormattingMode(visual, TextFormattingMode.Display);

        var target = new Rect(0, 0, request.Target.Width, request.Target.Height);
        var placement = FitCalculator.Compute(request.Canvas, request.Target, request.Fit);

        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(Brushes.Black, null, target);
            context.PushClip(new RectangleGeometry(target));
            context.PushTransform(new MatrixTransform(
                placement.Width / request.Canvas.Width,
                0,
                0,
                placement.Height / request.Canvas.Height,
                placement.X,
                placement.Y));
            DocumentPainter.Paint(context, request.Document, request.Canvas, images);
            context.Pop();
            context.Pop();
        }

        if (!request.SkipUnreadableAssets && images.Unreadable.Count > 0)
        {
            throw new ImageLoadException(images.Unreadable.First());
        }

        var bitmap = new RenderTargetBitmap(request.Target.Width, request.Target.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        return new FormatConvertedBitmap(bitmap, PixelFormats.Bgr24, null, 0);
    }

    private static BitmapSource Render(StaticImageRequest request)
    {
        var source = LoadedImage.Open(request.SourcePath);
        var placement = FitCalculator.Compute(source.DisplaySize, request.Target, request.Fit);
        var image = source.Decode(placement);

        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);

        using (var context = visual.RenderOpen())
        {
            var target = new Rect(0, 0, request.Target.Width, request.Target.Height);

            // Bars and transparent areas end up black. The output is always opaque.
            context.DrawRectangle(Brushes.Black, null, target);
            context.PushClip(new RectangleGeometry(target));
            context.DrawImage(image, new Rect(placement.X, placement.Y, placement.Width, placement.Height));
            context.Pop();
        }

        var bitmap = new RenderTargetBitmap(request.Target.Width, request.Target.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        return new FormatConvertedBitmap(bitmap, PixelFormats.Bgr24, null, 0);
    }

    private static void WritePng(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    /// <summary>
    /// WPF drawing objects need an STA thread with a dispatcher. Shutting that dispatcher down at the end
    /// matters: otherwise each thread's dispatcher, and what it holds, stays alive.
    /// </summary>
    private static Task<T> RunOnStaThread<T>(Func<T> work)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                completion.SetResult(work());
            }
            catch (OperationCanceledException ex)
            {
                completion.SetCanceled(ex.CancellationToken);
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        })
        {
            IsBackground = true,
            Name = "WallpaperEditor render",
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }
}
