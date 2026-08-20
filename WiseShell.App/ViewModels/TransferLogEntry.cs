using System.Globalization;
using WiseShell.App.Infrastructure;

namespace WiseShell.App.ViewModels;

public sealed class TransferLogEntry : ObservableObject
{
    private string _status = string.Empty;
    private string _message = string.Empty;
    private string _progressText = string.Empty;
    private string _fileSizeText = "--";
    private string _transferSpeedText = "--";
    private double _progressPercent;
    private bool _isCancelable;

    public Guid Id { get; } = Guid.NewGuid();

    public DateTime Timestamp { get; init; }

    public DateTime TransferStartedAt { get; set; }

    public DateTime? LastProgressAt { get; set; }

    public long LastBytesTransferred { get; set; }

    public long TotalBytes { get; set; }

    public string Direction { get; init; } = string.Empty;

    public string LocalPath { get; init; } = string.Empty;

    public string RemotePath { get; init; } = string.Empty;

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public string Message
    {
        get => _message;
        set => SetProperty(ref _message, value);
    }

    public string ProgressText
    {
        get => _progressText;
        set => SetProperty(ref _progressText, value);
    }

    public string FileSizeText
    {
        get => _fileSizeText;
        set => SetProperty(ref _fileSizeText, value);
    }

    public string TransferSpeedText
    {
        get => _transferSpeedText;
        set => SetProperty(ref _transferSpeedText, value);
    }

    public double ProgressPercent
    {
        get => _progressPercent;
        set => SetProperty(ref _progressPercent, value);
    }

    public bool IsCancelable
    {
        get => _isCancelable;
        set => SetProperty(ref _isCancelable, value);
    }

    public string TimestampDisplay => Timestamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
}
