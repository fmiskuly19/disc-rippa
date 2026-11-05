using System.Runtime.InteropServices;
using System.Text;

namespace DiscRippa.Core.Tools;

/// <summary>Finds MakeMKV and HandBrakeCLI on this computer.</summary>
public static class ToolLocator
{
    #region Properties

    /// <summary>The tools folder next to DiscRippa.exe, where the release zip puts HandBrakeCLI and cyanrip.</summary>
    public static string BundledToolsDir => Path.Combine(AppContext.BaseDirectory, "tools");

    #endregion

    #region Methods

    /// <summary>makemkvcon64.exe: the saved path if it exists, else the usual install folders.</summary>
    public static string? FindMakeMkv(string? saved)
        => Find(saved, new[] { "makemkvcon64.exe", "makemkvcon.exe", "makemkvcon" },
                ProgramFilesDirs("MakeMKV"));

    /// <summary>HandBrakeCLI.exe: the saved path if it exists, else the copy in the release's tools folder, else the usual folders.</summary>
    public static string? FindHandBrake(string? saved)
        => Find(saved, new[] { "HandBrakeCLI.exe", "HandBrakeCLI" },
                Combine(new[] { BundledToolsDir }, ProgramFilesDirs("HandBrake"), new[] { AppContext.BaseDirectory }, DownloadedHandBrakeDirs()));

    /// <summary>cyanrip (music CDs). It ships as a bare exe, often with the version in its name.</summary>
    public static string? FindCyanrip(string? saved)
    {
        var dirs = new List<string> { BundledToolsDir, AppContext.BaseDirectory };
        try
        {
            var parent = Directory.GetParent(AppContext.BaseDirectory.TrimEnd('\\', '/'))?.FullName;
            if (parent != null) dirs.Add(parent);
        }
        catch
        {
        }
        dirs.AddRange(ProgramFilesDirs("cyanrip"));
        var downloads = KnownFolders.Downloads();
        if (!string.IsNullOrEmpty(downloads) && Directory.Exists(downloads))
        {
            dirs.Add(downloads);
            try
            {
                dirs.AddRange(Directory.GetDirectories(downloads, "cyanrip*"));
            }
            catch
            {
            }
        }

        var exact = Find(saved, new[] { "cyanrip.exe", "cyanrip" }, dirs);
        if (exact != null) return exact;
        foreach (var dir in dirs)
        {
            try
            {
                foreach (var f in Directory.GetFiles(dir, "cyanrip*.exe")) return f;
            }
            catch
            {
            }
        }
        return null;
    }

    /// <summary>The saved path if it exists, else the first of <paramref name="names"/> found in <paramref name="dirs"/>.</summary>
    private static string? Find(string? saved, string[] names, IEnumerable<string> dirs)
    {
        if (!string.IsNullOrWhiteSpace(saved) && File.Exists(saved)) return saved;

        foreach (var dir in dirs)
        {
            foreach (var name in names)
            {
                try
                {
                    var p = Path.Combine(dir, name);
                    if (File.Exists(p)) return p;
                }
                catch
                {
                }
            }
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var name in names)
            {
                try
                {
                    var p = Path.Combine(dir.Trim().Trim('"'), name);
                    if (File.Exists(p)) return p;
                }
                catch
                {
                }
            }
        }
        return null;
    }

    /// <summary>All the lists, one after the other.</summary>
    private static IEnumerable<string> Combine(params IEnumerable<string>[] lists)
    {
        foreach (var list in lists)
            foreach (var item in list)
                yield return item;
    }

    /// <summary><paramref name="sub"/> inside each Program Files folder.</summary>
    private static IEnumerable<string> ProgramFilesDirs(string sub)
    {
        foreach (var folder in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
        {
            var b = Environment.GetFolderPath(folder);
            if (!string.IsNullOrEmpty(b)) yield return Path.Combine(b, sub);
        }
    }

    /// <summary>Unzipped HandBrakeCLI folders sitting in the Downloads folder (wherever Windows keeps it).</summary>
    private static IEnumerable<string> DownloadedHandBrakeDirs()
    {
        var downloads = KnownFolders.Downloads();
        if (string.IsNullOrEmpty(downloads) || !Directory.Exists(downloads)) return Array.Empty<string>();

        var list = new List<string> { downloads };
        try
        {
            list.AddRange(Directory.GetDirectories(downloads, "HandBrakeCLI*"));
        }
        catch
        {
        }
        return list;
    }

    #endregion
}
