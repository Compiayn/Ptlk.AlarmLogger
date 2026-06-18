namespace Ptlk.AlarmLogger.Services.Status;

public sealed class AlarmLoggerUiEventHub
{
    private static readonly TimeSpan CoalesceWindow = TimeSpan.FromSeconds(1);
    private readonly object _sync = new();
    private bool _statusScheduled;
    private bool _historyScheduled;

    public event Action? StatusChanged;
    public event Action? HistoryChanged;

    public void NotifyStatusChanged()
    {
        lock (_sync)
        {
            if (_statusScheduled)
            {
                return;
            }

            _statusScheduled = true;
        }

        _ = FireStatusChangedAsync();
    }

    public void NotifyHistoryChanged()
    {
        lock (_sync)
        {
            if (_historyScheduled)
            {
                return;
            }

            _historyScheduled = true;
        }

        _ = FireHistoryChangedAsync();
    }

    private async Task FireStatusChangedAsync()
    {
        await Task.Delay(CoalesceWindow);
        lock (_sync)
        {
            _statusScheduled = false;
        }

        Notify(StatusChanged);
    }

    private async Task FireHistoryChangedAsync()
    {
        await Task.Delay(CoalesceWindow);
        lock (_sync)
        {
            _historyScheduled = false;
        }

        Notify(HistoryChanged);
    }

    private static void Notify(Action? handlers)
    {
        if (handlers is null)
        {
            return;
        }

        foreach (Action handler in handlers.GetInvocationList().Cast<Action>())
        {
            try
            {
                handler();
            }
            catch
            {
                // UI circuits can disappear while background work continues.
            }
        }
    }
}
