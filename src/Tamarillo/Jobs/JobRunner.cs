using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Tamarillo.Core;
using Tamarillo.Engines;
using Tamarillo.Operations;

namespace Tamarillo.Jobs
{
    /// <summary>
    /// Runs chosen actions. At most two heavy jobs run at once (the rest queue), all off the UI thread at
    /// below-normal priority; progress reaches the UI at most once per frame. Instant actions run inline.
    /// </summary>
    public sealed class JobRunner
    {
        private readonly Dispatcher _dispatcher;
        private readonly Func<TamarilloSettings> _settings;
        private readonly Func<Capabilities> _capabilities;
        private readonly SemaphoreSlim _slots = new SemaphoreSlim(2);

        public JobRunner(Dispatcher dispatcher, Func<TamarilloSettings> settings, Func<Capabilities> capabilities)
        {
            _dispatcher = dispatcher;
            _settings = settings;
            _capabilities = capabilities;
        }

        /// <summary>A job was created (show it). UI thread.</summary>
        public event Action<Job> JobStarted;

        /// <summary>An instant action finished: message (or error) for a short confirmation. UI thread.</summary>
        public event Action<Job> InstantFinished;

        public void Run(MenuNode node, FileSet files)
        {
            if (node?.Operation == null || files == null || files.Count == 0)
            {
                return;
            }
            var job = new Job(node, files);
            if (node.Operation.IsInstant)
            {
                RunInstant(job);
                return;
            }
            JobStarted?.Invoke(job);
            _ = RunAsync(job);
        }

        private void RunInstant(Job job)
        {
            job.Start();
            try
            {
                var context = new OperationContext(job.Files, _settings(), _capabilities(), job.Token, null);
                OperationResult result = job.Node.Operation.RunAsync(context).GetAwaiter().GetResult();
                if (result.Message == null)
                {
                    return; // the action shows its own UI (Explorer, terminal, share sheet, float)
                }
                job.Complete(result);
            }
            catch (Exception ex)
            {
                Diagnostics.Log?.Error("Instant action failed: " + job.Node.Id, ex);
                job.Fail(UserMessage(ex));
            }
            InstantFinished?.Invoke(job);
        }

        private async Task RunAsync(Job job)
        {
            int pending = 0;
            double latest = -1;
            string current = null;
            void Report(double progress, string item)
            {
                Volatile.Write(ref latest, progress);
                Volatile.Write(ref current, item);
                if (Interlocked.Exchange(ref pending, 1) == 0)
                {
                    _dispatcher.BeginInvoke(new Action(() =>
                    {
                        Interlocked.Exchange(ref pending, 0);
                        job.Report(Volatile.Read(ref latest), Volatile.Read(ref current));
                    }), DispatcherPriority.Render);
                }
            }

            try
            {
                await _slots.WaitAsync(job.Token).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            try
            {
                job.Start();
                var context = new OperationContext(job.Files, _settings(), _capabilities(), job.Token, Report);
                OperationResult result = await Task.Run(() => job.Node.Operation.RunAsync(context), job.Token).ConfigureAwait(true);
                ApplyClipboard(result);
                job.Complete(result);
            }
            catch (OperationCanceledException)
            {
                job.Cancel();
            }
            catch (Exception ex)
            {
                if (job.Token.IsCancellationRequested)
                {
                    job.Cancel();
                }
                else
                {
                    Diagnostics.Log?.Error("Job failed: " + job.Node.Id, ex);
                    job.Fail(UserMessage(ex));
                }
            }
            finally
            {
                _slots.Release();
            }
        }

        private static void ApplyClipboard(OperationResult result)
        {
            try
            {
                if (result.ClipboardImage != null)
                {
                    ShellEngine.Retry(() => Clipboard.SetImage(result.ClipboardImage));
                }
                else if (!string.IsNullOrEmpty(result.ClipboardText))
                {
                    ShellEngine.SetClipboardText(result.ClipboardText);
                }
            }
            catch (Exception ex)
            {
                Diagnostics.Log?.Warn("Clipboard: " + ex.Message);
            }
        }

        /// <summary>Messages are written for people: known failures say what to do, the rest stay generic.</summary>
        public static string UserMessage(Exception ex)
        {
            Exception root = ex is AggregateException aggregate ? aggregate.Flatten().InnerException ?? ex : ex;
            switch (root)
            {
                case OperationException op:
                    return op.Message;
                case UnauthorizedAccessException _:
                    return "Sem permissão para gravar nesta pasta.";
                case System.IO.PathTooLongException _:
                    return "O caminho do arquivo é longo demais.";
                case System.IO.FileNotFoundException _:
                case System.IO.DirectoryNotFoundException _:
                    return "O arquivo não existe mais.";
                case System.IO.IOException io when (io.HResult & 0xFFFF) == 112:
                    return "Não há espaço suficiente no disco.";
                case System.IO.IOException _:
                    return "O arquivo está em uso por outro aplicativo.";
                case NotSupportedException _:
                    return "Este formato não é suportado neste computador.";
                default:
                    return "Algo deu errado. Os detalhes foram registrados no log.";
            }
        }
    }
}
