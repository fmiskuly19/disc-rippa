namespace DiscRippa.Tests;

public class DiscInfoParsingTests
{
    #region Properties

    private static readonly string[] InfoLines =
    {
        "MSG:1005,0,1,\"MakeMKV started\",\"%1 started\"",
        "CINFO:1,6209,\"Blu-ray disc\"",
        "CINFO:2,0,\"The Movie\"",
        "CINFO:32,0,\"THE_MOVIE\"",
        "TCOUNT:2",
        "TINFO:0,2,0,\"The Movie\"",
        "TINFO:0,8,0,\"28\"",
        "TINFO:0,9,0,\"1:52:10\"",
        "TINFO:0,10,0,\"25.3 GB\"",
        "TINFO:0,11,0,\"25300000000\"",
        "TINFO:0,16,0,\"00800.mpls\"",
        "TINFO:0,27,0,\"The_Movie_t00.mkv\"",
        "SINFO:0,0,1,6201,\"Video\"",
        "SINFO:0,0,7,0,\"Mpeg4 AVC High@L4.1\"",
        "SINFO:0,0,19,0,\"1920x1080\"",
        "SINFO:0,0,20,0,\"16:9\"",
        "SINFO:0,0,21,0,\"23.976 (24000/1001)\"",
        "SINFO:0,1,1,6202,\"Audio\"",
        "SINFO:0,1,4,0,\"English\"",
        "SINFO:0,1,6,0,\"TrueHD\"",
        "SINFO:0,1,14,0,\"8\"",
        "SINFO:0,1,40,0,\"7.1\"",
        "SINFO:0,2,1,6203,\"Subtitles\"",
        "SINFO:0,2,4,0,\"French\"",
        "SINFO:0,2,6,0,\"PGS\"",
        "TINFO:1,9,0,\"0:01:30\"",
        "TINFO:1,11,0,\"300000000\"",
    };

    #endregion

    #region Methods

    /// <summary>Reads the disc type and name.</summary>
    [Fact]
    public void Reads_the_disc_type_and_name()
    {
        var info = MakeMkvParser.ParseDiscInfo(InfoLines);
        Assert.Equal("Blu-ray disc", info.Type);
        Assert.Equal("The Movie", info.Name);
        Assert.Equal("THE_MOVIE", info.VolumeName);
    }

    /// <summary>Reads title details and tracks.</summary>
    [Fact]
    public void Reads_title_details_and_tracks()
    {
        var titles = MakeMkvParser.ParseTitles(InfoLines);
        Assert.Equal(2, titles.Count);
        var main = titles[0];
        Assert.Equal(28, main.Chapters);
        Assert.Equal("00800.mpls", main.SourceFile);
        Assert.Equal("25.3 GB", main.SizeText);
        Assert.Equal(3, main.Streams.Count);
        Assert.Equal("Video", main.Streams[0].Type);
        Assert.Equal("1920x1080", main.Streams[0].VideoSize);
        Assert.Equal("23.976 (24000/1001)", main.Streams[0].FrameRate);
        Assert.Equal("Audio", main.Streams[1].Type);
        Assert.Equal("7.1", main.Streams[1].ChannelLayout);
        Assert.Equal(8, main.Streams[1].Channels);
        Assert.Equal("French", main.Streams[2].LangName);
        Assert.Empty(titles[1].Streams);
    }

    /// <summary>Reads the drive name.</summary>
    [Fact]
    public void Reads_the_drive_name()
    {
        var drive = MakeMkvParser.FindDisc(new[] { "DRV:0,2,999,12,\"BD-RE HL-DT-ST WH16NS40\",\"THE_MOVIE\",\"D:\"" });
        Assert.Equal("BD-RE HL-DT-ST WH16NS40", drive!.DriveName);
    }

    /// <summary>Reads stage and step names.</summary>
    [Fact]
    public void Reads_stage_and_step_names()
    {
        Assert.Equal(new ProgressTitle(true, "Saving to MKV file"), MakeMkvParser.ParseProgressTitle("PRGT:5017,0,\"Saving to MKV file\""));
        Assert.Equal(new ProgressTitle(false, "Analyzing seamless segments"), MakeMkvParser.ParseProgressTitle("PRGC:5018,0,\"Analyzing seamless segments\""));
        Assert.Null(MakeMkvParser.ParseProgressTitle("PRGV:1,2,3"));
        Assert.Equal((25.0, 50.0), MakeMkvParser.ParseProgressValues("PRGV:16384,32768,65536"));
    }

