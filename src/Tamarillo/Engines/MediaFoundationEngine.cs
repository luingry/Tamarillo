using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Tamarillo.Operations;

namespace Tamarillo.Engines
{
    /// <summary>
    /// Audio and video through Windows' own Media Foundation transcoder (hardware encoders when present):
    /// MP4/H.264, WMV, MP3, M4A/AAC, WAV, WMA, FLAC. Nothing to install.
    /// </summary>
    internal static class MediaFoundationEngine
    {
        /// <summary>Containers MF reliably reads on Windows 10/11 without extra codecs.</summary>
        private static readonly string[] Inputs = { "mp4", "m4v", "mov", "wmv", "avi", "3gp", "mkv", "mp3", "m4a", "aac", "wav", "wma", "flac", "mpg", "mpeg", "ts", "mts", "m2ts" };

        public static bool CanRead(string ext) => Array.IndexOf(Inputs, ext) >= 0;

        public static bool CanWrite(string ext) =>
            ext == "mp4" || ext == "wmv" || ext == "mp3" || ext == "m4a" || ext == "wav" || ext == "wma" || ext == "flac";

        /// <summary>
        /// Transcodes <paramref name="input"/> to <paramref name="output"/>. <paramref name="maxHeight"/> &gt; 0
        /// shrinks video to that height (aspect kept, never upscaled) at a matching bitrate.
        /// </summary>
        public static async Task TranscodeAsync(string input, string output, string targetExt, int maxHeight, Action<double> progress, CancellationToken token)
        {
            StorageFile source = await StorageFile.GetFileFromPathAsync(input).AsTask(token).ConfigureAwait(false);
            MediaEncodingProfile profile = await ProfileForAsync(source, targetExt, maxHeight).ConfigureAwait(false);

            StorageFolder folder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(output)).AsTask(token).ConfigureAwait(false);
            StorageFile destination = await folder.CreateFileAsync(Path.GetFileName(output), CreationCollisionOption.ReplaceExisting).AsTask(token).ConfigureAwait(false);

            var transcoder = new MediaTranscoder { HardwareAccelerationEnabled = true };
            PrepareTranscodeResult prepared = await transcoder.PrepareFileTranscodeAsync(source, destination, profile).AsTask(token).ConfigureAwait(false);
            if (!prepared.CanTranscode)
            {
                throw new OperationException(Reason(prepared.FailureReason));
            }
            var report = new Progress<double>(percent => progress?.Invoke(percent / 100.0));
            await prepared.TranscodeAsync().AsTask(token, report).ConfigureAwait(false);
        }

        private static async Task<MediaEncodingProfile> ProfileForAsync(StorageFile source, string ext, int maxHeight)
        {
            switch (ext)
            {
                case "mp3": return MediaEncodingProfile.CreateMp3(AudioEncodingQuality.High);
                case "m4a": return MediaEncodingProfile.CreateM4a(AudioEncodingQuality.High);
                case "wav": return MediaEncodingProfile.CreateWav(AudioEncodingQuality.High);
                case "wma": return MediaEncodingProfile.CreateWma(AudioEncodingQuality.High);
                case "flac": return MediaEncodingProfile.CreateFlac(AudioEncodingQuality.High);
                case "wmv": return MediaEncodingProfile.CreateWmv(VideoEncodingQuality.Auto);
                case "mp4":
                    MediaEncodingProfile profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.Auto);
                    if (maxHeight > 0)
                    {
                        VideoProperties video = await source.Properties.GetVideoPropertiesAsync().AsTask().ConfigureAwait(false);
                        uint width = video.Width, height = video.Height;
                        if (video.Orientation == VideoOrientation.Rotate90 || video.Orientation == VideoOrientation.Rotate270)
                        {
                            (width, height) = (height, width);
                        }
                        if (height > 0 && height > maxHeight)
                        {
                            uint newHeight = (uint)maxHeight & ~1u;
                            uint newWidth = (uint)Math.Round(width * (double)maxHeight / height) & ~1u;
                            profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.Auto);
                            profile.Video.Width = newWidth;
                            profile.Video.Height = newHeight;
                        }
                        profile.Video.Bitrate = maxHeight >= 1080 ? 6_000_000u : maxHeight >= 720 ? 3_000_000u : 1_200_000u;
                        profile.Audio = AudioEncodingProperties.CreateAac(44100, 2, 128_000);
                    }
                    return profile;
                default:
                    throw new NotSupportedException(ext);
            }
        }

        private static string Reason(TranscodeFailureReason reason)
        {
            switch (reason)
            {
                case TranscodeFailureReason.CodecNotFound: return "O Windows não tem o codec necessário para este arquivo.";
                case TranscodeFailureReason.InvalidProfile: return "Este formato de saída não é suportado para este arquivo.";
                default: return "O Windows não conseguiu converter este arquivo.";
            }
        }
    }
}
