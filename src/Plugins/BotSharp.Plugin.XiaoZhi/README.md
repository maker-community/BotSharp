# BotSharp.Plugin.XiaoZhi

XiaoZhi server plugin for BotSharp, providing realtime voice conversation capabilities compatible with xiaozhi-esp32 and other XiaoZhi clients.

## Features

- **WebSocket-based Protocol**: Implements the XiaoZhi WebSocket protocol for bidirectional audio streaming
- **Multiple Protocol Versions**: Supports protocol versions 1, 2, and 3
- **OPUS Audio Codec**: Uses OPUS for efficient audio compression
- **Realtime Integration**: Seamlessly integrates with BotSharp's realtime API and LLM providers
- **Client Compatibility**: Works with official xiaozhi-esp32 clients and third-party implementations

## Configuration

Add the following configuration to your `appsettings.json`:

```json
{
  "XiaoZhi": {
    "EnableAuth": false,
    "AuthKey": "your-secret-key",
    "TokenExpireSeconds": 3600,
    "EndpointPath": "/xiaozhi/stream",
    "DefaultProtocolVersion": 3,
    "AudioFormat": "opus",
    "SampleRate": 24000,
    "Channels": 1,
    "FrameDuration": 60
  }
}
```

### Configuration Options

- **EnableAuth**: Enable JWT authentication for WebSocket connections
- **AuthKey**: Secret key for JWT token generation (required if EnableAuth is true)
- **TokenExpireSeconds**: Token expiration time in seconds (null for no expiration)
- **EndpointPath**: WebSocket endpoint path (default: `/xiaozhi/stream`)
- **DefaultProtocolVersion**: Default protocol version (1, 2, or 3)
- **AudioFormat**: Audio format (default: "opus")
- **SampleRate**: Audio sample rate in Hz (default: 24000)
- **Channels**: Number of audio channels (default: 1)
- **FrameDuration**: Audio frame duration in milliseconds (default: 60)

## Usage

### 1. Add the Plugin

Register the plugin in your BotSharp application:

```csharp
// In your Program.cs or Startup.cs
builder.Services.AddBotSharpPlugin<XiaoZhiPlugin>();
```

### 2. Enable the Middleware

Add the XiaoZhi stream middleware to your application pipeline:

```csharp
// In your Program.cs
app.UseXiaoZhiStream();
```

### 3. Configure XiaoZhi Client

Update your xiaozhi-esp32 client OTA configuration to point to your BotSharp server:

WebSocket URL format:
```
ws://your-server:port/xiaozhi/stream/{agentId}/{conversationId}
```

Example:
```
ws://localhost:5000/xiaozhi/stream/01acc315-cfd8-404b-8e2e-46fa5f7c3c39/test-conversation
```

### 4. Configure Agent for Realtime

Ensure your agent has realtime configuration in its LLM settings:

```json
{
  "LlmConfig": {
    "Realtime": {
      "Provider": "openai",
      "Model": "gpt-4o-realtime-preview"
    }
  }
}
```

## Protocol Details

### XiaoZhi WebSocket Protocol

The XiaoZhi protocol uses WebSocket for bidirectional communication with separate message types for control and audio data.

#### Client Hello (Text Message)

```json
{
  "type": "hello",
  "version": 3,
  "transport": "websocket",
  "features": {
    "aec": true,
    "mcp": true
  },
  "audio_params": {
    "format": "opus",
    "sample_rate": 16000,
    "channels": 1,
    "frame_duration": 20
  }
}
```

#### Server Hello Response (Text Message)

```json
{
  "type": "hello",
  "transport": "websocket",
  "session_id": "uuid-string",
  "audio_params": {
    "format": "opus",
    "sample_rate": 24000,
    "channels": 1,
    "frame_duration": 60
  }
}
```

#### Audio Streaming (Binary Messages)

**Protocol Version 1**: Raw OPUS audio data

**Protocol Version 2**: 
- Header: 16 bytes
  - Version (2 bytes, big-endian)
  - Type (2 bytes, big-endian, 0=OPUS)
  - Reserved (4 bytes)
  - Timestamp (4 bytes, big-endian)
  - Payload Size (4 bytes, big-endian)
- Payload: OPUS audio data

**Protocol Version 3**:
- Header: 4 bytes
  - Type (1 byte, 0=OPUS)
  - Reserved (1 byte)
  - Payload Size (2 bytes, big-endian)
- Payload: OPUS audio data

#### Control Messages (Text Messages)

- `wake_word_detected`: Wake word was detected by client
- `start_listening`: Start listening to user speech
- `stop_listening`: Stop listening to user speech
- `abort_speaking`: Abort current speaking/playback

## Supported Clients

- [xiaozhi-esp32](https://github.com/78/xiaozhi-esp32) - Official ESP32 client
- [Verdure.Assistant](https://github.com/maker-community/Verdure.Assistant) - .NET client
- [py-xiaozhi](https://github.com/huangjunsen0406/py-xiaozhi) - Python client

## References

- [XiaoZhi ESP32 Server](https://github.com/xinnan-tech/xiaozhi-esp32-server) - Python reference implementation
- [XiaoZhi Communication Protocol](https://ccnphfhqs21z.feishu.cn/wiki/M0XiwldO9iJwHikpXD5cEx71nKh) - Official protocol documentation

## License

This plugin is part of BotSharp and follows the same license terms.
