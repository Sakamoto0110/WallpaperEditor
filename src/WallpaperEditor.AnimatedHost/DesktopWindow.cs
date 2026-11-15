using System.ComponentModel;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
using WallpaperEditor.Core.Monitors;

namespace WallpaperEditor.AnimatedHost;

/// <summary>
/// Keeps one window between the wallpaper and the desktop icons, and owns the message loop.
/// </summary>
/// <remarks>
/// <para>
/// Two windows, because they live differently. The <b>video</b> window is a child of the shell's
/// <c>Progman</c>, and dies with it when Explorer restarts. The <b>controller</b> is a hidden top-level
/// window that outlives that, and receives what is only broadcast to top-level windows.
/// </para>
/// <para>
/// There is no official API for any of this: see docs/04-integracao-windows.md. A child window in another
/// process shares its input queue with Explorer, so the loop here must never block: all video work happens
/// on other threads.
/// </para>
/// </remarks>
internal sealed unsafe class DesktopWindow : IDisposable
{
    private const uint WmStop = PInvoke.WM_APP + 1;
    private const nuint RetryTimer = 1;
    private const uint RetryMilliseconds = 500;
    private const nuint OrderTimer = 2;
    private const uint OrderMilliseconds = 1000;

    private const string ClassName = "WallpaperEditor.AnimatedHost";

    private static DesktopWindow? current;

    private readonly Func<MonitorInfo?> findMonitor;
    private readonly WNDPROC procedure;
    private readonly uint taskbarCreated;
    private readonly HWND controller;
    private HWND video;
    private HWND icons;
    private bool stopping;

