using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Tamarillo.Core;
using Tamarillo.Engines;

namespace Tamarillo.Operations
{
    /// <summary>How a video is compressed; <see cref="None"/> is a plain conversion.</summary>
    public enum VideoProfile
    {
        None,

        /// <summary>Visually close to the source, at the smallest size that keeps it so.</summary>
        Quality,

        /// <summary>The smallest file, trading some visible quality.</summary>
        Efficiency,
    }

    /// <summary>
    /// Audio/video. Video prefers ffmpeg when present (it honours phone rotation and reads more codecs);
    /// otherwise, and for audio, Windows' Media Foundation, with ffmpeg as the fallback if MF refuses.
    /// </summary>
    internal sealed class MediaConvertOperation : PerFileOperation
    {
        private readonly string _target;
        private readonly int _maxHeight;
        private readonly string _title;
        private readonly string _suffix;
        private readonly VideoProfile _profile;

        public MediaConvertOperation(string target, string title, int maxHeight = 0, string suffix = "", VideoProfile profile = VideoProfile.None)
        {
            _target = target;
            _title = title;
            _maxHeight = maxHeight;
            _suffix = suffix;
            _profile = profile;
        }

        internal VideoProfile Profile => _profile;

        internal int MaxHeight => _maxHeight;

        public override string Title => _title;

        protected override string NameSuffix => _suffix;

        protected override string TargetExtension(FileItem input) => _target;

        public static bool CanProduce(string target, FileSet files, Capabilities caps) =>
            files.Items.All(i => (MediaFoundationEngine.CanRead(i.Extension) && MediaFoundationEngine.CanWrite(target)) || FfmpegEngine.ArgumentsFor(target, caps) != null && caps.HasFfmpeg);

        protected override async Task TransformAsync(FileItem input, string output, OperationContext context, Action<double> progress)
        {
            Capabilities caps = context.Capabilities;
            string ffmpegArgs = caps.HasFfmpeg ? FfmpegEngine.ArgumentsFor(_target, caps, _maxHeight, _profile) : null;
            bool mf = MediaFoundationEngine.CanRead(input.Extension) && MediaFoundationEngine.CanWrite(_target);
            bool preferFfmpeg = ffmpegArgs != null && (input.Kind == FileKind.Video || !mf);

            if (preferFfmpeg)
            {
                await FfmpegEngine.RunAsync(caps.Ffmpeg, input.Path, output, ffmpegArgs, progress, context.Token).ConfigureAwait(false);
                return;
            }
            try
            {
                await MediaFoundationEngine.TranscodeAsync(input.Path, output, _target, _maxHeight, _profile, progress, context.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException) && ffmpegArgs != null)
            {
                Diagnostics.Log?.Warn("Media Foundation failed, trying ffmpeg: " + ex.Message);
                OutputPaths.Discard(output);
                await FfmpegEngine.RunAsync(caps.Ffmpeg, input.Path, output, ffmpegArgs, progress, context.Token).ConfigureAwait(false);
            }
        }
    }

    /// <summary>PDF pages → images. One page gives "name.png"; more give a "name" folder of "name-01.png"…</summary>
    internal sealed class PdfToImagesOperation : Operation
    {
        private readonly string _ext;

        public PdfToImagesOperation(string ext) => _ext = ext;

        public override string Title => "Exportando páginas em " + _ext.ToUpperInvariant();

        public override async Task<OperationResult> RunAsync(OperationContext context)
        {
            var outputs = new List<string>();
            int files = context.Files.Count;
            for (int f = 0; f < files; f++)
            {
                FileItem pdf = context.Files.Items[f];
                var document = await WindowsPdfEngine.OpenAsync(pdf.Path, context.Token).ConfigureAwait(false);
                string folder = OutputPaths.FolderFor(pdf.Path, context.Settings);
                Directory.CreateDirectory(folder);
                string name = Path.GetFileNameWithoutExtension(pdf.Path);
                bool single = document.PageCount == 1;
                string planned = single ? OutputPaths.Unique(folder, name, null, _ext) : OutputPaths.Unique(folder, name, null, null);
                string temp = OutputPaths.TempFor(planned);
                try
                {
                    if (!single)
                    {
                        Directory.CreateDirectory(temp);
                    }
                    int digits = Math.Max(2, document.PageCount.ToString().Length);
                    foreach (uint page in WindowsPdfEngine.Pages(document))
                    {
                        context.Token.ThrowIfCancellationRequested();
                        context.Report((f + (double)page / document.PageCount) / files, $"{pdf.Name} · página {page + 1} de {document.PageCount}");
                        string target = single ? temp : Path.Combine(temp, $"{name}-{(page + 1).ToString().PadLeft(digits, '0')}.{_ext}");
                        await WindowsPdfEngine.RenderPageAsync(document, page, target, _ext, 300, context.Token).ConfigureAwait(false);
                    }
                    outputs.Add(OutputPaths.Commit(temp, planned));
                }
                catch
                {
                    OutputPaths.Discard(temp);
                    throw;
                }
            }
            return new OperationResult(outputs);
        }
    }

