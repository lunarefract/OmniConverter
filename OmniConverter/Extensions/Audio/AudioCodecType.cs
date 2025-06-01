using FFMpegCore;
using FFMpegCore.Enums;
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace OmniConverter
{
    public enum AudioCodecType
    {
        PCM,
        FLAC,
        LAME,
        Vorbis,
        WavPack,
        Max = WavPack
    }

    public static class AudioCodecTypeExtensions
    {
        public static string ToExtension(this AudioCodecType codec)
        {
            return codec switch
            {
                AudioCodecType.PCM => ".wav",
                AudioCodecType.FLAC => ".flac",
                AudioCodecType.LAME => ".mp3",
                AudioCodecType.Vorbis => ".ogg",
                AudioCodecType.WavPack => ".wv",
                _ => ""
            };
        }

        public static Codec? ToFFMpegCodec(this AudioCodecType codec)
        {
            return codec switch
            {
                AudioCodecType.PCM => null, // We don't need to convert PCM
                AudioCodecType.FLAC => FFMpeg.GetCodec("flac"),
                AudioCodecType.LAME => FFMpeg.GetCodec("libmp3lame"),
                AudioCodecType.Vorbis => FFMpeg.GetCodec("libvorbis"),
                AudioCodecType.WavPack => FFMpeg.GetCodec("wavpack"),
                _ => null
            };
        }

        public static bool CanHandleFloatingPoint(this AudioCodecType codec)
        {
            switch (codec)
            {
                case AudioCodecType.FLAC:
                case AudioCodecType.LAME:
                    return false;

                default:
                    return true;
            }
        }

        public static bool OffersBitrateSetting(this AudioCodecType codec)
        {
            switch (codec)
            {
                case AudioCodecType.LAME:
                case AudioCodecType.Vorbis:
                    return true;

                default:
                    return false;
            }
        }

        public static bool IsValidFormat(this AudioCodecType codec, int sampleRate, int bitrate, out string Reason)
        {
            bool checkFailed = false;
            string error = string.Empty;
            int maxSampleRate = 0;
            int maxBitrate = 0;

            switch (codec)
            {
                case AudioCodecType.FLAC:
                    maxSampleRate = 384000;
                    maxBitrate = int.MaxValue;
                    break;

                case AudioCodecType.LAME:
                    maxSampleRate = 48000;
                    maxBitrate = 320;
                    break;

                case AudioCodecType.Vorbis:
                    maxSampleRate = 48000;
                    maxBitrate = 480;
                    break;

                case AudioCodecType.PCM:
                case AudioCodecType.WavPack:
                default:
                    Reason = string.Empty;
                    return true;
            }

            if (sampleRate > maxSampleRate)
                error += $"{codec.ToExtension()} does not support sample rates above {maxSampleRate / 1000}kHz.";

            if (bitrate > maxBitrate)
                error += $"{(string.IsNullOrEmpty(error) ? "" : "\n\n")}{codec.ToExtension()} does not support bitrates above {maxBitrate}kbps.";

            Reason = error;
            return !checkFailed;
        }

        // Hacky shit
        public static string? CheckFFMpegDirectory()
        {
            var runCheck = RuntimeCheck.GetCurrentPlatform();
            string? query = null;
            string? finalQuery = null;
            string? result = null;
            var ffmpegBin = $"ffmpeg{(runCheck == OS.Windows ? ".exe" : "")}";
            var ocFfmpeg = $"{AppContext.BaseDirectory}/{ffmpegBin}";

            switch (runCheck)
            {
                case OS.Windows:
                    query = Environment.GetEnvironmentVariable("PATH")?
                        .Split(';')
                        .Where(s => File.Exists(Path.Combine(s, ffmpegBin)))
                        .FirstOrDefault();

                    break;

                case OS.Linux:
                case OS.BSD:
                    query = $"/usr/bin";
                    break;

                case OS.macOS:
                    query = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "/opt/homebrew/bin" : "/usr/local/bin";
                    break;

                default:
                    break;
            }

            if (query == null)
                query = AppContext.BaseDirectory;

            finalQuery = $"{query}/{ffmpegBin}";
            Debug.PrintToConsole(Debug.LogType.Message, $"Checking for ffmpeg in \"{finalQuery}\"...");

            if (File.Exists($"{finalQuery}")) result = Path.GetDirectoryName(finalQuery);
            else result = File.Exists(ocFfmpeg) ? AppContext.BaseDirectory : null;

            if (result != null) Debug.PrintToConsole(Debug.LogType.Message, $"Found ffmpeg at \"{result}\"");
            else Debug.PrintToConsole(Debug.LogType.Message, $"Could not find ffmpeg, only WAV is available.");

            return result;
        }
    }
}
