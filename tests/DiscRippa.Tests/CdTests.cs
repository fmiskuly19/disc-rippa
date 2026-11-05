namespace DiscRippa.Tests;

public class CyanripParsingTests
{
    #region Methods

    /// <summary>Reads the album from real cyanrip info output.</summary>
    [Fact]
    public void Reads_the_album_from_real_cyanrip_info_output()
    {
        var meta = Cyanrip.ParseInfo(Fixture.CyanripInfo);

        Assert.True(meta.Found);
        Assert.Equal("The Eternal Dance", meta.Album);
        Assert.Equal("Earth, Wind & Fire", meta.AlbumArtist);
        Assert.Equal("1992-09-22", meta.Date);
        Assert.Equal(18, meta.TrackTitles.Count);
        Assert.Equal("Fan the Fire", meta.TrackTitles[1]);
        Assert.Equal("I Think About Lovin’ You", meta.TrackTitles[3]);
        Assert.Equal("That’s the Way of the World", meta.TrackTitles[18]);
        Assert.Equal("Earth, Wind & Fire", meta.TrackArtists[7]);
        Assert.Equal(1, meta.Disc);
        Assert.Equal(3, meta.TotalDiscs);
        Assert.Equal("Volume 1, 1971-1975", meta.DiscName);
        Assert.StartsWith("HL-DT-ST BD-RE BP50NB40", meta.DriveModel);
        Assert.Equal(6, Cyanrip.KnownOffset(meta.DriveModel));
    }

    /// <summary>An unknown CD gives no names.</summary>
    [Fact]
    public void An_unknown_CD_gives_no_names()
    {
        var meta = Cyanrip.ParseInfo(new[] { "Opening drive...", "No MusicBrainz release found!", "Disc tracks:    4", "Tracks:", "Track 1 info:", "  Metadata:", "    track:        1" });
        Assert.False(meta.Found);
        Assert.Empty(meta.TrackTitles);
    }

    /// <summary>Follows the progress of a real rip.</summary>
    [Fact]
    public void Follows_the_progress_of_a_real_rip()
    {
        var lines = Fixture.CyanripRip;
        var progress = lines.Select(Cyanrip.ParseProgress).Where(p => p.Percent != null).ToList();

        Assert.True(progress.Count > 50);
        Assert.All(progress, p => Assert.Equal(4, p.Track));
        Assert.Equal(0.03, progress[0].Percent!.Value, 2);
        Assert.Equal(100, progress[^1].Percent!.Value, 2);
        Assert.Equal(TimeSpan.FromHours(5) + TimeSpan.FromMinutes(50), Cyanrip.ParseEta(lines.First(Cyanrip.IsProgressLine)));
        Assert.Equal(4, lines.Select(Cyanrip.ParseTrackDone).Single(n => n != null));
        Assert.Equal(0, lines.Select(Cyanrip.ParseErrorCount).Single(n => n != null));
    }

    /// <summary>Normal lookup notes are not counted as problems.</summary>
    [Fact]
    public void Normal_lookup_notes_are_not_counted_as_problems()
    {
        var flagged = Fixture.CyanripInfo.Concat(Fixture.CyanripRip).Where(Cyanrip.LooksLikeProblem).ToList();
        Assert.Empty(flagged);
        Assert.True(Cyanrip.LooksLikeProblem("Couldn't open path \"x\" for writing: No such file or directory!"));
        Assert.True(Cyanrip.IsOffsetUnset("Offset is unset! To continue with an offset of 0, run with -s 0!"));
    }

    /// <summary>Rip command is plain ASCII with no names and no quotes.</summary>
    [Fact]
    public void Rip_command_is_plain_ASCII_with_no_names_and_no_quotes()
    {
        var args = Cyanrip.RipArgs("G:", "_discrippa-1", new[] { 3, 1 }, offset: 6);

        Assert.Equal(new[] { "-d", "G:", "-s", "6", "-o", "flac", "-N", "-D", "_discrippa-1", "-F", "{track}", "-L", "rip", "-M", "rip", "-K", "-l", "1,3" }, args);
        Assert.All(args, a => Assert.True(a.All(c => c < 128 && c != '"'), a));

        //-K (no ReplayGain) is what makes cyanrip save each track as it finishes, so an aborted rip keeps them.
    }

