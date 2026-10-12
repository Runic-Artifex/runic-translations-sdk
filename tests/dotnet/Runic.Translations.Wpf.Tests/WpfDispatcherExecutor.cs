using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using TUnit.Core;
using TUnit.Core.Interfaces;

namespace Runic.Translations.Wpf.Tests;

/// <summary>
/// Runs every case on one STA thread that runs a WPF <see cref="Dispatcher"/>, as the former [STAThread] Main did:
/// a case starts as a dispatcher operation, and its Flush helpers push nested frames on that dispatcher. Unlike the
/// runic-sdk executor, no WPF Application is created: the former Main had none, and the construction case checks
/// that a thread without a dispatcher cannot create a TranslationSource, which Application.Current would satisfy.
/// The thread starts with the first case and its dispatcher shuts down after the last. An exception that reaches
/// the dispatcher fails the case it happened in.
/// </summary>
internal sealed class WpfDispatcherExecutor : ITestExecutor
{
    private static readonly Lock Gate = new();
    private static readonly List<Exception> Unhandled = [];
    private static Task<Dispatcher>? _started;
    private static Thread? _thread;

    public async ValueTask ExecuteTest(TestContext context, Func<ValueTask> action)
    {
        Dispatcher dispatcher = await StartAsync();
        if (dispatcher.HasShutdownStarted) throw new InvalidOperationException("The suite's WPF dispatcher has already shut down.");
        Exception? failure = null;
        Func<Task> run = async () => await action();
        try { await await dispatcher.InvokeAsync(run); }
        catch (Exception error) { failure = error; }
        InvalidOperationException? reached = TakeUnhandled();
        if (reached is null && failure is not null) ExceptionDispatchInfo.Throw(failure);
        if (reached is not null) throw failure is null ? reached : new AggregateException(failure, reached);
    }

    // After the last case: stop the dispatcher on its thread and wait for the thread to end.
    [After(HookType.Assembly)]
    public static async Task ShutDownAsync()
    {
        Task<Dispatcher>? started;
        lock (Gate) started = _started;
        if (started is null) return;
        Dispatcher dispatcher = await started;
        if (!dispatcher.HasShutdownStarted) dispatcher.InvokeShutdown();
        if (_thread is { } thread && !thread.Join(TimeSpan.FromSeconds(30)))
            throw new TimeoutException("The WPF dispatcher thread did not stop after InvokeShutdown.");
        if (TakeUnhandled() is { } reached) throw reached;
    }

    private static Task<Dispatcher> StartAsync()
    {
        lock (Gate)
        {
            if (_started is not null) return _started;
            var ready = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
            _thread = new Thread(() =>
            {
                try
                {
                    Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
                    dispatcher.UnhandledException += (_, e) =>
                    {
                        lock (Gate) Unhandled.Add(e.Exception);
                        e.Handled = true;
                    };
                    ready.TrySetResult(dispatcher);
                    Dispatcher.Run();
                }
                catch (Exception error) { ready.TrySetException(error); }
            })
            { IsBackground = true, Name = "WPF test dispatcher" };
            _thread.SetApartmentState(ApartmentState.STA);
            // The thread must not inherit the first case's context (TestContext.Current, its captured output):
            // each dispatcher operation carries the context of the case that queued it.
            using (ExecutionContext.SuppressFlow()) _thread.Start();
            return _started = ready.Task;
        }
    }

    private static InvalidOperationException? TakeUnhandled()
    {
        lock (Gate)
        {
            if (Unhandled.Count == 0) return null;
            var error = new InvalidOperationException(
                $"Exceptions reached the dispatcher: {string.Join(Environment.NewLine, Unhandled)}", Unhandled[0]);
            Unhandled.Clear();
            return error;
        }
    }
}
