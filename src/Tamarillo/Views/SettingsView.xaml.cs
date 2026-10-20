using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Tamarillo.Core;
using Tamarillo.Engines;

namespace Tamarillo.Views
{
    /// <summary>The "Configurações" tab: shortcut, Explorer selection, output folder, JPG quality, optional engines.</summary>
    public partial class SettingsView
    {
        private readonly TamarilloController _controller;
        private bool _ready;
        private bool _recording;
        private Shortcut _seen;

        public SettingsView(TamarilloController controller)
        {
            _controller = controller;
            Resources.MergedDictionaries.Add(Ui.Theme.Resources);
            InitializeComponent();
            Load(controller.Settings.Current);
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
            _ready = wasReady;
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
