namespace DiscRippa.Tests;

public class AutomationReviewTests
{
    #region Methods

    /// <summary>A pretend Blu-ray with a main movie and a few extras.</summary>
    private static DiscFound Disc() => new(
        0, "YES_DISC_1", "G:",
        new[]
        {
            new TitleInfo { Id = 0, Seconds = 1350, Duration = "0:22:30", Size = 4_000_000_000 },
            new TitleInfo { Id = 1, Seconds = 10047, Duration = "2:47:27", Size = 7_400_000_000 },
            new TitleInfo { Id = 2, Seconds = 23, Duration = "0:00:23", Size = 1 },
        },
        "DVD disc", "Yes Disc 1", "BD-RE HL-DT-ST BD-RE BP50NB40");

    /// <summary>A disc screen for the pretend disc.</summary>
    private static DiscReviewViewModel Review(bool canCompress = true, string defaultPreset = "Fast 1080p30")
    {
        var review = new DiscReviewViewModel(Disc(), System.IO.Path.GetTempPath(), new[] { Presets.Auto, "Fast 1080p30" },
                                    defaultPreset, canCompress, _ => 1_000_000_000_000);
        review.Titles[0].IsSelected = true;   //the user (or a previous state) ticked an extra title
        return review;
    }

    /// <summary>Automation takes only the main movie with Auto compression and ejects.</summary>
    [Fact]
    public void Automation_takes_only_the_main_movie_with_Auto_compression_and_ejects()
    {
        var review = Review();
        review.Eject = false;

        review.StartAutomation();

        Assert.True(review.IsAutomated);
        Assert.False(review.IsEditable);
        Assert.Equal(new[] { 1 }, review.SelectedTitles.Select(t => t.Id));   //2:47:27 main movie only
        Assert.True(review.Compress);
        Assert.Equal(Presets.Auto, review.Preset);
        Assert.True(review.Eject);
        Assert.False(review.CanChangeEject);
        Assert.False(review.CanRip);   //the RIP button is locked; automation presses it
    }

    /// <summary>Automation confirms with its own choices.</summary>
    [Fact]
    public async Task Automation_confirms_with_its_own_choices()
    {
        var review = Review();
        review.StartAutomation();
        review.Confirm();
        var choices = await review.Result;

        Assert.NotNull(choices);
        Assert.Equal("Yes Disc 1", choices!.Name);
        Assert.Single(choices.Titles);
        Assert.True(choices.Compress);
        Assert.Equal(Presets.Auto, choices.Preset);
        Assert.True(choices.Eject);
    }

    /// <summary>Without HandBrake automation rips without compressing.</summary>
    [Fact]
    public void Without_HandBrake_automation_rips_without_compressing()
    {
        var review = Review(canCompress: false);
        Assert.False(review.Compress);   //nothing to compress with, so not ticked
        review.StartAutomation();
        Assert.False(review.Compress);
    }

    /// <summary>Let me choose unlocks the screen for this disc.</summary>
    [Fact]
    public void Let_me_choose_unlocks_the_screen_for_this_disc()
    {
        var review = Review();
        review.StartAutomation();
        review.AutomationText = "Starting in 2 s…";

        review.TakeOver();

        Assert.False(review.IsAutomated);
        Assert.True(review.IsEditable);
        Assert.True(review.TookOver);
        Assert.True(review.CanRip);
        Assert.Equal("", review.AutomationText);
        Assert.False(review.CanChangeEject);   //eject stays on: automation will want the next disc
    }

    /// <summary>Keep Ripping! locks eject on.</summary>
    [Fact]
    public void Do_another_disc_locks_eject_on()
    {
        var review = Review();
        review.Eject = false;
        review.LockEject();
        Assert.True(review.Eject);
        Assert.False(review.CanChangeEject);
        Assert.Contains("ready for the next one", review.AfterText);
    }

    /// <summary>Taking over when not automated does nothing.</summary>
    [Fact]
    public void Taking_over_when_not_automated_does_nothing()
    {
        var review = Review();
        review.TakeOver();
        Assert.False(review.TookOver);
        Assert.True(review.IsEditable);
    }

    #endregion
}

public class AutomationReadFailureTests
{
    #region Methods

    /// <summary>A disc that cannot be read says which disc so automation can eject and skip it.</summary>
    [Fact]
    public async Task A_disc_that_cannot_be_read_says_which_disc_so_automation_can_eject_and_skip_it()
    {
        var runner = new FakeRunner((exe, args, onLine, ct) =>
        {
            if (args.Contains("disc:9999")) onLine?.Invoke("DRV:0,2,999,12,\"BD-RE\",\"DIRTY_DISC\",\"G:\"");
            return FakeRunner.Ok();
        });
        var watcher = new DiscWatcher(runner, () => "makemkvcon", TimeSpan.Zero) { RetryDelay = TimeSpan.Zero };

        var error = await Assert.ThrowsAsync<DiscReadException>(() => watcher.WaitForDiscAsync(null, CancellationToken.None));

        Assert.Equal("DIRTY_DISC", error.Label);
        Assert.Equal("G:", error.Device);
    }

    /// <summary>Automation and Keep Ripping! are saved settings.</summary>
    [Fact]
    public void Automation_and_do_another_are_saved_settings()
    {
        using var dir = new TempDir();
        var file = dir.Combine("settings.json");
        Assert.False(AppSettings.Load(file).AutomationMode);   //off unless switched on
        new AppSettings { AutomationMode = true, AutoNext = false }.Save(file);
        var s = AppSettings.Load(file);
        Assert.True(s.AutomationMode);
        Assert.False(s.AutoNext);
    }

    #endregion
}