    /// <summary>Knows the saving stage.</summary>
    [Theory]
    [InlineData("Saving to MKV file", true)]
    [InlineData("Saving all titles to MKV files", true)]
    [InlineData("Opening DVD disc", false)]
    [InlineData("Processing title sets", false)]
    [InlineData("Scanning CD-ROM devices", false)]
    public void Knows_the_saving_stage(string stage, bool saving) => Assert.Equal(saving, MakeMkvParser.IsSavingStage(stage));

    #endregion
}

public class RipProgressStageTests
{
    #region Methods

    /// <summary>Progress does not jump to 100 while MakeMKV is still opening the disc.</summary>
    [Fact]
    public async Task Progress_does_not_jump_to_100_while_MakeMKV_is_still_opening_the_disc()
    {
        //the bug: the bar went 100, then 0, then 3, because the "opening the disc" stage has its own 0-100%.
        using var dir = new TempDir();
        var runner = new FakeRunner((exe, args, onLine, ct) =>
        {
            var dest = args[args.Count - 1];
            onLine?.Invoke("PRGT:5018,0,\"Opening Blu-ray disc\"");
            onLine?.Invoke("PRGC:5018,0,\"Processing BD+ code\"");
            onLine?.Invoke("PRGV:32768,32768,65536");
            onLine?.Invoke("PRGV:65536,65536,65536");      //opening stage reaches 100%
            onLine?.Invoke("PRGT:5017,0,\"Saving to MKV file\"");
            File.WriteAllText(Path.Combine(dest, "title_t00.mkv"), "video");
            onLine?.Invoke("PRGV:0,0,65536");              //saving starts at 0
            onLine?.Invoke("PRGV:1966,1966,65536");        //3%
            onLine?.Invoke("PRGV:32768,32768,65536");      //50%
            onLine?.Invoke("PRGV:65536,65536,65536");      //100%
            onLine?.Invoke("MSG:5036,0,1,\"Copy complete. 1 titles saved.\",\"x\"");
            return FakeRunner.Ok();
        });

        var seen = new List<RipProgress>();
        var result = await new RipSession(runner, () => "makemkvcon").RunAsync(
            new RipRequest { Titles = new[] { new TitleInfo { Id = 0, Seconds = 6000, Duration = "1:40:00" } }, DestDir = dir.Path, BaseName = "Movie" },
            seen.Add, null, CancellationToken.None);

        Assert.True(result.Success);
        var percents = seen.Select(p => p.Percent).ToList();
        for (int i = 1; i < percents.Count; i++)
            Assert.True(percents[i] >= percents[i - 1], $"progress went backwards: {string.Join(", ", percents)}");

        //while opening the disc the bar stays at 0 and the stage is reported as "not saving yet".
        var opening = seen.Where(p => p.Stage == "Opening Blu-ray disc").ToList();
        Assert.NotEmpty(opening);
        Assert.All(opening, p => Assert.False(p.Saving));
        Assert.All(opening, p => Assert.Equal(0, p.Percent));
        Assert.Contains(opening, p => p.StagePercent == 100);   //...but its own progress is still shown

        var saving = seen.Where(p => p.Stage == "Saving to MKV file").ToList();
        Assert.All(saving, p => Assert.True(p.Saving));
        Assert.Contains(saving, p => Math.Abs(p.Percent - 3) < 0.1);
        Assert.Equal(100, percents.Max(), 1);
    }

    /// <summary>Messages and stage changes are passed on for the live log.</summary>
    [Fact]
    public async Task Messages_and_stage_changes_are_passed_on_for_the_live_log()
    {
        using var dir = new TempDir();
        var runner = new FakeRunner((exe, args, onLine, ct) =>
        {
            File.WriteAllText(Path.Combine(args[args.Count - 1], "title_t00.mkv"), "video");
            onLine?.Invoke("MSG:1005,0,1,\"MakeMKV v1.17.7 started\",\"x\"");
            onLine?.Invoke("PRGT:5017,0,\"Saving to MKV file\"");
            onLine?.Invoke("MSG:5036,0,1,\"Copy complete. 1 titles saved.\",\"x\"");
            return FakeRunner.Ok();
        });
        var log = new List<string>();
        await new RipSession(runner, () => "makemkvcon").RunAsync(
            new RipRequest { Titles = new[] { new TitleInfo { Id = 0 } }, DestDir = dir.Path, BaseName = "M" },
            null, null, CancellationToken.None, log.Add);

        Assert.Equal(new[] { "MakeMKV v1.17.7 started", "— Saving to MKV file —", "Copy complete. 1 titles saved." }, log);
    }

