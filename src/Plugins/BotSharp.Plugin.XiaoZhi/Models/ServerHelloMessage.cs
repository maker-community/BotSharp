namespace BotSharp.Plugin.XiaoZhi.Models;

/// <summary>
/// Server hello response message
/// </summary>
public class ServerHelloMessage
{
    /// <summary>
    /// Message type, should be "hello"
    /// </summary>
    public string Type { get; set; } = "hello";

    /// <summary>
    /// Transport type, should be "websocket"
    /// </summary>
    public string Transport { get; set; } = "websocket";

    /// <summary>
    /// Session ID
    /// </summary>
    public string SessionId { get; set; } = string.Empty;

    /// <summary>
    /// Server audio parameters
    /// </summary>
    public AudioParameters? AudioParams { get; set; }
}
