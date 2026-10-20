using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Tamarillo.Core;

namespace Tamarillo.Engines
{
    /// <summary>
    /// What this machine can do. Built-in engines (WIC, Media Foundation, Windows.Data.Pdf, tar.exe) are
    /// always there on Windows 10/11; optional ones (ffmpeg, Office, LibreOffice, extra WIC encoders, OCR
    /// languages) are detected once, in the background, and the wheel only offers what will work.
    /// </summary>
    public sealed class Capabilities
    {
        public static readonly Guid WebpContainer = new Guid("e094b0e2-67f2-45b3-b0ea-115337ca7cf3");
        public static readonly Guid HeifContainer = new Guid("e1e62521-6787-405b-a339-500715b5763f");

        /// <summary>Before detection finishes: only what Windows always has.</summary>
        public static Capabilities Baseline { get; } = new Capabilities { Tar = SystemTar() };

        /// <summary>Every optional engine present: lists every action the wheel can ever offer (settings catalog). Never run with it.</summary>
        public static Capabilities Everything { get; } = new Capabilities
        {
            WebpEncoder = true,
            HeifEncoder = true,
            Ocr = true,
            Ffmpeg = "ffmpeg.exe",
            Word = true,
            Excel = true,
            PowerPoint = true,
            LibreOffice = "soffice.exe",
            Tar = "tar.exe",
            _everyEncoder = true,
        };

        private bool _everyEncoder;

        public bool WebpEncoder { get; private set; }

        public bool HeifEncoder { get; private set; }

        public bool Ocr { get; private set; }

        public string Ffmpeg { get; private set; }

        public IReadOnlyCollection<string> FfmpegEncoders { get; private set; } = Array.Empty<string>();

        public bool Word { get; private set; }

        public bool Excel { get; private set; }

        public bool PowerPoint { get; private set; }

        public string LibreOffice { get; private set; }

        public string Tar { get; private set; }

        public bool HasFfmpeg => Ffmpeg != null;

        public bool FfmpegHas(string encoder) => _everyEncoder || FfmpegEncoders.Contains(encoder);

        public bool CanWebp => WebpEncoder || FfmpegHas("libwebp");

        public bool CanAvif => FfmpegHas("libaom-av1") || FfmpegHas("libsvtav1");

        public bool OfficeFor(FileKind kind) =>
            kind == FileKind.Document || kind == FileKind.Text || kind == FileKind.Pdf ? Word
            : kind == FileKind.Spreadsheet || kind == FileKind.Data ? Excel
            : kind == FileKind.Presentation && PowerPoint;

        public bool CanOfficeConvert(FileKind kind) => OfficeFor(kind) || LibreOffice != null;

        /// <summary>Full detection. Slow-ish (spawns ffmpeg once): call off the UI thread.</summary>
        public static Capabilities Detect(TamarilloSettings settings)
        {
            var caps = new Capabilities { Tar = SystemTar() };
            caps.WebpEncoder = HasWicEncoder(WebpContainer);
            caps.HeifEncoder = HasWicEncoder(HeifContainer);
            caps.Ocr = Safe(() => Windows.Media.Ocr.OcrEngine.AvailableRecognizerLanguages.Count > 0);
            caps.Ffmpeg = FindFfmpeg(settings?.FfmpegPath);
            if (caps.Ffmpeg != null)
            {
                caps.FfmpegEncoders = ReadFfmpegEncoders(caps.Ffmpeg);
            }
            caps.Word = HasProgId("Word.Application");
            caps.Excel = HasProgId("Excel.Application");
            caps.PowerPoint = HasProgId("PowerPoint.Application");
            caps.LibreOffice = FindLibreOffice();
            return caps;
        }

        public override string ToString() =>
            $"webp={WebpEncoder} heif={HeifEncoder} ocr={Ocr} ffmpeg={Ffmpeg ?? "-"} ({FfmpegEncoders.Count} encoders) " +
            $"word={Word} excel={Excel} powerpoint={PowerPoint} libreoffice={LibreOffice ?? "-"} tar={Tar ?? "-"}";

        public static string FindFfmpeg(string configured)
        {
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return File.Exists(configured) ? configured : null;
            }
            var candidates = new List<string>();
            foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(';'))
            {
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    candidates.Add(Path.Combine(dir.Trim().Trim('"'), "ffmpeg.exe"));
                }
            }
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            candidates.Add(@"C:\ffmpeg\bin\ffmpeg.exe");
            candidates.Add(Path.Combine(local, @"Microsoft\WinGet\Links\ffmpeg.exe"));
            candidates.Add(Path.Combine(profile, @"scoop\shims\ffmpeg.exe"));
            candidates.Add(@"C:\ProgramData\chocolatey\bin\ffmpeg.exe");
            return candidates.FirstOrDefault(c => Safe(() => File.Exists(c)));
        }

        private static string SystemTar()
        {
            string tar = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "tar.exe");
            return File.Exists(tar) ? tar : null;
        }

        private static IReadOnlyCollection<string> ReadFfmpegEncoders(string ffmpeg)
        {
            var encoders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var info = new ProcessStartInfo(ffmpeg, "-hide_banner -encoders")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                using (Process process = Process.Start(info))
                {
                    process.ErrorDataReceived += (s, e) => { };
                    process.BeginErrorReadLine();
                    string line;
                    while ((line = process.StandardOutput.ReadLine()) != null)
                    {
                        // " V....D libx264              libx264 H.264 ..."
                        string[] parts = line.Trim().Split(new[] { ' ' }, 3, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2 && parts[0].Length == 6 && !parts[0].Contains("="))
                        {
                            encoders.Add(parts[1]);
                        }
                    }
                    process.WaitForExit(5000);
                }
            }
            catch (Exception ex)
            {
                Diagnostics.Log?.Warn("ffmpeg probe failed: " + ex.Message);
            }
            return encoders;
        }

        private static bool HasWicEncoder(Guid container)
        {
            try
            {
                return StaWorker.Run(() =>
                {
                    BitmapEncoder encoder = BitmapEncoder.Create(container);
                    var pixel = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, new byte[16], 8);
                    encoder.Frames.Add(BitmapFrame.Create(pixel));
                    using (var stream = new MemoryStream())
                    {
                        encoder.Save(stream);
                        return stream.Length > 0;
                    }
                }).GetAwaiter().GetResult();
            }
            catch
            {
                return false;
            }
        }

        private static bool HasProgId(string progId)
        {
            try
            {
                using (RegistryKey key = Registry.ClassesRoot.OpenSubKey(progId + @"\CLSID"))
                {
                    return key != null;
                }
            }
            catch
            {
                return false;
            }
        }

        private static string FindLibreOffice()
        {
            foreach (string root in new[] { @"SOFTWARE\LibreOffice\UNO\InstallPath", @"SOFTWARE\WOW6432Node\LibreOffice\UNO\InstallPath" })
            {
                try
                {
                    using (RegistryKey key = Registry.LocalMachine.OpenSubKey(root))
                    {
                        if (key?.GetValue(null) is string path)
                        {
                            string soffice = Path.Combine(path, "soffice.exe");
                            if (File.Exists(soffice))
                            {
                                return soffice;
                            }
                        }
                    }
                }
                catch
                {
                }
            }
            string fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"LibreOffice\program\soffice.exe");
            return File.Exists(fallback) ? fallback : null;
        }

        private static bool Safe(Func<bool> probe)
        {
            try
            {
                return probe();
            }
            catch
            {
                return false;
            }
        }
    }
}