    /// <summary>Bytes written counts only this rips files.</summary>
    [Fact]
    public async Task Bytes_written_counts_only_this_rips_files()
    {
        using var dir = new TempDir();
        dir.File("Old Movie.mkv", new string('x', 1000));
        long midway = -1;
        RipSession? session = null;
        var runner = new FakeRunner((exe, args, onLine, ct) =>
        {
            File.WriteAllText(Path.Combine(args[args.Count - 1], "title_t00.mkv"), new string('v', 250));
            midway = session!.BytesWritten();
            return FakeRunner.Ok();
        });
        session = new RipSession(runner, () => "makemkvcon");
        await session.RunAsync(new RipRequest { Titles = new[] { new TitleInfo() }, DestDir = dir.Path, BaseName = "New" },
                               null, null, CancellationToken.None);
        Assert.Equal(250, midway);
    }

    #endregion
}

public class DiscReviewTests
{
    #region Methods

    /// <summary>A pretend disc with these titles.</summary>
    private static DiscFound Disc(params (int Id, int Seconds, long Size)[] titles) => new(
        0, "THE_MOVIE", "D:",
        titles.Select(t => new TitleInfo { Id = t.Id, Seconds = t.Seconds, Duration = $"{t.Seconds / 3600}:{t.Seconds / 60 % 60:00}:{t.Seconds % 60:00}", Size = t.Size }).ToList(),
        "Blu-ray disc", "The Movie", "BD-RE HL-DT-ST WH16NS40");

    /// <summary>A disc screen for <paramref name="disc"/>, saving to <paramref name="folder"/> with this much free space.</summary>
    private static DiscReviewViewModel Review(DiscFound disc, string folder, long? free = 1_000_000_000_000)
        => new(disc, folder, new[] { Presets.Auto, "Fast 1080p30" }, Presets.Auto, canCompress: true, freeSpace: _ => free);

    /// <summary>Starts with the main movie ticked and shown.</summary>
    [Fact]
    public void Starts_with_the_main_movie_ticked_and_shown()
    {
        using var dir = new TempDir();
        var review = Review(Disc((0, 130, 1), (1, 6730, 25_300_000_000), (2, 45, 1)), dir.Path);

        Assert.Equal("The Movie", review.DiscTitle);
        Assert.Equal("The Movie", review.Name);
        Assert.Equal(new[] { 1 }, review.SelectedTitles.Select(t => t.Id));
        Assert.Equal(1, review.SelectedTitle!.Info.Id);
        Assert.Contains("Blu-ray disc", review.DiscSummary);
        Assert.Contains("BD-RE HL-DT-ST WH16NS40 (D:)", review.DiscSummary);
        Assert.Contains("3 titles", review.DiscSummary);
        Assert.Contains("about 25.3 GB", review.SelectionText);
        Assert.Equal(new[] { "The Movie.mkv" }, review.PlannedFiles);
    }

    /// <summary>Keep the MKV is only offered with compression and deleting is the default.</summary>
    [Fact]
    public async Task Keep_the_MKV_is_only_offered_with_compression_and_deleting_is_the_default()
    {
        using var dir = new TempDir();
        var review = Review(Disc((0, 6730, 25_300_000_000)), dir.Path);

        Assert.True(review.Compress);             //compressing is ticked by default
        Assert.True(review.CanKeepOriginal);
        review.Compress = false;
        Assert.False(review.CanKeepOriginal);    //greyed out without a compressed copy
        review.Compress = true;
        Assert.True(review.CanKeepOriginal);
        Assert.False(review.KeepOriginal);
        Assert.Contains("MKV is deleted", review.AfterText);

        review.Confirm();
        var choice = await review.Result;
        Assert.True(choice!.Compress);
        Assert.False(choice.KeepOriginal);
    }

    /// <summary>Without compression the MKV is always kept.</summary>
    [Fact]
    public async Task Without_compression_the_MKV_is_always_kept()
    {
        using var dir = new TempDir();
        var review = Review(Disc((0, 6730, 25_300_000_000)), dir.Path);
        review.Compress = true;
        review.Compress = false;   //KeepOriginal was never ticked, but nothing gets compressed, so nothing is deleted
        review.Confirm();
        Assert.True((await review.Result)!.KeepOriginal);
    }

