namespace DshLauncher.Core;

/// <summary>每 2s 用 IPGlobalProperties 探测端口，变化时推送事件。</summary>
public sealed class StatusMonitor : IStatusMonitor
{
    private readonly object _gate = new();
    private Timer? _timer;
    private bool _isPortOpen;

    public int Port { get; set; } = 3080;

    public bool IsPortOpen
    {
        get { lock (_gate) return _isPortOpen; }
        private set { lock (_gate) _isPortOpen = value; }
    }

    public event EventHandler<bool>? StatusChanged;

    public void Start()
    {
        lock (_gate)
        {
            if (_timer is not null) return;
            _timer = new Timer(_ => Poll(), null, TimeSpan.Zero, TimeSpan.FromSeconds(2));
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
        }
    }

    public Task<bool> CheckAsync(CancellationToken ct = default)
        => Task.FromResult(Probe());

    private void Poll()
    {
        var open = Probe();
        if (open == IsPortOpen) return;
        IsPortOpen = open;
        StatusChanged?.Invoke(this, open);
    }

    private bool Probe() => PortProbe.IsListening(Port);
}
