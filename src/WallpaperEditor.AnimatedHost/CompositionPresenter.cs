using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DirectComposition;
using Vortice.DXGI;
using Vortice.Mathematics;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Win32.Foundation;
using WinRT;

namespace WallpaperEditor.AnimatedHost;

/// <summary>
/// A flip-model swap chain shown through DirectComposition, the only way reported to reach the screen
/// in the raised desktop topology (ADR 0006).
/// </summary>
/// <remarks>Survives the window: after Explorer restarts, the same swap chain goes on a new target.</remarks>
internal sealed partial class CompositionPresenter : IDisposable
{
    private readonly ID3D11Device device;
    private readonly ID3D11DeviceContext context;
    private readonly IDXGIDevice dxgiDevice;
    private readonly IDXGISwapChain1 swapChain;
    private readonly IDCompositionDevice composition;
    private IDCompositionTarget? target;
    private IDCompositionVisual? visual;

    public CompositionPresenter(int width, int height)
    {
        D3D11.D3D11CreateDevice(
            null,
            DriverType.Hardware,
            // VideoSupport: MediaPlayer runs a video processor on this device to copy each frame.
            DeviceCreationFlags.BgraSupport | DeviceCreationFlags.VideoSupport,
            [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0],
            out device!,
            out context!).CheckError();

        // MediaPlayer calls in from its own thread.
        using (var multithread = device.QueryInterface<ID3D11Multithread>())
        {
            multithread.SetMultithreadProtected(true);
        }

        dxgiDevice = device.QueryInterface<IDXGIDevice>();
        using var adapter = dxgiDevice.GetAdapter();
        using var factory = adapter.GetParent<IDXGIFactory2>();
        swapChain = factory.CreateSwapChainForComposition(
            device,
            new SwapChainDescription1
            {
                Width = (uint)width,
                Height = (uint)height,
                Format = Format.B8G8R8A8_UNorm,
                BufferCount = 2,
                BufferUsage = Usage.RenderTargetOutput | Usage.ShaderInput,
                SampleDescription = new SampleDescription(1, 0),
                SwapEffect = SwapEffect.FlipSequential,
                AlphaMode = Vortice.DXGI.AlphaMode.Ignore,
                Scaling = Scaling.Stretch,
            });

        DComp.DCompositionCreateDevice(dxgiDevice, out composition!).CheckError();
    }

    /// <summary>Points composition at a window. The previous target, if any, is dropped.</summary>
    public unsafe void Show(HWND window)
    {
        Hide();
        composition.CreateTargetForHwnd((nint)window.Value, true, out target).CheckError();
        visual = composition.CreateVisual();
        visual.SetContent(swapChain).CheckError();
        target!.SetRoot(visual).CheckError();
        composition.Commit().CheckError();
    }

    public void Hide()
    {
        visual?.Dispose();
        visual = null;
        target?.Dispose();
        target = null;
    }

    /// <summary>The surface MediaPlayer copies into. With a flip-sequential chain, buffer 0 is always the next frame.</summary>
    public IDirect3DSurface CreateBackBufferSurface()
    {
        using var backBuffer = swapChain.GetBuffer<IDXGISurface>(0);
        Marshal.ThrowExceptionForHR(CreateDirect3D11SurfaceFromDXGISurface(backBuffer.NativePointer, out var inspectable));
        try
        {
            return MarshalInterface<IDirect3DSurface>.FromAbi(inspectable);
        }
        finally
        {
            Marshal.Release(inspectable);
        }
    }

    public void Fill(byte red, byte green, byte blue)
    {
        using var backBuffer = swapChain.GetBuffer<ID3D11Texture2D>(0);
        using var view = device.CreateRenderTargetView(backBuffer);
        context.ClearRenderTargetView(view, new Color4(red / 255f, green / 255f, blue / 255f));
        Present();
    }

    public void Present() => swapChain.Present(1, PresentFlags.None).CheckError();

    public void Dispose()
    {
        Hide();
        composition.Dispose();
        swapChain.Dispose();
        dxgiDevice.Dispose();
        context.Dispose();
        device.Dispose();
    }

    // Not in CsWin32's metadata in a form usable with Vortice's interfaces, so declared by hand.
    [LibraryImport("d3d11.dll")]
    private static partial int CreateDirect3D11SurfaceFromDXGISurface(nint dxgiSurface, out nint graphicsSurface);
}