    /// <summary>MP4 or MKV is chosen per disc.</summary>
    [Fact]
    public async Task MP4_or_MKV_is_chosen_per_disc()
    {
        using var dir = new TempDir();
        var review = Review(Disc((0, 6730, 25_300_000_000)), dir.Path);
        Assert.True(review.SaveAsMp4);
        Assert.Contains("MP4 copy", review.AfterText);

        review.SaveAsMkv = true;
        Assert.False(review.SaveAsMp4);
        Assert.Contains("MKV copy", review.AfterText);
        review.Confirm();
        Assert.True((await review.Result)!.SaveAsMkv);

        var remembered = new DiscReviewViewModel(Disc((0, 6730, 1)), dir.Path, new[] { Presets.Auto }, Presets.Auto, true, _ => null, saveAsMkv: true);
        Assert.True(remembered.SaveAsMkv);   //last disc's choice is offered again
    }

    /// <summary>Automation locks the keep choice too.</summary>
    [Fact]
    public void Automation_locks_the_keep_choice_too()
    {
        using var dir = new TempDir();
        var review = Review(Disc((0, 6730, 25_300_000_000)), dir.Path);
        review.StartAutomation();
        Assert.True(review.Compress);
        Assert.False(review.CanKeepOriginal);
    }

    /// <summary>Quick selections work.</summary>
    [Fact]
    public void Quick_selections_work()
    {
        using var dir = new TempDir();
        var review = Review(Disc((0, 2650, 2), (1, 2640, 2), (2, 2660, 2), (3, 70, 1)), dir.Path);

        review.SelectAllLong();
        Assert.Equal(new[] { 0, 1, 2 }, review.SelectedTitles.Select(t => t.Id));
        Assert.Equal(new[] { "The Movie - 01.mkv", "The Movie - 02.mkv", "The Movie - 03.mkv" }, review.PlannedFiles);

        review.SelectNone();
        Assert.Empty(review.SelectedTitles);
        Assert.False(review.CanRip);
        Assert.Equal("Tick at least one title to rip.", review.Validate());

        review.SelectMainOnly();
        Assert.Equal(new[] { 2 }, review.SelectedTitles.Select(t => t.Id));
    }

    /// <summary>Ticking a box updates the rip details.</summary>
    [Fact]
    public void Ticking_a_box_updates_the_rip_details()
    {
        using var dir = new TempDir();
        var review = Review(Disc((0, 6730, 20_000_000_000), (1, 1350, 4_000_000_000)), dir.Path);
        var changed = new List<string?>();
        review.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        review.Titles[1].IsSelected = true;

        Assert.Contains(nameof(DiscReviewViewModel.SelectionText), changed);
        Assert.Contains(nameof(DiscReviewViewModel.FilesText), changed);
        Assert.Contains("about 24.0 GB", review.SelectionText);
    }

    /// <summary>Planned names never overwrite an existing file.</summary>
    [Fact]
    public void Planned_names_never_overwrite_an_existing_file()
    {
        using var dir = new TempDir();
        dir.File("The Movie.mkv");
        var review = Review(Disc((0, 6730, 1)), dir.Path);
        Assert.Equal(new[] { "The Movie (2).mkv" }, review.PlannedFiles);
    }

    /// <summary>Warns when the drive is too full.</summary>
    [Fact]
    public void Warns_when_the_drive_is_too_full()
    {
        using var dir = new TempDir();
        var review = Review(Disc((0, 6730, 25_000_000_000)), dir.Path, free: 10_000_000_000);
        Assert.True(review.SpaceIsLow);
        Assert.Contains("not enough room", review.SpaceText);
    }

    /// <summary>A missing folder will be created.</summary>
    [Fact]
    public void A_missing_folder_will_be_created()
    {
        using var dir = new TempDir();
        var review = Review(Disc((0, 6730, 1)), dir.Combine("new folder"));
        Assert.False(review.SpaceIsLow);
        Assert.Contains("will be created", review.SpaceText);
        Assert.Null(review.Validate());
    }

