using System;
using System.Collections.Generic;
using System.Linq;
using Tamarillo.Core;
using Tamarillo.Engines;

namespace Tamarillo.Operations
{
    /// <summary>One slot of the wheel: an action, or a group that opens a sub-ring.</summary>
    public sealed class MenuNode
    {
        public MenuNode(string id, string label, string title, string icon, Operation operation = null, IReadOnlyList<MenuNode> children = null, string badge = null)
        {
            Id = id;
            Label = label;
            Title = title;
            Icon = icon;
            Operation = operation;
            Children = children;
            Badge = badge;
        }

        public string Id { get; }

        /// <summary>Short label under the icon (≤ 12 chars).</summary>
        public string Label { get; }

        /// <summary>Full sentence shown in the hub while hovered ("Converter para PNG").</summary>
        public string Title { get; }

        /// <summary>Icon key in Theme/Icons.xaml.</summary>
        public string Icon { get; }

        /// <summary>Typographic glyph used instead of the icon (format names: "PNG").</summary>
        public string Badge { get; }

        public Operation Operation { get; }

        public IReadOnlyList<MenuNode> Children { get; }

        public bool IsGroup => Children != null && Children.Count > 0;
    }

    /// <summary>What the wheel needs from the module for UI-bound actions.</summary>
    public interface IWheelHost
    {
        string DataDirectory { get; }

        IntPtr ShareOwner { get; }

        void FloatImage(string path);
    }

    /// <summary>
    /// Builds the wheel for a file set. Rules: only actions that will work on this machine for every item
    /// are shown; at most 8 first-level slots (≈45° each, comfortable for a flick); formats the files already
    /// are in are left out; file actions live in "Mais" unless nothing else applies.
    /// </summary>
    public static class MenuBuilder
    {
        public const int MaxSlots = 8;
        public const int MaxSubItems = 8;

        public static IReadOnlyList<MenuNode> Build(FileSet files, Capabilities caps, IWheelHost host)
        {
            if (files == null || files.Count == 0)
            {
                return Array.Empty<MenuNode>();
            }
            var items = new List<MenuNode>();
            switch (files.Kind)
            {
                case FileKind.Image: AddImage(items, files, caps); break;
                case FileKind.Video: AddVideo(items, files, caps); break;
                case FileKind.Audio: AddAudio(items, files, caps); break;
                case FileKind.Pdf: AddPdf(items, files, caps, host); break;
                case FileKind.Document:
                case FileKind.Text:
                case FileKind.Spreadsheet:
                case FileKind.Presentation: AddOffice(items, files, caps, host); break;
                case FileKind.Subtitle: AddSubtitles(items, files); break;
                case FileKind.Data: AddData(items, files, caps, host); break;
                case FileKind.Archive: items.Add(Action("extract", "Extrair", "Extrair aqui", "Icon.Extract", new ExtractOperation())); break;
            }

            List<MenuNode> shell = ShellActions(files, caps, host);
            if (items.Count == 0)
            {
                // Folders, mixed or unknown files: the file actions are the wheel.
                return shell.Take(MaxSlots).ToList();
            }
            items.Add(Group("more", "Mais", "Mais ações", "Icon.More", shell));
            return items.Take(MaxSlots).ToList();
        }

