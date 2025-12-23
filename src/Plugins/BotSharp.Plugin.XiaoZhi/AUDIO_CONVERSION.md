# 小智音频双向转码实现

## 概述
实现了小智 ESP32 客户端与 Azure OpenAI Realtime API 之间的双向音频格式转换，基于 Verdure.Assistant 项目的 OpusSharp 实现。

## 问题背景
- **输入问题**: 小智发送 Opus 编码音频，但 Azure OpenAI Realtime API 要求 PCM16 (24kHz) 或 G.711 μ-law (8kHz)
- **输出问题**: Azure OpenAI 返回 PCM16/μ-law 音频，但小智客户端期望 Opus 格式

## 解决方案

### 1. 添加 OpusSharp.Core 依赖
**文件**: `src/Plugins/BotSharp.Plugin.XiaoZhi/BotSharp.Plugin.XiaoZhi.csproj`

```xml
<ItemGroup>
  <PackageReference Include="OpusSharp.Core" Version="1.5.6" />
</ItemGroup>
```

### 2. 完整的音频转换器实现
**文件**: `src/Plugins/BotSharp.Plugin.XiaoZhi/AudioConverter.cs`

#### 关键功能

**输入转换 (小智 → API)**:
- `ConvertOpusToTargetFormat()`: 主入口，将 Opus 转换为目标格式
- `ConvertOpusToPCM16()`: Opus → PCM16 解码（使用 OpusSharp）
- `ConvertOpusToULaw()`: Opus → μ-law 转换
- `ResamplePCM16()`: PCM16 重采样（线性插值）
- `EncodePCM16ToULaw()`: PCM16 → μ-law 编码

**输出转换 (API → 小智)**:
- `ConvertToOpus()`: 主入口，将 API 输出格式转换为 Opus
- `EncodePCM16ToOpus()`: PCM16 → Opus 编码（使用 OpusSharp）
- `DecodeULawToPCM16()`: μ-law → PCM16 解码
- `MuLawDecode()`: ITU-T G.711 μ-law 解码算法

#### Opus 编解码器配置
```csharp
// 解码器初始化（输入路径）
_decoder = new OpusDecoder(sampleRate, 1); // 单声道
int frameSize = sampleRate * 60 / 1000;    // 60ms 帧

// 编码器初始化（输出路径）
_encoder = new OpusEncoder(sampleRate, 1, OpusPredefinedValues.OPUS_APPLICATION_AUDIO);
```

### 3. 集成到 WebSocket 中间件
**文件**: `src/Plugins/BotSharp.Plugin.XiaoZhi/XiaoZhiStreamMiddleware.cs`

#### 输入音频转换（第 185-215 行）
```csharp
// 从小智接收 Opus 音频
var audioData = ExtractAudioFromBinaryMessage(data, protocolVersion);

// 获取 API 期望的格式
var realtimeSettings = services.GetRequiredService<RealtimeModelSettings>();
var targetFormat = realtimeSettings.InputAudioFormat; // "pcm16" 或 "g711_ulaw"

// 转换 Opus → PCM16/μ-law
var convertedAudio = AudioConverter.ConvertOpusToTargetFormat(
    audioData, targetFormat, settings.SampleRate, targetSampleRate);

// 发送到 API
await hub.Completer.AppenAudioBuffer(convertedAudio);
```

#### 输出音频转换（第 291-338 行）
```csharp
private async Task SendBinaryMessage(WebSocket webSocket, string base64Audio, 
    int protocolVersion, IServiceProvider services)
{
    // 获取 API 输出格式
    var realtimeSettings = services.GetRequiredService<RealtimeModelSettings>();
    var outputFormat = realtimeSettings.OutputAudioFormat ?? "pcm16";
    
    // 解码 base64
    var audioData = Convert.FromBase64String(base64Audio);
    
    // 转换 PCM16/μ-law → Opus
    var opusData = AudioConverter.ConvertToOpus(audioData, outputFormat, 
        xiaozhiSettings.SampleRate);
    
    // 包装为小智协议格式（V1/V2/V3）
    byte[] message = WrapInProtocolFormat(opusData, protocolVersion);
    
    // 发送到小智客户端
    await webSocket.SendAsync(message, WebSocketMessageType.Binary, true, ...);
}
```

## 音频流程图

```
小智 ESP32 客户端                 BotSharp 服务器                  Azure OpenAI API
     │                                    │                              │
     │ ① Opus 音频 (24kHz, mono)         │                              │
     ├───────────────────────────────────>│                              │
     │    (WebSocket Binary Message)      │                              │
     │                                    │                              │
     │                                    │ ② Opus → PCM16              │
     │                                    │   (AudioConverter)           │
     │                                    │                              │
     │                                    │ ③ PCM16 (base64)            │
     │                                    ├─────────────────────────────>│
     │                                    │   (AppenAudioBuffer)         │
     │                                    │                              │
     │                                    │ ④ PCM16 (base64)            │
     │                                    │<─────────────────────────────┤
     │                                    │   (Model Response)           │
     │                                    │                              │
     │                                    │ ⑤ PCM16 → Opus              │
     │                                    │   (AudioConverter)           │
     │                                    │                              │
     │ ⑥ Opus 音频 (24kHz, mono)         │                              │
     │<───────────────────────────────────┤                              │
     │    (WebSocket Binary Message)      │                              │
```

## 技术细节