    public DesktopWindow(Func<MonitorInfo?> findMonitor)
    {
        this.findMonitor = findMonitor;
        current = this;
        procedure = WindowProcedure;
        taskbarCreated = PInvoke.RegisterWindowMessage("TaskbarCreated");

        fixed (char* className = ClassName)
        {
            var windowClass = new WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                lpfnWndProc = procedure,
                hInstance = Module,
                lpszClassName = className,
            };
            if (PInvoke.RegisterClassEx(windowClass) == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "RegisterClassEx");
            }
        }

        controller = CreateWindow(WINDOW_EX_STYLE.WS_EX_TOOLWINDOW);
    }

    /// <summary>The window now behind the icons. Comes with a new handle after Explorer restarts.</summary>
    public event Action<HWND>? Attached;

    /// <summary>The video window is gone, normally because Explorer went away.</summary>
    public event Action? Detached;

    private static HINSTANCE Module => (HINSTANCE)(nint)PInvoke.GetModuleHandle((string?)null).DangerousGetHandle();

    /// <summary>Safe from any thread.</summary>
    public void Stop() => PInvoke.PostMessage(controller, WmStop, default, default);

    /// <summary>Attaches to the desktop, and runs until <see cref="Stop"/>.</summary>
    public void Run()
    {
        if (!TryAttach())
        {
            throw new InvalidOperationException("Could not attach. Only the raised desktop topology is supported.");
        }

        while (PInvoke.GetMessage(out var message, default, 0, 0).Value > 0)
        {
            PInvoke.DispatchMessage(message);
        }
    }

    public void Dispose()
    {
        stopping = true;
        if (video != default)
        {
            PInvoke.DestroyWindow(video);
        }

        PInvoke.DestroyWindow(controller);
        current = null;
    }

    private static LRESULT WindowProcedure(HWND window, uint message, WPARAM wParam, LPARAM lParam) =>
        current?.Handle(window, message, wParam, lParam) ?? PInvoke.DefWindowProc(window, message, wParam, lParam);

    private static HWND CreateWindow(WINDOW_EX_STYLE extendedStyle)
    {
        var window = PInvoke.CreateWindowEx(
            extendedStyle,
            ClassName,
            ClassName,
            WINDOW_STYLE.WS_POPUP,
            0,
            0,
            0,
            0,
            default,
            null,
            null,
            null);
        return window != default ? window : throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateWindowEx");
    }

    private LRESULT Handle(HWND window, uint message, WPARAM wParam, LPARAM lParam)
    {
        if (window == controller)
        {
            if (message == WmStop)
            {
                PInvoke.PostQuitMessage(0);
                return default;
            }

            if (message == taskbarCreated)
            {
                HostLog.Write("taskbar created: the shell restarted");
                ArmRetry();
                return default;
            }

            if (message == PInvoke.WM_TIMER && wParam.Value == RetryTimer)
            {
                RetryAttach();
                return default;
            }

            if (message == PInvoke.WM_TIMER && wParam.Value == OrderTimer)
            {
                KeepBelowIcons();
                return default;
            }

            if (message == PInvoke.WM_DISPLAYCHANGE)
            {
                HostLog.Write("display changed");
                ArmRetry();
                return default;
            }
        }
        else if (window == video && message == PInvoke.WM_DESTROY)
        {
            video = default;
            if (!stopping)
            {
                HostLog.Write("video window destroyed by the shell");
                Detached?.Invoke();
                ArmRetry();
            }
        }

        return PInvoke.DefWindowProc(window, message, wParam, lParam);
    }

    private void ArmRetry() => PInvoke.SetTimer(controller, RetryTimer, RetryMilliseconds, null);

    /// <summary>
    /// Measured: changing any wallpaper recreates the shell's WorkerW right below the icons, on top of the video.
    /// Polling, because the alternative is a hook on everything Explorer does, and this process is awake anyway.
    /// </summary>
    private void KeepBelowIcons()
    {
        if (video != default && PInvoke.GetWindow(video, GET_WINDOW_CMD.GW_HWNDPREV) != icons)
        {
            HostLog.Write("something moved between the icons and the video: putting it back");
            if (!TryAttach())
            {
                ArmRetry();
            }
        }
    }

    private void RetryAttach()
    {
        if (TryAttach())
        {
            PInvoke.KillTimer(controller, RetryTimer);
        }
    }

    /// <summary>Attaches or repositions. False while the shell is not ready, which is normal during a restart.</summary>
    private bool TryAttach()
    {
        try
        {
            return Attach();
        }
        catch (Exception exception) when (exception is COMException or Win32Exception)
        {
            // Measured: IDesktopWallpaper is served by Explorer, so while it is down even reading the monitors fails.
            HostLog.Write($"cannot attach yet: 0x{exception.HResult:X8}");
            return false;
        }
    }

    private bool Attach()
    {
        var monitor = findMonitor();
        if (monitor is null)
        {
            HostLog.Write("monitor not active: waiting");
            return false;
        }

        var progman = PInvoke.FindWindow("Progman", null);
        if (progman == default)
        {
            return false;
        }

        var extendedStyle = (WINDOW_EX_STYLE)PInvoke.GetWindowLongPtr(progman, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        if (!extendedStyle.HasFlag(WINDOW_EX_STYLE.WS_EX_NOREDIRECTIONBITMAP))
        {
            HostLog.Write("Progman has no WS_EX_NOREDIRECTIONBITMAP: not the raised desktop topology");
            return false;
        }

        icons = PInvoke.FindWindowEx(progman, default, "SHELLDLL_DefView", null);
        if (icons == default)
        {
            return false;
        }

        SeparateWallpaperLayer(progman);

        // A window left behind by a previous Progman is of no use, even if its destruction has not arrived yet.
        if (video != default && PInvoke.GetParent(video) != progman)
        {
            PInvoke.DestroyWindow(video);
        }

        var isNew = video == default;
        if (isNew)
        {
            // The style changes from popup to child, never both: the combination is what is reported to fail.
            video = CreateWindow(WINDOW_EX_STYLE.WS_EX_NOREDIRECTIONBITMAP | WINDOW_EX_STYLE.WS_EX_NOACTIVATE);
            PInvoke.SetWindowLongPtr(
                video,
                WINDOW_LONG_PTR_INDEX.GWL_STYLE,
                (nint)(uint)(WINDOW_STYLE.WS_CHILD | WINDOW_STYLE.WS_DISABLED));
            if (PInvoke.SetParent(video, progman) == default)
            {
                var error = new Win32Exception(Marshal.GetLastWin32Error(), "SetParent");
                PInvoke.DestroyWindow(video);
                video = default;
                throw error;
            }
        }

        // Relative to Progman, whose origin is the top-left of the whole virtual desktop.
        var corner = new System.Drawing.Point[] { new(monitor.Bounds.Left, monitor.Bounds.Top) };
        PInvoke.MapWindowPoints(default, progman, corner);

        var placed = PInvoke.SetWindowPos(
            video,
            icons,
            corner[0].X,
            corner[0].Y,
            monitor.Bounds.Width,
            monitor.Bounds.Height,
            SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW | SET_WINDOW_POS_FLAGS.SWP_FRAMECHANGED);
        if (!placed)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SetWindowPos");
        }

        HostLog.Write($"attached: monitor {monitor.Bounds}, inside Progman at ({corner[0].X},{corner[0].Y}), new={isNew}");
        if (isNew)
        {
            PInvoke.SetTimer(controller, OrderTimer, OrderMilliseconds, null);
            Attached?.Invoke(video);
        }

        return true;
    }

    /// <summary>
    /// Measured on build 26300: until asked with the undocumented <c>0x052C</c>, Progman has no WorkerW child,
    /// and a window below the icons stays hidden. Once asked, the wallpaper moves to that WorkerW, which goes to
    /// the bottom so that the video fits between it and the icons.
    /// </summary>
    private static void SeparateWallpaperLayer(HWND progman)
    {
        const uint SpawnWorker = 0x052C;

        var worker = PInvoke.FindWindowEx(progman, default, "WorkerW", null);
        if (worker == default)
        {
            foreach (var (wParam, lParam) in new (nuint, nint)[] { (0xD, 0x1), (0, 0) })
            {
                PInvoke.SendMessageTimeout(progman, SpawnWorker, wParam, lParam, SEND_MESSAGE_TIMEOUT_FLAGS.SMTO_NORMAL, 1000, null);
            }

            worker = PInvoke.FindWindowEx(progman, default, "WorkerW", null);
            HostLog.Write($"0x052C sent: WorkerW child {(worker == default ? "absent" : "present")}");
        }

        if (worker != default)
        {
            PInvoke.SetWindowPos(
                worker,
                new HWND(1), // HWND_BOTTOM
                0,
                0,
                0,
                0,
                SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
        }
    }
}