    /// <summary>Album folders follow artist album and box sets get a disc folder.</summary>
    [Fact]
    public void Album_folders_follow_artist_album_and_box_sets_get_a_disc_folder()
    {
        Assert.Equal(Path.Combine("M", "Earth, Wind & Fire", "The Eternal Dance", "Disc 2"),
                     Cyanrip.AlbumFolder("M", "Earth, Wind & Fire", "The Eternal Dance", 2, 3));
        Assert.Equal(Path.Combine("M", "ACDC", "Back in Black"), Cyanrip.AlbumFolder("M", "AC/DC", "Back in Black"));
        Assert.Equal(Path.Combine("M", "Unknown Artist", "Unknown Album"), Cyanrip.AlbumFolder("M", " ", ""));
        Assert.Equal(Path.Combine("M", "A", "Live (1999)"), Cyanrip.AlbumFolder("M", "A", "Live {1999}"));
    }

    /// <summary>Reads track lengths from cda files.</summary>
    [Fact]
    public void Reads_track_lengths_from_cda_files()
    {
        var data = new byte[44];
        "RIFF"u8.CopyTo(data);
        "CDDAfmt "u8.CopyTo(data.AsSpan(8));
        BitConverter.GetBytes((ushort)4).CopyTo(data, 22);       //track 4
        BitConverter.GetBytes(3927u).CopyTo(data, 32);           //3927 sectors = 52.36 s
        var track = CdaReader.Parse(data);

        Assert.NotNull(track);
        Assert.Equal(4, track!.Number);
        Assert.Equal(52, track.Seconds);
        Assert.Equal("0:52", track.Duration);
        Assert.Null(CdaReader.Parse(new byte[44]));
    }

    #endregion
}

public class CdRipSessionTests
{
    #region Methods

    /// <summary>A rip request for these tracks into <paramref name="folder"/>.</summary>
    private static CdRipRequest Request(string folder, params int[] tracks)
    {
        var meta = new CdMetadata { Album = "The Eternal Dance", AlbumArtist = "Earth, Wind & Fire", Date = "1992", Disc = 1, TotalDiscs = 3 };
        meta.TrackTitles[1] = "Fan the Fire";
        meta.TrackTitles[3] = "I Think About Lovin’ You";
        meta.TrackTitles[4] = "Interlude";
        meta.TrackArtists[3] = "Björk & Friends";
        return new() { Drive = "G:", Folder = folder, Tracks = tracks, Offset = 6, TotalTracks = 18, Metadata = meta };
    }

    /// <summary>Behaves like real cyanrip: writes NN.flac into -D (relative to where it was started), plus rip.cue and rip.log.</summary>
    private static FakeRunner Cyanrip(Func<int, byte[]>? fileFor = null, Action<Action<string>?>? extra = null)
    {
        FakeRunner? runner = null;
        runner = new FakeRunner((exe, args, onLine, ct) =>
        {
            var list = args.ToList();
            var dir = Path.Combine(runner!.WorkingDirectory!, list[list.IndexOf("-D") + 1]);
            Directory.CreateDirectory(dir);
            var tracks = list[list.IndexOf("-l") + 1].Split(',').Select(int.Parse).ToList();
            var cue = new List<string> { "REM GENRE Funk" };
            foreach (var n in tracks)
            {
                onLine?.Invoke($"Ripping and encoding track {n}, progress - 50.00%, ETA - 10s");
                File.WriteAllBytes(Path.Combine(dir, $"{n:00}.flac"), fileFor?.Invoke(n) ?? Flac.Minimal("cyanrip 0.9.3", 60, "audio"));
                onLine?.Invoke($"Track {n} ripped and encoded successfully!");
                cue.Add($"FILE \"{n:00}.flac\" WAVE");
                cue.Add($"  TRACK {n:00} AUDIO");
                cue.Add("    INDEX 01 00:00:00");
            }
            File.WriteAllLines(Path.Combine(dir, "rip.cue"), cue);
            File.WriteAllText(Path.Combine(dir, "rip.log"), "log");
            extra?.Invoke(onLine);
            onLine?.Invoke("Ripping errors: 0");
            return FakeRunner.Ok();
        });
        return runner;
    }

