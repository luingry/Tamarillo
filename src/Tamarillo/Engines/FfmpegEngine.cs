using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Tamarillo.Operations;

namespace Tamarillo.Engines
{
    /// <summary>
    /// Optional ffmpeg backend (detected, never bundled): the formats Media Foundation lacks (MKV, WebM, GIF,
    /// MOV, AVI, OGG, Opus, AIFF, WebP/AVIF images) and a fallback when MF refuses a file. Runs at below-normal
    /// priority with progress parsed from <c>-progress pipe:1</c>; cancelling kills the process.
    /// </summary>
    internal static class FfmpegEngine
    {
        private static readonly Regex DurationPattern = new Regex(@"Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)", RegexOptions.Compiled);

        /// <summary>Encoding arguments for a target, or null if this ffmpeg build cannot produce it.</summary>
        public static string ArgumentsFor(string ext, Capabilities caps, int maxHeight = 0, VideoProfile profile = VideoProfile.None)
        {
            string h264 = H264(caps, VideoProfile.None, 0);
            string aac = "-c:a aac -b:a 160k";
            string scale = maxHeight > 0 ? $"-vf \"scale=-2:'min({maxHeight},ih)'\" " : string.Empty;
            switch (ext)
            {
                case "mp4":
                    string audio = profile == VideoProfile.Efficiency ? "-c:a aac -b:a 96k" : aac;
                    return scale + H264(caps, profile, maxHeight) + " -pix_fmt yuv420p " + audio + " -movflags +faststart";
                case "mov":
                    return h264 + " -pix_fmt yuv420p " + aac;
                case "mkv":
                    // Matroska takes nearly any codec: remux losslessly.
                    return "-map 0 -c copy";
                case "webm":
                    return caps.FfmpegHas("libvpx-vp9") && caps.FfmpegHas("libopus")
                        ? "-c:v libvpx-vp9 -crf 32 -b:v 0 -row-mt 1 -deadline good -cpu-used 4 -c:a libopus -b:a 128k"
                        : null;
                case "avi":
                    return "-c:v mpeg4 -q:v 4 " + (caps.FfmpegHas("libmp3lame") ? "-c:a libmp3lame -q:a 4" : "-c:a pcm_s16le");
                case "wmv":
                    return "-c:v wmv2 -b:v 4M -c:a wmav2 -b:a 192k";
                case "gif":
                    return "-vf \"fps=12,scale='min(640,iw)':-2:flags=lanczos,split[a][b];[a]palettegen=max_colors=128[p];[b][p]paletteuse=dither=bayer:bayer_scale=4\" -loop 0";
                case "mp3":
                    return caps.FfmpegHas("libmp3lame") ? "-vn -c:a libmp3lame -q:a 2" : null;
                case "m4a":
                    return "-vn " + aac;
                case "wav":
                    return "-vn -c:a pcm_s16le";
                case "flac":
                    return "-vn -c:a flac";
                case "ogg":
                    return caps.FfmpegHas("libvorbis") ? "-vn -c:a libvorbis -q:a 5" : null;
                case "opus":
                    return caps.FfmpegHas("libopus") ? "-vn -c:a libopus -b:a 128k" : null;
                case "aiff":
                    return "-vn -c:a pcm_s16be";
                case "wma":
                    return "-vn -c:a wmav2 -b:a 192k";
                case "webp":
                    return caps.FfmpegHas("libwebp") ? "-frames:v 1 -c:v libwebp -quality 85" : null;
                case "avif":
                    return caps.FfmpegHas("libsvtav1") ? "-frames:v 1 -c:v libsvtav1 -crf 30 -still-picture 1"
                        : caps.FfmpegHas("libaom-av1") ? "-frames:v 1 -c:v libaom-av1 -crf 30 -still-picture 1" : null;
                case "png":
                    return "-frames:v 1";
                default:
                    return null;
            }
        }

        /// <summary>
        /// H.264 settings for a compression profile. x264 uses the slow preset for both profiles (smaller files
        /// at the same quality); the bitrate-driven fallbacks size the stream by output height.
        /// </summary>
        private static string H264(Capabilities caps, VideoProfile profile, int maxHeight)
        {
            if (caps.FfmpegHas("libx264"))
            {
                switch (profile)
                {
                    case VideoProfile.Quality: return "-c:v libx264 -preset slow -crf 21";
                    case VideoProfile.Efficiency: return "-c:v libx264 -preset slow -crf 28";
                    default: return "-c:v libx264 -preset medium -crf 23";
                }
            }
            if (caps.FfmpegHas("h264_mf"))
            {
                return "-c:v h264_mf -b:v " + (profile == VideoProfile.None ? "5M" : (MediaFoundationEngine.Bitrate(profile, maxHeight) / 1000) + "k");
            }
            return profile == VideoProfile.Efficiency ? "-c:v mpeg4 -q:v 6" : "-c:v mpeg4 -q:v 3";
        }

        public static async Task RunAsync(string ffmpeg, string input, string output, string arguments, Action<double> progress, CancellationToken token)
        {
            string args = $"-hide_banner -nostdin -y -i \"{input}\" {arguments} -progress pipe:1 -nostats \"{output}\"";
            var info = new ProcessStartInfo(ffmpeg, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            double duration = 0;
            var tail = new Queue<string>();
            var exited = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (var process = new Process { StartInfo = info, EnableRaisingEvents = true })
            {
                process.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data == null)
                    {
                        return;
                    }
                    lock (tail)
                    {
                        tail.Enqueue(e.Data);
                        if (tail.Count > 12)
                        {
                            tail.Dequeue();
                        }
                    }
                    if (duration <= 0)
                    {
                        Match match = DurationPattern.Match(e.Data);
                        if (match.Success)
                        {
                            duration = int.Parse(match.Groups[1].Value) * 3600 + int.Parse(match.Groups[2].Value) * 60
                                       + double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
                        }
                    }
                };
                process.OutputDataReceived += (s, e) =>
                {
                    // out_time_us=12345678 (out_time_ms is also microseconds, historically misnamed).
                    if (e.Data != null && duration > 0 && e.Data.StartsWith("out_time_us=", StringComparison.Ordinal)
                        && long.TryParse(e.Data.Substring(12), out long micros) && micros > 0)
                    {
                        progress?.Invoke(Math.Min(0.999, micros / 1_000_000.0 / duration));
                    }
                };
                process.Exited += (s, e) => exited.TrySetResult(0);

                process.Start();
                try
                {
                    process.PriorityClass = ProcessPriorityClass.BelowNormal;
                }
                catch (InvalidOperationException)
                {
                    // Already exited.
                }
                process.BeginErrorReadLine();
                process.BeginOutputReadLine();

                using (token.Register(() =>
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            process.Kill();
                        }
                    }
                    catch
                    {
                    }
                }))
                {
                    await exited.Task.ConfigureAwait(false);
                }
                process.WaitForExit(); // flush async readers
                token.ThrowIfCancellationRequested();
                if (process.ExitCode != 0)
                {
                    string detail;
                    lock (tail)
                    {
                        detail = string.Join(" | ", tail);
                    }
                    Core.Diagnostics.Log?.Warn($"ffmpeg exit {process.ExitCode}: {detail}");
                    throw new OperationException("O ffmpeg não conseguiu converter este arquivo.");
                }
            }
            progress?.Invoke(1);
        }
    }
}
