namespace DiscRippa.Tests;

public class PresetCatalogTests
{
    #region Methods

    /// <summary>Reads every preset from a real HandBrake 1 11 list.</summary>
    [Fact]
    public void Reads_every_preset_from_a_real_HandBrake_1_11_list()
    {
        var catalog = Fixture.Catalog;
        Assert.Equal(106, catalog.Names.Count);
        Assert.Equal("Very Fast 2160p60 4K AV1", catalog.Names[0]);
        Assert.Equal("Preservation FFV1 PCM", catalog.Names[^1]);
        Assert.DoesNotContain("CLI Default", catalog.Names);
    }

    /// <summary>Knows which file type each preset makes.</summary>
    [Fact]
    public void Knows_which_file_type_each_preset_makes()
    {
        var catalog = Fixture.Catalog;
        Assert.Equal(".mp4", catalog.ExtensionFor("Fast 1080p30"));
        Assert.Equal(".mp4", catalog.ExtensionFor("Super HQ 480p30 Surround"));
        Assert.Equal(".mkv", catalog.ExtensionFor("H.265 MKV 1080p30"));
        Assert.Equal(".mkv", catalog.ExtensionFor("Roku 2160p60 4K HEVC Surround"));
        Assert.Equal(".mkv", catalog.ExtensionFor("Preservation FFV1"));
        Assert.Equal(".mov", catalog.ExtensionFor("Production ProRes 422"));
    }

    /// <summary>Counts of each file type match the list.</summary>
    [Fact]
    public void Counts_of_each_file_type_match_the_list()
    {
        var catalog = Fixture.Catalog;
        var counts = catalog.Names.GroupBy(catalog.ExtensionFor).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(75, counts[".mp4"]);
        Assert.Equal(20, counts[".mkv"]);
        Assert.Equal(11, counts[".mov"]);
    }

    /// <summary>Unknown presets get a sensible extension.</summary>
    [Fact]
    public void Unknown_presets_get_a_sensible_extension()
    {
        var empty = PresetCatalog.Empty;
        Assert.Equal(".mp4", empty.ExtensionFor("Whatever 1080p"));
        Assert.Equal(".mkv", empty.ExtensionFor("H.265 MKV 720p30"));
        Assert.Equal(".mov", empty.ExtensionFor("Production DNxHR HQX"));
    }

    /// <summary>Ignores the prompt and exit message around the list.</summary>
    [Fact]
    public void Ignores_the_prompt_and_exit_message_around_the_list()
    {
        var catalog = PresetCatalog.Parse("General/\n    Fast 1080p30\n        H.264 video in an MP4 container.\n\nHandBrake has exited.\n\nC:\\tools>");
        Assert.Equal(new[] { "Fast 1080p30" }, catalog.Names);
    }

    #endregion
}

public class HandBrakeTests
{
    #region Methods

    /// <summary>Auto preset matches the source.</summary>
    [Theory]
    [InlineData(2160, 3840, "HQ 2160p60 4K HEVC Surround")]
    [InlineData(1080, 1920, "HQ 1080p30 Surround")]
    [InlineData(800, 1920, "HQ 1080p30 Surround")]   //cropped widescreen Blu-ray must not shrink to 720p
    [InlineData(720, 1280, "HQ 720p30 Surround")]
    [InlineData(536, 1280, "HQ 720p30 Surround")]
    [InlineData(576, 720, "HQ 576p25 Surround")]     //PAL DVD
    [InlineData(480, 720, "HQ 480p30 Surround")]     //NTSC DVD
    [InlineData(480, 640, "HQ 480p30 Surround")]
    [InlineData(360, 480, "HQ 480p30 Surround")]
    public void Auto_preset_matches_the_source(int height, int width, string expected)
    {
        var chosen = HandBrake.ChooseAutoPreset(height, width: width, available: Fixture.Catalog.Names.ToList());
        Assert.Equal(expected, chosen);
        Assert.Contains(chosen, Fixture.Catalog.Names);
    }

    /// <summary>Auto preset falls back when the wanted one does not exist.</summary>
    [Fact]
    public void Auto_preset_falls_back_when_the_wanted_one_does_not_exist()
    {
        var onlyFast = new[] { "Fast 1080p30", "Fast 480p30" };
        Assert.Equal("Fast 1080p30", HandBrake.ChooseAutoPreset(1080, onlyFast, 1920));
        Assert.Equal("Fast 480p30", HandBrake.ChooseAutoPreset(480, onlyFast, 720));
    }