    /// <summary>Files get their real names and tags written by the app.</summary>
    [Fact]
    public async Task Files_get_their_real_names_and_tags_written_by_the_app()
    {
        using var dir = new TempDir();
        var folder = Path.Combine(dir.Path, "Earth, Wind & Fire", "The Eternal Dance", "Disc 1");
        var runner = Cyanrip();
        var progress = new List<CdRipProgress>();
        var session = new CdRipSession(runner, () => "cyanrip.exe");

        var result = await session.RunAsync(Request(folder, 1, 3, 4), progress.Add, CancellationToken.None);

        Assert.True(result.Success, result.Error);
        Assert.Equal(folder, runner.WorkingDirectory);
        Assert.All(runner.Calls[0], a => Assert.True(a.All(c => c < 128 && c != '"'), a));   //nothing cyanrip can garble
        Assert.Equal(new[] { "01 - Fan the Fire.flac", "03 - I Think About Lovin’ You.flac", "04 - Interlude.flac" }, result.Files);

        var files = Directory.GetFiles(folder).Select(f => Path.GetFileName(f)).OrderBy(f => f, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "01 - Fan the Fire.flac", "03 - I Think About Lovin’ You.flac", "04 - Interlude.flac",
                             "The Eternal Dance (disc 1).cue", "The Eternal Dance (disc 1).log" }, files);
        Assert.Empty(Directory.GetDirectories(folder));   //the scratch folder is gone

        var tags = Flac.ReadTags(Path.Combine(folder, "03 - I Think About Lovin’ You.flac")).ToDictionary(t => t.Key, t => t.Value);
        Assert.Equal("I Think About Lovin’ You", tags["TITLE"]);
        Assert.Equal("Björk & Friends", tags["ARTIST"]);
        Assert.Equal("Earth, Wind & Fire", tags["ALBUMARTIST"]);
        Assert.Equal("The Eternal Dance", tags["ALBUM"]);
        Assert.Equal("1992", tags["DATE"]);
        Assert.Equal("3", tags["TRACKNUMBER"]);
        Assert.Equal("18", tags["TRACKTOTAL"]);
        Assert.Equal("1", tags["DISCNUMBER"]);
        Assert.Equal("3", tags["DISCTOTAL"]);
        Assert.NotNull(Flac.Inspect(Path.Combine(folder, "03 - I Think About Lovin’ You.flac")));

        var cue = File.ReadAllText(Path.Combine(folder, "The Eternal Dance (disc 1).cue"));
        Assert.Contains("FILE \"03 - I Think About Lovin’ You.flac\" WAVE", cue);
        Assert.Contains("PERFORMER \"Earth, Wind & Fire\"", cue);
        Assert.Contains("    TITLE \"Interlude\"", cue);
        Assert.Contains("REM GENRE Funk", cue);

