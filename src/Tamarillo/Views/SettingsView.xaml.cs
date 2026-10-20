using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Tamarillo.Core;
using Tamarillo.Engines;
using Tamarillo.Operations;

namespace Tamarillo.Views
{
    /// <summary>The "Configurações" tab: shortcut, Explorer selection, output folder, JPG quality, optional engines, wheel entries, version.</summary>
    public partial class SettingsView
    {
        private readonly TamarilloController _controller;
        private bool _ready;
        private bool _recording;
        private Shortcut _seen;
        private readonly List<CheckBox> _entryBoxes = new List<CheckBox>();

        public SettingsView(TamarilloController controller)
        {
            _controller = controller;
            Resources.MergedDictionaries.Add(Ui.Theme.Resources);
            InitializeComponent();
            VersionText.Text = InstalledVersion();
            MenuSections.ItemsSource = MenuBuilder.Catalog(controller);
            Load(controller.Settings.Current);
            MenuSections.SelectedIndex = 0;
            controller.StatusChanged += () => Dispatcher.BeginInvoke(new Action(RefreshEngines));
            controller.Settings.Changed += s => Dispatcher.BeginInvoke(new Action(() => Load(s)));
            RefreshEngines();
            _ready = true;
        }

        private void Load(TamarilloSettings s)
        {
            bool wasReady = _ready;
            _ready = false;
            if (!_recording)
            {
                Keys.Shortcut = s.Shortcut;
            }
            ResetShortcut.Visibility = s.Shortcut.SameAs(Shortcut.Default) ? Visibility.Collapsed : Visibility.Visible;
            SelectionTrigger.IsChecked = s.SelectionTrigger;
            SameFolder.IsChecked = s.Output == OutputLocation.SameFolder;
            Subfolder.IsChecked = s.Output == OutputLocation.Subfolder;
            CustomFolder.IsChecked = s.Output == OutputLocation.Custom;
            CustomRow.Visibility = s.Output == OutputLocation.Custom ? Visibility.Visible : Visibility.Collapsed;
            CustomPath.Text = string.IsNullOrEmpty(s.CustomFolder) ? "Nenhuma pasta escolhida" : s.CustomFolder;
            Quality.Value = s.JpegQuality;
            QualityValue.Text = s.JpegQuality.ToString();
            FfmpegAuto.Visibility = string.IsNullOrEmpty(s.FfmpegPath) ? Visibility.Collapsed : Visibility.Visible;
            ShowAll.Visibility = s.HiddenItems.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            RefreshEntries(s);
            _ready = wasReady;
        }

        private static string InstalledVersion()
        {
            string version = typeof(SettingsView).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? typeof(SettingsView).Assembly.GetName().Version.ToString(3);
            int metadata = version.IndexOf('+');
            return metadata < 0 ? version : version.Substring(0, metadata);
        }

        // ═══ Wheel entries ═══

        private void OnMenuSectionChanged(object sender, SelectionChangedEventArgs e)
        {
            MenuEntries.Children.Clear();
            _entryBoxes.Clear();
            if (MenuSections.SelectedItem is MenuSection section)
            {
                AddEntries(MenuEntries, section.Entries);
            }
            bool wasReady = _ready;
            _ready = false;
            RefreshEntries(_controller.Settings.Current);
            _ready = wasReady;
        }

        private void AddEntries(Panel panel, IReadOnlyList<MenuEntry> entries)
        {
            foreach (MenuEntry entry in entries)
            {
                var box = new CheckBox { Content = entry.Title, Tag = entry.Key, Margin = new Thickness(0, 4, 0, 4) };
                box.Checked += OnEntryToggled;
                box.Unchecked += OnEntryToggled;
                panel.Children.Add(box);
                _entryBoxes.Add(box);
                if (entry.Children.Count > 0)
                {
                    // Indented under the group's box (18 box + 10 gap); off with the group.
                    var children = new StackPanel { Margin = new Thickness(28, 0, 0, 6) };
                    AddEntries(children, entry.Children);
                    panel.Children.Add(children);
                    box.Tag = new GroupTag(entry.Key, children);
                }
            }
        }

