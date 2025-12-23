using OpusSharp.Core;
using System.Collections.Generic;

namespace BotSharp.Plugin.XiaoZhi;

/// <summary>
/// Audio format converter for XiaoZhi clients
/// Converts opus audio from XiaoZhi ESP32 clients to formats compatible with various LLM Realtime APIs
/// Uses OpusSharp library for Opus encoding/decoding
/// </summary>
public static class AudioConverter
{
    private static readonly object _lockEncoder = new();
    private static readonly object _lockDecoder = new();
    private static OpusEncoder? _encoder;
    private static OpusDecoder? _decoder;
    private static int _currentEncoderSampleRate;
    private static int _currentDecoderSampleRate;

    /// <summary>
    /// Convert XiaoZhi opus audio to target format (for input to API)
    /// </summary>
    /// <param name="opusData">Opus encoded audio data</param>
    /// <param name="targetFormat">Target format (pcm16, g711_ulaw, etc.)</param>
    /// <param name="sourceSampleRate">Source sample rate (usually 24000 for XiaoZhi)</param>
    /// <param name="targetSampleRate">Target sample rate</param>
    /// <returns>Converted audio data as base64 string</returns>
    public static string ConvertOpusToTargetFormat(
        byte[] opusData, 
        string targetFormat, 
        int sourceSampleRate = 24000,
        int targetSampleRate = 24000)
    {
        try
        {
            switch (targetFormat.ToLower())
            {
                case "pcm16":
                    return ConvertOpusToPCM16(opusData, sourceSampleRate, targetSampleRate);
                
                case "g711_ulaw":
                case "ulaw":
                    return ConvertOpusToULaw(opusData, sourceSampleRate, targetSampleRate);
                
                case "opus":
                    // Already in opus format
                    return Convert.ToBase64String(opusData);
                
                default:
                    // Try to treat as PCM16
                    return ConvertOpusToPCM16(opusData, sourceSampleRate, targetSampleRate);
            }
        }
        catch (Exception ex)
        {
            // Log error and return empty data
            Console.WriteLine($"Audio conversion failed: {ex.Message}");
            return string.Empty; // Return empty instead of corrupted data
        }
    }