        Assert.Equal(100, progress[^1].Percent, 1);
        Assert.True(progress.Select(p => p.Percent).Zip(progress.Skip(1).Select(p => p.Percent)).All(x => x.Second >= x.First));
        Assert.DoesNotContain(session.Log, CdTestsHelpers.IsProgress);
    }

    /// <summary>Empty files like tonights are caught and never called a success.</summary>
    [Fact]
    public async Task Empty_files_like_tonights_are_caught_and_never_called_a_success()
    {
        //what actually happened: cyanrip said every track was "ripped and encoded successfully" but wrote 0-byte files.
        using var dir = new TempDir();
        var runner = Cyanrip(n => n == 3 ? Array.Empty<byte>() : Flac.Minimal("cyanrip", 60, "audio"));
        var result = await new CdRipSession(runner, () => "c").RunAsync(Request(dir.Path, 1, 3), null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("empty or damaged", result.Error);
        Assert.Contains("(3)", result.Error);
        Assert.Equal(new[] { "01 - Fan the Fire.flac" }, result.Files);
        Assert.False(File.Exists(Path.Combine(dir.Path, "03 - I Think About Lovin’ You.flac")));
    }

    /// <summary>Text that is not FLAC is caught too.</summary>
    [Fact]
    public async Task Text_that_is_not_FLAC_is_caught_too()
    {
        using var dir = new TempDir();
        var runner = Cyanrip(_ => System.Text.Encoding.ASCII.GetBytes("not a flac file at all"));
        var result = await new CdRipSession(runner, () => "c").RunAsync(Request(dir.Path, 1), null, CancellationToken.None);
        Assert.False(result.Success);
        Assert.Empty(result.Files);
    }

    /// <summary>Ripping the same CD again never overwrites the first copy.</summary>
    [Fact]
    public async Task Ripping_the_same_CD_again_never_overwrites_the_first_copy()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "01 - Fan the Fire.flac"), "the first rip");
        var result = await new CdRipSession(Cyanrip(), () => "c").RunAsync(Request(dir.Path, 1), null, CancellationToken.None);

        Assert.True(result.Success, result.Error);
        Assert.Equal(new[] { "01 - Fan the Fire (2).flac" }, result.Files);
        Assert.Equal("the first rip", File.ReadAllText(Path.Combine(dir.Path, "01 - Fan the Fire.flac")));
    }

    /// <summary>Progress moves through several tracks.</summary>
    [Fact]
    public async Task Progress_moves_through_several_tracks()
    {
        using var dir = new TempDir();
        var seen = new List<CdRipProgress>();
        var result = await new CdRipSession(Cyanrip(), () => "c").RunAsync(Request(dir.Path, 1, 3), seen.Add, CancellationToken.None);

        Assert.True(result.Success, result.Error);
        Assert.Contains(seen, p => p.TrackIndex == 1 && Math.Abs(p.Percent - 25) < 0.1);
        Assert.Contains(seen, p => p.TrackIndex == 2 && Math.Abs(p.Percent - 75) < 0.1);
    }

    /// <summary>Missing offset is explained.</summary>
    [Fact]
    public async Task Missing_offset_is_explained()
    {
        using var dir = new TempDir();
        var runner = new FakeRunner((exe, args, onLine, ct) =>
        {
            onLine?.Invoke("Offset is unset! To continue with an offset of 0, run with -s 0!");
            return FakeRunner.Ok();   //real cyanrip exits 0 here
        });
        var result = await new CdRipSession(runner, () => "c").RunAsync(Request(dir.Path, 1), null, CancellationToken.None);
        Assert.False(result.Success);
        Assert.Contains("offset", result.Error);
    }

    /// <summary>Aborting keeps finished tracks and removes the unfinished one.</summary>
    [Fact]
    public async Task Aborting_keeps_finished_tracks_and_removes_the_unfinished_one()
    {
        using var dir = new TempDir();
        using var cts = new CancellationTokenSource();
        FakeRunner? runner = null;
        runner = new FakeRunner(async (exe, args, onLine, ct) =>
        {
            var list = args.ToList();
            var work = Path.Combine(runner!.WorkingDirectory!, list[list.IndexOf("-D") + 1]);
            Directory.CreateDirectory(work);
            File.WriteAllBytes(Path.Combine(work, "01.flac"), Flac.Minimal("c", 60, "a"));
            onLine?.Invoke("Track 1 ripped and encoded successfully!");
            File.WriteAllBytes(Path.Combine(work, "03.flac"), Flac.Minimal("c", 60, "half"));
            File.WriteAllText(Path.Combine(work, "rip.cue"), "FILE \"01.flac\" WAVE");
            File.WriteAllText(Path.Combine(work, "rip.log"), "half a log");
            onLine?.Invoke("Ripping and encoding track 3, progress - 40.00%, ETA - 5s");
            cts.Cancel();
            return await FakeRunner.RunUntilCancelled(ct);
        });
        var result = await new CdRipSession(runner, () => "c").RunAsync(Request(dir.Path, 1, 3), null, cts.Token);

        Assert.True(result.Cancelled);
        Assert.Equal(new[] { "01 - Fan the Fire.flac" }, result.Files);
        Assert.Equal(new[] { "01 - Fan the Fire.flac" }, dir.FileNames());
        Assert.Empty(Directory.GetDirectories(dir.Path));
    }

    /// <summary>A rip that saves nothing leaves no empty folders.</summary>
    [Fact]
    public async Task A_rip_that_saves_nothing_leaves_no_empty_folders()
    {
        using var dir = new TempDir();
        var folder = Path.Combine(dir.Path, "Artist", "Album", "Disc 1 (2)");
        var runner = new FakeRunner((exe, args, onLine, ct) =>
        {
            onLine?.Invoke("Invalid number of tracks: 255!");
            return FakeRunner.Exit(1);
        });
        var result = await new CdRipSession(runner, () => "c").RunAsync(Request(folder, 1), null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Empty(Directory.GetFileSystemEntries(dir.Path));
    }

    /// <summary>A cyanrip error fails the rip with its message.</summary>
    [Fact]
    public async Task A_cyanrip_error_fails_the_rip_with_its_message()
    {
        using var dir = new TempDir();
        var runner = new FakeRunner((exe, args, onLine, ct) =>
        {
            onLine?.Invoke("Unable to init cdio context");
            return FakeRunner.Exit(1);
        });
        var result = await new CdRipSession(runner, () => "c").RunAsync(Request(dir.Path, 1), null, CancellationToken.None);
        Assert.False(result.Success);
        Assert.Contains("Unable to init cdio context", result.Error);
    }

    #endregion
}