        private static void AddImage(List<MenuNode> items, FileSet files, Capabilities caps)
        {
            var formats = new List<MenuNode>();
            foreach (string ext in new[] { "jpg", "png", "webp", "heic", "avif", "tiff", "bmp", "gif", "ico", "pdf" })
            {
                bool available = ext == "webp" ? caps.CanWebp : ext == "heic" ? caps.HeifEncoder : ext == "avif" ? caps.CanAvif : true;
                if (!available || files.AllExtensions(Aliases(ext)))
                {
                    continue;
                }
                Operation op = ext == "pdf" ? (Operation)new ImagesToPdfOperation() : new ImageConvertOperation(ext);
                if (ext == "pdf" && files.Count > 1)
                {
                    continue; // offered as "PDF" (merge) at the first level
                }
                formats.Add(Format(ext, op));
            }
            items.Add(Group("convert", "Converter", "Converter imagem", "Icon.Convert", formats));
            items.Add(Group("resize", "Tamanho", "Redimensionar", "Icon.Resize", new[]
            {
                Action("resize-50", "50%", "Reduzir para 50%", "Icon.Resize", new ImageResizeOperation(ResizeKind.Scale, 0.5, "50%"), "50%"),
                Action("resize-25", "25%", "Reduzir para 25%", "Icon.Resize", new ImageResizeOperation(ResizeKind.Scale, 0.25, "25%"), "25%"),
                Action("resize-3840", "4K", "Lado maior com 3840 px", "Icon.Resize", new ImageResizeOperation(ResizeKind.LongEdge, 3840, "3840 px"), "4K"),
                Action("resize-1920", "1920", "Lado maior com 1920 px", "Icon.Resize", new ImageResizeOperation(ResizeKind.LongEdge, 1920, "1920 px"), "1920"),
                Action("resize-1280", "1280", "Lado maior com 1280 px", "Icon.Resize", new ImageResizeOperation(ResizeKind.LongEdge, 1280, "1280 px"), "1280"),
                Action("resize-640", "640", "Lado maior com 640 px", "Icon.Resize", new ImageResizeOperation(ResizeKind.LongEdge, 640, "640 px"), "640"),
            }));
            items.Add(Action("compress", "Comprimir", "Comprimir em JPG", "Icon.Compress", new ImageCompressOperation()));
            items.Add(Group("rotate", "Girar", "Girar ou espelhar", "Icon.Rotate", new[]
            {
                Action("rotate-left", "Esquerda", "Girar 90° à esquerda", "Icon.RotateLeft", new ImageRotateOperation(RotateKind.Left)),
                Action("rotate-right", "Direita", "Girar 90° à direita", "Icon.RotateRight", new ImageRotateOperation(RotateKind.Right)),
                Action("rotate-180", "180°", "Girar 180°", "Icon.Rotate180", new ImageRotateOperation(RotateKind.Half)),
                Action("flip", "Espelhar", "Espelhar na horizontal", "Icon.Flip", new ImageRotateOperation(RotateKind.FlipHorizontal)),
            }));
            items.Add(Action("pdf", "PDF", files.Count > 1 ? $"Juntar {files.Count} imagens em um PDF" : "Criar PDF", "Icon.Pdf", new ImagesToPdfOperation()));
            if (caps.Ocr)
            {
                items.Add(Action("ocr", "Texto", "Reconhecer texto (OCR)", "Icon.Scan", new OcrOperation()));
            }
            items.Add(Action("strip", "Metadados", "Remover metadados (EXIF, GPS)", "Icon.Erase", new ImageStripMetadataOperation()));
        }

        private static void AddVideo(List<MenuNode> items, FileSet files, Capabilities caps)
        {
            var formats = new List<MenuNode>();
            foreach (string ext in new[] { "mp4", "mov", "mkv", "webm", "avi", "wmv", "gif" })
            {
                if (!files.AllSameExtension(ext) && MediaConvertOperation.CanProduce(ext, files, caps))
                {
                    formats.Add(Format(ext, new MediaConvertOperation(ext, "Convertendo para " + ext.ToUpperInvariant())));
                }
            }
            if (formats.Count > 0)
            {
                items.Add(Group("convert", "Converter", "Converter vídeo", "Icon.Convert", formats));
            }
            var audio = new List<MenuNode>();
            foreach (string ext in new[] { "mp3", "m4a", "wav", "flac", "opus" })
            {
                if (MediaConvertOperation.CanProduce(ext, files, caps))
                {
                    audio.Add(Format(ext, new MediaConvertOperation(ext, "Extraindo áudio em " + ext.ToUpperInvariant())));
                }
            }
            if (audio.Count > 0)
            {
                items.Add(Group("audio", "Áudio", "Extrair áudio", "Icon.Waveform", audio));
            }
            if (MediaConvertOperation.CanProduce("mp4", files, caps))
            {
                items.Add(Group("compress", "Comprimir", "Comprimir vídeo (MP4)", "Icon.Compress", new[]
                {
                    Action("compress-1080", "1080p", "Comprimir para 1080p", "Icon.Compress", new MediaConvertOperation("mp4", "Comprimindo para 1080p", 1080, " (1080p)"), "1080p"),
                    Action("compress-720", "720p", "Comprimir para 720p", "Icon.Compress", new MediaConvertOperation("mp4", "Comprimindo para 720p", 720, " (720p)"), "720p"),
                    Action("compress-480", "480p", "Comprimir para 480p", "Icon.Compress", new MediaConvertOperation("mp4", "Comprimindo para 480p", 480, " (480p)"), "480p"),
                }));
            }
        }

