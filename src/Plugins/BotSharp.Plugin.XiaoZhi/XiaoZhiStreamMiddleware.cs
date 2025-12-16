using BotSharp.Abstraction.Realtime.Settings;
using BotSharp.Plugin.XiaoZhi.Models;
using BotSharp.Plugin.XiaoZhi.Services;
using BotSharp.Plugin.XiaoZhi.Settings;
using Microsoft.AspNetCore.Http;
using System.Buffers.Binary;
using System.Net.WebSockets;
using System.Text;

namespace BotSharp.Plugin.XiaoZhi;

/// <summary>
/// XiaoZhi WebSocket stream middleware
/// Handles WebSocket connections from XiaoZhi clients (xiaozhi-esp32, etc.)
/// Reference: https://github.com/xinnan-tech/xiaozhi-esp32-server
/// </summary>
public class XiaoZhiStreamMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<XiaoZhiStreamMiddleware> _logger;

    public XiaoZhiStreamMiddleware(
        RequestDelegate next,
        ILogger<XiaoZhiStreamMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task Invoke(HttpContext httpContext)
    {
        var request = httpContext.Request;
        var services = httpContext.RequestServices;
        var settings = services.GetRequiredService<XiaoZhiSettings>();

        // Check if this is a XiaoZhi WebSocket request
        if (request.Path.StartsWithSegments(settings.EndpointPath))
        {
            if (httpContext.WebSockets.IsWebSocketRequest)
            {
                // Parse path: /xiaozhi/stream/{agentId}/{conversationId}
                var parts = request.Path.Value?.Split("/") ?? Array.Empty<string>();
                if (parts.Length < 4)
                {
                    httpContext.Response.StatusCode = 400;
                    await httpContext.Response.WriteAsync("Invalid path format. Expected: /xiaozhi/stream/{agentId}/{conversationId}");
                    return;
                }

                var agentId = parts[3];
                var conversationId = parts.Length > 4 ? parts[4] : Guid.NewGuid().ToString();

                using WebSocket webSocket = await httpContext.WebSockets.AcceptWebSocketAsync();
                try
                {
                    await HandleWebSocket(services, agentId, conversationId, webSocket);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in XiaoZhi WebSocket communication for conversation {ConversationId}", conversationId);
                }
                return;
            }
            else
            {
                httpContext.Response.StatusCode = 400;
                await httpContext.Response.WriteAsync("WebSocket connection required");
                return;
            }
        }

        await _next(httpContext);
    }

    private async Task HandleWebSocket(IServiceProvider services, string agentId, string conversationId, WebSocket webSocket)
    {
        var settings = services.GetRequiredService<XiaoZhiSettings>();
        var hub = services.GetRequiredService<IRealtimeHub>();
        var conn = hub.SetHubConnection(conversationId);
        conn.CurrentAgentId = agentId;

        // Initialize event handlers to prevent null reference errors
        InitEvents(conn, webSocket, services);

        // Load conversation and state
        var convService = services.GetRequiredService<IConversationService>();
        convService.SetConversationId(conversationId, []);
        convService.States.Save();

        var routing = services.GetRequiredService<IRoutingService>();
        routing.Context.Push(agentId);

        var audioCodedec = services.GetRequiredService<IAudioCodec>();

        // XiaoZhi connection state
        string? sessionId = null;
        int protocolVersion = settings.DefaultProtocolVersion;
        bool isConnected = false;

        _logger.LogInformation("XiaoZhi client connected for conversation {ConversationId}", conversationId);

        var buffer = new byte[1024 * 32];

        try
        {
            while (webSocket.State == WebSocketState.Open)
            {
                var receiveResult = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                if (receiveResult.MessageType == WebSocketMessageType.Close)
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                    break;
                }

                // Handle text messages (JSON control messages)
                if (receiveResult.MessageType == WebSocketMessageType.Text)
                {
                    var message = Encoding.UTF8.GetString(buffer, 0, receiveResult.Count);
                    _logger.LogDebug("Received text message: {Message}", message);

                    try
                    {
                        var json = JsonSerializer.Deserialize<JsonElement>(message);
                        var messageType = json.GetProperty("type").GetString();

                        if (messageType == "hello")
                        {
                            // Handle client hello
                            var clientHello = JsonSerializer.Deserialize<ClientHelloMessage>(message);
                            if (clientHello != null)
                            {
                                protocolVersion = clientHello.Version;
                                sessionId = Guid.NewGuid().ToString();

                                _logger.LogInformation("Client hello received: version={Version}, transport={Transport}",
                                    protocolVersion, clientHello.Transport);

                                // Send server hello
                                var serverHello = new ServerHelloMessage
                                {
                                    SessionId = sessionId,
                                    AudioParams = new AudioParameters
                                    {
                                        Format = settings.AudioFormat,
                                        SampleRate = settings.SampleRate,
                                        Channels = settings.Channels,
                                        FrameDuration = settings.FrameDuration
                                    }
                                };

                                var serverHelloJson = JsonSerializer.Serialize(serverHello);
                                await SendTextMessage(webSocket, serverHelloJson);

                                // Connect to model after handshake
                                if (!isConnected)
                                {
                                    await ConnectToModel(hub, webSocket, protocolVersion, services);
                                    isConnected = true;
                                }
                            }
                        }
                        else if (messageType == "wake_word_detected")
                        {
                            _logger.LogDebug("Wake word detected");
                            // Handle wake word detection if needed
                        }
                        else if (messageType == "start_listening")
                        {
                            _logger.LogDebug("Start listening");
                            // Handle start listening if needed
                        }
                        else if (messageType == "stop_listening")
                        {
                            _logger.LogDebug("Stop listening");
                            // Handle stop listening if needed
                        }
                        else if (messageType == "abort_speaking")
                        {
                            _logger.LogDebug("Abort speaking");
                            // Handle abort speaking if needed
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error parsing text message: {Message}", message);
                    }
                }
                // Handle binary messages (audio)
                else if (receiveResult.MessageType == WebSocketMessageType.Binary)
                {
                    if (!isConnected)
                    {
                        _logger.LogWarning("Received audio before connection established, ignoring");
                        continue;
                    }

                    var audioData = new byte[receiveResult.Count];
                    Array.Copy(buffer, audioData, receiveResult.Count);

                    //var audioData = ExtractAudioFromBinaryMessage(buffer.AsSpan(0, receiveResult.Count).ToArray(), protocolVersion);
                    if (audioData != null && audioData.Length > 0)
                    {
                        try
                        {
                            // Convert Opus to target format
                            var convertedPcmAudio = audioCodedec.Decode(audioData, settings.SampleRate, settings.Channels);
                            try
                            {
                                if (convertedPcmAudio.Length > 0)
                                {
                                    await hub.Completer.AppenAudioBuffer(convertedPcmAudio, convertedPcmAudio.Length);
                                }
                            }
                            catch (FormatException ex)
                            {
                                _logger.LogError(ex, "Invalid base64 audio data, skipping frame");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error converting audio data: {Message}", ex.Message);
                        }
                    }
                }
            }
        }
        catch (WebSocketException ex)
        {
            _logger.LogInformation("XiaoZhi client disconnected: {Message}", ex.Message);
        }
        finally
        {
            _logger.LogInformation("XiaoZhi connection closed for conversation {ConversationId}", conversationId);
            if (isConnected && hub.Completer != null)
            {
                await hub.Completer.Disconnect();
            }
            convService.SaveStates();
        }
    }

    private async Task ConnectToModel(IRealtimeHub hub, WebSocket webSocket, int protocolVersion, IServiceProvider services)
    {
        await hub.ConnectToModel(async data =>
        {
            // Convert response data to XiaoZhi format and send
            await SendBinaryMessage(webSocket, data, protocolVersion, services);
        });
    }

    private void InitEvents(RealtimeHubConnection conn, WebSocket webSocket, IServiceProvider services)
    {
        var xiaozhiSettings = services.GetRequiredService<XiaoZhiSettings>();
        
        // When model sends audio data
        conn.OnModelMessageReceived = message =>
        {
            // Return the raw audio data, will be sent via SendBinaryMessage
            return message;
        };

        // When model audio response is complete
        conn.OnModelAudioResponseDone = () =>
        {
            // XiaoZhi doesn't require special done marker in binary protocol
            // Return empty string to prevent null reference
            return string.Empty;
        };

        // When user interrupts the model
        conn.OnModelUserInterrupted = () =>
        {
            // XiaoZhi handles interruption by simply stopping audio playback
            // Return empty string to prevent null reference
            return string.Empty;
        };

        // Initialize OnModelReady to prevent null reference
        conn.OnModelReady = () =>
        {
            _logger.LogInformation("XiaoZhi model ready for conversation {ConversationId}", conn.ConversationId);
            return string.Empty;
        };

        // Initialize OnUserSpeechDetected to prevent null reference
        conn.OnUserSpeechDetected = () =>
        {
            return string.Empty;
        };
    }

    private byte[]? ExtractAudioFromBinaryMessage(byte[] data, int protocolVersion)
    {
        try
        {
            if (protocolVersion == 2)
            {
                // Protocol V2: version(2) + type(2) + reserved(4) + timestamp(4) + payloadSize(4) + payload
                if (data.Length < 16) return null;

                var payloadSize = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(12, 4));
                if (data.Length < 16 + payloadSize) return null;

                var payload = new byte[payloadSize];
                Array.Copy(data, 16, payload, 0, (int)payloadSize);
                return payload;
            }
            else if (protocolVersion == 3)
            {
                // Protocol V3: type(1) + reserved(1) + payloadSize(2) + payload
                if (data.Length < 4) return null;

                var payloadSize = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(2, 2));
                if (data.Length < 4 + payloadSize) return null;

                var payload = new byte[payloadSize];
                Array.Copy(data, 4, payload, 0, payloadSize);
                return payload;
            }
            else
            {
                // Protocol V1: raw audio data
                return data;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting audio from binary message");
            return null;
        }
    }

    private async Task SendTextMessage(WebSocket webSocket, string message)
    {
        var buffer = Encoding.UTF8.GetBytes(message);
        await webSocket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private async Task SendBinaryMessage(WebSocket webSocket, string base64Audio, int protocolVersion, IServiceProvider services)
    {
        try
        {
            // Get RealtimeModelSettings to determine output audio format
            var realtimeSettings = services.GetRequiredService<RealtimeModelSettings>();
            var xiaozhiSettings = services.GetRequiredService<XiaoZhiSettings>();

            // Azure OpenAI returns audio in the format specified by OutputAudioFormat (pcm16 or g711_ulaw)
            // XiaoZhi expects opus format
            var audioData = Convert.FromBase64String(base64Audio);

            // Convert API output format to opus for XiaoZhi client
            var outputFormat = realtimeSettings.OutputAudioFormat ?? "pcm16";
            var opusData = AudioConverter.ConvertToOpus(audioData, outputFormat, xiaozhiSettings.SampleRate);

            byte[] message;

            if (protocolVersion == 2)
            {
                // Protocol V2: version(2) + type(2) + reserved(4) + timestamp(4) + payloadSize(4) + payload
                message = new byte[16 + opusData.Length];
                BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(0, 2), 2); // version
                BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(2, 2), 0); // type: OPUS
                BinaryPrimitives.WriteUInt32BigEndian(message.AsSpan(4, 4), 0); // reserved
                BinaryPrimitives.WriteUInt32BigEndian(message.AsSpan(8, 4), 0); // timestamp (not used for server->client)
                BinaryPrimitives.WriteUInt32BigEndian(message.AsSpan(12, 4), (uint)opusData.Length);
                Array.Copy(opusData, 0, message, 16, opusData.Length);
            }
            else if (protocolVersion == 3)
            {
                // Protocol V3: type(1) + reserved(1) + payloadSize(2) + payload
                message = new byte[4 + opusData.Length];
                message[0] = 0; // type: OPUS
                message[1] = 0; // reserved
                BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(2, 2), (ushort)opusData.Length);
                Array.Copy(opusData, 0, message, 4, opusData.Length);
            }
            else
            {
                // Protocol V1: raw audio data
                message = opusData;
            }

            await webSocket.SendAsync(new ArraySegment<byte>(message), WebSocketMessageType.Binary, true, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending binary message");
        }
    }
}