public static class CdTestsHelpers
{
    #region Methods

    /// <summary>True for cyanrip's progress lines.</summary>
    public static bool IsProgress(string line) => DiscRippa.Core.Music.Cyanrip.IsProgressLine(line);

    #endregion
}

public class FlacTests
{
    #region Methods

    /// <summary>A real FLAC header is recognised and empty or junk files are not.</summary>
    [Fact]
    public void A_real_FLAC_header_is_recognised_and_empty_or_junk_files_are_not()
    {
        using var dir = new TempDir();
        File.WriteAllBytes(dir.Combine("good.flac"), Flac.Minimal("v", 283, "audio"));
        File.WriteAllBytes(dir.Combine("empty.flac"), Array.Empty<byte>());
        File.WriteAllBytes(dir.Combine("cut.flac"), Flac.Minimal("v", 283, "audio").Take(30).ToArray());
        dir.File("text.flac", "hello");

        var info = Flac.Inspect(dir.Combine("good.flac"));
        Assert.NotNull(info);
        Assert.Equal(44100, info!.SampleRate);
        Assert.Equal(2, info.Channels);
        Assert.Equal(16, info.BitsPerSample);
        Assert.Equal(283, info.Seconds, 1);
        Assert.Null(Flac.Inspect(dir.Combine("empty.flac")));
        Assert.Null(Flac.Inspect(dir.Combine("cut.flac")));
        Assert.Null(Flac.Inspect(dir.Combine("text.flac")));
        Assert.Null(Flac.Inspect(dir.Combine("missing.flac")));
    }