        private static void AddAudio(List<MenuNode> items, FileSet files, Capabilities caps)
        {
            var formats = new List<MenuNode>();
            foreach (string ext in new[] { "mp3", "m4a", "wav", "flac", "ogg", "opus", "aiff", "wma" })
            {
                if (!files.AllSameExtension(ext) && MediaConvertOperation.CanProduce(ext, files, caps))
                {
                    formats.Add(Format(ext, new MediaConvertOperation(ext, "Convertendo para " + ext.ToUpperInvariant())));
                }
            }
            if (formats.Count > 0)
            {
                items.Add(Group("convert", "Converter", "Converter áudio", "Icon.Convert", formats));
            }
            if (MediaConvertOperation.CanProduce("mp3", files, caps))
            {
                items.Add(Action("compress", "Comprimir", "Comprimir em MP3", "Icon.Compress", new MediaConvertOperation("mp3", "Comprimindo em MP3", 0, " (comprimido)")));
            }
        }

        private static void AddPdf(List<MenuNode> items, FileSet files, Capabilities caps, IWheelHost host)
        {
            items.Add(Group("images", "Imagens", "Exportar páginas como imagens", "Icon.Image", new[]
            {
                Format("png", new PdfToImagesOperation("png")),
                Format("jpg", new PdfToImagesOperation("jpg")),
            }));
            if (caps.Word)
            {
                items.Add(Action("docx", "Word", "Converter para Word (DOCX)", "Icon.Document", new OfficeConvertOperation("docx", host.DataDirectory)));
            }
            if (caps.Ocr)
            {
                items.Add(Action("ocr", "Texto", "Reconhecer texto (OCR)", "Icon.Scan", new OcrOperation()));
            }
        }

        private static void AddOffice(List<MenuNode> items, FileSet files, Capabilities caps, IWheelHost host)
        {
            string[] targets;
            switch (files.Kind)
            {
                case FileKind.Spreadsheet: targets = new[] { "xlsx", "ods", "csv" }; break;
                case FileKind.Presentation: targets = new[] { "pptx", "odp" }; break;
                case FileKind.Text: targets = new[] { "docx", "rtf", "html", "odt" }; break;
                default: targets = new[] { "docx", "odt", "rtf", "txt", "html" }; break;
            }
            bool office = caps.CanOfficeConvert(files.Kind);
            if (office)
            {
                items.Add(Action("pdf", "PDF", "Converter para PDF", "Icon.Pdf", new OfficeConvertOperation("pdf", host.DataDirectory)));
            }
            else if (files.Items.All(i => TextConvertOperation.Supports(i.Extension, "pdf")))
            {
                items.Add(Action("pdf", "PDF", "Converter para PDF", "Icon.Pdf", new TextConvertOperation("pdf")));
            }

            var formats = new List<MenuNode>();
            foreach (string ext in targets.Concat(new[] { "txt" }).Distinct())
            {
                if (files.AllSameExtension(ext))
                {
                    continue;
                }
                if (files.Items.All(i => TextConvertOperation.Supports(i.Extension, ext)))
                {
                    formats.Add(Format(ext, new TextConvertOperation(ext)));
                }
                else if (office && files.Kind != FileKind.Text && targets.Contains(ext))
                {
                    formats.Add(Format(ext, new OfficeConvertOperation(ext, host.DataDirectory)));
                }
            }
            if (formats.Count > 0)
            {
                items.Add(Group("convert", "Converter", "Converter documento", "Icon.Convert", formats));
            }
        }

        private static void AddSubtitles(List<MenuNode> items, FileSet files)
        {
            var formats = new[] { "srt", "vtt", "txt" }
                .Where(ext => !files.AllSameExtension(ext))
                .Select(ext => Format(ext, new TextConvertOperation(ext)))
                .ToList();
            items.Add(Group("convert", "Converter", "Converter legenda", "Icon.Convert", formats));
        }

