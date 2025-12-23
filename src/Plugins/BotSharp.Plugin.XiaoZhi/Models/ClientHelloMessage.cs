namespace BotSharp.Plugin.XiaoZhi.Models;

/// <summary>
/// Client hello message
/// </summary>
public class ClientHelloMessage
{
    /// <summary>
    /// Message type, should be "hello"
    /// </summary>
    public string Type { get; set; } = "hello";

    /// <summary>
    /// Protocol version (1, 2, or 3)
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// Transport type, should be "websocket"
    /// </summary>
    public string Transport { get; set; } = "websocket";

    /// <summary>
    /// Client features
    /// </summary>
    public ClientFeatures? Features { get; set; }

    /// <summary>
    /// Client audio parameters
    /// </summary>
    public AudioParameters? AudioParams { get; set; }
}

/// <summary>
/// Client features
/// </summary>
public class ClientFeatures
{
    /// <summary>
    /// Acoustic Echo Cancellation support
    /// </summary>
    public bool Aec { get; set; }

    /// <summary>
    /// MCP (Model Context Protocol) support
    /// </summary>
    public bool Mcp { get; set; }
}

/// <summary>
/// Audio parameters
/// </summary>
public class AudioParameters
{
    /// <summary>
    /// Audio format (e.g., "opus")
    /// </summary>
    public string Format { get; set; } = "opus";

    /// <summary>
    /// Sample rate in Hz
    /// </summary>
    public int SampleRate { get; set; } = 16000;

    /// <summary>
    /// Number of channels
    /// </summary>
    public int Channels { get; set; } = 1;

    /// <summary>
    /// Frame duration in milliseconds
    /// </summary>
    public int FrameDuration { get; set; } = 20;
}