        private void RefreshEntries(TamarilloSettings s)
        {
            var hidden = new HashSet<string>(s.HiddenItems, StringComparer.Ordinal);
            foreach (CheckBox box in _entryBoxes)
            {
                bool shown = !hidden.Contains(KeyOf(box));
                box.IsChecked = shown;
                if (box.Tag is GroupTag group)
                {
                    group.Children.IsEnabled = shown;
                }
            }
        }

        private void OnEntryToggled(object sender, RoutedEventArgs e)
        {
            var box = (CheckBox)sender;
            if (box.Tag is GroupTag group)
            {
                group.Children.IsEnabled = box.IsChecked == true;
            }
            if (!_ready)
            {
                return;
            }
            string key = KeyOf(box);
            bool show = box.IsChecked == true;
            _controller.Settings.Update(s => s.HiddenItems = show
                ? s.HiddenItems.Where(k => k != key).ToArray()
                : s.HiddenItems.Concat(new[] { key }).ToArray());
        }

        private void OnShowAll(object sender, RoutedEventArgs e) => _controller.Settings.Update(s => s.HiddenItems = Array.Empty<string>());

        private static string KeyOf(CheckBox box) => box.Tag is GroupTag group ? group.Key : (string)box.Tag;

        private sealed class GroupTag
        {
            public GroupTag(string key, Panel children)
            {
                Key = key;
                Children = children;
            }

            public string Key { get; }

            public Panel Children { get; }
        }

        private void RefreshEngines()
        {
            Capabilities caps = _controller.Capabilities;
            string configured = _controller.Settings.Current.FfmpegPath;
            FfmpegText.Text = _controller.IsDetecting ? "Verificando…"
                : caps.HasFfmpeg ? (string.IsNullOrEmpty(configured) ? "Encontrado: " : "Usando: ") + caps.Ffmpeg
                : "Não encontrado. Com ele: MKV, WebM, GIF, MOV, OGG, Opus e AVIF.";
            OfficeText.Text = caps.Word || caps.Excel || caps.PowerPoint
                ? "Microsoft Office: " + string.Join(", ", new[] { caps.Word ? "Word" : null, caps.Excel ? "Excel" : null, caps.PowerPoint ? "PowerPoint" : null }).Replace(", ,", ",").Trim(',', ' ')
                : caps.LibreOffice != null ? "LibreOffice encontrado." : "Nem Microsoft Office nem LibreOffice encontrados.";
            Redetect.IsEnabled = !_controller.IsDetecting;
        }

        // ═══ Shortcut recorder ═══

        private void OnRecord(object sender, RoutedEventArgs e)
        {
            if (_recording)
            {
                StopRecording(commit: false);
                return;
            }
            _recording = true;
            _seen = new Shortcut();
            Keys.Visibility = Visibility.Hidden;
            RecordingText.Visibility = Visibility.Visible;
            ShortcutHint.Text = "Pressione a combinação. Esc cancela.";
            Recorder.Focus();
            Keyboard.Focus(Recorder);
        }

        private void OnRecorderKeyDown(object sender, KeyEventArgs e)
        {
            if (!_recording)
            {
                return;
            }
            e.Handled = true;
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Escape)
            {
                StopRecording(commit: false);
                return;
            }
            Shortcut now = CurrentModifiers();
            _seen.Ctrl |= now.Ctrl;
            _seen.Alt |= now.Alt;
            _seen.Shift |= now.Shift;
            _seen.Win |= now.Win;
            int vk = KeyInterop.VirtualKeyFromKey(key);
            if (!Shortcut.IsModifierKey(vk) && vk != 0)
            {
                now.Key = vk;
                Commit(now);
                return;
            }
            Keys.Shortcut = _seen;
            Keys.Visibility = Visibility.Visible;
            RecordingText.Visibility = Visibility.Collapsed;
        }

        private void OnRecorderKeyUp(object sender, KeyEventArgs e)
        {
            if (!_recording)
            {
                return;
            }
            e.Handled = true;
            // Modifiers-only chord: decided when everything is released.
            if (CurrentModifiers().ModifierCount == 0 && _seen.ModifierCount > 0)
            {
                Commit(_seen);
            }
        }

