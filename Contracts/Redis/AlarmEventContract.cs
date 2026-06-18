using System.Text.Json;

namespace Ptlk.AlarmLogger.Contracts.Redis;

public sealed class AlarmEventContract
{
    public string? SourceName { get; set; }
    public string? ConditionName { get; set; }
    public string? ConditionSubName { get; set; }
    public bool? ConditionActive { get; set; }
    public string? Quality { get; set; }
    public long? QualityTime { get; set; }
    public long? EventTime { get; set; }
    public long? Timestamp { get; set; }
    public bool? IsAcknowledge { get; set; }
    public JsonElement? OldValue { get; set; }
    public JsonElement? NewValue { get; set; }
    public string? Message { get; set; }
}