    /// <summary>Tags with any characters are written and other tags and the audio are kept.</summary>
    [Fact]
    public void Tags_with_any_characters_are_written_and_other_tags_and_the_audio_are_kept()
    {
        using var dir = new TempDir();
        var path = dir.Combine("t.flac");
        File.WriteAllBytes(path, Flac.Minimal("cyanrip 0.9.3", 60, "AUDIO-BYTES"));
        Flac.WriteTags(path, new Dictionary<string, string> { ["REPLAYGAIN_TRACK_GAIN"] = "-0.63 dB", ["title"] = "Old", ["TRACK"] = "1" });

        Flac.WriteTags(path, new Dictionary<string, string> { ["TITLE"] = "Lovin’ You — Björk «live»", ["TRACK"] = "", ["TRACKNUMBER"] = "3" });
        var tags = Flac.ReadTags(path);

        Assert.Contains(tags, t => t.Key == "TITLE" && t.Value == "Lovin’ You — Björk «live»");
        Assert.DoesNotContain(tags, t => t.Value == "Old");
        Assert.DoesNotContain(tags, t => t.Key == "TRACK");
        Assert.Contains(tags, t => t.Key == "TRACKNUMBER" && t.Value == "3");
        Assert.Contains(tags, t => t.Key == "REPLAYGAIN_TRACK_GAIN" && t.Value == "-0.63 dB");
        Assert.NotNull(Flac.Inspect(path));
        Assert.EndsWith("AUDIO-BYTES", System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(path)));
        Assert.False(File.Exists(path + ".tagging"));
    }

    #endregion
}

public class CdScreenTests
{
    #region Methods

    /// <summary>A pretend CD with tracks of these lengths.</summary>
    private static CdDriveState Cd(params int[] seconds) => new()
    {
        Letter = "G:", IsReady = true, IsAudioCd = true, Label = "Audio CD",
        Tracks = seconds.Select((s, i) => new CdTrack { Number = i + 1, Seconds = s }).ToList(),
    };

    /// <summary>Shows the found names and where the files go.</summary>
    [Fact]
    public void Shows_the_found_names_and_where_the_files_go()
    {
        var meta = Cyanrip.ParseInfo(Fixture.CyanripInfo);
        var review = new CdReviewViewModel(Cd(Enumerable.Repeat(200, 18).ToArray()), meta, @"C:\Music");

        Assert.True(review.FoundNames);
        Assert.Equal("Earth, Wind & Fire", review.Artist);
        Assert.Equal("The Eternal Dance", review.Album);
        Assert.Equal("1992", review.Year);
        Assert.Equal("Disc 1 of 3: Volume 1, 1971-1975", review.DiscOfText);
        Assert.EndsWith(Path.Combine("Earth, Wind & Fire", "The Eternal Dance", "Disc 1"), review.AlbumFolder);
        Assert.Equal("01 - Fan the Fire.flac", review.PlannedFiles[0]);
        Assert.Equal(18, review.SelectedTracks.Count);
    }

    /// <summary>An unknown CD gets numbered tracks that can be renamed.</summary>
    [Fact]
    public async Task An_unknown_CD_gets_numbered_tracks_that_can_be_renamed()
    {
        var review = new CdReviewViewModel(Cd(180, 200), new CdMetadata(), @"C:\Music");
        Assert.False(review.FoundNames);
        Assert.Equal("Track 01", review.Tracks[0].Title);

        review.Artist = "Me";
        review.Album = "Mixtape";
        review.Tracks[0].Title = "Song: One?";
        review.Tracks[1].IsSelected = false;
        review.Confirm();
        var choice = (await review.Result)!;

        Assert.Equal(new[] { 1 }, choice.Tracks);
        Assert.Equal("Song: One?", choice.Metadata.TrackTitles[1]);
        Assert.Equal("Mixtape", choice.Metadata.Album);
        Assert.EndsWith(Path.Combine("Me", "Mixtape"), choice.Folder);
        Assert.Equal("01 - Song One.flac", review.PlannedFiles[0]);
    }

    /// <summary>Automation locks the screen and takes every track.</summary>
    [Fact]
    public void Automation_locks_the_screen_and_takes_every_track()
    {
        var review = new CdReviewViewModel(Cd(100, 100, 100), new CdMetadata(), @"C:\Music");
        review.Tracks[1].IsSelected = false;
        review.Eject = false;
        review.StartAutomation();

        Assert.True(review.IsAutomated);
        Assert.False(review.IsEditable);
        Assert.False(review.CanRip);
        Assert.Equal(3, review.SelectedTracks.Count);
        Assert.True(review.Eject);

        review.TakeOver();
        Assert.True(review.IsEditable && review.TookOver && review.CanRip);
        Assert.False(review.CanChangeEject);   //still locked: the next CD needs the tray open
    }

