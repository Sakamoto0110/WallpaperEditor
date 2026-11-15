using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;
using WallpaperEditor.Core.Monitors;
using WallpaperEditor.Core.Publishing;

namespace WallpaperEditor.App.Companion;

/// <summary>
/// How the app finds and talks to the companion: the small process that stays in the notification area while a
/// dynamic wallpaper is applied, so the wallpaper can be used without opening the editor.
/// </summary>
/// <remarks>
/// One companion per data folder: a test run with <c>--data</c> gets its own, and never touches the real one or
/// the startup list.
/// </remarks>
public sealed class CompanionChannel(string dataRoot, bool isTestData)
{
    public const string Argument = "--companion";
    public const string InteractArgument = "--interact";

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "WallpaperEditor";

    private readonly string _key = Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(dataRoot).ToUpperInvariant())))[..16];

    public string DataRoot { get; } = dataRoot;

    public bool IsTestData { get; } = isTestData;

    public string MutexName => $@"Local\WallpaperEditor.Companion.{_key}";

    public string PipeName => $"WallpaperEditor.Companion.{_key}";

    public bool IsRunning()
    {
        if (Mutex.TryOpenExisting(MutexName, out var mutex))
        {
            mutex.Dispose();
            return true;
        }

        return false;
    }

    /// <summary>Opens the interaction window for a monitor, starting the companion if it is not running.</summary>
    public void Interact(MonitorId monitor)
    {
        if (!IsRunning() || !TrySend($"interact {monitor.Value}"))
        {
            Start(InteractArgument, monitor.Value);
        }
    }

    /// <summary>
    /// After anything that may change what the monitors show: the companion runs, and starts with Windows, exactly
    /// while some monitor shows a dynamic wallpaper.
    /// </summary>
    public void Sync(WallpaperPublisher publisher)
    {
        bool needed;
        try
        {
            needed = publisher.ShownProjects().Count > 0;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or IOException)
        {
            // Unknown this time; the next change or the next start tries again.
            return;
        }

        SetStartWithWindows(needed);
        if (needed && !IsRunning())
        {
            Start();
        }
        else if (needed)
        {
            // Another project or monitor may be on screen now: the icon's menu and tooltip follow.
            TrySend("refresh");
        }
        else if (IsRunning())
        {
            TrySend("exit");
        }
    }

    /// <summary>Sends one line to the running companion. False if it could not be reached.</summary>
    public bool TrySend(string command)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            pipe.Connect(3000);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false));
            writer.WriteLine(command);
            writer.Flush();
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Starts the main window: the editor, which reopens the last project.</summary>
    public void OpenEditor(string? project = null)
    {
        var args = new List<string>();
        if (project is not null)
        {
            args.AddRange(["--project", project]);
        }

        Launch(args);
    }

    public void SetStartWithWindows(bool enabled)
    {
        // A test run must never change what starts with Windows.
        if (IsTestData)
        {
            return;
        }

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (enabled)
            {
                key.SetValue(RunValue, $"\"{Environment.ProcessPath}\" {Argument}");
            }
            else if (key.GetValue(RunValue) is not null)
            {
                key.DeleteValue(RunValue);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            // Without it the companion simply does not come back after a restart until the app is opened.
        }
    }

    private void Start(params string[] extra) => Launch([Argument, .. extra]);

    private void Launch(IEnumerable<string> args)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        if (IsTestData)
        {
            start.ArgumentList.Add("--data");
            start.ArgumentList.Add(DataRoot);
        }

        Process.Start(start)?.Dispose();
    }
}
