namespace DotNet.Vault.Configuration.Refresh;

/// <summary>
/// Schedules refresh callbacks for <see cref="SecretRefresher"/>.
/// </summary>
public interface ISecretRefreshScheduler : IDisposable
{
    /// <summary>
    /// Starts invoking <paramref name="refresh"/> at <paramref name="interval"/>.
    /// </summary>
    void Start(TimeSpan interval, Func<Task> refresh);

    /// <summary>
    /// Prevents scheduled callbacks from running.
    /// </summary>
    void Stop();
}

internal sealed class TimerSecretRefreshScheduler : ISecretRefreshScheduler
{
    private Timer? _timer;

    public void Start(TimeSpan interval, Func<Task> refresh)
    {
        _timer?.Dispose();
        _timer = new Timer(
            static state => _ = ((Func<Task>)state!).Invoke(),
            refresh,
            interval,
            interval);
    }

    public void Stop()
    {
        _timer?.Change(Timeout.Infinite, Timeout.Infinite);
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
    }
}
