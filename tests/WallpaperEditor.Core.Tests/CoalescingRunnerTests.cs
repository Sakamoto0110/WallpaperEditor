using WallpaperEditor.Core.Publishing;

namespace WallpaperEditor.Core.Tests;

public class CoalescingRunnerTests
{
    [Fact]
    public async Task A_single_request_runs_once()
    {
        var runs = 0;
        var runner = new CoalescingRunner(() =>
        {
            runs++;
            return Task.CompletedTask;
        });

        await runner.RequestAsync();

        Assert.Equal(1, runs);
        Assert.False(runner.IsRunning);
    }

    [Fact]
    public async Task Requests_during_a_run_collapse_into_one_more_run()
    {
        var gate = new TaskCompletionSource();
        var runs = 0;
        var runner = new CoalescingRunner(async () =>
        {
            runs++;
            if (runs == 1)
            {
                await gate.Task;
            }
        });

        var first = runner.RequestAsync();
        await runner.RequestAsync();
        await runner.RequestAsync();
        await runner.RequestAsync();
        Assert.Equal(1, runs);

        gate.SetResult();
        await first;

        Assert.Equal(2, runs);
    }

    [Fact]
    public async Task The_last_run_starts_after_the_last_request()
    {
        // What matters: the state the final run sees includes the last click.
        var state = 0;
        var seen = new List<int>();
        var gate = new TaskCompletionSource();
        var runner = new CoalescingRunner(async () =>
        {
            seen.Add(state);
            if (seen.Count == 1)
            {
                await gate.Task;
            }
        });

        state = 1;
        var first = runner.RequestAsync();
        state = 2;
        await runner.RequestAsync();
        state = 3;
        await runner.RequestAsync();

        gate.SetResult();
        await first;

        Assert.Equal(3, seen[^1]);
    }

    [Fact]
    public async Task A_failed_run_does_not_block_later_requests()
    {
        var fail = true;
        var runs = 0;
        var runner = new CoalescingRunner(() =>
        {
            runs++;
            return fail ? Task.FromException(new IOException("refused")) : Task.CompletedTask;
        });

        await Assert.ThrowsAsync<IOException>(runner.RequestAsync);
        Assert.False(runner.IsRunning);

        fail = false;
        await runner.RequestAsync();

        Assert.Equal(2, runs);
    }
}
