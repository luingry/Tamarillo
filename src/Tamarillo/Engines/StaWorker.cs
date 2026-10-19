using System;
using System.Threading;
using System.Threading.Tasks;

namespace Tamarillo.Engines
{
    /// <summary>
    /// Runs a piece of work on its own short-lived STA thread at below-normal priority. Used for WPF imaging,
    /// FlowDocument and Office automation, which want an STA and must never run on the shared UI thread.
    /// The thread ends with the work, so nothing (Dispatcher, COM apartment) outlives a job.
    /// </summary>
    internal static class StaWorker
    {
        public static Task<T> Run<T>(Func<T> work, string name = "Tamarillo.Worker")
        {
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                try
                {
                    completion.SetResult(work());
                }
                catch (Exception ex)
                {
                    completion.SetException(ex);
                }
                finally
                {
                    // WPF creates a Dispatcher lazily for imaging objects; shut it down so the thread can end clean.
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown();
                }
            })
            {
                IsBackground = true,
                Name = name,
                Priority = ThreadPriority.BelowNormal,
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return completion.Task;
        }

        public static Task Run(Action work, string name = "Tamarillo.Worker") => Run(() =>
        {
            work();
            return true;
        }, name);
    }
}
