using System.Net.Http;
using System.Text.RegularExpressions;

namespace DiscRippa.Core.Video;

/// <summary>
/// MakeMKV's registration key, which unlocks its Blu-ray support. While MakeMKV is in beta its author posts a free key on
/// the MakeMKV forum (renewed every month or two). The app can fetch that key and install it the same way MakeMKV's own
/// Help > Register does, into HKEY_CURRENT_USER\Software\MakeMKV, value "app_Key".
/// </summary>
public static class MakeMkvKey
{
    #region Fields

    public const string BetaKeyPage = "https://forum.makemkv.com/forum/viewtopic.php?t=1053";
    private const string RegistryPath = @"Software\MakeMKV";
    private const string RegistryValue = "app_Key";

    //keys look like "T-" followed by ~60 letters, digits, '@' and '_'.
    private static readonly Regex KeyPattern = new(@"\b([A-Z]-[A-Za-z0-9@_]{40,})", RegexOptions.Compiled);

    private static readonly Regex ValidUntil = new(@"valid\s+until\s+([^<\r\n]{4,40}?)(?:[.<\r\n]|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    #endregion

    #region Methods

    /// <summary>True if this looks like a MakeMKV key.</summary>
    public static bool IsValidKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        var k = key.Trim();
        var m = KeyPattern.Match(k);
        return m.Success && m.Index == 0 && m.Length == k.Length;
    }

    /// <summary>The beta key (and "valid until" text, if given) from the forum page's HTML, or null.</summary>
    public static (string Key, string ValidUntil)? ExtractBetaKey(string html)
    {
        if (string.IsNullOrEmpty(html)) return null;
        var text = Regex.Replace(html, "<[^>]+>", "\n");   //the key sits inside <code>…</code>; tags out of the way
        var found = KeyPattern.Matches(text).Select(m => m.Groups[1].Value).ToList();
        var key = found.FirstOrDefault(k => k.StartsWith("T-", StringComparison.Ordinal)) ?? found.FirstOrDefault();
        if (key == null) return null;
        var until = ValidUntil.Match(text);
        return (key, until.Success ? until.Groups[1].Value.Trim() : "");
    }

    /// <summary>Downloads the forum page and pulls out the current beta key. Throws with a readable message if it can't.</summary>
    public static async Task<(string Key, string ValidUntil)> FetchBetaKeyAsync(CancellationToken ct, HttpClient? client = null)
    {
        var http = client ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, BetaKeyPage);
            //the forum turns away requests that don't look like a browser.
            request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36");
            request.Headers.TryAddWithoutValidation("Accept", "text/html");
            using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"The MakeMKV forum answered with an error ({(int)response.StatusCode}).");
            var html = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return ExtractBetaKey(html) ?? throw new InvalidOperationException("The MakeMKV forum page didn't contain a key this time.");
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Couldn't reach the MakeMKV forum: {ex.Message}");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new InvalidOperationException("The MakeMKV forum didn't answer in time.");
        }
        finally
        {
            if (client == null) http.Dispose();
        }
    }

    /// <summary>The key MakeMKV is using now, or null (none, or not on Windows).</summary>
    public static string? InstalledKey()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RegistryPath);
            return key?.GetValue(RegistryValue) as string is { Length: > 0 } k ? k : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Installs a key for MakeMKV, exactly where its own Register window puts it.</summary>
    public static void Install(string key)
    {
        key = key.Trim();
        if (!IsValidKey(key)) throw new ArgumentException("That doesn't look like a MakeMKV key (they start with \"T-\" or similar and are about 60 characters long).");
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("MakeMKV keys can only be installed on Windows.");
        using var reg = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RegistryPath, true);
        reg.SetValue(RegistryValue, key, Microsoft.Win32.RegistryValueKind.String);
    }

    /// <summary>"T-abcd…wxyz": enough to recognise a key without showing all of it.</summary>
    public static string Short(string key) => key.Length <= 12 ? key : $"{key[..6]}…{key[^4..]}";

    #endregion
}
