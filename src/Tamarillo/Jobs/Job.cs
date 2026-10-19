using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Tamarillo.Core;
using Tamarillo.Operations;

namespace Tamarillo.Jobs
{
    public enum JobState
    {
        Queued,
        Running,
        Done,
        Failed,
        Cancelled,
    }

    /// <summary>One run of an operation, as the overlay shows it. UI-thread only.</summary>
    public sealed class Job : INotifyPropertyChanged
    {
        private readonly CancellationTokenSource _cancel = new CancellationTokenSource();
        private JobState _state = JobState.Queued;
        private string _title;
        private string _detail;
        private double _progress = -1;

        public Job(MenuNode node, FileSet files)
        {
            Node = node;
            Files = files;
            _title = node.Operation.Title;
            _detail = files.Summary;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public MenuNode Node { get; }

        public FileSet Files { get; }

        public CancellationToken Token => _cancel.Token;

        public IReadOnlyList<string> Outputs { get; private set; } = Array.Empty<string>();

        public JobState State
        {
            get => _state;
            private set => Set(ref _state, value);
        }

        public string Title
        {
            get => _title;
            private set => Set(ref _title, value);
        }

        public string Detail
        {
            get => _detail;
            private set => Set(ref _detail, value);
        }

        /// <summary>0..1, or -1 while the amount of work is unknown.</summary>
        public double Progress
        {
            get => _progress;
            private set => Set(ref _progress, value);
        }

        /// <summary>The file the title opens when clicked (single output), or null.</summary>
        public string PrimaryOutput => Outputs.Count == 1 ? Outputs[0] : null;

        public void Start() => State = JobState.Running;

        public void Report(double progress, string current)
        {
            if (_state != JobState.Running)
            {
                return;
            }
            Progress = progress;
            if (!string.IsNullOrEmpty(current))
            {
                Detail = Files.Count > 1 && Files.Items.Any(i => i.Name == current)
                    ? $"{current} · {IndexOf(current) + 1} de {Files.Count}"
                    : current;
            }
        }

        public void Complete(OperationResult result)
        {
            Outputs = result.Outputs;
            Progress = 1;
            if (Outputs.Count == 1)
            {
                Title = Path.GetFileName(Outputs[0].TrimEnd('\\'));
                Detail = Path.GetDirectoryName(Outputs[0].TrimEnd('\\'));
            }
            else if (Outputs.Count > 1)
            {
                Title = Outputs.Count + " arquivos criados";
                string folder = Path.GetDirectoryName(Outputs[0].TrimEnd('\\'));
                Detail = Outputs.All(o => string.Equals(Path.GetDirectoryName(o.TrimEnd('\\')), folder, StringComparison.OrdinalIgnoreCase)) ? folder : "Em várias pastas";
            }
            else
            {
                Title = result.Message ?? "Pronto";
                Detail = Files.Summary;
            }
            if (Outputs.Count > 0 && !string.IsNullOrEmpty(result.Message))
            {
                Title += " · " + result.Message;
            }
            State = JobState.Done;
        }

        public void Fail(string message)
        {
            Title = "Não foi possível concluir";
            Detail = message;
            State = JobState.Failed;
        }

        public void Cancel()
        {
            if (_state == JobState.Running || _state == JobState.Queued)
            {
                _cancel.Cancel();
                State = JobState.Cancelled;
            }
        }

        private int IndexOf(string name)
        {
            for (int i = 0; i < Files.Count; i++)
            {
                if (Files.Items[i].Name == name)
                {
                    return i;
                }
            }
            return 0;
        }

        private void Set<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (!EqualityComparer<T>.Default.Equals(field, value))
            {
                field = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            }
        }
    }
}
