namespace WallpaperEditor.Core.Publishing;

/// <summary>
/// Runs an operation on request, one at a time. Requests made while it runs collapse into one more run at
/// the end, which is enough since each run acts on the latest state. Marking five tasks in quick succession
/// therefore changes the wallpaper twice, and the last change always includes the last click.
/// </summary>
/// <remarks>Call from one thread, such as the UI thread.</remarks>
public sealed class CoalescingRunner(Func<Task> operation)
{
    private bool _running;
    private bool _requestedAgain;

    public bool IsRunning => _running;

    /// <summary>Completes when this call's run finishes, or at once if a run already in progress will cover it.</summary>
    public async Task RequestAsync()
    {
        if (_running)
        {
            _requestedAgain = true;
            return;
        }

        _running = true;
        try
        {
            do
            {
                _requestedAgain = false;
                await operation();
            }
            while (_requestedAgain);
        }
        finally
        {
            _running = false;
        }
    }
}