    /// <summary>RIP hands back exactly what was chosen.</summary>
    [Fact]
    public async Task RIP_hands_back_exactly_what_was_chosen()
    {
        using var dir = new TempDir();
        var review = Review(Disc((0, 6730, 1), (1, 1350, 1)), dir.Path);
        review.Name = "My: Movie?";
        review.Titles[1].IsSelected = true;
        review.Compress = true;
        review.Preset = "Fast 1080p30";
        review.Eject = false;

        review.Confirm();
        var choices = await review.Result;

        Assert.NotNull(choices);
        Assert.Equal("My Movie", choices!.Name);   //made safe for Windows
        Assert.Equal(Path.Combine(dir.Path, "My Movie"), choices.Folder);   //two titles: a folder of their own
        Assert.Equal(dir.Path, choices.SaveFolder);
        Assert.Equal(new[] { 0, 1 }, choices.Titles.Select(t => t.Id));
        Assert.True(choices.Compress);
        Assert.Equal("Fast 1080p30", choices.Preset);
        Assert.False(choices.Eject);
    }

    /// <summary>Cancel hands back nothing.</summary>
    [Fact]
    public async Task Cancel_hands_back_nothing()
    {
        using var dir = new TempDir();
        var review = Review(Disc((0, 6730, 1)), dir.Path);
        review.Cancel();
        Assert.Null(await review.Result);
    }

    /// <summary>The simulated disc has full details for the screen.</summary>
    [Fact]
    public async Task The_simulated_disc_has_full_details_for_the_screen()
    {
        var sim = new SimulatedRunner { SpeedFactor = 0, DiscSwapDelay = TimeSpan.Zero };
        var disc = await new DiscWatcher(sim, () => SimulatedRunner.FakeMakeMkv, TimeSpan.Zero)
            .WaitForDiscAsync(null, CancellationToken.None);

        Assert.Equal("Blu-ray disc", disc.DiscType);
        Assert.Equal("Simulated Movie 1", disc.DiscName);
        Assert.Equal("BD-RE SIMULATED DRIVE", disc.DriveName);
        var main = disc.Titles[0];
        Assert.Equal(28, main.Chapters);
        Assert.Equal("00800.mpls", main.SourceFile);
        Assert.Equal(new[] { "Video", "Audio", "Audio", "Audio", "Subtitles", "Subtitles", "Subtitles" }, main.Streams.Select(s => s.Type));

        using var dir = new TempDir();
        var review = new DiscReviewViewModel(disc, dir.Path, new[] { Presets.Auto }, Presets.Auto, true, _ => 1_000_000_000_000);
        Assert.Contains("Main movie", review.Titles[0].Summary);
        Assert.Contains("1920x1080", review.Titles[0].Summary);
        Assert.Contains("3 audio", review.Titles[0].Summary);
        Assert.Equal("1920x1080  ·  23.976 fps  ·  16:9", review.Titles[0].Streams[0].Details);
    }

    #endregion
}

public class SeveralTitlesTests
{
    #region Methods

    /// <summary>A pretend disc with these titles.</summary>
    private static DiscFound Disc() => new(0, "THE_MOVIE", "D:", new[]
    {
        new TitleInfo { Id = 0, Seconds = 6730, Duration = "1:52:10", Size = 1, SourceFile = "00800.mpls" },
        new TitleInfo { Id = 1, Seconds = 1350, Duration = "0:22:30", Size = 1, SourceFile = "00801.mpls" },
    });

    /// <summary>One title is a file name and several are a folder name.</summary>
    [Fact]
    public void One_title_is_a_file_name_and_several_are_a_folder_name()
    {
        using var dir = new TempDir();
        var review = new DiscReviewViewModel(Disc(), dir.Path, new[] { Presets.Auto }, Presets.Auto, true, _ => null);
        review.Name = "The Movie";
        Assert.False(review.IsMultiple);
        Assert.Equal("File name", review.NameLabel);
        Assert.Equal(dir.Path, review.TargetFolder);

        var changed = new List<string?>();
        review.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        review.Titles[1].IsSelected = true;

        Assert.True(review.IsMultiple);
        Assert.Equal("Folder name", review.NameLabel);
        Assert.Equal(Path.Combine(dir.Path, "The Movie"), review.TargetFolder);
        Assert.Contains(Path.Combine(dir.Path, "The Movie"), review.NameHint);
        Assert.Contains(nameof(DiscReviewViewModel.NameLabel), changed);
        Assert.Equal(new[] { "The Movie - 01 (00800.mpls).mkv", "The Movie - 02 (00801.mpls).mkv" }, review.PlannedFiles);

        review.Titles[1].IsSelected = false;
        Assert.Equal("File name", review.NameLabel);
        Assert.Equal(new[] { "The Movie.mkv" }, review.PlannedFiles);   //one title: just the name
    }

    #endregion
}
