namespace DiscRippa.ViewModels.Video;

/// <summary>
/// The live numbers under the progress bar while MakeMKV rips: stage and step, elapsed, time left, amount written and
/// speed. Updated once a second while a rip runs.
/// </summary>
public sealed partial class VideoRipStats : ObservableObject
{
    #region Fields

    private Ticker? _ticker;
    private Func<long> _bytesOnDisk = () => 0;
    private Func<double> _progress = () => 0;
    private DateTime _started;
    private DateTime? _savingStarted;
    private double _savingStartPercent;
    private long _expectedBytes;
    private readonly Queue<(DateTime When, long Bytes)> _samples = new();

    #endregion

    #region Properties

    [ObservableProperty] private bool isVisible;
    [ObservableProperty] private string stageText = "";
    [ObservableProperty] private string stepText = "";
    [ObservableProperty] private string elapsedText = "—";
    [ObservableProperty] private string remainingText = "—";
    [ObservableProperty] private string writtenText = "—";
    [ObservableProperty] private string speedText = "—";

    /// <summary>
    /// True once MakeMKV is actually writing the file (not just opening / scanning the disc). Until then "Time left" and
    /// "Written" mean nothing, so they're hidden.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Columns))]
    private bool isWriting;

    /// <summary>4 tiles while writing (elapsed, time left, written, speed); 2 before (elapsed, speed).</summary>
    public int Columns => IsWriting ? 4 : 2;

    #endregion

    #region Methods

    /// <summary>Starts the clock for a rip of about <paramref name="expectedBytes"/>.</summary>
    /// <param name="bytesOnDisk">How much the rip has written so far.</param>
    /// <param name="progress">The rip's overall percent.</param>
    public void Start(long expectedBytes, Func<long> bytesOnDisk, Func<double> progress)
    {
        _started = DateTime.Now;
        _savingStarted = null;
        _savingStartPercent = 0;
        _expectedBytes = expectedBytes;
        _bytesOnDisk = bytesOnDisk;
        _progress = progress;
        _samples.Clear();
        IsWriting = false;
        StageText = "Starting MakeMKV…";
        StepText = "";
        ElapsedText = "0:00";
        RemainingText = "working it out…";
        WrittenText = expectedBytes > 0 ? $"0.0 of {DiscReviewViewModel.Gb(expectedBytes)}" : "0.0 GB";
        SpeedText = "—";
        IsVisible = true;

        _ticker?.Dispose();
        _ticker = Ticker.Start(TimeSpan.FromSeconds(1), () => Update(stillRipping: true));
    }

    /// <summary>Stops the clock, with one last update of the numbers.</summary>
    public void Stop()
    {
        if (_ticker == null) return;
        _ticker.Dispose();
        _ticker = null;
        Update(stillRipping: false);
    }

    /// <summary>MakeMKV has started writing (its "saving" stage reached <paramref name="percent"/>).</summary>
    public void SavingAt(double percent, double stagePercent)
    {
        if (_savingStarted == null && stagePercent > 0)
        {
            _savingStarted = DateTime.Now;
            _savingStartPercent = percent;
        }
        if (stagePercent > 0) IsWriting = true;
    }

    /// <summary>Shows the numbers as finished.</summary>
    public void ShowFinished()
    {
        StageText = "Done";
        StepText = "";
        RemainingText = "—";
    }

    /// <summary>Stops the clock and hides the numbers.</summary>
    public void Hide()
    {
        Stop();
        IsVisible = false;
    }

    /// <summary>Works out elapsed, written, speed and time left.</summary>
    private void Update(bool stillRipping)
    {
        var now = DateTime.Now;
        var progress = _progress();
        ElapsedText = Text.Clock(now - _started);

        //bytes on disk, or (when the files are tiny, e.g. debug mode) an estimate from the progress.
        long onDisk = _bytesOnDisk();
        if (onDisk > 1_000_000) IsWriting = true;
        long written = onDisk > 1_000_000 ? onDisk : (long)(_expectedBytes * progress / 100.0);
        WrittenText = _expectedBytes > 0
            ? $"{DiscReviewViewModel.Gb(written)} of {DiscReviewViewModel.Gb(_expectedBytes)}"
            : DiscReviewViewModel.Gb(written);

        _samples.Enqueue((now, written));
        while (_samples.Count > 10) _samples.Dequeue();
        double speed = 0;   //bytes per second over the last ~10 seconds
        if (_samples.Count >= 2)
        {
            var first = _samples.Peek();
            var seconds = (now - first.When).TotalSeconds;
            if (seconds > 0) speed = Math.Max(0, (written - first.Bytes) / seconds);
        }
        SpeedText = speed > 0 ? $"{speed / 1_000_000.0:0.0} MB/s" : "—";

        //time left: from the rate of progress since saving started (works for any disc size).
        if (_savingStarted is DateTime started && progress > _savingStartPercent + 0.5 && progress < 100)
        {
            var spent = (now - started).TotalSeconds;
            var rate = (progress - _savingStartPercent) / spent;   //percent per second
            RemainingText = rate > 0 ? Text.Clock(TimeSpan.FromSeconds((100 - progress) / rate)) : "—";
        }
        else if (stillRipping)
        {
            RemainingText = "working it out…";
        }
    }

    #endregion
}
