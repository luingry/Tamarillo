using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace Tamarillo.Core
{
    public enum OutputLocation
    {
        /// <summary>Next to the source file.</summary>
        SameFolder = 0,

        /// <summary>A "Tamarillo" folder next to the source file.</summary>
        Subfolder = 1,

        /// <summary>One fixed folder chosen by the user.</summary>
        Custom = 2,
    }

    [DataContract]
    public sealed class TamarilloSettings
    {
        public TamarilloSettings()
        {
            ApplyDefaults();
        }

        [DataMember] public bool Enabled { get; set; }

        [DataMember] public Shortcut Shortcut { get; set; }

        /// <summary>The chord without a drag opens the wheel for the files selected in Explorer.</summary>
        [DataMember] public bool SelectionTrigger { get; set; }

        [DataMember] public OutputLocation Output { get; set; }

        [DataMember] public string CustomFolder { get; set; }

        /// <summary>Finished jobs leave the overlay by themselves after a few seconds.</summary>
        [DataMember] public bool AutoDismiss { get; set; }

        [DataMember] public int JpegQuality { get; set; }

        /// <summary>Optional ffmpeg.exe override; empty means PATH and common install folders.</summary>
        [DataMember] public string FfmpegPath { get; set; }

        [DataMember] public double OverlayLeft { get; set; }

        [DataMember] public double OverlayTop { get; set; }

        [DataMember] public bool OverlayPlaced { get; set; }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context) => ApplyDefaults();

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context) => Normalize();

        private void ApplyDefaults()
        {
            Enabled = true;
            Shortcut = Shortcut.Default;
            SelectionTrigger = true;
            Output = OutputLocation.SameFolder;
            CustomFolder = null;
            AutoDismiss = false;
            JpegQuality = 90;
            FfmpegPath = null;
        }

        public void Normalize()
        {
            if (Shortcut == null || !Shortcut.IsValid)
            {
                Shortcut = Shortcut.Default;
            }
            if (!Enum.IsDefined(typeof(OutputLocation), Output))
            {
                Output = OutputLocation.SameFolder;
            }
            JpegQuality = Math.Max(40, Math.Min(100, JpegQuality == 0 ? 90 : JpegQuality));
        }

        public TamarilloSettings Clone()
        {
            var copy = (TamarilloSettings)MemberwiseClone();
            copy.Shortcut = Shortcut?.Clone();
            return copy;
        }
    }

    /// <summary>
    /// settings.json in the module data folder. Writes are atomic (temp file + replace) and keep the previous
    /// file as settings.json.bak, which is used if the main file is ever unreadable.
    /// </summary>
    public sealed class SettingsStore
    {
        private readonly string _path;
        private readonly object _gate = new object();
        private TamarilloSettings _current = new TamarilloSettings();

        public SettingsStore(string dataDirectory)
        {
            _path = Path.Combine(dataDirectory, "settings.json");
        }

        /// <summary>Raised on the thread that called <see cref="Update"/>.</summary>
        public event Action<TamarilloSettings> Changed;

        /// <summary>Snapshot; never mutate it, use <see cref="Update"/>.</summary>
        public TamarilloSettings Current
        {
            get
            {
                lock (_gate)
                {
                    return _current;
                }
            }
        }

        public void Load()
        {
            TamarilloSettings loaded = TryRead(_path) ?? TryRead(_path + ".bak") ?? new TamarilloSettings();
            loaded.Normalize();
            lock (_gate)
            {
                _current = loaded;
            }
        }

        public void Update(Action<TamarilloSettings> change)
        {
            TamarilloSettings next;
            lock (_gate)
            {
                next = _current.Clone();
                change(next);
                next.Normalize();
                _current = next;
            }
            // Disk work off the caller's (UI) thread; writes are serialized and the newest snapshot wins.
            System.Threading.Tasks.Task.Run(() => Save(next));
            Changed?.Invoke(next);
        }

        private readonly object _saveGate = new object();

        /// <summary>Synchronous save, for tests and shutdown.</summary>
        public void Flush() => Save(Current);

        private void Save(TamarilloSettings settings)
        {
            lock (_saveGate)
            {
                if (!ReferenceEquals(settings, Current))
                {
                    return; // a newer snapshot is queued behind us
                }
                SaveNow(settings);
            }
        }

        private void SaveNow(TamarilloSettings settings)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                string temp = _path + ".tmp";
                File.WriteAllText(temp, Serialize(settings), new UTF8Encoding(false));
                if (File.Exists(_path))
                {
                    File.Replace(temp, _path, _path + ".bak", ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(temp, _path);
                }
            }
            catch (Exception ex)
            {
                Diagnostics.Log?.Error("Could not save settings", ex);
            }
        }

        public static string Serialize(TamarilloSettings settings)
        {
            var serializer = new DataContractJsonSerializer(typeof(TamarilloSettings));
            using (var stream = new MemoryStream())
            {
                using (var writer = JsonReaderWriterFactory.CreateJsonWriter(stream, Encoding.UTF8, ownsStream: false, indent: true))
                {
                    serializer.WriteObject(writer, settings);
                }
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        public static TamarilloSettings Deserialize(string json)
        {
            var serializer = new DataContractJsonSerializer(typeof(TamarilloSettings));
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                return (TamarilloSettings)serializer.ReadObject(stream);
            }
        }

        private static TamarilloSettings TryRead(string path)
        {
            try
            {
                return File.Exists(path) ? Deserialize(File.ReadAllText(path, Encoding.UTF8)) : null;
            }
            catch (Exception ex)
            {
                Diagnostics.Log?.Warn("Ignoring unreadable " + Path.GetFileName(path) + ": " + ex.Message);
                return null;
            }
        }
    }
}