    internal sealed class OfficeConvertOperation : PerFileOperation
    {
        private readonly string _target;
        private readonly string _dataDirectory;

        public OfficeConvertOperation(string target, string dataDirectory)
        {
            _target = target;
            _dataDirectory = dataDirectory;
        }

        public override string Title => "Convertendo para " + _target.ToUpperInvariant();

        protected override string TargetExtension(FileItem input) => _target;

        protected override Task TransformAsync(FileItem input, string output, OperationContext context, Action<double> progress) =>
            OfficeEngine.ConvertAsync(context.Capabilities, _dataDirectory, input, output, _target, context.Token);
    }

    /// <summary>Text-family conversions done in-process (no Office): subtitles, CSV/TSV/JSON, TXT/MD/DOCX/RTF.</summary>
    internal sealed class TextConvertOperation : PerFileOperation
    {
        private readonly string _target;
        private readonly string _title;
        private readonly string _suffix;

        public TextConvertOperation(string target, string title = null, string suffix = "")
        {
            _target = target;
            _title = title;
            _suffix = suffix;
        }

        public override string Title => _title ?? "Convertendo para " + _target.ToUpperInvariant();

        protected override string NameSuffix => _suffix;

        protected override string TargetExtension(FileItem input) => _target == "json-min" || _target == "json-pretty" ? "json" : _target;

        /// <summary>True if the in-process engine handles <paramref name="from"/> → <paramref name="to"/>.</summary>
        public static bool Supports(string from, string to)
        {
            switch (from)
            {
                case "srt":
                case "vtt":
                    return to == "srt" || to == "vtt" || to == "txt";
                case "csv":
                case "tsv":
                    return to == "csv" || to == "tsv" || to == "json";
                case "json":
                    return to == "csv" || to == "tsv" || to == "json-min" || to == "json-pretty";
                case "txt":
                case "md":
                case "markdown":
                case "log":
                    return to == "docx" || to == "rtf" || to == "pdf";
                case "docx":
                case "rtf":
                    return to == "txt";
                default:
                    return false;
            }
        }

        protected override async Task TransformAsync(FileItem input, string output, OperationContext context, Action<double> progress)
        {
            string from = input.Extension;
            if (from == "rtf")
            {
                string plain = await StaWorker.Run(() => TextEngine.ReadRtf(input.Path)).ConfigureAwait(false);
                TextEngine.WriteText(output, plain);
                return;
            }
            await Task.Run(() =>
            {
                switch (from)
                {
                    case "srt":
                    case "vtt":
                        var cues = TextEngine.ParseSubtitles(TextEngine.ReadText(input.Path));
                        if (cues.Count == 0)
                        {
                            throw new OperationException("Nenhuma legenda encontrada neste arquivo.");
                        }
                        TextEngine.WriteText(output, TextEngine.WriteSubtitles(cues, _target));
                        break;
                    case "csv":
                    case "tsv":
                        string table = TextEngine.ReadText(input.Path);
                        var rows = TextEngine.ParseDelimited(table, TextEngine.DetectDelimiter(table, from));
                        TextEngine.WriteText(output, _target == "json" ? TextEngine.TableToJson(rows) : TextEngine.WriteDelimited(rows, _target == "tsv" ? '\t' : ','));
                        break;
                    case "json":
                        string json = TextEngine.ReadText(input.Path);
                        try
                        {
                            if (_target == "json-min" || _target == "json-pretty")
                            {
                                TextEngine.WriteText(output, Json.Write(Json.Parse(json), indent: _target == "json-pretty"));
                            }
                            else
                            {
                                TextEngine.WriteText(output, TextEngine.WriteDelimited(TextEngine.JsonToTable(json), _target == "tsv" ? '\t' : ','));
                            }
                        }
                        catch (FormatException ex)
                        {
                            throw new OperationException(ex.Message, ex);
                        }
                        break;
                    case "docx":
                        TextEngine.WriteText(output, TextEngine.ReadDocx(input.Path));
                        break;
                    default:
                        string text = TextEngine.ReadText(input.Path);
                        if (_target == "docx")
                        {
                            TextEngine.WriteDocx(output, text);
                        }
                        else if (_target == "rtf")
                        {
                            File.WriteAllText(output, TextEngine.WriteRtf(text), System.Text.Encoding.ASCII);
                        }
                        else
                        {
                            using (var stream = new FileStream(output, FileMode.Create, FileAccess.Write, FileShare.None))
                            using (var pdf = new PdfWriter(stream))
                            {
                                pdf.AddText(text, progress);
                            }
                        }
                        break;
                }
            }, context.Token).ConfigureAwait(false);
        }
    }

