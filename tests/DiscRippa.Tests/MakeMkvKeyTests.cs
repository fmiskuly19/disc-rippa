namespace DiscRippa.Tests;

public class MakeMkvKeyTests
{
    #region Fields

    //a made-up key in the same shape as MakeMKV's (not a real one).
    private const string Key = "T-AbCdEfGhIjKlMnOpQrStUvWxYz0123456789@_abcdefghijklmnopqrstuvwx";

    #endregion

    #region Methods

    /// <summary>Finds the key in the forum post.</summary>
    [Fact]
    public void Finds_the_key_in_the_forum_post()
    {
        var html = "<div class=\"content\">MakeMKV is free while in beta.<br>The current beta key:<br>" +
                   $"<div class=\"codebox\"><pre><code>{Key}</code></pre></div>valid until end of December 2026<br>" +
                   "You can always find the latest key here.</div>";

        var found = MakeMkvKey.ExtractBetaKey(html);

        Assert.NotNull(found);
        Assert.Equal(Key, found!.Value.Key);
        Assert.Equal("end of December 2026", found.Value.ValidUntil);
    }

    /// <summary>A page without a key gives nothing.</summary>
    [Fact]
    public void A_page_without_a_key_gives_nothing()
    {
        Assert.Null(MakeMkvKey.ExtractBetaKey("<html><body>Please log in. T-short</body></html>"));
        Assert.Null(MakeMkvKey.ExtractBetaKey(""));
    }

    /// <summary>Checks typed keys.</summary>
    [Fact]
    public void Checks_typed_keys()
    {
        Assert.True(MakeMkvKey.IsValidKey(Key));
        Assert.True(MakeMkvKey.IsValidKey("  " + Key + "  "));
        Assert.False(MakeMkvKey.IsValidKey("T-tooshort"));
        Assert.False(MakeMkvKey.IsValidKey(Key + " extra"));
        Assert.False(MakeMkvKey.IsValidKey(""));
        Assert.Equal("T-AbCd…uvwx", MakeMkvKey.Short(Key));
    }

    #endregion
}
