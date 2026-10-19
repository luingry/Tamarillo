using System;
using System.IO;

namespace Tamarillo.Core
{
    /// <summary>
    /// Where results go and what they are called. Sources are never overwritten: a name that is taken
    /// becomes "name (2).ext", like Explorer does. Work happens in a hidden temp file in the destination
    /// folder that is renamed into place only when it is complete, so a cancelled or failed job leaves nothing
    /// half-written behind.
    /// </summary>
    public static class OutputPaths
    {
        public const string SubfolderName = "Tamarillo";

        public static string FolderFor(string source, TamarilloSettings settings)
        {
            string sourceFolder = Path.GetDirectoryName(source.TrimEnd('\\', '/'));
            switch (settings.Output)
            {
                case OutputLocation.Subfolder:
                    return Path.Combine(sourceFolder, SubfolderName);
                case OutputLocation.Custom when !string.IsNullOrWhiteSpace(settings.CustomFolder):
                    return settings.CustomFolder;
                default:
                    return sourceFolder;
            }
        }

        /// <summary>
        /// Final path for "<paramref name="baseName"/><paramref name="suffix"/>.<paramref name="extension"/>" in
        /// <paramref name="folder"/>, numbered if taken. <paramref name="extension"/> may be empty (folders).
        /// </summary>
        public static string Unique(string folder, string baseName, string suffix, string extension)
        {
            string ext = string.IsNullOrEmpty(extension) ? string.Empty : "." + extension.TrimStart('.');
            string stem = Sanitize(baseName + (suffix ?? string.Empty));
            string candidate = Path.Combine(folder, stem + ext);
            for (int n = 2; Exists(candidate); n++)
            {
                candidate = Path.Combine(folder, $"{stem} ({n}){ext}");
            }
            return candidate;
        }

        /// <summary>Hidden work file next to where <paramref name="finalPath"/> will go; same extension, so tools that pick a format from it still do.</summary>
        public static string TempFor(string finalPath)
        {
            string folder = Path.GetDirectoryName(finalPath);
            string ext = Path.GetExtension(finalPath);
            return Path.Combine(folder, "~tamarillo-" + Guid.NewGuid().ToString("N").Substring(0, 10) + ext);
        }

        /// <summary>Moves a finished temp file or folder into place, picking a fresh name if the planned one got taken meanwhile.</summary>
        public static string Commit(string tempPath, string plannedPath)
        {
            string folder = Path.GetDirectoryName(plannedPath);
            string target = plannedPath;
            if (Exists(target))
            {
                string ext = Path.GetExtension(plannedPath);
                target = Unique(folder, Path.GetFileNameWithoutExtension(plannedPath), null, ext);
            }
            if (Directory.Exists(tempPath))
            {
                Directory.Move(tempPath, target);
            }
            else
            {
                File.SetAttributes(tempPath, FileAttributes.Normal);
                File.Move(tempPath, target);
            }
            return target;
        }

        public static void Discard(string tempPath)
        {
            try
            {
                if (Directory.Exists(tempPath))
                {
                    Directory.Delete(tempPath, recursive: true);
                }
                else if (File.Exists(tempPath))
                {
                    File.SetAttributes(tempPath, FileAttributes.Normal);
                    File.Delete(tempPath);
                }
            }
            catch (Exception ex)
            {
                Diagnostics.Log?.Warn("Could not remove temp " + tempPath + ": " + ex.Message);
            }
        }

        private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

        private static string Sanitize(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name.Trim();
        }
    }
}
