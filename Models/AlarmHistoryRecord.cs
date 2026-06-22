namespace Ptlk.AlarmLogger.Models;

public sealed class AlarmHistoryRecord
{
    public long Id { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public DateTimeOffset EventTime { get; set; }
    public string SourceName { get; set; } = "";
    public string? CategoryTag { get; set; }
    public AlarmConditionName ConditionName { get; set; }
    public string ConditionSubName { get; set; } = "";
    public bool ConditionActive { get; set; }
    public AlarmQuality Quality { get; set; }
    public DateTimeOffset QualityTime { get; set; }
    public bool IsAcknowledge { get; set; }
    public bool NeedAck { get; set; }
    public string? OldValueJson { get; set; }
    public string? NewValueJson { get; set; }
    public string Message { get; set; } = "";
    public DateTimeOffset ReceivedAt { get; set; }
}
