using System.Collections.Concurrent;

namespace DiscRippa.Tests;

/// <summary>A stand-in for the WPF UI thread: one thread that runs posted work in order.</summary>
public sealed class SingleThreadContext : SynchronizationContext, IDisposable
{
    #region Fields

    private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();
    private readonly Thread _thread;

    #endregion

    #region Properties

    public int ThreadId => _thread.ManagedThreadId;

    #endregion

    #region Constructors

    /// <summary>Starts the thread.</summary>
    public SingleThreadContext()
    {
        _thread = new Thread(() =>
        {
            SetSynchronizationContext(this);
            foreach (var (callback, state) in _queue.GetConsumingEnumerable()) callback(state);
        }) { IsBackground = true };
        _thread.Start();
    }

    #endregion

    #region Methods

    /// <summary>Queues the callback to run on the thread.</summary>
    public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));

    /// <summary>Runs <paramref name="work"/> on the thread and returns its result.</summary>
    public Task<T> Run<T>(Func<Task<T>> work)
    {
        var tcs = new TaskCompletionSource<T>();
        Post(async _ =>
        {
            try
            {
                tcs.SetResult(await work());
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        }, null);
        return tcs.Task;
    }

    /// <summary>Stops the thread.</summary>
    public void Dispose() => _queue.CompleteAdding();

    #endregion
}

public class ThreadingTests
{
    #region Methods

    /// <summary>Debug mode output arrives on the UI thread.</summary>
    [Fact]
    public async Task Debug_mode_output_arrives_on_the_UI_thread()
    {
        //the bug: in debug mode the simulator reported progress from a background thread, and adding a
        //line to the on-screen MakeMKV log threw "This type of CollectionView does not support changes...".
        using var ui = new SingleThreadContext();
        using var dir = new TempDir();
        var sim = new SimulatedRunner { SpeedFactor = 0.01, DiscSwapDelay = TimeSpan.Zero };
        int wrongThread = 0, progressCalls = 0, messages = 0;

        var ok = await ui.Run(async () =>
        {
            var disc = await new DiscWatcher(sim, () => SimulatedRunner.FakeMakeMkv, TimeSpan.Zero)
                .WaitForDiscAsync(null, CancellationToken.None);
            if (Environment.CurrentManagedThreadId != ui.ThreadId) wrongThread++;

            var result = await new RipSession(sim, () => SimulatedRunner.FakeMakeMkv).RunAsync(
                new RipRequest { Titles = new[] { disc.Titles[2] }, DestDir = dir.Path, BaseName = "Trailer" },
                p =>
                {
                    progressCalls++;
                    if (Environment.CurrentManagedThreadId != ui.ThreadId) wrongThread++;
                },
                n =>
                {
                    if (Environment.CurrentManagedThreadId != ui.ThreadId) wrongThread++;
                },
                CancellationToken.None,
                m =>
                {
                    messages++;
                    if (Environment.CurrentManagedThreadId != ui.ThreadId) wrongThread++;
                });
            return result.Success;
        }).WaitAsync(TimeSpan.FromSeconds(30));

        Assert.True(ok);
        Assert.Equal(0, wrongThread);
        Assert.True(progressCalls > 10);
        Assert.True(messages > 0);
    }

    /// <summary>Debug mode compression updates arrive on the UI thread.</summary>
    [Fact]
    public async Task Debug_mode_compression_updates_arrive_on_the_UI_thread()
    {
        using var ui = new SingleThreadContext();
        using var dir = new TempDir();
        var sim = new SimulatedRunner { SpeedFactor = 0.02 };
        var file = dir.File("movie.mkv", "placeholder\nsize=1920x1080\n");
        int wrongThread = 0;

        var status = await ui.Run(async () =>
        {
            var queue = new CompressQueue(sim, dir.Combine("queue.json")) { HandBrakePath = () => SimulatedRunner.FakeHandBrake };
            var done = new TaskCompletionSource<JobStatus>();
            queue.JobFinished += j => done.TrySetResult(j.Status);
            queue.Add(new[] { file }, dir.Path, Presets.Auto);
            queue.Jobs[0].PropertyChanged += (_, _) =>
            {
                if (Environment.CurrentManagedThreadId != ui.ThreadId) Interlocked.Increment(ref wrongThread);
            };
            return await done.Task;
        }).WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(JobStatus.Done, status);
        Assert.Equal(0, wrongThread);
    }

    #endregion
}
