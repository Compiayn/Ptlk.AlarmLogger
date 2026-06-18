namespace Ptlk.AlarmLogger.Models;

public enum AlarmConditionName
{
    Digital,
    Limit
}

public enum AlarmQuality
{
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