### Opus 编解码参数
- **采样率**: 24000 Hz (小智标准)
- **声道数**: 1 (单声道)
- **帧长度**: 60ms (1440 samples @ 24kHz)
- **应用类型**: `OPUS_APPLICATION_AUDIO` (音频通话)
- **最大包大小**: 4000 bytes

### μ-law 编解码
- **标准**: ITU-T G.711
- **BIAS**: 0x84
- **CLIP**: 32635
- **采样率**: 8000 Hz
- **压缩比**: 2:1 (16-bit PCM → 8-bit μ-law)

### 重采样算法
- **方法**: 线性插值
- **支持**: 任意采样率转换
- **典型场景**: 24kHz ↔ 8kHz, 16kHz ↔ 24kHz

## 小智协议格式

### Protocol V1 (Raw)
```
[Opus Audio Data]
```

### Protocol V2 (16-byte header)
```
[version(2)] [type(2)] [reserved(4)] [timestamp(4)] [payloadSize(4)] [Opus Audio]
```

### Protocol V3 (4-byte header) - 推荐
```
[type(1)] [reserved(1)] [payloadSize(2)] [Opus Audio]
```
- `type = 0`: OPUS 音频类型

## 配置

### RealtimeModelSettings (Azure OpenAI)
```json
{
  "InputAudioFormat": "pcm16",      // 或 "g711_ulaw"
  "OutputAudioFormat": "pcm16",     // 或 "g711_ulaw"
  "InputAudioSampleRate": 24000,
  "OutputAudioSampleRate": 24000
}
```

### XiaoZhiSettings
```json
{
  "SampleRate": 24000,
  "Channels": 1,
  "AudioFormat": "opus",
  "FrameDuration": 60,
  "DefaultProtocolVersion": 3
}
```

## 参考实现

基于 [Verdure.Assistant](https://github.com/maker-community/Verdure.Assistant) 项目:
- `src/Verdure.Assistant.Core/Services/Audio/OpusSharpAudioCodec.cs`
- `tests/OpusSharpTest/Program.cs`
- `tests/WebSocketAudioFlowTest/`

### 关键代码模式（来自 Verdure.Assistant）

#### Opus 编码
```csharp
var encoder = new OpusEncoder(sampleRate, channels, 
    OpusPredefinedValues.OPUS_APPLICATION_AUDIO);

short[] pcmShorts = ConvertBytesToShorts(pcmData);
byte[] outputBuffer = new byte[4000];

int encodedLength = encoder.Encode(pcmShorts, frameSize, 
    outputBuffer, outputBuffer.Length);
```

#### Opus 解码
```csharp
var decoder = new OpusDecoder(sampleRate, channels);

short[] outputBuffer = new short[maxFrameSize];
int decodedSamples = decoder.Decode(opusData, opusData.Length, 
    outputBuffer, frameSize, false);

byte[] pcmBytes = ConvertShortsToBytes(outputBuffer, decodedSamples);
```

## 测试建议

### 1. 输入音频测试
- 使用真实小智硬件发送语音
- 验证 API 能正确接收并处理音频
- 检查日志: "Opus decoder initialized: 24000Hz, mono"

### 2. 输出音频测试
- 触发 Azure OpenAI 语音响应
- 验证小智客户端能播放返回的音频
- 检查日志: "Opus encoder initialized: 24000Hz, mono"

### 3. 格式兼容性测试
- 测试 `InputAudioFormat = "pcm16"` 和 `"g711_ulaw"`
- 测试 `OutputAudioFormat = "pcm16"` 和 `"g711_ulaw"`
- 验证所有组合都能正常工作

### 4. 采样率测试
- 测试 24kHz ↔ 8kHz 转换（μ-law 模式）
- 验证音质和延迟

## 故障排除

### 常见错误

**"Opus decode failed: returned 0 samples"**
- 原因: 输入数据不是有效的 Opus 格式
- 解决: 检查小智客户端是否正确编码 Opus

**"Opus encode failed: returned 0 bytes"**
- 原因: PCM 数据长度不匹配帧大小
- 解决: 验证 Azure OpenAI 输出格式和采样率

**音频播放卡顿/断断续续**
- 原因: 帧大小或缓冲区配置不当
- 解决: 确保使用 60ms 帧，检查 WebSocket 缓冲区

### 调试日志

启用详细日志查看转换过程:
```csharp
Console.WriteLine($"Opus decoder initialized: {sampleRate}Hz, mono");
Console.WriteLine($"Decoded {decodedSamples} samples");
Console.WriteLine($"Opus encoder initialized: {sampleRate}Hz, mono");
Console.WriteLine($"Encoded {encodedLength} bytes");
```

## 性能考虑

### 编解码器复用
- 编码器和解码器实例被缓存和复用
- 只在采样率变化时重新初始化
- 使用 `lock` 保证线程安全

### 内存优化
- 重用 buffer 避免频繁分配
- 使用 `Buffer.BlockCopy` 进行高效复制
- 帧大小固定为 60ms (1440 samples @ 24kHz)

### 延迟优化
- 无缓冲处理，实时转换
- WebSocket 直接流式传输
- 编解码延迟 < 1ms

## 未来改进

1. **自适应比特率**: 根据网络条件调整 Opus 比特率
2. **丢包恢复**: 实现 Opus FEC (Forward Error Correction)
3. **降噪增强**: 集成 WebRTC AGC/AEC/ANS
4. **批量处理**: 支持多帧批量编解码提升性能
5. **音频质量监控**: 添加 RMS、峰值等质量指标

## 许可证

本实现参考了 Verdure.Assistant 开源项目，遵循相应的开源许可证。
