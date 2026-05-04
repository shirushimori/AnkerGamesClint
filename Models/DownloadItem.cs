using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AnkerGamesClient.Models;

public class DownloadItem : INotifyPropertyChanged
{
    private int _progress;
    private string _speed = "-- MB/s";
    private string _status = "Queued";
    private bool _canCancel = true;
    private bool _isPaused;
    private string _eta = "";
    private long _totalBytes = -1;
    private long _receivedBytes;

    public string FileName { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string BannerPath { get; set; } = string.Empty;

    // Two CTSs: outer for full cancel, inner for pause (recreated on resume)
    public CancellationTokenSource Cts { get; } = new();
    public CancellationTokenSource PauseCts { get; set; } = new();

    public int Progress
    {
        get => _progress;
        set { _progress = value; OnPropertyChanged(); }
    }

    public string Speed
    {
        get => _speed;
        set { _speed = value; OnPropertyChanged(); }
    }

    public string Status
    {
        get => _status;
        set { _status = value; OnPropertyChanged(); }
    }

    public bool CanCancel
    {
        get => _canCancel;
        set { _canCancel = value; OnPropertyChanged(); }
    }

    public bool IsPaused
    {
        get => _isPaused;
        set { _isPaused = value; OnPropertyChanged(); }
    }

    public string Eta
    {
        get => _eta;
        set { _eta = value; OnPropertyChanged(); }
    }

    public long TotalBytes
    {
        get => _totalBytes;
        set { _totalBytes = value; OnPropertyChanged(); OnPropertyChanged(nameof(TotalBytesFormatted)); }
    }

    public long ReceivedBytes
    {
        get => _receivedBytes;
        set { _receivedBytes = value; OnPropertyChanged(); OnPropertyChanged(nameof(ReceivedBytesFormatted)); }
    }

    public string TotalBytesFormatted => TotalBytes > 0 ? FormatBytes(TotalBytes) : "?";
    public string ReceivedBytesFormatted => FormatBytes(ReceivedBytes);

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1_073_741_824) return $"{bytes / 1_073_741_824.0:F2} GB";
        if (bytes >= 1_048_576) return $"{bytes / 1_048_576.0:F1} MB";
        if (bytes >= 1024) return $"{bytes / 1024.0:F0} KB";
        return $"{bytes} B";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
