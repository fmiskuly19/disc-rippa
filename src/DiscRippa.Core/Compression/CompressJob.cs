using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace DiscRippa.Core.Compression;

public enum JobStatus { Queued, Compressing, Done, Failed, Cancelled }

/// <summary>One file waiting to be (or already) compressed with HandBrake.</summary>
public sealed class CompressJob : ObservableObject
{
    #region Fields

    private JobStatus _status = JobStatus.Queued;
    private double _percent;
    private string _eta = "";
    private string _output = "";
    private string _error = "";
    private string _resolved = "";
    private string _note = "";
    private string _format = "MP4";

    #endregion

    #region Properties

    public int Id { get; set; }
    public string Path { get; set; } = "";
    public string OutDir { get; set; } = "";
    public string Preset { get; set; } = Presets.Auto;

    /// <summary>Save the compressed copy as MKV with every audio track and subtitle kept (otherwise MP4).</summary>
    public bool SaveAsMkv { get; set; }

    /// <summary>Delete the original (the ripped MKV) once the compressed copy is safely finished.</summary>
    public bool DeleteOriginal { get; set; }

    /// <summary>The original was deleted after compressing, as asked.</summary>
    public bool OriginalDeleted { get; set; }

    /// <summary>The unfinished output file while compressing (so it can be cleaned up).</summary>
    public string Partial { get; set; } = "";

    public JobStatus Status
    {
        get => _status;
        set
        {
            if (!SetProperty(ref _status, value)) return;
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(ProgressText));
            OnPropertyChanged(nameof(ProgressValue));
            OnPropertyChanged(nameof(Detail));
        }
    }

    public double Percent
    {
        get => _percent;
        set
        {
            if (!SetProperty(ref _percent, value)) return;
            OnPropertyChanged(nameof(ProgressText));
            OnPropertyChanged(nameof(ProgressValue));
        }
    }

    public string Eta
    {
        get => _eta;
        set
        {
            if (SetProperty(ref _eta, value)) OnPropertyChanged(nameof(ProgressText));
        }
    }

    public string Output
    {
        get => _output;
        set
        {
            if (SetProperty(ref _output, value)) OnPropertyChanged(nameof(Detail));
        }
    }

    public string Error
    {
        get => _error;
        set
        {
            if (SetProperty(ref _error, value)) OnPropertyChanged(nameof(Detail));
        }
    }

    /// <summary>The real preset chosen when the job's preset is Auto.</summary>
    public string Resolved
    {
        get => _resolved;
        set
        {
            if (SetProperty(ref _resolved, value)) OnPropertyChanged(nameof(QualityText));
        }
    }

    public string Note
    {
        get => _note;
        set
        {
            if (SetProperty(ref _note, value)) OnPropertyChanged(nameof(Detail));
        }
    }

    public string FileName => System.IO.Path.GetFileName(Path);
    public string StatusText => Status.ToString();
    public string QualityText => Preset == Presets.Auto && Resolved.Length > 0 ? $"Auto: {Resolved}" : Preset;

    /// <summary>"MP4", or "MKV (all tracks)" when every audio track and subtitle is being kept. Shown in the queue.</summary>
    public string Format { get => _format; set => SetProperty(ref _format, value); }

    public double ProgressValue => Status == JobStatus.Done ? 100 : Percent;

    public string ProgressText => Status switch
    {
        JobStatus.Compressing => $"{Percent:0.0}%" + (Eta.Length > 0 ? $"   ETA {Eta}" : ""),
        JobStatus.Done => "100%",
        JobStatus.Failed => "See details below",
        _ => "",
    };

    /// <summary>What to show when this row is selected.</summary>
    public string Detail
    {
        get
        {
            var note = Note.Length > 0 ? "\n" + Note : "";
            return Status switch
            {
                JobStatus.Failed => (Error.Length > 0 ? Error : "Failed.") + note,
                JobStatus.Done => $"Saved: {Output}{note}",
                _ => Path + note,
            };
        }
    }

    #endregion
}
