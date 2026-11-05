using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace DiscRippa.Core.Processes;

public sealed class ProcessRunner : IProcessRunner
{
    #region Methods

    /// <summary>Runs a program and passes each line it prints to <paramref name="onLine"/>.</summary>
    public Task<ProcessOutcome> RunAsync(string fileName, IReadOnlyList<string> args, Action<string>? onLine, CancellationToken ct)
        => RunInAsync(null, fileName, args, onLine, ct);

    /// <summary>Runs a program in <paramref name="workingDirectory"/> and passes each line it prints to <paramref name="onLine"/>.</summary>
    public async Task<ProcessOutcome> RunInAsync(string? workingDirectory, string fileName, IReadOnlyList<string> args, Action<string>? onLine, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory ?? "",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        //MakeMKV and HandBrake print progress very fast. Lines are handed to the UI thread in
        //batches about 10 times a second, so the window always has time to redraw and react to clicks.
        var batcher = new LineBatcher(SynchronizationContext.Current, onLine ?? (_ =>
        {
        }), TimeSpan.FromMilliseconds(100));

        void Emit(string? line)
        {
            if (line != null && onLine != null) batcher.Add(line);
        }

        using var process = new Process { StartInfo = psi };
        process.OutputDataReceived += (_, e) => Emit(e.Data);
        process.ErrorDataReceived += (_, e) => Emit(e.Data);

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            return new ProcessOutcome { ExitCode = -1, StartFailed = true, Error = ex.Message };
        }

        //programs that wait for a keypress must not hang on an open stdin.
        try
        {
            process.StandardInput.Close();
        }
        catch
        {
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var registration = ct.Register(() =>
        {
            try
            {
                if (!process.HasExited) process.Kill(true);
            }
            catch
            {
            }
        });

        //WaitForExit() with no timeout also waits until all redirected output has been delivered.
        await Task.Run(() => process.WaitForExit()).ConfigureAwait(true);

        //deliver whatever is still waiting before reporting that the program has finished.
        batcher.Drain();

        return new ProcessOutcome
        {
            ExitCode = SafeExitCode(process),
            Cancelled = ct.IsCancellationRequested,
        };
    }

    /// <summary>The exit code, or -1 if it can't be read.</summary>
    private static int SafeExitCode(Process p)
    {
        try
        {
            return p.ExitCode;
        }
        catch
        {
            return -1;
        }
    }

    #endregion
}
