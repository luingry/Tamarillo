using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Tamarillo.Core;
using Tamarillo.Engines;

namespace Tamarillo.Operations
{
    /// <summary>What a finished operation produced.</summary>
    public sealed class OperationResult
    {
        public OperationResult(IReadOnlyList<string> outputs, string message = null)
        {
            Outputs = outputs ?? Array.Empty<string>();
            Message = message;
        }

        /// <summary>Files or folders created (may be empty for clipboard/shell actions).</summary>
        public IReadOnlyList<string> Outputs { get; }

        /// <summary>Optional one-line note shown instead of the output name ("Caminho copiado").</summary>
        public string Message { get; }

        /// <summary>Text put on the clipboard (on the UI thread) when the job completes.</summary>
        public string ClipboardText { get; set; }

        /// <summary>Frozen image put on the clipboard when the job completes.</summary>
        public System.Windows.Media.Imaging.BitmapSource ClipboardImage { get; set; }

        public static OperationResult Note(string message) => new OperationResult(null, message);
    }

    /// <summary>Recoverable failure with a message written for the user (pt-BR).</summary>
    public sealed class OperationException : Exception
    {
        public OperationException(string userMessage, Exception inner = null) : base(userMessage, inner)
        {
        }
    }

    public sealed class OperationContext
    {
        private readonly Action<double, string> _report;

        public OperationContext(FileSet files, TamarilloSettings settings, Capabilities capabilities, CancellationToken token, Action<double, string> report)
        {
            Files = files;
            Settings = settings;
            Capabilities = capabilities;
            Token = token;
            _report = report;
        }

        public FileSet Files { get; }

        public TamarilloSettings Settings { get; }

        public Capabilities Capabilities { get; }

        public CancellationToken Token { get; }

        /// <summary>Overall progress 0..1 plus the item being worked on. Cheap; throttled by the job.</summary>
        public void Report(double fraction, string current = null) => _report?.Invoke(Math.Max(0, Math.Min(1, fraction)), current);
    }

    /// <summary>
    /// One thing the wheel can do. Instances are stateless and reusable; everything about a run lives in
    /// <see cref="OperationContext"/>.
    /// </summary>
    public abstract class Operation
    {
        /// <summary>Progress title, e.g. "Convertendo para PNG".</summary>
        public abstract string Title { get; }

        /// <summary>
        /// Instant operations (clipboard, shell) run on the UI thread, never show a progress card and report
        /// with a short confirmation chip instead.
        /// </summary>
        public virtual bool IsInstant => false;

        public abstract Task<OperationResult> RunAsync(OperationContext context);
    }

    /// <summary>Converts every input into one output next to it (or in the configured folder).</summary>
    public abstract class PerFileOperation : Operation
    {
        /// <summary>Output extension for an input, without the dot.</summary>
        protected abstract string TargetExtension(FileItem input);

        /// <summary>Suffix appended to the output name ("" keeps the source name: foto.heic → foto.jpg).</summary>
        protected virtual string NameSuffix => string.Empty;

        /// <summary>Writes <paramref name="output"/> (a temp path). <paramref name="progress"/> takes 0..1 for this file.</summary>
        protected abstract Task TransformAsync(FileItem input, string output, OperationContext context, Action<double> progress);

        public override async Task<OperationResult> RunAsync(OperationContext context)
        {
            var outputs = new List<string>();
            int count = context.Files.Count;
            for (int i = 0; i < count; i++)
            {
                context.Token.ThrowIfCancellationRequested();
                FileItem input = context.Files.Items[i];
                int index = i;
                context.Report((double)i / count, input.Name);

                string folder = OutputPaths.FolderFor(input.Path, context.Settings);
                Directory.CreateDirectory(folder);
                string planned = OutputPaths.Unique(folder, Path.GetFileNameWithoutExtension(input.Path), NameSuffix, TargetExtension(input));
                string temp = OutputPaths.TempFor(planned);
                try
                {
                    await TransformAsync(input, temp, context, p => context.Report((index + p) / count, input.Name)).ConfigureAwait(false);
                    context.Token.ThrowIfCancellationRequested();
                    if (!File.Exists(temp) && !Directory.Exists(temp))
                    {
                        throw new OperationException("A conversão terminou sem gerar o arquivo.");
                    }
                    outputs.Add(OutputPaths.Commit(temp, planned));
                }
                catch
                {
                    OutputPaths.Discard(temp);
                    throw;
                }
            }
            context.Report(1);
            return new OperationResult(outputs);
        }
    }

    /// <summary>Turns all inputs into a single output (merge, archive).</summary>
    public abstract class AggregateOperation : Operation
    {
        protected abstract string TargetExtension { get; }

        /// <summary>Base name of the single output; defaults to the first item's name, or the folder name for many.</summary>
        protected virtual string OutputName(FileSet files)
        {
            if (files.Count == 1)
            {
                FileItem only = files.Items[0];
                return only.IsDirectory ? only.Name : Path.GetFileNameWithoutExtension(only.Path);
            }
            string parent = Path.GetFileName(files.Items[0].Folder ?? string.Empty);
            return string.IsNullOrEmpty(parent) ? "Tamarillo" : parent;
        }

        protected abstract Task CreateAsync(FileSet files, string output, OperationContext context);

        public override async Task<OperationResult> RunAsync(OperationContext context)
        {
            string folder = OutputPaths.FolderFor(context.Files.Items[0].Path, context.Settings);
            Directory.CreateDirectory(folder);
            string planned = OutputPaths.Unique(folder, OutputName(context.Files), null, TargetExtension);
            string temp = OutputPaths.TempFor(planned);
            try
            {
                await CreateAsync(context.Files, temp, context).ConfigureAwait(false);
                context.Token.ThrowIfCancellationRequested();
                string final = OutputPaths.Commit(temp, planned);
                context.Report(1);
                return new OperationResult(new[] { final });
            }
            catch
            {
                OutputPaths.Discard(temp);
                throw;
            }
        }
    }

    /// <summary>Synchronous UI-thread action (clipboard, shell). Throwing reports an error chip.</summary>
    public sealed class InstantOperation : Operation
    {
        private readonly string _title;
        private readonly Func<FileSet, string> _run;

        public InstantOperation(string title, Func<FileSet, string> run)
        {
            _title = title;
            _run = run;
        }

        public override string Title => _title;

        public override bool IsInstant => true;

        public override Task<OperationResult> RunAsync(OperationContext context) => Task.FromResult(OperationResult.Note(_run(context.Files)));
    }
}
