using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Tamarillo.Core;
using Tamarillo.Engines;

namespace Tamarillo.Operations
{
    internal static class ImageIo
    {
        /// <summary>Decodes with WIC; formats Windows cannot read (AVIF without the extension, odd WebP) go through ffmpeg.</summary>
        public static ImageEngine.Decoded Load(string path, OperationContext context, int maxEdge = 0, double scale = 0)
        {
            try
            {
                return ImageEngine.Load(path, maxEdge, scale);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                if (!context.Capabilities.HasFfmpeg)
                {
                    throw new OperationException("O Windows não consegue abrir esta imagem. Instale a extensão do formato na Microsoft Store ou o ffmpeg.", ex);
                }
                string temp = Path.Combine(Path.GetTempPath(), "tamarillo-" + Guid.NewGuid().ToString("N") + ".png");
                try
                {
                    FfmpegEngine.RunAsync(context.Capabilities.Ffmpeg, path, temp, "-frames:v 1", null, context.Token).GetAwaiter().GetResult();
                    return ImageEngine.Load(temp, maxEdge, scale);
                }
                finally
                {
                    OutputPaths.Discard(temp);
                }
            }
        }

        /// <summary>Encodes with WIC when Windows can; WebP/AVIF without a WIC encoder go through ffmpeg (from a lossless PNG).</summary>
        public static void Save(BitmapSource bitmap, string output, string ext, OperationContext context, int? quality = null)
        {
            int q = quality ?? context.Settings.JpegQuality;
            bool wic = ext == "webp" ? context.Capabilities.WebpEncoder : ext == "heic" ? context.Capabilities.HeifEncoder : ImageEngine.IsWicTarget(ext);
            if (wic)
            {
                ImageEngine.Save(bitmap, output, ext, q);
                return;
            }
            string args = FfmpegEngine.ArgumentsFor(ext, context.Capabilities);
            if (args == null)
            {
                throw new OperationException("Não há um codificador disponível para " + ext.ToUpperInvariant() + ".");
            }
            string temp = Path.Combine(Path.GetTempPath(), "tamarillo-" + Guid.NewGuid().ToString("N") + ".png");
            try
            {
                ImageEngine.Save(bitmap, temp, "png");
                FfmpegEngine.RunAsync(context.Capabilities.Ffmpeg, temp, output, args, null, context.Token).GetAwaiter().GetResult();
            }
            finally
            {
                OutputPaths.Discard(temp);
            }
        }

        /// <summary>Same format as the source when it can be written back, else PNG (alpha) or JPEG.</summary>
        public static string SameFormat(FileItem input, Capabilities caps)
        {
            switch (input.Extension)
            {
                case "jpeg":
                case "jpe":
                case "jfif":
                    return "jpg";
                case "tif":
                    return "tiff";
                case "jpg":
                case "png":
                case "bmp":
                case "tiff":
                case "gif":
                case "ico":
                    return input.Extension;
                case "webp":
                    return caps.CanWebp ? "webp" : "png";
                case "heic":
                case "heif":
                    return caps.HeifEncoder ? "heic" : "jpg";
                default:
                    return "png";
            }
        }
    }

    internal sealed class ImageConvertOperation : PerFileOperation
    {
        private readonly string _target;

        public ImageConvertOperation(string target) => _target = target;

        public override string Title => "Convertendo para " + _target.ToUpperInvariant();

        protected override string TargetExtension(FileItem input) => _target;

        protected override Task TransformAsync(FileItem input, string output, OperationContext context, Action<double> progress) =>
            StaWorker.Run(() =>
            {
                ImageEngine.Decoded image = ImageIo.Load(input.Path, context);
                progress(0.5);
                context.Token.ThrowIfCancellationRequested();
                ImageIo.Save(image.Bitmap, output, _target, context);
            });
    }

    internal enum ResizeKind
    {
        Scale,
        LongEdge,
    }

    internal sealed class ImageResizeOperation : PerFileOperation
    {
        private readonly ResizeKind _mode;
        private readonly double _value;
        private readonly string _label;

        public ImageResizeOperation(ResizeKind mode, double value, string label)
        {
            _mode = mode;
            _value = value;
            _label = label;
        }

        public override string Title => "Redimensionando para " + _label;

        protected override string NameSuffix => " (" + _label + ")";

