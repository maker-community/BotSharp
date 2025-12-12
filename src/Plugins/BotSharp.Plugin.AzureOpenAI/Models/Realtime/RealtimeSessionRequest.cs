namespace BotSharp.Plugin.AzureOpenAI.Models.Realtime;

public class RealtimeSessionCreationRequest
{
    [JsonPropertyName("model")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Model { get; set; } = null!;

    [JsonPropertyName("modalities")]
    public string[] Modalities { get; set; } = ["audio", "text"];

    [JsonPropertyName("instructions")]
    public string Instructions { get; set; } = null!;

    [JsonPropertyName("tool_choice")]
    public string ToolChoice { get; set; } = "auto";

    [JsonPropertyName("tools")]
    public FunctionDef[] Tools { get; set; } = [];

    [JsonPropertyName("turn_detection")]
    public RealtimeSessionTurnDetection TurnDetection { get; set; } = new();
}

/// <summary>
/// https://learn.microsoft.com/en-us/azure/ai-services/openai/realtime-audio-reference
/// </summary>
public class RealtimeSessionUpdateRequest : RealtimeSessionBody
{

}
