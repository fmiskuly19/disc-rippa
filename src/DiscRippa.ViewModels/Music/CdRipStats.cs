namespace DiscRippa.ViewModels.Music;

/// <summary>The live numbers under the progress bar while cyanrip rips: current track, elapsed, time left and speed ("8.2x").</summary>
public sealed partial class CdRipStats : ObservableObject
{
    #region Fields

    private Ticker? _ticker;
    private Func<double> _progress = () => 0;
    private DateTime _started;
    private int _audioSeconds;

    #endregion

    #region Properties

    [ObservableProperty] private bool isVisible;
    [ObservableProperty] private string trackText = "";
    [ObservableProperty] private string elapsedText = "—";
    [ObservableProperty] private string remainingText = "—";
    [ObservableProperty] private string speedText = "—";

    #endregion

    #region Methods

    /// <summary>Starts the clock for a rip of <paramref name="audioSeconds"/> of music.</summary>
    /// <param name="audioSeconds">How much music is being ripped, to work out the speed (1x = real time).</param>
    public void Start(int audioSeconds, Func<double> progress)
    {
        _started = DateTime.Now;
        _audioSeconds = audioSeconds;
        _progress = progress;
        TrackText = "Starting cyanrip…";
        ElapsedText = "0:00";
        RemainingText = "working it out…";
        SpeedText = "—";
        IsVisible = true;
        _ticker?.Dispose();
        _ticker = Ticker.Start(TimeSpan.FromSeconds(1), Update);
    }

    /// <summary>Stops the clock, with one last update of the numbers.</summary>
    public void Stop()
    {
        if (_ticker == null) return;
        _ticker.Dispose();
        _ticker = null;
        Update();
    }

    /// <summary>Shows the numbers as finished.</summary>
    public void ShowFinished()
    {
        TrackText = "Done";
        RemainingText = "—";
    }

    /// <summary>Stops the clock and hides the numbers.</summary>
    public void Hide()
    {
        Stop();
        IsVisible = false;
    }

    /// <summary>Works out elapsed, time left and speed.</summary>
    private void Update()
    {
        var spent = DateTime.Now - _started;
        var progress = _progress();
        ElapsedText = Text.Clock(spent);
        if (progress <= 0.5 || spent.TotalSeconds < 3) return;

        //speed as CD players show it: 1x = real time.
        var x = _audioSeconds * progress / 100.0 / spent.TotalSeconds;
        SpeedText = x > 0 ? $"{x:0.0}x" : "—";
        if (progress < 100)
        {
            var rate = progress / spent.TotalSeconds;
            RemainingText = rate > 0 ? Text.Clock(TimeSpan.FromSeconds((100 - progress) / rate)) : "—";
        }
    }

    #endregion
}
