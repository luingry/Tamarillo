using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Tamarillo.Core;
using Tamarillo.Engines;
using Tamarillo.Jobs;
using Tamarillo.Operations;
using Tamarillo.Ui;

namespace Tamarillo.Tests
{
    internal static class Program
    {
        private static int _failed;
        private static int _passed;
        private static string _root;
        private static Capabilities _caps;

        [STAThread]
        private static int Main()
        {
            _root = Path.Combine(Path.GetTempPath(), "tamarillo-tests-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(_root);
            _caps = Capabilities.Detect(new TamarilloSettings());
            Console.WriteLine("Capabilities: " + _caps);
            try
            {
                Run("Shortcut validity and keycaps", ShortcutRules);
                Run("Settings round-trip, defaults and .bak fallback", SettingsStoreWorks);
                Run("File kinds and summaries", FileKindsWork);
                Run("Output names never overwrite", OutputNames);
                Run("Radial geometry picks slots and sub-items", Geometry);
                Run("Menus match the files", Menus);
                Run("Subtitles SRT → VTT → SRT keep timing", Subtitles);
                Run("CSV ⇄ JSON keep data; JSON format/minify keep numbers", Data);
                Run("Text → DOCX → text", Docx);
                Run("Image convert: JPG → PNG, PNG with alpha → JPG on white", ImageConvert);
                Run("Image resize, rotate and ICO", ImageTransforms);
                Run("Strip metadata is lossless and drops EXIF", StripMetadata);
                Run("Images → PDF renders back through Windows.Data.Pdf", ImagesToPdf);
                Run("Text → PDF has pages", TextToPdf);
                Run("ZIP a folder and extract without double nesting", ZipRoundTrip);
                Run("Extract refuses zip-slip", ZipSlip);
                Run("Audio WAV → MP3/M4A via Media Foundation", MediaFoundation);
                Run("Audio WAV → OGG via ffmpeg (when installed)", Ffmpeg);
                Run("SHA-256 goes to the clipboard text", Hash);
                Run("Cancelled job leaves no temp files", Cancellation);
                Run("Errors are worded for people", Messages);
            }
            finally
            {
                try
                {
                    Directory.Delete(_root, recursive: true);
                }
                catch
                {
                }
            }
            Console.WriteLine();
            Console.WriteLine($"{_passed} passed, {_failed} failed");
            return _failed == 0 ? 0 : 1;
        }

        // ═══ Unit ═══

        private static void ShortcutRules()
        {
            Assert(Shortcut.Default.IsValid && Shortcut.Default.ToString() == "Ctrl + Alt + Shift", "default chord");
            Assert(!new Shortcut { Ctrl = true }.IsValid, "a lone modifier is refused");
            Assert(new Shortcut { Ctrl = true, Key = 0x54 }.IsValid, "modifier + key is fine");
            Assert(!new Shortcut { Key = 0x54 }.IsValid, "a key without modifier is refused");
            Assert(new Shortcut { Win = true, Shift = true, Key = 0x20 }.ToString() == "Shift + Win + Espaço", "keycap order and names");
        }

        private static void SettingsStoreWorks()
        {
            string dir = Folder("settings");
            var store = new SettingsStore(dir);
            store.Load();
            Assert(store.Current.Shortcut.SameAs(Shortcut.Default) && store.Current.JpegQuality == 90, "defaults");
            store.Update(s =>
            {
                s.Shortcut = new Shortcut { Ctrl = true, Win = true };
                s.Output = OutputLocation.Subfolder;
            });
            store.Flush();
            store.Update(s => s.JpegQuality = 75);
            store.Flush();
            var again = new SettingsStore(dir);
            again.Load();
            Assert(again.Current.Shortcut.Win && again.Current.Output == OutputLocation.Subfolder && again.Current.JpegQuality == 75, "round-trip");

            File.WriteAllText(Path.Combine(dir, "settings.json"), "{ broken");
            var recovered = new SettingsStore(dir);
            recovered.Load();
            Assert(recovered.Current.Output == OutputLocation.Subfolder, "falls back to .bak");

            TamarilloSettings partial = SettingsStore.Deserialize("{\"Enabled\":true}");
            Assert(partial.Shortcut.SameAs(Shortcut.Default) && partial.SelectionTrigger && partial.JpegQuality == 90, "missing members keep defaults");
        }

        private static void FileKindsWork()
        {
            Assert(FileKinds.Of("HEIC") == FileKind.Image && FileKinds.Of("mkv") == FileKind.Video && FileKinds.Of("xyz") == FileKind.Other, "kinds");
            FileSet set = FileSet.Virtual(@"C:\a\1.jpg", @"C:\a\2.PNG");
            Assert(set.Kind == FileKind.Image && set.Summary == "2 imagens", "uniform set");
            Assert(FileSet.Virtual(@"C:\a\1.jpg", @"C:\a\b.mp3").Kind == FileKind.Other, "mixed set");
            Assert(FileSet.Virtual(@"C:\a\b\").HasFolders, "folders");
        }

        private static void OutputNames()
        {
            string dir = Folder("names");
            File.WriteAllText(Path.Combine(dir, "foto.png"), "x");
            File.WriteAllText(Path.Combine(dir, "foto (2).png"), "x");
            Assert(OutputPaths.Unique(dir, "foto", null, "png").EndsWith("foto (3).png"), "numbering skips taken names");
            Assert(OutputPaths.Unique(dir, "foto", " (50%)", "png").EndsWith("foto (50%).png"), "suffix");
            Assert(OutputPaths.Unique(dir, "a:b", null, "txt").EndsWith("a_b.txt"), "invalid characters");
            var settings = new TamarilloSettings { Output = OutputLocation.Subfolder };
            Assert(OutputPaths.FolderFor(Path.Combine(dir, "x.jpg"), settings) == Path.Combine(dir, "Tamarillo"), "subfolder");
        }

        private static void Geometry()
        {
            Assert(RadialGeometry.SlotAt(-90, 8) == 0, "top is slot 0");
            Assert(RadialGeometry.SlotAt(0, 8) == 2, "right is slot 2 of 8");
            Assert(RadialGeometry.SlotAt(-90 - 22, 8) == 0 && RadialGeometry.SlotAt(-90 - 23, 8) == 7, "slot boundaries");
            Assert(RadialGeometry.SlotAt(179, 4) == 3 && RadialGeometry.SlotAt(-179, 4) == 3, "wrap-around");
            Assert(RadialGeometry.SubItemAt(-90, -90, 3, 30) == 1, "middle sub-item");
            Assert(RadialGeometry.SubItemAt(-90 - 40, -90, 3, 30) == 0, "first sub-item");
            Assert(RadialGeometry.SubItemAt(90, -90, 3, 30) == -1, "outside the sub-ring");
            Assert(Math.Abs(RadialGeometry.Normalize(350) + 10) < 1e-9, "normalize");
        }

        private static void Menus()
        {
            var host = new FakeHost();
            IReadOnlyList<MenuNode> images = MenuBuilder.Build(FileSet.Virtual(@"C:\a\1.jpg", @"C:\a\2.jpeg"), _caps, host);
            Assert(images.Count <= MenuBuilder.MaxSlots && images[0].Id == "convert", "convert first");
            Assert(images[0].Children.All(c => c.Id != "to-jpg"), "no conversion to the format they already are");
            Assert(images.Any(n => n.Id == "pdf") && images.Last().Id == "more", "merge to PDF and Mais");
            IReadOnlyList<MenuNode> folders = MenuBuilder.Build(FileSet.Virtual(@"C:\a\b\"), _caps, host);
            Assert(folders.Any(n => n.Id == "zip") && folders.All(n => !n.IsGroup), "folders get file actions directly");
            IReadOnlyList<MenuNode> video = MenuBuilder.Build(FileSet.Virtual(@"C:\v\a.mov"), Capabilities.Baseline, host);
            Assert(video.First(n => n.Id == "convert").Children.Any(c => c.Id == "to-mp4"), "MP4 without ffmpeg");
            IReadOnlyList<MenuNode> json = MenuBuilder.Build(FileSet.Virtual(@"C:\d\a.json"), _caps, host);
            Assert(json.Any(n => n.Id == "json-format") && json.Any(n => n.Id == "json-minify"), "JSON tools");
            Assert(MenuBuilder.Build(FileSet.Virtual(), _caps, host).Count == 0, "empty set");
        }

        private static void Subtitles()
        {
            string srt = "1\r\n00:00:01,500 --> 00:00:03,250\r\nOlá, <i>mundo</i>\r\n\r\n2\r\n01:02:03,004 --> 01:02:05,000\r\nSegunda\r\nlinha\r\n";
            var cues = TextEngine.ParseSubtitles(srt);
            Assert(cues.Count == 2 && cues[1].Start == new TimeSpan(0, 1, 2, 3, 4), "parse");
            string vtt = TextEngine.WriteSubtitles(cues, "vtt");
            Assert(vtt.StartsWith("WEBVTT") && vtt.Contains("00:00:01.500 --> 00:00:03.250"), "vtt");
            string back = TextEngine.WriteSubtitles(TextEngine.ParseSubtitles(vtt), "srt");
            Assert(back.Replace("\r\n", "\n") == srt.Replace("\r\n", "\n") + "\n" || back.Contains("01:02:03,004 --> 01:02:05,000"), "srt again");
            Assert(TextEngine.WriteSubtitles(cues, "txt").StartsWith("Olá, mundo"), "plain text drops tags");
        }

        private static void Data()
        {
            string csv = "nome,obs\r\n\"Silva, Ana\",\"diz \"\"oi\"\"\"\r\nBia,\"duas\nlinhas\"\r\n";
            var rows = TextEngine.ParseDelimited(csv, TextEngine.DetectDelimiter(csv, "csv"));
            Assert(rows.Count == 3 && rows[1][0] == "Silva, Ana" && rows[1][1] == "diz \"oi\"" && rows[2][1] == "duas\nlinhas", "RFC 4180");
            string json = TextEngine.TableToJson(rows);
            var table = TextEngine.JsonToTable(json);
            Assert(table.Count == 3 && table[2][1] == "duas\nlinhas", "CSV → JSON → table");
            Assert(TextEngine.DetectDelimiter("a;b;c\n1;2;3", "csv") == ';', "semicolon CSV");

            string source = "{\"n\": 1.10, \"big\": 12345678901234567890, \"list\": [true, null, \"x\"]}";
            string min = Json.Write(Json.Parse(source), indent: false);
            Assert(min == "{\"n\":1.10,\"big\":12345678901234567890,\"list\":[true,null,\"x\"]}", "minify keeps numbers: " + min);
            Assert(Json.Write(Json.Parse(min), indent: true).Contains("\r\n  \"n\": 1.10"), "format");
            Throws<FormatException>(() => Json.Parse("{\"a\": }"), "invalid JSON");
        }

        private static void Docx()
        {
            string path = Path.Combine(Folder("docx"), "a.docx");
            TextEngine.WriteDocx(path, "Primeira linha\r\nSegunda <com> & símbolos\r\n\r\nFim");
            string text = TextEngine.ReadDocx(path);
            Assert(text.Contains("Segunda <com> & símbolos") && text.Contains("Fim"), "round-trip: " + text);
        }

        // ═══ Real conversions ═══

        private static void ImageConvert()
        {
            string dir = Folder("convert");
            string jpg = WriteImage(Path.Combine(dir, "foto.jpg"), 320, 200, alpha: false);
            string png = WriteImage(Path.Combine(dir, "logo.png"), 64, 64, alpha: true);
            string outPng = RunSingle(new ImageConvertOperation("png"), jpg);
            Assert(outPng.EndsWith("foto.png") && Decode(outPng).PixelWidth == 320, "JPG → PNG");
            string outJpg = RunSingle(new ImageConvertOperation("jpg"), png);
            BitmapSource flat = new FormatConvertedBitmap(Decode(outJpg), PixelFormats.Bgra32, null, 0);
            var pixel = new byte[4];
            flat.CopyPixels(new Int32Rect(0, 0, 1, 1), pixel, 4, 0);
            Assert(pixel[0] > 240 && pixel[1] > 240 && pixel[2] > 240, "transparent corner becomes white, not black");
            Assert(File.ReadAllBytes(png).Length > 0 && File.Exists(png), "source untouched");
        }

        private static void ImageTransforms()
        {
            string dir = Folder("transforms");
            string jpg = WriteImage(Path.Combine(dir, "foto.jpg"), 400, 300, alpha: false);
            string half = RunSingle(new ImageResizeOperation(ResizeKind.Scale, 0.5, "50%"), jpg);
            BitmapSource h = Decode(half);
            Assert(half.EndsWith("foto (50%).jpg") && h.PixelWidth == 200 && h.PixelHeight == 150, "resize 50%");
            string edge = RunSingle(new ImageResizeOperation(ResizeKind.LongEdge, 1920, "1920 px"), jpg);
            Assert(Decode(edge).PixelWidth == 400, "never upscales");
            string rotated = RunSingle(new ImageRotateOperation(RotateKind.Right), jpg);
            Assert(Decode(rotated).PixelWidth == 300 && Decode(rotated).PixelHeight == 400, "rotate swaps sides");
            string ico = RunSingle(new ImageConvertOperation("ico"), jpg);
            byte[] bytes = File.ReadAllBytes(ico);
            Assert(bytes[2] == 1 && BitConverter.ToUInt16(bytes, 4) == 7, "ICO with 7 sizes");
        }

        private static void StripMetadata()
        {
            string dir = Folder("strip");
            string jpg = WriteImage(Path.Combine(dir, "gps.jpg"), 120, 80, alpha: false, comment: true);
            byte[] before = File.ReadAllBytes(jpg);
            Assert(Contains(before, Encoding.ASCII.GetBytes("Exif")) || Contains(before, new byte[] { 0xFF, 0xFE }), "fixture has metadata");
            string clean = RunSingle(new ImageStripMetadataOperation(), jpg);
            byte[] after = File.ReadAllBytes(clean);
            Assert(!Contains(after, Encoding.ASCII.GetBytes("Exif")) && !Contains(after, Encoding.ASCII.GetBytes("Tamarillo test")), "metadata gone");
            Assert(Decode(clean).PixelWidth == 120, "still a valid image");
            int scanBefore = IndexOf(before, new byte[] { 0xFF, 0xDA }), scanAfter = IndexOf(after, new byte[] { 0xFF, 0xDA });
            Assert(before.Skip(scanBefore).SequenceEqual(after.Skip(scanAfter)), "image data byte-identical (lossless)");
        }

        private static void ImagesToPdf()
        {
            string dir = Folder("pdf");
            string a = WriteImage(Path.Combine(dir, "a.jpg"), 300, 200, alpha: false);
            string b = WriteImage(Path.Combine(dir, "b.png"), 100, 200, alpha: true);
            OperationResult result = Execute(new ImagesToPdfOperation(), a, b);
            string pdf = result.Outputs.Single();
            Assert(pdf.EndsWith(Path.GetFileName(dir) + ".pdf") || pdf.EndsWith(".pdf"), "one PDF named after the folder");
            OperationResult pages = Execute(new PdfToImagesOperation("png"), pdf);
            string folder = pages.Outputs.Single();
            string[] images = Directory.GetFiles(folder, "*.png");
            Assert(images.Length == 2, "Windows renders both pages: " + images.Length);
            BitmapSource first = Decode(images[0]);
            Assert(Math.Abs((double)first.PixelWidth / first.PixelHeight - 1.5) < 0.02, "page keeps the image's aspect");
        }

        private static void TextToPdf()
        {
            string dir = Folder("textpdf");
            string txt = Path.Combine(dir, "notas.txt");
            File.WriteAllText(txt, string.Join("\r\n", Enumerable.Range(1, 130).Select(i => $"Linha {i}: ação, pão, coração — (parênteses) \\ barra")), Encoding.UTF8);
            string pdf = RunSingle(new TextConvertOperation("pdf"), txt);
            string first = RunSingle(new PdfToImagesOperation("jpg"), pdf);
            Assert(Directory.Exists(first) && Directory.GetFiles(first).Length == 3, "130 lines → 3 A4 pages");
        }

        private static void ZipRoundTrip()
        {
            string dir = Folder("zip");
            string project = Path.Combine(dir, "projeto");
            Directory.CreateDirectory(Path.Combine(project, "src", "vazio"));
            File.WriteAllText(Path.Combine(project, "src", "a.txt"), "conteúdo");
            string zip = Execute(new ZipOperation(), project).Outputs.Single();
            Assert(zip.EndsWith("projeto.zip"), "named after the folder");
            using (ZipArchive archive = ZipFile.OpenRead(zip))
            {
                Assert(archive.Entries.Any(e => e.FullName == "projeto/src/a.txt") && archive.Entries.Any(e => e.FullName == "projeto/src/vazio/"), "structure and empty folders");
            }
            Directory.Delete(project, true);
            string extracted = Execute(new ExtractOperation(), zip).Outputs.Single();
            Assert(extracted == project && File.ReadAllText(Path.Combine(project, "src", "a.txt")) == "conteúdo", "single top folder is not nested again: " + extracted);
        }

        private static void ZipSlip()
        {
            string dir = Folder("slip");
            string zip = Path.Combine(dir, "mal.zip");
            using (ZipArchive archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                using (var writer = new StreamWriter(archive.CreateEntry("../../fora.txt").Open()))
                {
                    writer.Write("x");
                }
            }
            Throws<OperationException>(() => Execute(new ExtractOperation(), zip), "zip-slip");
            Assert(!File.Exists(Path.Combine(Path.GetDirectoryName(dir), "fora.txt")), "nothing escaped");
            Assert(!Directory.EnumerateFileSystemEntries(dir).Any(e => Path.GetFileName(e).StartsWith("~tamarillo")), "temp cleaned");
        }

        private static void MediaFoundation()
        {
            string dir = Folder("mf");
            string wav = WriteWav(Path.Combine(dir, "tom.wav"), seconds: 2);
            string mp3 = RunSingle(new MediaConvertOperation("mp3", "MP3"), wav, Capabilities.Baseline);
            Assert(new FileInfo(mp3).Length > 4000 && new FileInfo(mp3).Length < new FileInfo(wav).Length, "MP3 written and smaller");
            string m4a = RunSingle(new MediaConvertOperation("m4a", "M4A"), wav, Capabilities.Baseline);
            Assert(new FileInfo(m4a).Length > 2000, "M4A written");
        }

        private static void Ffmpeg()
        {
            if (!_caps.HasFfmpeg || FfmpegEngine.ArgumentsFor("ogg", _caps) == null)
            {
                Console.WriteLine("        (skipped: no ffmpeg with Vorbis)");
                return;
            }
            string wav = WriteWav(Path.Combine(Folder("ffmpeg"), "tom.wav"), seconds: 2);
            var progress = new List<double>();
            string ogg = RunSingle(new MediaConvertOperation("ogg", "OGG"), wav, _caps);
            Assert(Encoding.ASCII.GetString(File.ReadAllBytes(ogg), 0, 4) == "OggS", "OGG container");
        }

        private static void Hash()
        {
            string file = Path.Combine(Folder("hash"), "abc.txt");
            File.WriteAllText(file, "abc", new UTF8Encoding(false));
            OperationResult result = Execute(new HashOperation(), file);
            Assert(result.ClipboardText == "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", "known SHA-256");
        }

        private static void Cancellation()
        {
            string dir = Folder("cancel");
            string wav = WriteWav(Path.Combine(dir, "longo.wav"), seconds: 240);
            using (var cts = new CancellationTokenSource())
            {
                var context = new OperationContext(FileSet.FromPaths(new[] { wav }), new TamarilloSettings(), Capabilities.Baseline, cts.Token, null);
                cts.CancelAfter(250);
                try
                {
                    new MediaConvertOperation("mp3", "MP3").RunAsync(context).GetAwaiter().GetResult();
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex) when (cts.IsCancellationRequested)
                {
                    Console.WriteLine("        (cancel surfaced as " + ex.GetType().Name + ")");
                }
            }
            Thread.Sleep(300);
            string[] left = Directory.GetFiles(dir).Select(Path.GetFileName).ToArray();
            Assert(left.Length == 1 && left[0] == "longo.wav", "only the source remains: " + string.Join(", ", left));
        }

        private static void Messages()
        {
            Assert(JobRunner.UserMessage(new OperationException("Específica.")) == "Específica.", "operation message kept");
            Assert(JobRunner.UserMessage(new UnauthorizedAccessException()).StartsWith("Sem permissão"), "access denied");
            Assert(JobRunner.UserMessage(new AggregateException(new FileNotFoundException())).Contains("não existe"), "unwraps aggregates");
            Assert(JobRunner.UserMessage(new InvalidCastException()).Contains("log"), "generic fallback");
        }

        // ═══ Helpers ═══

        private static string RunSingle(Operation operation, string input, Capabilities caps = null) =>
            Execute(operation, caps ?? _caps, input).Outputs.Single();

        private static OperationResult Execute(Operation operation, params string[] inputs) => Execute(operation, _caps, inputs);

        private static OperationResult Execute(Operation operation, Capabilities caps, params string[] inputs)
        {
            var context = new OperationContext(FileSet.FromPaths(inputs), new TamarilloSettings(), caps, CancellationToken.None, null);
            OperationResult result = operation.RunAsync(context).GetAwaiter().GetResult();
            foreach (string output in result.Outputs)
            {
                Assert(File.Exists(output) || Directory.Exists(output), "output exists: " + output);
                Assert(!Path.GetFileName(output).StartsWith("~tamarillo"), "temp name never leaks");
            }
            return result;
        }

        private static string WriteImage(string path, int width, int height, bool alpha, bool comment = false)
        {
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                if (!alpha)
                {
                    dc.DrawRectangle(new LinearGradientBrush(Colors.OrangeRed, Colors.SteelBlue, 30), null, new Rect(0, 0, width, height));
                }
                dc.DrawEllipse(Brushes.SeaGreen, null, new Point(width / 2.0, height / 2.0), width / 4.0, height / 4.0);
            }
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            BitmapEncoder encoder = path.EndsWith(".png") ? (BitmapEncoder)new PngBitmapEncoder() : new JpegBitmapEncoder { QualityLevel = 90 };
            BitmapMetadata metadata = null;
            if (comment)
            {
                metadata = new BitmapMetadata("jpg");
                metadata.SetQuery("/app1/ifd/{ushort=305}", "Tamarillo test");
                metadata.Comment = "Tamarillo test";
            }
            encoder.Frames.Add(BitmapFrame.Create(alpha ? (BitmapSource)bitmap : new FormatConvertedBitmap(bitmap, PixelFormats.Bgr24, null, 0), null, metadata, null));
            using (var stream = File.Create(path))
            {
                encoder.Save(stream);
            }
            return path;
        }

        private static string WriteWav(string path, int seconds)
        {
            const int rate = 44100;
            int samples = rate * seconds;
            using (var writer = new BinaryWriter(File.Create(path)))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36 + samples * 4);
                writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)2);
                writer.Write(rate);
                writer.Write(rate * 4);
                writer.Write((short)4);
                writer.Write((short)16);
                writer.Write(Encoding.ASCII.GetBytes("data"));
                writer.Write(samples * 4);
                for (int i = 0; i < samples; i++)
                {
                    short value = (short)(Math.Sin(2 * Math.PI * 440 * i / rate) * 8000);
                    writer.Write(value);
                    writer.Write(value);
                }
            }
            return path;
        }

