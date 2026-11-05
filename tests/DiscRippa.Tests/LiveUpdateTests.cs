namespace DiscRippa.Tests;

/// <summary>A stand-in for the UI thread that only runs posted work when the test says so.</summary>
public sealed class RecordingContext : SynchronizationContext
{
    #region Fields

    private readonly List<(SendOrPostCallback Callback, object? State)> _posted = new();

    #endregion

    #region Properties

    public int PostCount
    {
        get
        {
            lock (_posted) return _posted.Count;
        }
    }

    #endregion

    #region Methods

    /// <summary>Queues the callback instead of running it.</summary>
    public override void Post(SendOrPostCallback d, object? state)
    {
        lock (_posted) _posted.Add((d, state));
    }

    /// <summary>Runs every queued callback.</summary>
    public void RunPosted()
    {
        List<(SendOrPostCallback Callback, object? State)> work;
        lock (_posted)
        {
            work = _posted.ToList();
            _posted.Clear();
        }
        foreach (var (callback, state) in work) callback(state);
    }

    #endregion
}

public class LineBatcherTests
{
    #region Methods

    /// <summary>A flood of lines reaches the UI thread as one batch not thousands of calls.</summary>
    [Fact]
    public async Task A_flood_of_lines_reaches_the_UI_thread_as_one_batch_not_thousands_of_calls()
    {
        //this is what froze the window: MakeMKV prints progress so fast that posting every line
        //to the UI thread left it no time to redraw or respond to the Abort button.
        var context = new RecordingContext();
        var received = new List<string>();
        var batcher = new LineBatcher(context, received.Add, TimeSpan.FromMilliseconds(50));

        await Task.Run(() =>
        {
            for (int i = 0; i < 10_000; i++) batcher.Add($"PRGV:{i},{i},10000");
        });
        await Task.Delay(300);

        Assert.Equal(1, context.PostCount);   //one delivery, however many lines
        Assert.Empty(received);               //nothing runs until the UI thread gets to it

        context.RunPosted();
        Assert.Equal(10_000, received.Count);
        Assert.Equal("PRGV:0,0,10000", received[0]);
        Assert.Equal("PRGV:9999,9999,10000", received[^1]);
    }

    /// <summary>Lines arriving after a batch get their own batch.</summary>
    [Fact]
    public async Task Lines_arriving_after_a_batch_get_their_own_batch()
    {
        var context = new RecordingContext();
        var received = new List<string>();
        var batcher = new LineBatcher(context, received.Add, TimeSpan.FromMilliseconds(20));

        batcher.Add("first");
        await Task.Delay(150);
        context.RunPosted();
        batcher.Add("second");
        await Task.Delay(150);
        Assert.Equal(1, context.PostCount);
        context.RunPosted();

        Assert.Equal(new[] { "first", "second" }, received);
    }

    /// <summary>Drain delivers everything still waiting.</summary>
    [Fact]
    public void Drain_delivers_everything_still_waiting()
    {
        //used when the program exits, so no line is lost before the caller looks at the result.
        var context = new RecordingContext();
        var received = new List<string>();
        var batcher = new LineBatcher(context, received.Add, TimeSpan.FromSeconds(10));

        batcher.Add("a");
        batcher.Add("b");
        batcher.Drain();

        Assert.Equal(new[] { "a", "b" }, received);
    }

    /// <summary>Without a UI thread lines are handled straight away.</summary>
    [Fact]
    public void Without_a_UI_thread_lines_are_handled_straight_away()
    {
        var received = new List<string>();
        var batcher = new LineBatcher(null, received.Add, TimeSpan.FromSeconds(10));
        batcher.Add("now");
        Assert.Equal(new[] { "now" }, received);
    }

    #endregion
}

public class LiveHistoryTests
{
    #region Methods