        private void Commit(Shortcut candidate)
        {
            if (candidate.IsValid)
            {
                _controller.Settings.Update(s => s.Shortcut = candidate.Clone());
                StopRecording(commit: true);
            }
            else
            {
                _seen = new Shortcut();
                Keys.Visibility = Visibility.Hidden;
                RecordingText.Visibility = Visibility.Visible;
                ShortcutHint.Text = "Use pelo menos duas teclas modificadoras (Ctrl, Alt, Shift, Win), ou uma modificadora e outra tecla.";
            }
        }

        private void StopRecording(bool commit)
        {
            _recording = false;
            Keys.Visibility = Visibility.Visible;
            RecordingText.Visibility = Visibility.Collapsed;
            ShortcutHint.Text = "Segure enquanto arrasta arquivos. Clique nas teclas para trocar.";
            Keys.Shortcut = _controller.Settings.Current.Shortcut;
        }

        private void OnRecorderLostFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (_recording)
            {
                StopRecording(commit: false);
            }
        }

        private static Shortcut CurrentModifiers() => new Shortcut
        {
            Ctrl = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl),
            Alt = Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt),
            Shift = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift),
            Win = Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin),
        };

        private void OnResetShortcut(object sender, RoutedEventArgs e) => _controller.Settings.Update(s => s.Shortcut = Shortcut.Default);

        // ═══ Other settings ═══

        private void OnSelectionTrigger(object sender, RoutedEventArgs e)
        {
            if (_ready)
            {
                _controller.Settings.Update(s => s.SelectionTrigger = SelectionTrigger.IsChecked == true);
            }
        }

        private void OnOutputChanged(object sender, RoutedEventArgs e)
        {
            if (!_ready)
            {
                return;
            }
            OutputLocation output = Subfolder.IsChecked == true ? OutputLocation.Subfolder : CustomFolder.IsChecked == true ? OutputLocation.Custom : OutputLocation.SameFolder;
            if (output == OutputLocation.Custom && string.IsNullOrEmpty(_controller.Settings.Current.CustomFolder))
            {
                string folder = FolderPicker.Pick(OwnerHandle(), null);
                if (folder == null)
                {
                    Load(_controller.Settings.Current); // nothing chosen: back to the previous option
                    return;
                }
                _controller.Settings.Update(s =>
                {
                    s.CustomFolder = folder;
                    s.Output = OutputLocation.Custom;
                });
                return;
            }
            _controller.Settings.Update(s => s.Output = output);
        }

        private void OnPickFolder(object sender, RoutedEventArgs e)
        {
            string folder = FolderPicker.Pick(OwnerHandle(), _controller.Settings.Current.CustomFolder);
            if (folder != null)
            {
                _controller.Settings.Update(s =>
                {
                    s.CustomFolder = folder;
                    s.Output = OutputLocation.Custom;
                });
            }
        }

        private void OnQualityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            int value = (int)Math.Round(e.NewValue);
            if (QualityValue != null)
            {
                QualityValue.Text = value.ToString();
            }
            if (_ready && value != _controller.Settings.Current.JpegQuality)
            {
                _controller.Settings.Update(s => s.JpegQuality = value);
            }
        }

        private void OnFfmpegBrowse(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Localizar o ffmpeg.exe",
                Filter = "FFmpeg|ffmpeg.exe|Programas|*.exe",
                CheckFileExists = true,
            };
            if (dialog.ShowDialog(Window.GetWindow(this)) == true)
            {
                _controller.Settings.Update(s => s.FfmpegPath = dialog.FileName);
            }
        }

        private void OnFfmpegAuto(object sender, RoutedEventArgs e) => _controller.Settings.Update(s => s.FfmpegPath = null);

        private void OnRedetect(object sender, RoutedEventArgs e) => _ = _controller.RedetectAsync();

        private IntPtr OwnerHandle()
        {
            Window window = Window.GetWindow(this);
            return window == null ? IntPtr.Zero : new WindowInteropHelper(window).Handle;
        }
    }
}