    /// <summary>Nothing selected cannot be ripped.</summary>
    [Fact]
    public void Nothing_selected_cannot_be_ripped()
    {
        var review = new CdReviewViewModel(Cd(100), new CdMetadata(), @"C:\Music");
        review.SelectNone();
        Assert.False(review.CanRip);
        Assert.NotNull(review.Validate());
    }

    /// <summary>CD history talks about tracks.</summary>
    [Fact]
    public void CD_history_talks_about_tracks()
    {
        var store = new HistoryStore(null, music: true);
        var entry = store.BeginRip("Audio CD", "Artist - Album", "C:\\Music", 12);
        Assert.Equal("12 tracks", entry.FilesText);
        store.FinishRip(entry, new[] { "01 - a.flac", "02 - b.flac" }, 1);
        Assert.Equal("2 tracks", entry.FilesText);
        Assert.Contains("cyanrip reported 1 problem", entry.Detail);
    }

    #endregion
}

public class CdTextTests
{
    #region Methods

    /// <summary>Reads album and track names stored on the disc.</summary>
    [Fact]
    public void Reads_album_and_track_names_stored_on_the_disc()
    {
        var packs = CdText.Encode(
            new[] { "The Eternal Dance", "Fan the Fire", "Love Is Life", "I Think About Lovin' You (a long title that spans packs)" },
            new[] { "Earth, Wind & Fire", "\t", "\t", "Guest Singer" });
        var meta = CdText.Parse(packs);

        Assert.NotNull(meta);
        Assert.Equal(MetadataSource.CdText, meta!.Source);
        Assert.Equal("The Eternal Dance", meta.Album);
        Assert.Equal("Earth, Wind & Fire", meta.AlbumArtist);
        Assert.Equal(3, meta.TrackTitles.Count);
        Assert.Equal("Fan the Fire", meta.TrackTitles[1]);
        Assert.Equal("I Think About Lovin' You (a long title that spans packs)", meta.TrackTitles[3]);
        Assert.Equal("Earth, Wind & Fire", meta.TrackArtists[2]);   //TAB = same as the track before
        Assert.Equal("Guest Singer", meta.TrackArtists[3]);
        Assert.True(meta.HasTrackNames);
    }

    /// <summary>Accented names survive.</summary>
    [Fact]
    public void Accented_names_survive()
    {
        var meta = CdText.Parse(CdText.Encode(new[] { "Café Tacvba", "Déjà vu" }, new[] { "Björk" }))!;
        Assert.Equal("Café Tacvba", meta.Album);
        Assert.Equal("Déjà vu", meta.TrackTitles[1]);
        Assert.Equal("Björk", meta.AlbumArtist);
    }

    /// <summary>A disc without CD text gives nothing.</summary>
    [Fact]
    public void A_disc_without_CD_text_gives_nothing()
    {
        Assert.Null(CdText.Parse(Array.Empty<byte>()));
        Assert.Null(CdText.Parse(new byte[CdText.PackSize * 3]));
    }

    /// <summary>Only the first language block is used and double byte text is skipped.</summary>
    [Fact]
    public void Only_the_first_language_block_is_used_and_double_byte_text_is_skipped()
    {
        var english = CdText.Encode(new[] { "Album", "Song" }, Array.Empty<string>());
        var other = CdText.Encode(new[] { "Albüm", "Lied" }, Array.Empty<string>());
        for (int i = 0; i < other.Length; i += CdText.PackSize) other[i + 3] |= 0x10;      //block 1
        var wide = CdText.Encode(new[] { "XX", "YY" }, Array.Empty<string>());
        for (int i = 0; i < wide.Length; i += CdText.PackSize) wide[i + 3] |= 0x80;        //double-byte
        var meta = CdText.Parse(other.Concat(wide).Concat(english).ToArray())!;

        Assert.Equal("Album", meta.Album);
        Assert.Equal("Song", meta.TrackTitles[1]);
    }

