namespace DiscRippa.ViewModels.Common;

/// <summary>
/// Runs something every so often until disposed. Ticks arrive on the thread that started it (the UI thread in the app),
/// so they can update bound properties directly.
/// </summary>
public sealed class Ticker : IDisposable
{
    #region Fields

    private readonly PeriodicTimer _timer;

    #endregion

    #region Constructors

    /// <summary>Starts calling <paramref name="tick"/> every <paramref name="interval"/>.</summary>
    private Ticker(TimeSpan interval, Func<Task> tick)
    {
        _timer = new PeriodicTimer(interval);
        _ = RunAsync(tick);
    }

    #endregion

    #region Methods

    /// <summary>Calls <paramref name="tick"/> every <paramref name="interval"/> until disposed.</summary>
    public static Ticker Start(TimeSpan interval, Action tick) => new(interval, () =>
    {
        tick();
        return Task.CompletedTask;
    });

    /// <summary>Calls <paramref name="tick"/> every <paramref name="interval"/> until disposed.</summary>
    public static Ticker Start(TimeSpan interval, Func<Task> tick) => new(interval, tick);

    /// <summary>The timer loop. A tick that throws is logged and the timer carries on.</summary>
    private async Task RunAsync(Func<Task> tick)
    {
        try
        {
            while (await _timer.WaitForNextTickAsync())
            {
                try
                {
                    await tick();
                }
                catch (Exception ex)
                {
                    CrashLog.Write(ex);
                }   //one bad tick mustn't stop the timer
            }
        }
        catch (ObjectDisposedException)
        {
        }
    }

    /// <summary>Stops the timer.</summary>
    public void Dispose() => _timer.Dispose();

    #endregion
}
