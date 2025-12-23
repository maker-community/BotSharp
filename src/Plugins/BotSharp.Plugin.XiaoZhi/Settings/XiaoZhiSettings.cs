namespace BotSharp.Plugin.XiaoZhi.Settings;

/// <summary>
/// Settings for XiaoZhi server plugin
/// </summary>
public class XiaoZhiSettings
{
    /// <summary>
    /// Enable authentication for WebSocket connections
    /// </summary>
    public bool EnableAuth { get; set; } = false;

    /// <summary>
    /// Secret key for JWT authentication
    /// </summary>
    public string? AuthKey { get; set; }

    /// <summary>
    /// Token expiration time in seconds (null means no expiration)
    /// </summary>
    public int? TokenExpireSeconds { get; set; }

    /// <summary>
    /// WebSocket endpoint path
    /// </summary>
    public string EndpointPath { get; set; } = "/xiaozhi/stream";

    /// <summary>
    /// Default protocol version to use
    /// </summary>
    public int DefaultProtocolVersion { get; set; } = 3;

    /// <summary>
    /// Server audio format
    /// </summary>
    public string AudioFormat { get; set; } = "opus";

    /// <summary>
    /// Server audio sample rate
    /// </summary>
    public int SampleRate { get; set; } = 24000;

    /// <summary>
    /// Server audio channels
    /// </summary>
    public int Channels { get; set; } = 1;

    /// <summary>
    /// Audio frame duration in milliseconds
    /// </summary>
    public int FrameDuration { get; set; } = 60;
}
