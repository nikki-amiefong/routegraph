using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Routegraph;

public partial class MainWindow : Window
{
    private const int MaximumHopLimit = 30;
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan ProbeInterval = TimeSpan.FromSeconds(1);

    private readonly List<Task> _workers = [];
    private int _lastHopNumber;
    private CancellationTokenSource? _sessionCancellation;
    private double _graphScaleMaximum = 10;
    private int? _resizingColumn;
    private double _resizeStartX;
    private double _resizeStartWidth;
    private long _probeCount;
    private long _lostProbeCount;

    public ObservableCollection<HopMonitor> Hops { get; } = [];
    public ObservableCollection<object> HopRows { get; } = [];

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
    }

    private void ColumnResize_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border { Tag: string columnText } handle ||
            !int.TryParse(columnText, out var column))
            return;

        _resizingColumn = column;
        _resizeStartX = e.GetPosition(this).X;
        _resizeStartWidth = HeaderGrid.ColumnDefinitions[column].ActualWidth;
        e.Pointer.Capture(handle);
        e.Handled = true;
    }

    private void ColumnResize_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_resizingColumn is not int column)
            return;

        var width = Math.Max(48, _resizeStartWidth + e.GetPosition(this).X - _resizeStartX);
        HeaderGrid.ColumnDefinitions[column].Width = new GridLength(width);
        ApplyResultColumnWidths();
        e.Handled = true;
    }

    private void ColumnResize_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_resizingColumn is null)
            return;

        _resizingColumn = null;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void ApplyResultColumnWidths()
    {
        var widths = HeaderGrid.ColumnDefinitions.Select(definition => definition.Width).ToArray();
        foreach (var grid in this.GetVisualDescendants().OfType<Grid>())
        {
            if (grid == HeaderGrid || !grid.Classes.Contains("result-columns") ||
                grid.ColumnDefinitions.Count != widths.Length)
                continue;

            for (var index = 0; index < widths.Length; index++)
                grid.ColumnDefinitions[index].Width = widths[index];
        }
    }

    private async void StartButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_sessionCancellation is not null)
            return;

        var target = TargetInput.Text?.Trim();
        if (string.IsNullOrWhiteSpace(target))
        {
            SetStatus("Ready");
            DetailText.Text = "Enter a hostname or IPv4 address to begin.";
            TargetInput.Focus();
            return;
        }

        Hops.Clear();
        HopRows.Clear();
        _lastHopNumber = 0;
        _workers.Clear();
        _graphScaleMaximum = 10;
        _probeCount = 0;
        _lostProbeCount = 0;
        GraphScaleText.Text = "0 — 10 ms";
        TargetIpText.Text = "—";
        HopCountText.Text = "0";
        ProbeCountText.Text = "0";
        LossText.Text = "—";

        var cancellation = new CancellationTokenSource();
        _sessionCancellation = cancellation;
        StartButton.IsEnabled = false;
        StopButton.IsEnabled = true;
        SetStatus("Resolving");
        DetailText.Text = $"Looking up {target}…";

        try
        {
            var requestedAddressFamily = Ipv4OnlyOption.IsChecked == true
                ? AddressFamily.InterNetwork
                : Ipv6OnlyOption.IsChecked == true
                    ? AddressFamily.InterNetworkV6
                    : AddressFamily.Unspecified;
            var addresses = await Dns.GetHostAddressesAsync(target, cancellation.Token);
            var destination = requestedAddressFamily switch
            {
                AddressFamily.InterNetwork => addresses.FirstOrDefault(address =>
                    address.AddressFamily == AddressFamily.InterNetwork),
                AddressFamily.InterNetworkV6 => addresses.FirstOrDefault(address =>
                    address.AddressFamily == AddressFamily.InterNetworkV6),
                _ => addresses.FirstOrDefault(address =>
                         address.AddressFamily == AddressFamily.InterNetworkV6) ??
                     addresses.FirstOrDefault(address =>
                         address.AddressFamily == AddressFamily.InterNetwork)
            };

            if (destination is null)
            {
                var familyName = requestedAddressFamily == AddressFamily.InterNetwork
                    ? "IPv4"
                    : requestedAddressFamily == AddressFamily.InterNetworkV6
                        ? "IPv6"
                        : "IPv4 or IPv6";
                throw new InvalidOperationException($"The target has no {familyName} address.");
            }

            TargetIpText.Text = destination.ToString();
            DetailText.Text = $"Tracing route to {target} ({destination})…";
            SetStatus("Tracing");
            await TraceRouteAsync(target, destination, cancellation.Token);

            if (Hops.Count == 0)
                DetailText.Text = "Route discovery finished without a responding hop. Monitoring will continue.";
            else
                DetailText.Text = $"Route discovery finished. Monitoring {Hops.Count} hop(s) until you stop.";

            SetStatus("Monitoring");
            await Task.Delay(Timeout.Infinite, cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (SocketException exception)
        {
            SetStatus("Network error");
            DetailText.Text = $"Could not resolve or reach the target: {exception.Message}";
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        {
            SetStatus("Error");
            DetailText.Text = exception.Message;
        }
        finally
        {
            cancellation.Cancel();
            try
            {
                await Task.WhenAll(_workers);
            }
            catch (OperationCanceledException)
            {
            }

            _workers.Clear();
            _sessionCancellation = null;
            cancellation.Dispose();
            StartButton.IsEnabled = true;
            StopButton.IsEnabled = false;
            if (StatusText.Text == "Monitoring" || StatusText.Text == "Tracing" || StatusText.Text == "Resolving")
            {
                SetStatus("Stopped");
                DetailText.Text = $"Monitoring stopped. {_probeCount:N0} probe(s) sent.";
            }
        }
    }

    private void StopButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _sessionCancellation?.Cancel();
    }

    private void TargetInput_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && StartButton.IsEnabled)
            StartButton_OnClick(sender, new Avalonia.Interactivity.RoutedEventArgs());
    }

    private async Task TraceRouteAsync(
        string targetName,
        IPAddress destination,
        CancellationToken cancellationToken)
    {
        for (var hopLimit = 1; hopLimit <= MaximumHopLimit; hopLimit++)
        {
            var reply = await TraceRouteProbe.SendAsync(
                destination,
                hopLimit,
                ProbeTimeout,
                cancellationToken);

            if (reply is not null)
                AddHop(hopLimit, reply.Value.Address, destination, targetName, cancellationToken);

            if (reply?.ReachedDestination == true)
                break;
        }
    }

    private void AddHop(
        int hopNumber,
        IPAddress address,
        IPAddress destination,
        string targetName,
        CancellationToken cancellationToken)
    {
        if (hopNumber <= _lastHopNumber)
            return;

        for (var missingHop = _lastHopNumber + 1; missingHop < hopNumber; missingHop++)
            HopRows.Add(new HopGap(missingHop));
        _lastHopNumber = hopNumber;

        var hop = new HopMonitor(hopNumber, address);
        var isDestination = address.AddressFamily == destination.AddressFamily &&
                            address.GetAddressBytes().SequenceEqual(destination.GetAddressBytes());
        if (isDestination)
            hop.SetInferredHostName(targetName);
        hop.GraphScaleMaximum = _graphScaleMaximum;
        hop.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(HopMonitor.MaximumLatencyMilliseconds))
                UpdateGraphScale();
        };
        Hops.Add(hop);
        HopRows.Add(hop);
        HopCountText.Text = Hops.Count.ToString();
        Dispatcher.UIThread.Post(ApplyResultColumnWidths, DispatcherPriority.Loaded);

        _workers.Add(MonitorHopAsync(hop, cancellationToken));
        if (!isDestination)
            _workers.Add(ResolveHostNameAsync(hop, cancellationToken));
    }

    private async Task MonitorHopAsync(HopMonitor hop, CancellationToken cancellationToken)
    {
        using var ping = new Ping();
        var payload = new byte[32];

        while (!cancellationToken.IsCancellationRequested)
        {
            var startedAt = Stopwatch.GetTimestamp();
            try
            {
                var reply = await ping.SendPingAsync(
                    hop.IpAddress,
                    ProbeTimeout,
                    payload,
                    new PingOptions(64, true),
                    cancellationToken);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (reply.Status == IPStatus.Success)
                        hop.RecordSuccess(reply.RoundtripTime);
                    else
                        hop.RecordFailure();

                    RecordProbe(reply.Status != IPStatus.Success);
                });
            }
            catch (PingException)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    hop.RecordFailure();
                    RecordProbe(lost: true);
                });
            }
            catch (SocketException)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    hop.RecordFailure();
                    RecordProbe(lost: true);
                });
            }

            var elapsed = Stopwatch.GetElapsedTime(startedAt);
            var delay = ProbeInterval - elapsed;
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationToken);
        }
    }

    private async Task ResolveHostNameAsync(HopMonitor hop, CancellationToken cancellationToken)
    {
        try
        {
            var entry = await Dns.GetHostEntryAsync(hop.IpAddress).WaitAsync(cancellationToken);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                hop.HostName = string.IsNullOrWhiteSpace(entry.HostName) ||
                               entry.HostName == hop.Address
                    ? "No reverse DNS"
                    : entry.HostName.TrimEnd('.');
            });
        }
        catch (SocketException)
        {
            await Dispatcher.UIThread.InvokeAsync(() => hop.HostName = "No reverse DNS");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void RecordProbe(bool lost)
    {
        _probeCount++;
        if (lost)
            _lostProbeCount++;

        ProbeCountText.Text = _probeCount.ToString("N0");
        LossText.Text = $"{100d * _lostProbeCount / _probeCount:F0}%";
    }

    private void UpdateGraphScale()
    {
        var largestLatency = Hops.Max(hop => hop.MaximumLatencyMilliseconds);
        var scaleMaximum = Math.Max(10, Math.Ceiling(largestLatency / 10) * 10);
        if (scaleMaximum <= _graphScaleMaximum)
            return;

        _graphScaleMaximum = scaleMaximum;
        GraphScaleText.Text = $"0 — {scaleMaximum:F0} ms";
        foreach (var hop in Hops)
            hop.GraphScaleMaximum = scaleMaximum;
    }

    private void SetStatus(string status)
    {
        StatusText.Text = status;
        var color = status switch
        {
            "Monitoring" => "#4BD6A0",
            "Tracing" or "Resolving" => "#83A0FF",
            "Stopped" => "#8994AA",
            "Error" or "Network error" => "#F07474",
            _ => "#5B7CFA"
        };
        StatusDot.Fill = new SolidColorBrush(Color.Parse(color));
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        _sessionCancellation?.Cancel();
        base.OnClosing(e);
    }
}
