using System.Runtime.InteropServices;

namespace BotSharp.Plugin.XiaoZhi.Models;

/// <summary>
/// Binary protocol version 2 packet structure
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct BinaryProtocol2
{
    public ushort Version;      // Protocol version (big-endian)
    public ushort Type;         // Message type (0: OPUS, 1: JSON) (big-endian)
    public uint Reserved;       // Reserved for future use (big-endian)
    public uint Timestamp;      // Timestamp in milliseconds (big-endian)
    public uint PayloadSize;    // Payload size in bytes (big-endian)
    // Payload data follows
}

/// <summary>
/// Binary protocol version 3 packet structure
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct BinaryProtocol3
{
    public byte Type;           // Message type (0: OPUS, 1: JSON)
    public byte Reserved;       // Reserved for future use
    public ushort PayloadSize;  // Payload size in bytes (big-endian)
    // Payload data follows
}

/// <summary>
/// Protocol version enumeration
/// </summary>
public enum ProtocolVersion
{
    V1 = 1,
    V2 = 2,
    V3 = 3
}