        protected override string TargetExtension(FileItem input) => ImageIo.SameFormat(input, Capabilities.Baseline);

        protected override Task TransformAsync(FileItem input, string output, OperationContext context, Action<double> progress) =>
            StaWorker.Run(() =>
            {
                ImageEngine.Decoded image = _mode == ResizeKind.LongEdge
                    ? ImageIo.Load(input.Path, context, maxEdge: (int)_value)
                    : ImageIo.Load(input.Path, context, scale: _value);
                progress(0.6);
                ImageIo.Save(image.Bitmap, output, ImageIo.SameFormat(input, context.Capabilities), context);
            });
    }

    internal sealed class ImageCompressOperation : PerFileOperation
    {
        public override string Title => "Comprimindo";

        protected override string NameSuffix => " (comprimida)";

        protected override string TargetExtension(FileItem input) => "jpg";

        public override async Task<OperationResult> RunAsync(OperationContext context)
        {
            OperationResult result = await base.RunAsync(context).ConfigureAwait(false);
            long before = context.Files.Items.Sum(i => new FileInfo(i.Path).Length);
            long after = result.Outputs.Sum(o => new FileInfo(o).Length);
            int saved = before > 0 ? (int)Math.Round(100.0 * (before - after) / before) : 0;
            return new OperationResult(result.Outputs, saved > 0 ? saved + "% menor" : null);
        }

        protected override Task TransformAsync(FileItem input, string output, OperationContext context, Action<double> progress) =>
            StaWorker.Run(() =>
            {
                // Photos rarely need more than 4K on the long edge; quality 78 with 4:2:0 is visually clean.
                ImageEngine.Decoded image = ImageIo.Load(input.Path, context, maxEdge: 3840);
                progress(0.5);
                ImageEngine.Save(image.Bitmap, output, "jpg", 78);
            });
    }

    internal enum RotateKind
    {
        Left,
        Right,
        Half,
        FlipHorizontal,
    }

    internal sealed class ImageRotateOperation : PerFileOperation
    {
        private readonly RotateKind _kind;

        public ImageRotateOperation(RotateKind kind) => _kind = kind;

        public override string Title => _kind == RotateKind.FlipHorizontal ? "Espelhando" : "Girando";

        protected override string NameSuffix => _kind == RotateKind.FlipHorizontal ? " (espelhada)" : " (girada)";

        protected override string TargetExtension(FileItem input) => ImageIo.SameFormat(input, Capabilities.Baseline);

        protected override Task TransformAsync(FileItem input, string output, OperationContext context, Action<double> progress) =>
            StaWorker.Run(() =>
            {
                BitmapSource bitmap = ImageIo.Load(input.Path, context).Bitmap;
                Transform transform = _kind == RotateKind.Left ? new RotateTransform(270)
                    : _kind == RotateKind.Right ? new RotateTransform(90)
                    : _kind == RotateKind.Half ? (Transform)new RotateTransform(180)
                    : new ScaleTransform(-1, 1);
                progress(0.5);
                ImageIo.Save(ImageEngine.Transform(bitmap, transform), output, ImageIo.SameFormat(input, context.Capabilities), context, 95);
            });
    }

    /// <summary>
    /// Removes EXIF/XMP/IPTC/comments. JPEG and PNG are rewritten segment by segment without re-encoding
    /// (lossless); a rotated JPEG gets the rotation baked in first so it still displays upright.
    /// </summary>
    internal sealed class ImageStripMetadataOperation : PerFileOperation
    {
        public override string Title => "Removendo metadados";

        protected override string NameSuffix => " (sem metadados)";

        protected override string TargetExtension(FileItem input) => ImageIo.SameFormat(input, Capabilities.Baseline);

        protected override Task TransformAsync(FileItem input, string output, OperationContext context, Action<double> progress) =>
            StaWorker.Run(() =>
            {
                string ext = ImageIo.SameFormat(input, context.Capabilities);
                if (ext == "jpg" && ReadOrientation(input.Path) == 1)
                {
                    MetadataStripper.StripJpeg(input.Path, output);
                }
                else if (ext == "png")
                {
                    MetadataStripper.StripPng(input.Path, output);
                }
                else
                {
                    ImageIo.Save(ImageIo.Load(input.Path, context).Bitmap, output, ext, context, 95);
                }
            });

