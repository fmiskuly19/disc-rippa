namespace DiscRippa.Core.Video;

/// <summary>MakeMKV's registration key, which unlocks its Blu-ray support (see <see cref="MakeMkvKey"/>).</summary>
public interface IMakeMkvKeyService
{
    #region Methods

    /// <summary>The key MakeMKV is using now, or null.</summary>
    string? InstalledKey();

    /// <summary>Installs a key where MakeMKV looks for it.</summary>
    void Install(string key);

    /// <summary>The free beta key currently posted on the MakeMKV forum, and its "valid until" text.</summary>
    Task<(string Key, string ValidUntil)> FetchBetaKeyAsync(CancellationToken ct);

    #endregion
}

public sealed class MakeMkvKeyService : IMakeMkvKeyService
{
    #region Methods

    /// <summary>The key MakeMKV is using now, or null.</summary>
    public string? InstalledKey() => MakeMkvKey.InstalledKey();

    /// <summary>Installs a key where MakeMKV looks for it.</summary>
    public void Install(string key) => MakeMkvKey.Install(key);

    /// <summary>The free beta key currently posted on the MakeMKV forum.</summary>
    public Task<(string Key, string ValidUntil)> FetchBetaKeyAsync(CancellationToken ct) => MakeMkvKey.FetchBetaKeyAsync(ct);

    #endregion
}