    /// <summary>Album name only is not enough to skip the online lookup.</summary>
    [Fact]
    public void Album_name_only_is_not_enough_to_skip_the_online_lookup()
    {
        var meta = CdText.Parse(CdText.Encode(new[] { "Just The Album" }, new[] { "Someone" }))!;
        Assert.False(meta.HasTrackNames);
    }

    #endregion
}

public class CdDebugModeTests
{
    #region Methods

    /// <summary>What the app does when a CD goes in: the disc's own names first, MusicBrainz only if it has none.</summary>
    private static async Task<CdMetadata> NamesFor(SimulatedCd sim, CdRipSession session, CdDriveState cd)
    {
        var meta = sim.ReadCdText(cd.Letter) ?? new CdMetadata();
        if (!meta.HasTrackNames) meta = await session.LookUpAsync(cd.Letter, CancellationToken.None);
        return meta;
    }

    /// <summary>The simulated CD goes through the whole workflow.</summary>
    [Fact]
    public async Task The_simulated_CD_goes_through_the_whole_workflow()
    {
        using var dir = new TempDir();
        var sim = new SimulatedCd { SpeedFactor = 0, DiscSwapDelay = TimeSpan.Zero };
        var watcher = new CdWatcher(sim, TimeSpan.FromMilliseconds(1));
        var cd = await watcher.WaitForCdAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(5, cd.Tracks.Count);

        //CD 1 has its names on the disc: nothing goes online, not even during the rip.
        var session = new CdRipSession(sim, () => SimulatedCd.FakeCyanrip);
        var meta = await NamesFor(sim, session, cd);
        Assert.Equal(MetadataSource.CdText, meta.Source);
        Assert.Equal("Opening Track", meta.TrackTitles[1]);
        Assert.Equal("The Simulators", meta.TrackArtists[5]);

        var review = new CdReviewViewModel(cd, meta, dir.Path);
        review.Confirm();
        var choice = await review.Result;
        var result = await session.RunAsync(new CdRipRequest
        {
            Drive = cd.Letter, Folder = choice!.Folder, Tracks = choice.Tracks, Metadata = choice.Metadata, Offset = 6,
            TotalTracks = cd.Tracks.Count,
        }, null, CancellationToken.None);

        Assert.True(result.Success, result.Error);
        Assert.Equal(0, sim.OnlineLookups);
        Assert.Equal(5, result.Files.Count);
        Assert.Equal("01 - Opening Track.flac", result.Files[0]);
        Assert.True(File.Exists(Path.Combine(choice.Folder, result.Files[0])));
        Assert.Equal(7, SimulatedRunner.CleanUpPlaceholders(dir.Path));   //5 tracks, the cue sheet and the log

        //the finished CD is ignored until it leaves; the next one has no CD-Text, so it is looked up online.
        watcher.Ignore = cd.Signature;
        sim.Eject();
        var next = await watcher.WaitForCdAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(3, next.Tracks.Count);
        var nextMeta = await NamesFor(sim, session, next);
        Assert.Equal(MetadataSource.MusicBrainz, nextMeta.Source);
        Assert.Equal("The Box Set", nextMeta.Album);
        Assert.Equal(1, sim.OnlineLookups);
    }

    /// <summary>The same CD is not picked up twice.</summary>
    [Fact]
    public async Task The_same_CD_is_not_picked_up_twice()
    {
        var sim = new SimulatedCd { SpeedFactor = 0, DiscSwapDelay = TimeSpan.Zero };
        var watcher = new CdWatcher(sim, TimeSpan.FromMilliseconds(5));
        var cd = await watcher.WaitForCdAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        watcher.Ignore = cd.Signature;

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => watcher.WaitForCdAsync(cts.Token));
    }

    #endregion
}