    /// <summary>
    /// Convert raw PCM audio to target format (when XiaoZhi sends PCM instead of Opus)
    /// </summary>
    /// <param name="pcmData">Raw PCM16 audio data</param>
    /// <param name="targetFormat">Target format (pcm16, g711_ulaw, etc.)</param>
    /// <param name="sourceSampleRate">Source sample rate</param>
    /// <param name="targetSampleRate">Target sample rate</param>
    /// <returns>Converted audio data as base64 string</returns>
    public static string ConvertRawPCMToTargetFormat(
        byte[] pcmData,
        string targetFormat,
        int sourceSampleRate = 24000,
        int targetSampleRate = 24000)
    {
        try
        {
            // Resample if needed
            if (sourceSampleRate != targetSampleRate)
            {
                pcmData = ResamplePCM16(pcmData, sourceSampleRate, targetSampleRate);
            }

            switch (targetFormat.ToLower())
            {
                case "pcm16":
                    return Convert.ToBase64String(pcmData);
                
                case "g711_ulaw":
                case "ulaw":
                    var ulawData = EncodePCM16ToULaw(pcmData);
                    return Convert.ToBase64String(ulawData);
                
                case "opus":
                    // Encode to opus
                    var opusData = EncodePCM16ToOpus(pcmData, targetSampleRate);
                    return Convert.ToBase64String(opusData);
                
                default:
                    // Default to PCM16
                    return Convert.ToBase64String(pcmData);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Raw PCM conversion failed: {ex.Message}");
            return string.Empty;
        }
    }

    /// <summary>
    /// Convert API output format to opus for XiaoZhi client
    /// </summary>
    /// <param name="audioData">Audio data in source format (PCM16 or g711_ulaw)</param>
    /// <param name="sourceFormat">Source format (pcm16, g711_ulaw)</param>
    /// <param name="sampleRate">Sample rate</param>
    /// <returns>Opus encoded audio data</returns>
    public static byte[] ConvertToOpus(byte[] audioData, string sourceFormat, int sampleRate = 24000)
    {
        try
        {
            byte[] pcm16Data;

            switch (sourceFormat.ToLower())
            {
                case "pcm16":
                    pcm16Data = audioData;
                    break;

                case "g711_ulaw":
                case "ulaw":
                    // Decode μ-law to PCM16 first
                    pcm16Data = DecodeULawToPCM16(audioData);
                    break;

                default:
                    // Assume PCM16
                    pcm16Data = audioData;
                    break;
            }

            // Encode PCM16 to Opus
            return EncodePCM16ToOpus(pcm16Data, sampleRate);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Opus encoding failed: {ex.Message}");
            return Array.Empty<byte>();
        }
    }

    /// <summary>
    /// Convert opus to PCM16 using OpusSharp decoder
    /// </summary>
    private static string ConvertOpusToPCM16(byte[] opusData, int sourceSampleRate, int targetSampleRate)
    {
        lock (_lockDecoder)
        {
            // Initialize decoder if needed
            if (_decoder == null || _currentDecoderSampleRate != sourceSampleRate)
            {
                _decoder = new OpusDecoder(sourceSampleRate, 1); // XiaoZhi uses mono
                _currentDecoderSampleRate = sourceSampleRate;
                Console.WriteLine($"Opus decoder initialized: {sourceSampleRate}Hz, mono");
            }

            try
            {
                // Calculate frame size for 60ms (XiaoZhi standard)
                int frameSize = sourceSampleRate * 60 / 1000;
                int maxFrameSize = sourceSampleRate * 120 / 1000; // 120ms max for Opus

                // Decode opus to PCM16 - use maxFrameSize as buffer size, not frameSize
                // Let the decoder determine the actual decoded size based on the encoded data
                short[] outputBuffer = new short[maxFrameSize];
                int decodedSamples = _decoder.Decode(opusData, opusData.Length, outputBuffer, maxFrameSize, false);

                if (decodedSamples <= 0)
                {
                    Console.WriteLine($"Opus decode failed: returned {decodedSamples} samples, input size: {opusData.Length} bytes");
                    return string.Empty; // Return empty on decode failure
                }

                // Limit to actual decoded samples
                if (decodedSamples > maxFrameSize)
                {
                    Console.WriteLine($"Warning: decoded samples({decodedSamples}) exceeds max frame size({maxFrameSize})");
                    decodedSamples = maxFrameSize;
                }

                Console.WriteLine($"Successfully decoded {decodedSamples} samples from {opusData.Length} bytes of Opus data");

                // Convert to byte array (Little Endian PCM16)
                byte[] pcmBytes = new byte[decodedSamples * 2]; // 2 bytes per Int16
                for (int i = 0; i < decodedSamples; i++)
                {
                    var bytes = BitConverter.GetBytes(outputBuffer[i]);
                    pcmBytes[i * 2] = bytes[0];     // Low byte
                    pcmBytes[i * 2 + 1] = bytes[1]; // High byte
                }

                // Validate PCM data quality before returning
                if (!ValidatePCMData(pcmBytes, decodedSamples))
                {
                    Console.WriteLine($"Warning: PCM data validation failed - potential audio quality issue");
                }

                // Resample if needed
                if (sourceSampleRate != targetSampleRate)
                {
                    Console.WriteLine($"Resampling from {sourceSampleRate}Hz to {targetSampleRate}Hz");
                    pcmBytes = ResamplePCM16(pcmBytes, sourceSampleRate, targetSampleRate);
                }

                return Convert.ToBase64String(pcmBytes);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Opus decoding error: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                return string.Empty; // Return empty on error
            }
        }
    }

    /// <summary>
    /// Encode PCM16 to Opus using OpusSharp encoder
    /// </summary>
    private static byte[] EncodePCM16ToOpus(byte[] pcmData, int sampleRate)
    {
        lock (_lockEncoder)
        {
            // Initialize encoder if needed
            if (_encoder == null || _currentEncoderSampleRate != sampleRate)
            {
                _encoder = new OpusEncoder(sampleRate, 1, OpusPredefinedValues.OPUS_APPLICATION_AUDIO);
                _currentEncoderSampleRate = sampleRate;
                Console.WriteLine($"Opus encoder initialized: {sampleRate}Hz, mono");
            }

            try
            {
                // Calculate frame size for 60ms (XiaoZhi standard)
                int frameSize = sampleRate * 60 / 1000;
                int expectedBytes = frameSize * 2; // 2 bytes per Int16 sample

                // Adjust PCM data length if needed
                if (pcmData.Length != expectedBytes)
                {
                    byte[] adjustedData = new byte[expectedBytes];
                    Array.Copy(pcmData, 0, adjustedData, 0, Math.Min(pcmData.Length, expectedBytes));
                    pcmData = adjustedData;
                }

                // Convert to 16-bit short array
                short[] pcmShorts = new short[frameSize];
                for (int i = 0; i < frameSize && i * 2 + 1 < pcmData.Length; i++)
                {
                    pcmShorts[i] = BitConverter.ToInt16(pcmData, i * 2);
                }

                // Encode to Opus
                byte[] outputBuffer = new byte[4000]; // Opus max packet size
                int encodedLength = _encoder.Encode(pcmShorts, frameSize, outputBuffer, outputBuffer.Length);

                if (encodedLength > 0)
                {
                    // Return actual encoded data
                    byte[] result = new byte[encodedLength];
                    Array.Copy(outputBuffer, result, encodedLength);
                    return result;
                }
                else
                {
                    Console.WriteLine($"Opus encode failed: returned {encodedLength} bytes");
                    return Array.Empty<byte>();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Opus encoding error: {ex.Message}");
                return Array.Empty<byte>();
            }
        }
    }

    /// <summary>
    /// Convert opus to μ-law (requires opus decoding first)
    /// </summary>
    private static string ConvertOpusToULaw(byte[] opusData, int sourceSampleRate, int targetSampleRate)
    {
        // First decode opus to PCM16
        var pcm16Base64 = ConvertOpusToPCM16(opusData, sourceSampleRate, targetSampleRate);
        var pcm16Data = Convert.FromBase64String(pcm16Base64);
        
        // Then encode to μ-law
        var ulawData = EncodePCM16ToULaw(pcm16Data);
        return Convert.ToBase64String(ulawData);
    }

    /// <summary>
    /// Resample PCM16 audio using linear interpolation
    /// </summary>
    private static byte[] ResamplePCM16(byte[] pcmData, int sourceSampleRate, int targetSampleRate)
    {
        if (sourceSampleRate == targetSampleRate || pcmData.Length < 2)
        {
            return pcmData;
        }

        // Convert bytes to 16-bit samples
        int sourceFrameCount = pcmData.Length / 2;
        short[] sourceSamples = new short[sourceFrameCount];
        Buffer.BlockCopy(pcmData, 0, sourceSamples, 0, pcmData.Length);

        // Calculate target frame count
        double ratio = (double)targetSampleRate / sourceSampleRate;
        int targetFrameCount = (int)(sourceFrameCount * ratio);
        short[] targetSamples = new short[targetFrameCount];

        // Linear interpolation resampling
        for (int i = 0; i < targetFrameCount; i++)
        {
            double sourceIndex = i / ratio;
            int index1 = (int)sourceIndex;
            int index2 = Math.Min(index1 + 1, sourceFrameCount - 1);
            double fraction = sourceIndex - index1;

            // Linear interpolation
            targetSamples[i] = (short)(sourceSamples[index1] * (1 - fraction) + sourceSamples[index2] * fraction);
        }

        // Convert back to bytes
        byte[] result = new byte[targetFrameCount * 2];
        Buffer.BlockCopy(targetSamples, 0, result, 0, result.Length);
        return result;
    }

    /// <summary>
    /// Encode PCM16 to μ-law
    /// </summary>
    private static byte[] EncodePCM16ToULaw(byte[] pcm16Data)
    {
        int sampleCount = pcm16Data.Length / 2;
        byte[] ulawData = new byte[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            short sample = BitConverter.ToInt16(pcm16Data, i * 2);
            ulawData[i] = MuLawEncode(sample);
        }

        return ulawData;
    }

    /// <summary>
    /// Decode μ-law to PCM16
    /// </summary>
    private static byte[] DecodeULawToPCM16(byte[] ulawData)
    {
        byte[] pcm16Data = new byte[ulawData.Length * 2];

        for (int i = 0; i < ulawData.Length; i++)
        {
            short sample = MuLawDecode(ulawData[i]);
            byte[] sampleBytes = BitConverter.GetBytes(sample);
            pcm16Data[i * 2] = sampleBytes[0];
            pcm16Data[i * 2 + 1] = sampleBytes[1];
        }

        return pcm16Data;
    }

    /// <summary>
    /// μ-law encoding algorithm
    /// </summary>
    private static byte MuLawEncode(short pcm)
    {
        const int BIAS = 0x84;
        const int CLIP = 32635;
        
        // Get the sign and magnitude
        int sign = (pcm < 0) ? 0x80 : 0;
        int magnitude = Math.Abs(pcm);
        
        // Clip the magnitude
        if (magnitude > CLIP)
            magnitude = CLIP;
        
        // Add bias
        magnitude += BIAS;
        
        // Find the exponent
        int exponent = 7;
        for (int exp = 7; exp >= 0; exp--)
        {
            if (magnitude >= (0x100 << exp))
            {
                exponent = exp;
                break;
            }
        }
        
        // Get mantissa
        int mantissa = (magnitude >> (exponent + 3)) & 0x0F;
        
        // Combine and invert
        byte mulaw = (byte)(~(sign | (exponent << 4) | mantissa));
        
        return mulaw;
    }

    /// <summary>
    /// μ-law decoding algorithm
    /// </summary>
    private static short MuLawDecode(byte mulaw)
    {
        // Invert bits
        mulaw = (byte)~mulaw;
        
        // Extract components
        int sign = (mulaw & 0x80) != 0 ? -1 : 1;
        int exponent = (mulaw >> 4) & 0x07;
        int mantissa = mulaw & 0x0F;
        
        // Calculate magnitude
        int magnitude = ((mantissa << 3) + 0x84) << exponent;
        magnitude -= 0x84;
        
        return (short)(sign * magnitude);
    }

    /// <summary>
    /// Check if XiaoZhi is sending raw PCM instead of opus
    /// Some XiaoZhi configurations send raw PCM16 data
    /// </summary>
    public static bool IsLikelyRawPCM(byte[] data)
    {
        if (data.Length < 8)
            return false;
        
        // Opus packets have specific characteristics:
        // - TOC (Table of Contents) byte at the beginning with specific patterns
        // - Typically small size (20-200 bytes for 60ms @ 24kHz)
        // - The first byte contains configuration information
        
        byte firstByte = data[0];
        
        // Opus TOC byte structure: config(5 bits) + s(1 bit) + c(2 bits)
        // Valid opus config values are 0-31
        // Common Opus configs for speech: 16-27 (SILK or Hybrid modes)
        int opusConfig = (firstByte >> 3) & 0x1F;
        
        // Heuristic checks:
        
        // 1. Check data length - Opus frames are typically much smaller than raw PCM
        //    60ms @ 24kHz PCM16 = 2880 bytes
        //    60ms @ 24kHz Opus = typically 40-150 bytes
        if (data.Length > 1000)
        {
            // Likely raw PCM due to size
            return true;
        }
        
        // 2. For small packets, check if first byte looks like valid Opus TOC
        //    Most audio Opus packets use configs 16-31
        if (data.Length < 200)
        {
            // Check if TOC byte is within reasonable range for Opus
            if (opusConfig >= 4 && opusConfig <= 31)
            {
                // Could be Opus, check more
                
                // 3. Opus packets should NOT have all bytes in similar range
                //    PCM audio typically has more uniform distribution across the packet
                int similarByteCount = 0;
                for (int i = 1; i < Math.Min(data.Length, 10); i++)
                {
                    if (Math.Abs(data[i] - data[0]) < 20)
                        similarByteCount++;
                }
                
                // If most bytes are similar, likely raw PCM
                if (similarByteCount > 7)
                    return true;
                
                // Looks like valid Opus
                return false;
            }
        }
        
        // 4. Check data variance - PCM has different characteristics than Opus
        //    Calculate simple variance of first 32 bytes
        if (data.Length >= 32)
        {
            long sum = 0;
            for (int i = 0; i < 32; i++)
            {
                sum += data[i];
            }
            double mean = sum / 32.0;
            
            double variance = 0;
            for (int i = 0; i < 32; i++)
            {
                variance += Math.Pow(data[i] - mean, 2);
            }
            variance /= 32;
            
            // Raw PCM typically has higher variance in byte distribution
            // Opus compressed data has more structured byte patterns
            if (variance > 3000)
            {
                return true; // High variance - likely raw PCM
            }
        }
        
        // 5. Check if data length is even (PCM16 is always even bytes)
        //    AND doesn't match typical Opus frame sizes
        if (data.Length % 2 == 0 && data.Length > 500)
        {
            return true;
        }
        
        // Default to false (assume Opus) if unsure
        // This is safer as attempting Opus decode will fail gracefully
        return false;
    }

    /// <summary>
    /// Validate PCM16 data quality to ensure it's not corrupted or silent
    /// Based on Verdure.Assistant CheckAudioQuality implementation
    /// </summary>
    private static bool ValidatePCMData(byte[] pcmData, int sampleCount)
    {
        if (pcmData.Length < 4 || sampleCount == 0)
            return false;

        // Convert to 16-bit samples for analysis
        var samples = new short[sampleCount];
        Buffer.BlockCopy(pcmData, 0, samples, 0, Math.Min(pcmData.Length, sampleCount * 2));

        // Calculate audio statistics
        double sum = 0;
        double sumSquares = 0;
        short min = short.MaxValue;
        short max = short.MinValue;
        int zeroCount = 0;

        foreach (short sample in samples)
        {
            sum += sample;
            sumSquares += sample * sample;
            min = Math.Min(min, sample);
            max = Math.Max(max, sample);
            if (sample == 0) zeroCount++;
        }

        double mean = sum / samples.Length;
        double rms = Math.Sqrt(sumSquares / samples.Length);
        double zeroPercent = (double)zeroCount / samples.Length * 100;

        // Check for quality issues
        bool hasIssues = false;
        var issues = new List<string>();

        // Check if mostly silence (more than 95% zeros)
        if (zeroPercent > 95)
        {
            issues.Add("nearly all silence");
            hasIssues = true;
        }

        // Check for clipping/saturation
        if (max >= 32760 || min <= -32760)
        {
            issues.Add("potential audio clipping");
            hasIssues = true;
        }

        // Check for abnormal DC offset
        if (Math.Abs(mean) > 1000)
        {
            issues.Add($"abnormal DC offset: {mean:F1}");
            hasIssues = true;
        }

        // Check for abnormally low RMS (potential corrupted signal)
        if (rms < 10 && zeroPercent < 50)
        {
            issues.Add($"abnormally low RMS: {rms:F1}");
            hasIssues = true;
        }

        if (hasIssues)
        {
            Console.WriteLine($"PCM quality warning: {string.Join(", ", issues)}");
            Console.WriteLine($"  Stats: samples={samples.Length}, RMS={rms:F1}, range=[{min}, {max}], zero%={zeroPercent:F1}%");
            return false;
        }

        // Data looks good
        Console.WriteLine($"PCM quality OK: samples={samples.Length}, RMS={rms:F1}, range=[{min}, {max}]");
        return true;
    }
}
