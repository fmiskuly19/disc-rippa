using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace DiscRippa.Core.Processes;

/// <summary>
/// Collects lines from background threads and hands them to <c>onLine</c> on the UI thread in batches,
/// at most once per interval. Without a UI thread (tests, console) each line is handled straight away.
/// </summary>
public sealed class LineBatcher
{
    #region Fields

    private readonly SynchronizationContext? _context;
    private readonly Action<string> _onLine;
    private readonly TimeSpan _interval;
    private readonly ConcurrentQueue<string> _lines = new();
    private readonly object _drainGate = new();
    private int _scheduled;

    #endregion

    #region Constructors

    /// <summary>Collects lines and hands them to <paramref name="onLine"/> on the UI thread every <paramref name="interval"/>.</summary>
    public LineBatcher(SynchronizationContext? context, Action<string> onLine, TimeSpan interval)
    {
        _context = context;
        _onLine = onLine;
        _interval = interval;
    }

    #endregion

    #region Methods

    /// <summary>Thread-safe. Queues a line; schedules one delivery if none is pending.</summary>
    public void Add(string line)
    {
        _lines.Enqueue(line);

        var context = _context;
        if (context == null)
        {
            Drain();
            return;
        }

        if (Interlocked.CompareExchange(ref _scheduled, 1, 0) != 0) return;   //a delivery is already on its way
        Task.Delay(_interval).ContinueWith(delayTask => context.Post(state =>
        {
            Interlocked.Exchange(ref _scheduled, 0);   //lines added from here on schedule the next batch
            Drain();
        }, null), TaskScheduler.Default);
    }

    /// <summary>Delivers every queued line, in order. Call on the UI thread.</summary>
    public void Drain()
    {
        lock (_drainGate)
        {
            while (_lines.TryDequeue(out var line)) _onLine(line);
        }
    }

    #endregion
}
