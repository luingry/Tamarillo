using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Tamarillo.Core
{
    public enum FileKind
    {
        Other,
        Folder,
        Image,
        Video,
        Audio,
        Pdf,
        Document,
        Text,
        Spreadsheet,
        Presentation,
        Subtitle,
        Data,
        Archive,
    }

    public sealed class FileItem
    {
        public FileItem(string path, bool isDirectory)
        {
            Path = path;
            IsDirectory = isDirectory;
            Extension = isDirectory ? string.Empty : System.IO.Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
            Kind = isDirectory ? FileKind.Folder : FileKinds.Of(Extension);
        }

        public string Path { get; }

        public bool IsDirectory { get; }

        /// <summary>Lower-case, without the dot ("jpg"); empty for folders.</summary>
        public string Extension { get; }

        public FileKind Kind { get; }

        public string Name => System.IO.Path.GetFileName(Path.TrimEnd('\\', '/'));

        public string Folder => System.IO.Path.GetDirectoryName(Path.TrimEnd('\\', '/'));
    }

    /// <summary>The files a wheel was opened for. Immutable.</summary>
    public sealed class FileSet
    {
        private FileSet(IReadOnlyList<FileItem> items)
        {
            Items = items;
            Kind = items.Count > 0 && items.All(i => i.Kind == items[0].Kind) ? items[0].Kind : FileKind.Other;
        }

        public IReadOnlyList<FileItem> Items { get; }

        public int Count => Items.Count;

        /// <summary>The kind shared by every item, or <see cref="FileKind.Other"/> for mixed sets.</summary>
        public FileKind Kind { get; }

        public bool HasFolders => Items.Any(i => i.IsDirectory);

        public bool AllExtensions(params string[] extensions) => Items.All(i => extensions.Contains(i.Extension));

        public bool AllSameExtension(string extension) => Items.All(i => i.Extension == extension);

        /// <summary>Builds a set from dropped or selected paths, dropping anything that no longer exists.</summary>
        public static FileSet FromPaths(IEnumerable<string> paths)
        {
            var items = new List<FileItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in paths ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(raw) || !seen.Add(raw))
                {
                    continue;
                }
                if (Directory.Exists(raw))
                {
                    items.Add(new FileItem(raw, isDirectory: true));
                }
                else if (File.Exists(raw))
                {
                    items.Add(new FileItem(raw, isDirectory: false));
                }
            }
            return new FileSet(items);
        }

        /// <summary>For tests: classifies paths without touching the disk.</summary>
        public static FileSet Virtual(params string[] paths) =>
            new FileSet(paths.Select(p => new FileItem(p, p.EndsWith("\\", StringComparison.Ordinal))).ToList());

        /// <summary>"3 imagens", "foto.jpg", "2 itens".</summary>
        public string Summary
        {
            get
            {
                if (Count == 1)
                {
                    return Items[0].Name;
                }
                return Count + " " + FileKinds.PluralNoun(Kind);
            }
        }
    }

    public static class FileKinds
    {
        private static readonly Dictionary<string, FileKind> Map = Build();

        public static FileKind Of(string extension) =>
            extension != null && Map.TryGetValue(extension, out FileKind kind) ? kind : FileKind.Other;

        public static string PluralNoun(FileKind kind)
        {
            switch (kind)
            {
                case FileKind.Image: return "imagens";
                case FileKind.Video: return "vídeos";
                case FileKind.Audio: return "áudios";
                case FileKind.Pdf: return "PDFs";
                case FileKind.Document: return "documentos";
                case FileKind.Text: return "textos";
                case FileKind.Spreadsheet: return "planilhas";
                case FileKind.Presentation: return "apresentações";
                case FileKind.Subtitle: return "legendas";
                case FileKind.Data: return "arquivos de dados";
                case FileKind.Archive: return "arquivos compactados";
                case FileKind.Folder: return "pastas";
                default: return "itens";
            }
        }

        /// <summary>Icon key (Theme/Icons.xaml) for a kind.</summary>
        public static string Icon(FileKind kind)
        {
            switch (kind)
            {
                case FileKind.Image: return "Icon.Image";
                case FileKind.Video: return "Icon.Video";
                case FileKind.Audio: return "Icon.Audio";
                case FileKind.Pdf: return "Icon.Pdf";
                case FileKind.Document:
                case FileKind.Text: return "Icon.Document";
                case FileKind.Spreadsheet: return "Icon.Table";
                case FileKind.Presentation: return "Icon.Slides";
                case FileKind.Subtitle: return "Icon.Subtitles";
                case FileKind.Data: return "Icon.Code";
                case FileKind.Archive: return "Icon.Archive";
                case FileKind.Folder: return "Icon.Folder";
                default: return "Icon.Files";
            }
        }

        private static Dictionary<string, FileKind> Build()
        {
            var map = new Dictionary<string, FileKind>(StringComparer.OrdinalIgnoreCase);
            void Add(FileKind kind, string list)
            {
                foreach (string ext in list.Split(' '))
                {
                    map[ext] = kind;
                }
            }
            Add(FileKind.Image, "jpg jpeg jpe jfif png bmp dib gif tif tiff webp heic heif avif ico jxr wdp");
            Add(FileKind.Video, "mp4 m4v mov mkv webm avi wmv mpg mpeg ts mts m2ts 3gp flv");
            Add(FileKind.Audio, "mp3 m4a aac wav wma flac ogg oga opus aiff aif amr");
            Add(FileKind.Pdf, "pdf");
            Add(FileKind.Document, "doc docx odt rtf");
            Add(FileKind.Text, "txt md markdown log");
            Add(FileKind.Spreadsheet, "xls xlsx ods");
            Add(FileKind.Presentation, "ppt pptx odp");
            Add(FileKind.Subtitle, "srt vtt");
            Add(FileKind.Data, "csv tsv json");
            Add(FileKind.Archive, "zip tar gz tgz bz2 tbz2 xz txz 7z rar");
            return map;
        }
    }
}
