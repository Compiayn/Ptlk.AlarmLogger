namespace Ptlk.AlarmLogger.Models;

public enum AlarmConditionName
{
    Digital,
    Limit
}

public enum AlarmQuality
{
    Unset,
    Good,
    Uncertain,
    Bad
}

public enum AlarmLoggerServiceStatus
{
    Starting,
    Running,
    Degraded,
    Stopping,
    Failed
}
