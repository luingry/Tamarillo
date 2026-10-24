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

    /// <summary>One entry of the settings catalog; <see cref="Key"/> is what <see cref="Core.TamarilloSettings.HiddenItems"/> stores.</summary>
    public sealed class MenuEntry
    {
        public MenuEntry(string key, string title, string badge, IReadOnlyList<MenuEntry> children)
        {
            Key = key;
            Title = title;
            Badge = badge;
            Children = children;
        }

        public string Key { get; }

        public string Title { get; }

        public string Badge { get; }

        public IReadOnlyList<MenuEntry> Children { get; }
    }

    /// <summary>The wheel of one file kind (or the shared file actions), as listed in the settings.</summary>
    public sealed class MenuSection
    {
        public MenuSection(string key, string title, IReadOnlyList<MenuEntry> entries)
        {
            Key = key;
            Title = title;
            Entries = entries;
        }

        public string Key { get; }

        public string Title { get; }

        public IReadOnlyList<MenuEntry> Entries { get; }

        /// <summary>What a ComboBox shows (the host template ignores DisplayMemberPath in the selection box).</summary>
        public override string ToString() => Title;
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

        /// <summary>Key prefix of the file actions ("Mais"), shared by every kind.</summary>
        public const string FilesSection = "files";

        /// <summary>
        /// The wheel for <paramref name="files"/>. <paramref name="hidden"/> holds keys the user turned off
        /// (see <see cref="Catalog"/>); they are removed before the slot limits apply, so they free room.
        /// </summary>
        public static IReadOnlyList<MenuNode> Build(FileSet files, Capabilities caps, IWheelHost host, ICollection<string> hidden = null)
        {
            if (files == null || files.Count == 0)
            {
                return Array.Empty<MenuNode>();
            }
            IReadOnlyList<MenuNode> all = BuildAll(files, caps, host, out string section);
            return Prune(all, section, hidden, MaxSlots);
        }

        /// <summary>Key of the section a file set's own actions live in ("image"); <see cref="FilesSection"/> when only file actions apply.</summary>
        public static string SectionOf(FileKind kind)
        {
            switch (kind)
            {
                case FileKind.Image: return "image";
                case FileKind.Video: return "video";
                case FileKind.Audio: return "audio";
                case FileKind.Pdf: return "pdf";
                case FileKind.Document: return "document";
                case FileKind.Text: return "text";
                case FileKind.Spreadsheet: return "spreadsheet";
                case FileKind.Presentation: return "presentation";
                case FileKind.Subtitle: return "subtitle";
                case FileKind.Data: return "data";
                case FileKind.Archive: return "archive";
                default: return FilesSection;
            }
        }

        /// <summary>Key of a child of the node with key <paramref name="parentKey"/>; "Mais" holds the shared file actions.</summary>
        private static string ChildPrefix(string parentKey, MenuNode parent) => parent.Id == "more" ? FilesSection : parentKey;

        /// <summary>Drops hidden entries and groups left empty, then applies the slot limits.</summary>
        private static IReadOnlyList<MenuNode> Prune(IReadOnlyList<MenuNode> nodes, string prefix, ICollection<string> hidden, int max)
        {
            var kept = new List<MenuNode>();
            foreach (MenuNode node in nodes)
            {
                string key = prefix + "/" + node.Id;
                if (hidden != null && hidden.Contains(key))
                {
                    continue;
                }
                if (node.IsGroup)
                {
                    IReadOnlyList<MenuNode> children = Prune(node.Children, ChildPrefix(key, node), hidden, MaxSubItems);
                    if (children.Count == 0)
                    {
                        continue;
                    }
                    kept.Add(new MenuNode(node.Id, node.Label, node.Title, node.Icon, node.Operation, children, node.Badge));
                }
                else
                {
                    kept.Add(node);
                }
                if (kept.Count == max)
                {
                    break;
                }
            }
            return kept;
        }

        // ═══ Settings catalog ═══

        /// <summary>
        /// Every entry the wheel can ever show, by section, for the settings page: built as if every optional
        /// engine were present, from sample sets that together cover the per-format and per-count variants.
        /// </summary>
        public static IReadOnlyList<MenuSection> Catalog(IWheelHost host)
        {
            var sections = new List<MenuSection>
            {
                Section(host, "Imagens", @"a.jpg", @"a.png", @"a.gif"),
                Section(host, "Vídeos", @"a.mp4", @"a.mov"),
                Section(host, "Áudios", @"a.mp3", @"a.wav"),
                Section(host, "PDFs", @"a.pdf"),
                Section(host, "Documentos", @"a.docx", @"a.odt"),
                Section(host, "Textos", @"a.txt", @"a.md"),
                Section(host, "Planilhas", @"a.xlsx", @"a.ods"),
                Section(host, "Apresentações", @"a.pptx", @"a.odp"),
                Section(host, "Legendas", @"a.srt", @"a.vtt"),
                Section(host, "Dados", @"a.csv", @"a.json"),
                Section(host, "Compactados", @"a.zip"),
            };
            List<MenuEntry> files = null;
            foreach (FileSet sample in new[] { FileSet.Virtual(@"C:\c\a.jpg"), FileSet.Virtual(@"C:\c\a.bin"), FileSet.Virtual(@"C:\c\d\") })
            {
                files = Merge(files, Entries(ShellActions(sample, Capabilities.Everything, host), FilesSection));
            }
            sections.Add(new MenuSection(FilesSection, "Ações de arquivo (Mais)", files));
            return sections;
        }

        private static MenuSection Section(IWheelHost host, string title, params string[] samples)
        {
            List<MenuEntry> entries = null;
            string key = null;
            foreach (string sample in samples)
            {
                IReadOnlyList<MenuNode> nodes = BuildAll(FileSet.Virtual(@"C:\c\" + sample), Capabilities.Everything, host, out key);
                entries = Merge(entries, Entries(nodes, key));
            }
            return new MenuSection(key, title, entries);
        }

        private static List<MenuEntry> Entries(IEnumerable<MenuNode> nodes, string prefix) =>
            nodes.Select(n =>
            {
                string key = prefix + "/" + n.Id;
                // "Mais" is one switch here; its contents are the shared file-actions section.
                List<MenuEntry> children = n.IsGroup && n.Id != "more" ? Entries(n.Children, ChildPrefix(key, n)) : new List<MenuEntry>();
                return new MenuEntry(key, n.Title, n.Badge, children);
            }).ToList();

        /// <summary>Union of two variants of one menu; an entry only <paramref name="b"/> has lands after its predecessor in <paramref name="b"/>.</summary>
        private static List<MenuEntry> Merge(List<MenuEntry> a, List<MenuEntry> b)
        {
            if (a == null)
            {
                return b;
            }
            var result = new List<MenuEntry>(a);
            for (int i = 0; i < b.Count; i++)
            {
                int existing = result.FindIndex(e => e.Key == b[i].Key);
                if (existing >= 0)
                {
                    if (b[i].Children.Count > 0)
                    {
                        result[existing] = new MenuEntry(result[existing].Key, result[existing].Title, result[existing].Badge, Merge(result[existing].Children.ToList(), b[i].Children.ToList()));
                    }
                    continue;
                }
                int after = i == 0 ? -1 : result.FindIndex(e => e.Key == b[i - 1].Key);
                result.Insert(after + 1, b[i]);
            }
            return result;
        }

        /// <summary>Everything that applies to <paramref name="files"/>, before hiding and slot limits.</summary>
        private static IReadOnlyList<MenuNode> BuildAll(FileSet files, Capabilities caps, IWheelHost host, out string section)
        {
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
                section = FilesSection;
                return shell;
            }
            section = SectionOf(files.Kind);
            items.Add(Group("more", "Mais", "Mais ações", "Icon.More", shell));
            return items;
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
            if (files.AllExtensions("gif") && MediaConvertOperation.CanProduce("mp4", files, caps))
            {
                // Animated GIFs play better (and weigh far less) as video; ffmpeg reads them, MF does not.
                // Near the front so the sub-ring cap never drops it; after JPG so the settings catalog keeps its order.
                formats.Insert(Math.Min(1, formats.Count), Format("mp4", new MediaConvertOperation("mp4", "Convertendo para MP4")));
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
                    Group("quality", "Qualidade", "Preservar qualidade", "Icon.Quality", Resolutions(VideoProfile.Quality)),
                    Group("efficiency", "Eficiência", "Máxima eficiência", "Icon.Efficiency", Resolutions(VideoProfile.Efficiency)),
                }));
            }
        }

        /// <summary>The third ring of "Comprimir": one profile at the source resolution or shrunk to a height (never upscaled).</summary>
        private static IEnumerable<MenuNode> Resolutions(VideoProfile profile)
        {
            bool quality = profile == VideoProfile.Quality;
            // Hub titles stay short (two lines in the hub); job titles can say more.
            string name = quality ? "Qualidade" : "Eficiência";
            string how = quality ? "com qualidade" : "ao máximo";
            string tag = quality ? string.Empty : " leve";
            yield return Action("original", "Original", name + " · resolução original", "Icon.Compress",
                new MediaConvertOperation("mp4", $"Comprimindo {how}", 0, quality ? " (comprimido)" : " (leve)", profile), "Original");
            foreach (int height in new[] { 1080, 720, 480 })
            {
                yield return Action("res-" + height, height + "p", $"{name} · {height}p", "Icon.Compress",
                    new MediaConvertOperation("mp4", $"Comprimindo {how} em {height}p", height, $" ({height}p{tag})", profile), height + "p");
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
            return actions;
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
            new MenuNode(id, label, title, icon, children: children.ToList());

        private static MenuNode Format(string ext, Operation operation) =>
            new MenuNode("to-" + ext, ext.ToUpperInvariant(), "Converter para " + ext.ToUpperInvariant(), "Icon.Convert", operation, badge: ext.ToUpperInvariant());
    }
}