        private static void AddData(List<MenuNode> items, FileSet files, Capabilities caps, IWheelHost host)
        {
            var formats = new List<MenuNode>();
            foreach (string ext in new[] { "csv", "tsv", "json" })
            {
                if (!files.AllSameExtension(ext) && files.Items.All(i => TextConvertOperation.Supports(i.Extension, ext)))
                {
                    formats.Add(Format(ext, new TextConvertOperation(ext)));
                }
            }
            if (files.AllExtensions("csv") && caps.CanOfficeConvert(FileKind.Spreadsheet))
            {
                formats.Add(Format("xlsx", new OfficeConvertOperation("xlsx", host.DataDirectory)));
            }
            if (formats.Count > 0)
            {
                items.Add(Group("convert", "Converter", "Converter dados", "Icon.Convert", formats));
            }
            if (files.AllExtensions("json"))
            {
                items.Add(Action("json-format", "Formatar", "Formatar JSON (indentado)", "Icon.Format", new TextConvertOperation("json-pretty", "Formatando JSON", " (formatado)")));
                items.Add(Action("json-minify", "Minificar", "Minificar JSON", "Icon.Minify", new TextConvertOperation("json-min", "Minificando JSON", " (min)")));
            }
        }

        private static List<MenuNode> ShellActions(FileSet files, Capabilities caps, IWheelHost host)
        {
            var actions = new List<MenuNode>();
            if (files.Count == 1 && files.Kind == FileKind.Image)
            {
                actions.Add(Action("float", "Fixar", "Fixar imagem na tela", "Icon.Pin",
                    new InstantOperation("Fixar na tela", set =>
                    {
                        host.FloatImage(set.Items[0].Path);
                        return null;
                    })));
                actions.Add(Action("copy-image", "Copiar", "Copiar imagem", "Icon.Clipboard", new CopyImageOperation()));
            }
            actions.Add(Action("zip", "ZIP", "Compactar em ZIP", "Icon.Zip", new ZipOperation()));
            actions.Add(Action("copy-path", "Caminho", files.Count == 1 ? "Copiar caminho" : "Copiar caminhos", "Icon.Link", new InstantOperation("Copiar caminho", ShellEngine.CopyPaths)));
            actions.Add(Action("reveal", "Mostrar", "Mostrar no Explorador", "Icon.Reveal",
                new InstantOperation("Mostrar no Explorador", set => ShellEngine.Reveal(set.Items.Select(i => i.Path)))));
            actions.Add(Action("terminal", "Terminal", "Abrir terminal aqui", "Icon.Terminal", new InstantOperation("Abrir terminal", ShellEngine.OpenTerminal)));
            actions.Add(Action("share", "Compartilhar", "Compartilhar", "Icon.Share", new InstantOperation("Compartilhar", set => ShellEngine.Share(set, host.ShareOwner))));
            actions.Add(Action("copy-name", "Nome", files.Count == 1 ? "Copiar nome" : "Copiar nomes", "Icon.Name", new InstantOperation("Copiar nome", ShellEngine.CopyNames)));
            if (!files.HasFolders)
            {
                actions.Add(Action("hash", "SHA-256", "Copiar SHA-256", "Icon.Hash", new HashOperation()));
            }
            if (files.HasFolders || files.Kind == FileKind.Other)
            {
                actions.Insert(1, Action("targz", "TAR.GZ", "Compactar em TAR.GZ", "Icon.Archive", new TarGzOperation()));
            }
            return actions.Take(MaxSubItems).ToList();
        }

        private static string[] Aliases(string ext)
        {
            switch (ext)
            {
                case "jpg": return new[] { "jpg", "jpeg", "jpe", "jfif" };
                case "tiff": return new[] { "tif", "tiff" };
                case "heic": return new[] { "heic", "heif" };
                default: return new[] { ext };
            }
        }

        private static MenuNode Action(string id, string label, string title, string icon, Operation operation, string badge = null) =>
            new MenuNode(id, label, title, icon, operation, badge: badge);

        private static MenuNode Group(string id, string label, string title, string icon, IEnumerable<MenuNode> children) =>
            new MenuNode(id, label, title, icon, children: children.Take(MaxSubItems).ToList());

        private static MenuNode Format(string ext, Operation operation) =>
            new MenuNode("to-" + ext, ext.ToUpperInvariant(), "Converter para " + ext.ToUpperInvariant(), "Icon.Convert", operation, badge: ext.ToUpperInvariant());
    }
}