    internal sealed class ZipOperation : AggregateOperation
    {
        public override string Title => "Compactando";

        protected override string TargetExtension => "zip";

        protected override Task CreateAsync(FileSet files, string output, OperationContext context) =>
            Task.Run(() => ArchiveEngine.CreateZip(files.Items, output, p => context.Report(p), context.Token), context.Token);
    }

    internal sealed class TarGzOperation : AggregateOperation
    {
        public override string Title => "Compactando em TAR.GZ";

        protected override string TargetExtension => "tar.gz";

        protected override Task CreateAsync(FileSet files, string output, OperationContext context) =>
            ArchiveEngine.CreateTarGzAsync(files.Items, output, context.Capabilities, context.Token);
    }

    /// <summary>Extracts each archive next to itself, without double nesting.</summary>
    internal sealed class ExtractOperation : Operation
    {
        public override string Title => "Extraindo";

        public override async Task<OperationResult> RunAsync(OperationContext context)
        {
            var outputs = new List<string>();
            for (int i = 0; i < context.Files.Count; i++)
            {
                FileItem archive = context.Files.Items[i];
                int index = i;
                string folder = OutputPaths.FolderFor(archive.Path, context.Settings);
                Directory.CreateDirectory(folder);
                string name = archive.Name;
                foreach (string ext in new[] { ".tar.gz", ".tar.bz2", ".tar.xz" })
                {
                    if (name.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                    {
                        name = name.Substring(0, name.Length - ext.Length);
                    }
                }
                name = Path.GetFileNameWithoutExtension(name);
                string work = OutputPaths.TempFor(Path.Combine(folder, name));
                try
                {
                    context.Report((double)i / context.Files.Count, archive.Name);
                    await ArchiveEngine.ExtractAsync(archive, work, context.Capabilities, p => context.Report((index + p) / context.Files.Count, archive.Name), context.Token).ConfigureAwait(false);
                    string single = ArchiveEngine.SingleChildOrSelf(work);
                    if (single != work)
                    {
                        string planned = Path.Combine(folder, Path.GetFileName(single));
                        outputs.Add(OutputPaths.Commit(single, planned));
                    }
                    else
                    {
                        outputs.Add(OutputPaths.Commit(work, Path.Combine(folder, name)));
                    }
                }
                finally
                {
                    OutputPaths.Discard(work);
                }
            }
            return new OperationResult(outputs);
        }
    }

    internal sealed class HashOperation : Operation
    {
        public override string Title => "Calculando SHA-256";

        public override Task<OperationResult> RunAsync(OperationContext context) => Task.Run(() =>
        {
            var lines = new List<string>();
            long total = Math.Max(1, context.Files.Items.Sum(i => new FileInfo(i.Path).Length));
            long done = 0;
            var buffer = new byte[1 << 20];
            foreach (FileItem item in context.Files.Items)
            {
                using (var sha = SHA256.Create())
                using (var stream = new FileStream(item.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16, FileOptions.SequentialScan))
                {
                    int read;
                    while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        context.Token.ThrowIfCancellationRequested();
                        sha.TransformBlock(buffer, 0, read, null, 0);
                        done += read;
                        context.Report((double)done / total, item.Name);
                    }
                    sha.TransformFinalBlock(buffer, 0, 0);
                    string hex = BitConverter.ToString(sha.Hash).Replace("-", string.Empty).ToLowerInvariant();
                    lines.Add(context.Files.Count == 1 ? hex : hex + "  " + item.Name);
                }
            }
            return new OperationResult(null, "SHA-256 copiado") { ClipboardText = string.Join(Environment.NewLine, lines) };
        }, context.Token);
    }
}
