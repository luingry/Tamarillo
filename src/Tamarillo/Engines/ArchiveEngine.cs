using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Tamarillo.Core;
using Tamarillo.Operations;

namespace Tamarillo.Engines
{
    /// <summary>
    /// ZIP with System.IO.Compression (streamed, cancellable, byte-accurate progress); every other archive
    /// format through Windows' bundled bsdtar (tar.exe: tar, gz, bz2, xz, 7z and rar on recent builds).
    /// </summary>
    internal static class ArchiveEngine
    {
        /// <summary>Zips files and folders (recursively), named relative to their parent folder.</summary>
        public static void CreateZip(IReadOnlyList<FileItem> items, string output, Action<double> progress, CancellationToken token)
        {
            var entries = new List<KeyValuePair<string, string>>(); // entry name → file path ("" path = empty folder)
            foreach (FileItem item in items)
            {
                string root = item.Folder ?? string.Empty;
                if (item.IsDirectory)
                {
                    bool any = false;
                    foreach (string file in Directory.EnumerateFiles(item.Path, "*", SearchOption.AllDirectories))
                    {
                        entries.Add(new KeyValuePair<string, string>(Relative(root, file), file));
                        any = true;
                    }
                    foreach (string dir in Directory.EnumerateDirectories(item.Path, "*", SearchOption.AllDirectories))
                    {
                        if (!Directory.EnumerateFileSystemEntries(dir).Any())
                        {
                            entries.Add(new KeyValuePair<string, string>(Relative(root, dir) + "/", string.Empty));
                        }
                    }
                    if (!any)
                    {
                        entries.Add(new KeyValuePair<string, string>(Relative(root, item.Path) + "/", string.Empty));
                    }
                }
                else
                {
                    entries.Add(new KeyValuePair<string, string>(Relative(root, item.Path), item.Path));
                }
            }

            long total = Math.Max(1, entries.Where(e => e.Value.Length > 0).Sum(e => new FileInfo(e.Value).Length));
            long done = 0;
            var buffer = new byte[81920];
            using (var stream = new FileStream(output, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                foreach (KeyValuePair<string, string> entry in entries)
                {
                    token.ThrowIfCancellationRequested();
                    if (entry.Value.Length == 0)
                    {
                        zip.CreateEntry(entry.Key);
                        continue;
                    }
                    ZipArchiveEntry zipEntry = zip.CreateEntry(entry.Key, CompressionLevel.Optimal);
                    zipEntry.LastWriteTime = File.GetLastWriteTime(entry.Value);
                    using (var source = new FileStream(entry.Value, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (Stream target = zipEntry.Open())
                    {
                        int read;
                        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            token.ThrowIfCancellationRequested();
                            target.Write(buffer, 0, read);
                            done += read;
                            progress((double)done / total);
                        }
                    }
                }
            }
        }

        /// <summary>Extracts into <paramref name="folder"/> (created). Refuses entries that escape it (zip-slip).</summary>
        public static async Task ExtractAsync(FileItem archive, string folder, Capabilities caps, Action<double> progress, CancellationToken token)
        {
            Directory.CreateDirectory(folder);
            if (archive.Extension == "zip")
            {
                ExtractZip(archive.Path, folder, progress, token);
                return;
            }
            if (archive.Extension == "gz" && !archive.Path.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
            {
                // A single gzip'd file, not a tarball.
                string name = Path.GetFileNameWithoutExtension(archive.Path);
                using (var source = new FileStream(archive.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var gzip = new GZipStream(source, CompressionMode.Decompress))
                using (var target = new FileStream(Path.Combine(folder, name), FileMode.CreateNew))
                {
                    await gzip.CopyToAsync(target, 81920, token).ConfigureAwait(false);
                }
                return;
            }
            if (caps.Tar == null)
            {
                throw new OperationException("Este Windows não tem o tar.exe para abrir este formato.");
            }
            await RunTarAsync(caps.Tar, $"-xf \"{archive.Path}\" -C \"{folder}\"", token).ConfigureAwait(false);
            progress(1);
        }

        /// <summary>tar.gz of files and folders.</summary>
        public static Task CreateTarGzAsync(IReadOnlyList<FileItem> items, string output, Capabilities caps, CancellationToken token)
        {
            if (caps.Tar == null)
            {
                throw new OperationException("Este Windows não tem o tar.exe.");
            }
            string root = items[0].Folder;
            string names = string.Join(" ", items.Select(i => "\"" + Relative(root, i.Path) + "\""));
            return RunTarAsync(caps.Tar, $"-czf \"{output}\" -C \"{root}\" {names}", token);
        }

        private static void ExtractZip(string path, string folder, Action<double> progress, CancellationToken token)
        {
            string root = Path.GetFullPath(folder).TrimEnd('\\') + "\\";
            using (ZipArchive zip = ZipFile.OpenRead(path))
            {
                long total = Math.Max(1, zip.Entries.Sum(e => e.Length));
                long done = 0;
                foreach (ZipArchiveEntry entry in zip.Entries)
                {
                    token.ThrowIfCancellationRequested();
                    string target = Path.GetFullPath(Path.Combine(root, entry.FullName));
                    if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new OperationException("O arquivo compactado contém caminhos inválidos.");
                    }
                    if (entry.FullName.EndsWith("/", StringComparison.Ordinal) || entry.FullName.EndsWith("\\", StringComparison.Ordinal))
                    {
                        Directory.CreateDirectory(target);
                        continue;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    entry.ExtractToFile(target, overwrite: false);
                    done += entry.Length;
                    progress((double)done / total);
                }
            }
        }

        /// <summary>
        /// After extracting into a temp folder: if it holds a single item, that item is the result (no
        /// "project/project-main/..." double nesting); otherwise the folder itself is.
        /// </summary>
        public static string SingleChildOrSelf(string folder)
        {
            string[] children = Directory.GetFileSystemEntries(folder);
            return children.Length == 1 ? children[0] : folder;
        }

        private static async Task RunTarAsync(string tar, string args, CancellationToken token)
        {
            var info = new ProcessStartInfo(tar, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
            };
            using (Process process = Process.Start(info))
            {
                try
                {
                    process.PriorityClass = ProcessPriorityClass.BelowNormal;
                }
                catch
                {
                }
                Task<string> errors = process.StandardError.ReadToEndAsync();
                while (!process.HasExited)
                {
                    if (token.IsCancellationRequested)
                    {
                        try
                        {
                            process.Kill();
                        }
                        catch
                        {
                        }
                        token.ThrowIfCancellationRequested();
                    }
                    await Task.Delay(100).ConfigureAwait(false);
                }
                string detail = await errors.ConfigureAwait(false);
                if (process.ExitCode != 0)
                {
                    Diagnostics.Log?.Warn("tar failed: " + detail);
                    throw new OperationException("Este formato não pôde ser aberto neste Windows.");
                }
            }
        }

        private static string Relative(string root, string path)
        {
            string relative = string.IsNullOrEmpty(root) ? Path.GetFileName(path) : path.Substring(root.TrimEnd('\\').Length).TrimStart('\\');
            return relative.Replace('\\', '/');
        }
    }
}