        private static int ReadOrientation(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                BitmapDecoder decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                return ImageEngine.ReadOrientation(decoder.Frames[0]);
            }
        }
    }

    /// <summary>All images into one PDF, one page each, in the order they were dropped.</summary>
    internal sealed class ImagesToPdfOperation : AggregateOperation
    {
        public override string Title => "Criando PDF";

        protected override string TargetExtension => "pdf";

        protected override Task CreateAsync(FileSet files, string output, OperationContext context) =>
            StaWorker.Run(() =>
            {
                using (var stream = new FileStream(output, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var pdf = new PdfWriter(stream))
                {
                    for (int i = 0; i < files.Count; i++)
                    {
                        context.Token.ThrowIfCancellationRequested();
                        FileItem item = files.Items[i];
                        context.Report((double)i / files.Count, item.Name);
                        AddImage(pdf, item, context);
                    }
                }
            });

        private static void AddImage(PdfWriter pdf, FileItem item, OperationContext context)
        {
            ImageEngine.Decoded image = ImageIo.Load(item.Path, context);
            BitmapSource bitmap = image.Bitmap;
            byte[] jpeg;
            bool gray = bitmap.Format == PixelFormats.Gray8;
            if (image.IsJpeg && image.Orientation == 1 && (bitmap.Format == PixelFormats.Bgr24 || bitmap.Format == PixelFormats.Bgr32 || gray))
            {
                jpeg = File.ReadAllBytes(item.Path); // embed the original bytes: no generation loss
            }
            else
            {
                jpeg = ImageEngine.EncodeJpeg(bitmap, 92);
                gray = false;
            }
            // Pages share the A4 long edge so a mixed set prints and scrolls evenly; aspect is the image's.
            double longEdge = PdfWriter.A4Height;
            double ratio = (double)bitmap.PixelWidth / bitmap.PixelHeight;
            double width = ratio >= 1 ? longEdge : longEdge * ratio;
            double height = ratio >= 1 ? longEdge / ratio : longEdge;
            pdf.AddJpegPage(jpeg, bitmap.PixelWidth, bitmap.PixelHeight, gray, width, height);
        }
    }

    /// <summary>Recognizes text in images or PDFs: a .txt per file, and everything on the clipboard.</summary>
    internal sealed class OcrOperation : PerFileOperation
    {
        public override string Title => "Reconhecendo texto";

        protected override string TargetExtension(FileItem input) => "txt";

        public override async Task<OperationResult> RunAsync(OperationContext context)
        {
            OperationResult result = await base.RunAsync(context).ConfigureAwait(false);
            string text = string.Join(Environment.NewLine + Environment.NewLine, result.Outputs.Select(TextEngine.ReadText)).Trim();
            if (text.Length == 0)
            {
                return new OperationResult(result.Outputs, "Nenhum texto encontrado");
            }
            return new OperationResult(result.Outputs, "Texto copiado") { ClipboardText = text };
        }

        protected override async Task TransformAsync(FileItem input, string output, OperationContext context, Action<double> progress)
        {
            var engine = WindowsPdfEngine.CreateOcr();
            string text;
            if (input.Kind == FileKind.Pdf)
            {
                var document = await WindowsPdfEngine.OpenAsync(input.Path, context.Token).ConfigureAwait(false);
                var pages = new List<string>();
                foreach (uint page in WindowsPdfEngine.Pages(document))
                {
                    context.Token.ThrowIfCancellationRequested();
                    pages.Add(await WindowsPdfEngine.RecognizePageAsync(engine, document, page, context.Token).ConfigureAwait(false));
                    progress((page + 1.0) / document.PageCount);
                }
                text = string.Join("\r\n\f\r\n", pages);
            }
            else
            {
                text = await WindowsPdfEngine.RecognizeImageAsync(engine, input.Path, context.Token).ConfigureAwait(false);
            }
            TextEngine.WriteText(output, text);
        }
    }

    internal sealed class CopyImageOperation : Operation
    {
        public override string Title => "Copiando imagem";

        public override async Task<OperationResult> RunAsync(OperationContext context)
        {
            BitmapSource bitmap = await StaWorker.Run(() => ImageIo.Load(context.Files.Items[0].Path, context, maxEdge: 8192).Bitmap).ConfigureAwait(false);
            return new OperationResult(null, "Imagem copiada") { ClipboardImage = bitmap };
        }
    }
}