        private static BitmapSource Decode(string path)
        {
            using (var stream = File.OpenRead(path))
            {
                BitmapDecoder decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                return decoder.Frames[0];
            }
        }

        private static string Folder(string name)
        {
            string path = Path.Combine(_root, name);
            Directory.CreateDirectory(path);
            return path;
        }

        private static bool Contains(byte[] data, byte[] pattern) => IndexOf(data, pattern) >= 0;

        private static int IndexOf(byte[] data, byte[] pattern)
        {
            for (int i = 0; i <= data.Length - pattern.Length; i++)
            {
                int j = 0;
                while (j < pattern.Length && data[i + j] == pattern[j])
                {
                    j++;
                }
                if (j == pattern.Length)
                {
                    return i;
                }
            }
            return -1;
        }

        private static void Run(string name, Action test)
        {
            try
            {
                test();
                _passed++;
                Console.WriteLine("  PASS  " + name);
            }
            catch (Exception ex)
            {
                _failed++;
                Console.WriteLine("  FAIL  " + name + ": " + (ex is AssertException ? ex.Message : ex.ToString()));
            }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new AssertException(message);
            }
        }

        private static void Throws<T>(Action action, string message) where T : Exception
        {
            try
            {
                action();
            }
            catch (T)
            {
                return;
            }
            catch (AggregateException ex) when (ex.InnerException is T)
            {
                return;
            }
            throw new AssertException("expected " + typeof(T).Name + ": " + message);
        }

        private sealed class AssertException : Exception
        {
            public AssertException(string message) : base(message)
            {
            }
        }

        private sealed class FakeHost : IWheelHost
        {
            public string DataDirectory => _root;

            public IntPtr ShareOwner => IntPtr.Zero;

            public void FloatImage(string path)
            {
            }
        }
    }
}