    /// <summary>A disc shows up at the top while it is ripping.</summary>
    [Fact]
    public void A_disc_shows_up_at_the_top_while_it_is_ripping()
    {
        using var dir = new TempDir();
        var store = new HistoryStore(dir.Combine("history.json"));
        store.Add("OLD", "Old Movie", dir.Path, new[] { "Old Movie.mkv" }, 0);

        var entry = store.BeginRip("New Movie", "New Movie", dir.Path, 1);

        Assert.Same(entry, store.Entries[0]);
        Assert.True(entry.IsActive);
        Assert.Equal("Starting…", entry.Status);
        Assert.Equal("1 title", entry.FilesText);

        store.UpdateRip(entry, 45.2, "Ripping 45%", 0);
        Assert.Equal(45.2, entry.Progress);
        Assert.Equal("Ripping 45%", entry.Status);

        store.RefreshAll();   //switching tabs must not wipe the live status
        Assert.Equal("Ripping 45%", entry.Status);
    }

    /// <summary>A rip in progress is never saved so a crash cannot leave a ghost row.</summary>
    [Fact]
    public void A_rip_in_progress_is_never_saved_so_a_crash_cannot_leave_a_ghost_row()
    {
        using var dir = new TempDir();
        var file = dir.Combine("history.json");
        var store = new HistoryStore(file);
        store.Add("OLD", "Old", dir.Path, new[] { "Old.mkv" }, 0);
        store.BeginRip("NEW", "New", dir.Path, 1);

        var reloaded = new HistoryStore(file);
        Assert.Equal(new[] { "Old" }, reloaded.Entries.Select(e => e.Name));
    }

    /// <summary>When the rip finishes the row becomes a normal saved record.</summary>
    [Fact]
    public void When_the_rip_finishes_the_row_becomes_a_normal_saved_record()
    {
        using var dir = new TempDir();
        dir.File("Movie.mkv");
        var file = dir.Combine("history.json");
        var store = new HistoryStore(file);
        var entry = store.BeginRip("MOVIE", "Movie", dir.Path, 1);

        store.FinishRip(entry, new[] { "Movie.mkv" }, 0);

        Assert.False(entry.IsActive);
        Assert.Equal("Ripped", entry.Status);
        Assert.Equal("Movie.mkv", entry.FilesText);
        var reloaded = new HistoryStore(file);
        Assert.Single(reloaded.Entries);
        Assert.Equal("Movie", reloaded.Entries[0].Name);
        Assert.False(reloaded.Entries[0].IsActive);
    }

    /// <summary>An aborted rip with nothing finished disappears from the list.</summary>
    [Fact]
    public void An_aborted_rip_with_nothing_finished_disappears_from_the_list()
    {
        using var dir = new TempDir();
        var store = new HistoryStore(dir.Combine("history.json"));
        var entry = store.BeginRip("X", "X", dir.Path, 1);

        store.EndUnfinishedRip(entry, Array.Empty<string>(), 0);

        Assert.Empty(store.Entries);
    }

    /// <summary>An aborted rip keeps the titles that did finish.</summary>
    [Fact]
    public void An_aborted_rip_keeps_the_titles_that_did_finish()
    {
        using var dir = new TempDir();
        dir.File("Show - 01.mkv");
        var store = new HistoryStore(dir.Combine("history.json"));
        var entry = store.BeginRip("SHOW", "Show", dir.Path, 3);

        store.EndUnfinishedRip(entry, new[] { "Show - 01.mkv" }, 0);

        Assert.Single(store.Entries);
        Assert.False(entry.IsActive);
        Assert.Equal("Ripped", entry.Status);
    }

    /// <summary>A disc that is still ripping cannot be removed or cleared.</summary>
    [Fact]
    public void A_disc_that_is_still_ripping_cannot_be_removed_or_cleared()
    {
        using var dir = new TempDir();
        var store = new HistoryStore(dir.Combine("history.json"));
        store.Add("OLD", "Old", dir.Path, new[] { "Old.mkv" }, 0);
        var live = store.BeginRip("NEW", "New", dir.Path, 1);

        Assert.False(store.Remove(live));
        store.Clear();

        Assert.Equal(new[] { live }, store.Entries);
    }

    #endregion
}