    /// <summary>Reads the source size from a scan.</summary>
    [Fact]
    public void Reads_the_source_size_from_a_scan()
    {
        var scan = "+ title 1:\n  + size: 720x480, pixel aspect: 32/27, display aspect: 1.78, 29.970 fps\n";
        Assert.Equal((720, 480), HandBrake.ParseSourceSize(scan));
        Assert.Null(HandBrake.ParseSourceSize("libhb: no valid title"));
    }

    /// <summary>Reads encoding progress and eta.</summary>
    [Fact]
    public void Reads_encoding_progress_and_eta()
    {
        Assert.True(HandBrake.TryParseProgress("Encoding: task 1 of 1, 45.12 % (30.1 fps, avg 29.9 fps, ETA 00h12m34s)", out var pct, out var eta));
        Assert.Equal(45.12, pct, 2);
        Assert.Equal("0:12:34", eta);
    }

    /// <summary>Multi pass progress is overall.</summary>
    [Fact]
    public void Multi_pass_progress_is_overall()
    {
        Assert.True(HandBrake.TryParseProgress("Encoding: task 2 of 2, 50.00 % (10 fps)", out var pct, out _));
        Assert.Equal(75.0, pct, 2);
    }

    /// <summary>Other lines are not progress.</summary>
    [Fact]
    public void Other_lines_are_not_progress()
    {
        Assert.False(HandBrake.TryParseProgress("Scanning title 1 of 1, 25.00 %", out _, out _));
        Assert.False(HandBrake.TryParseProgress("[12:00:00] hb_init: starting", out _, out _));
    }

    #endregion
}

public class MakeMkvParserTests
{
    #region Methods

    /// <summary>Splits quoted fields.</summary>
    [Fact]
    public void Splits_quoted_fields()
    {
        var f = MakeMkvParser.SplitCsv("1005,0,1,\"MakeMKV started, ok\",\"%1 started\",\"v1\"");
        Assert.Equal(new[] { "1005", "0", "1", "MakeMKV started, ok", "%1 started", "v1" }, f);
    }

    /// <summary>Handles escaped quotes and keeps windows paths.</summary>
    [Fact]
    public void Handles_escaped_quotes_and_keeps_windows_paths()
    {
        var f = MakeMkvParser.SplitCsv("\"say \\\"hi\\\"\",\"C:\\Users\\me\\file.mkv\"");
        Assert.Equal("say \"hi\"", f[0]);
        Assert.Equal("C:\\Users\\me\\file.mkv", f[1]);
    }

    /// <summary>Reads messages and ignores other lines.</summary>
    [Fact]
    public void Reads_messages_and_ignores_other_lines()
    {
        Assert.Equal("Copy complete. 1 titles saved.", MakeMkvParser.ParseMessage("MSG:5005,0,0,\"Copy complete. 1 titles saved.\",\"x\""));
        Assert.Null(MakeMkvParser.ParseMessage("PRGV:1,2,3"));
    }

    /// <summary>Reads progress.</summary>
    [Fact]
    public void Reads_progress()
    {
        Assert.Equal(50.0, MakeMkvParser.ParseProgress("PRGV:3000,32768,65536"));
        Assert.Null(MakeMkvParser.ParseProgress("PRGV:1,2,0"));
        Assert.Null(MakeMkvParser.ParseProgress("MSG:1,2,3,\"x\""));
    }

    /// <summary>Recognises read problems.</summary>
    [Theory]
    [InlineData("Error 'Scsi error - MEDIUM ERROR:UNRECOVERED READ ERROR' occurred while reading 'x' at offset 100", true)]
    [InlineData("Failed to save title 0 to file x", true)]
    [InlineData("The evaluation period has expired", true)]
    [InlineData("This application version is too old.", true)]
    [InlineData("Copy complete. 1 titles saved.", false)]
    [InlineData("Evaluation version, 25 day(s) out of 30 remaining", false)]
    [InlineData("MakeMKV v1.17.7 win(x64-release) started", false)]
    public void Recognises_read_problems(string message, bool expected)
        => Assert.Equal(expected, MakeMkvParser.LooksLikeError(message));

    /// <summary>Reads the copy result.</summary>
    [Fact]
    public void Reads_the_copy_result()
    {
        Assert.Equal((1, 0), MakeMkvParser.ParseCopyComplete("Copy complete. 1 titles saved."));
        Assert.Equal((0, 1), MakeMkvParser.ParseCopyComplete("Copy complete. 0 titles saved, 1 failed."));
        Assert.Null(MakeMkvParser.ParseCopyComplete("Something else"));
    }

