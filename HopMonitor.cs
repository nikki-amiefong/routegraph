using System.ComponentModel;
using System.Net;
using System.Runtime.CompilerServices;
using Avalonia.Media;

namespace Routegraph;

public sealed class HopMonitor(int number, IPAddress address) : INotifyPropertyChanged
{
    private string _hostName = "Resolving…";
    private string _lastText = "—";
    private string _minimumText = "—";
    private string _averageText = "—";
    private string _maximumText = "—";
    private string _lossText = "0%";
    private bool _isHostNameInferred;
    private long? _minimum;
    private long _maximum;
    private long _successfulSamples;
    private long _failedSamples;
    private double _totalLatency;
    private double _graphScaleMaximum = 10;

    public event PropertyChangedEventHandler? PropertyChanged;

    public int Number { get; } = number;
    public string NumberText => Number.ToString("00");
    public IPAddress IpAddress { get; } = address;
    public string Address => IpAddress.ToString();
    public double MinimumLatencyMilliseconds => _minimum ?? 0;
    public double AverageLatencyMilliseconds =>
        _successfulSamples == 0 ? 0 : _totalLatency / _successfulSamples;
    public double MaximumLatencyMilliseconds => _maximum;
    public bool HasSamples => _successfulSamples > 0;
    public bool IsHostNameInferred
    {
        get => _isHostNameInferred;
        private set
        {
            if (SetField(ref _isHostNameInferred, value))
            {
                NotifyPropertyChanged(nameof(HostNameFontStyle));
                NotifyPropertyChanged(nameof(HostNameSourceDescription));
            }
        }
    }
    public FontStyle HostNameFontStyle => IsHostNameInferred ? FontStyle.Italic : FontStyle.Normal;
    public string HostNameSourceDescription => IsHostNameInferred
        ? "Inferred from the target hostname; not resolved by reverse DNS."
        : "Resolved through reverse DNS.";

    public double GraphScaleMaximum
    {
        get => _graphScaleMaximum;
        set => SetField(ref _graphScaleMaximum, value);
    }

    public string HostName
    {
        get => _hostName;
        set => SetField(ref _hostName, value);
    }

    public void SetInferredHostName(string hostName)
    {
        HostName = hostName;
        IsHostNameInferred = true;
    }

    public string LastText
    {
        get => _lastText;
        private set => SetField(ref _lastText, value);
    }

    public string MinimumText
    {
        get => _minimumText;
        private set => SetField(ref _minimumText, value);
    }

    public string AverageText
    {
        get => _averageText;
        private set => SetField(ref _averageText, value);
    }

    public string MaximumText
    {
        get => _maximumText;
        private set => SetField(ref _maximumText, value);
    }

    public string LossText
    {
        get => _lossText;
        private set => SetField(ref _lossText, value);
    }

    public void RecordSuccess(long latencyMilliseconds)
    {
        _successfulSamples++;
        _totalLatency += latencyMilliseconds;
        _minimum = _minimum is null
            ? latencyMilliseconds
            : Math.Min(_minimum.Value, latencyMilliseconds);
        _maximum = Math.Max(_maximum, latencyMilliseconds);

        LastText = $"{latencyMilliseconds} ms";
        MinimumText = $"{_minimum} ms";
        AverageText = $"{_totalLatency / _successfulSamples:F1} ms";
        MaximumText = $"{_maximum} ms";
        NotifyPropertyChanged(nameof(MinimumLatencyMilliseconds));
        NotifyPropertyChanged(nameof(AverageLatencyMilliseconds));
        NotifyPropertyChanged(nameof(MaximumLatencyMilliseconds));
        NotifyPropertyChanged(nameof(HasSamples));
        UpdateLoss();
    }

    public void RecordFailure()
    {
        _failedSamples++;
        LastText = "Timeout";
        UpdateLoss();
    }

    private void UpdateLoss()
    {
        var total = _successfulSamples + _failedSamples;
        LossText = $"{100d * _failedSamples / total:F0}%";
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    private void NotifyPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