    /// <summary>The drive's serial number and firmware version are never shown.</summary>
    [Fact]
    public void Drive_names_lose_their_serial_number()
    {
        Assert.Equal("BD-RE HL-DT-ST BD-RE BP50NB40", MakeMkvParser.CleanDriveName("BD-RE HL-DT-ST BD-RE  BP50NB40 1.01 AB1C2D3E45678"));
        Assert.Equal("BD-RE HL-DT-ST WH16NS40", MakeMkvParser.CleanDriveName("BD-RE HL-DT-ST WH16NS40"));
        var drive = MakeMkvParser.FindDisc(new[] { "DRV:0,2,999,12,\"BD-RE HL-DT-ST BD-RE  BP50NB40 1.01 AB1C2D3E45678\",\"MY_MOVIE\",\"G:\"" });
        Assert.Equal("BD-RE HL-DT-ST BD-RE BP50NB40", drive!.DriveName);
    }

    /// <summary>Finds the drive with a readable disc.</summary>
    [Fact]
    public void Finds_the_drive_with_a_readable_disc()
    {
        var lines = new[]
        {
            "MSG:1005,0,0,\"started\"",
            "DRV:0,0,999,0,\"\",\"\",\"\"",
            "DRV:1,2,999,12,\"BD-RE HL-DT-ST WH16NS40\",\"MY_MOVIE\",\"D:\"",
        };
        var drive = MakeMkvParser.FindDisc(lines);
        Assert.NotNull(drive);
        Assert.Equal(1, drive!.Index);
        Assert.Equal("MY_MOVIE", drive.DiscName);
        Assert.Equal("D:", drive.Device);
    }

    /// <summary>No disc when the tray is empty or open.</summary>
    [Fact]
    public void No_disc_when_the_tray_is_empty_or_open()
    {
        Assert.Null(MakeMkvParser.FindDisc(new[] { "DRV:0,0,999,0,\"\",\"\",\"\"" }));
        Assert.Null(MakeMkvParser.FindDisc(new[] { "DRV:0,1,999,0,\"BD-RE\",\"\",\"D:\"" }));
    }

    /// <summary>Reads titles with duration and size.</summary>
    [Fact]
    public void Reads_titles_with_duration_and_size()
    {
        var lines = new[]
        {
            "TINFO:0,9,0,\"1:56:40\"", "TINFO:0,11,0,\"23456789012\"",
            "TINFO:1,9,0,\"0:02:10\"", "TINFO:1,11,0,\"123456\"",
            "TINFO:0,2,0,\"Main Feature\"",
        };
        var titles = MakeMkvParser.ParseTitles(lines);
        Assert.Equal(2, titles.Count);
        Assert.Equal(7000, titles[0].Seconds);
        Assert.Equal("1:56:40", titles[0].Duration);
        Assert.Equal(23456789012L, titles[0].Size);
        Assert.Equal(130, titles[1].Seconds);
    }

    #endregion
}

public class FileNamesTests
{
    #region Methods

    /// <summary>Removes characters Windows does not allow.</summary>
    [Theory]
    [InlineData("A: Movie?/Name*", "A MovieName")]
    [InlineData("  trailing dots...  ", "trailing dots")]
    [InlineData("???", "Movie")]
    [InlineData("", "Movie")]
    public void Removes_characters_Windows_does_not_allow(string input, string expected)
        => Assert.Equal(expected, FileNames.Safe(input));

    /// <summary>Tidies disc labels.</summary>
    [Theory]
    [InlineData("THE_MOVIE_NAME", "The Movie Name")]
    [InlineData("Already Nice", "Already Nice")]
    [InlineData("DISC_1", "Disc 1")]
    [InlineData("1234", "1234")]
    public void Tidies_disc_labels(string input, string expected)
        => Assert.Equal(expected, FileNames.CleanDiscName(input));

    /// <summary>Never overwrites an existing file.</summary>
    [Fact]
    public void Never_overwrites_an_existing_file()
    {
        using var dir = new TempDir();
        Assert.Equal(dir.Combine("Movie.mkv"), FileNames.UniquePath(dir.Combine("Movie.mkv")));
        dir.File("Movie.mkv");
        Assert.Equal(dir.Combine("Movie (2).mkv"), FileNames.UniquePath(dir.Combine("Movie.mkv")));
        dir.File("Movie (2).mkv");
        Assert.Equal(dir.Combine("Movie (3).mkv"), FileNames.UniquePath(dir.Combine("Movie.mkv")));
    }

    #endregion
}
