namespace OutWit.Database.Native;

/// <summary>
/// Dedicated CLR worker: managed database code must not run on the UCO/reverse-PInvoke thread.
/// </summary>
internal static class WitDbClrThread
{
    private const int Idle = 0;
    private const int Running = 1;
    private const int Done = 2;
    private const int Failed = 3;

    private static readonly Lock s_gate = new();
    private static readonly AutoResetEvent s_signal = new(false);
    private static Thread? s_worker;
    private static Action? s_work;
    private static Exception? s_error;
    private static int s_state = Idle;

    public static void EnsureStarted()
    {
        lock (s_gate)
        {
            if (s_worker is { IsAlive: true })
            {
                return;
            }

            s_worker = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = "witdb-clr-worker",
            };
            s_worker.Start();
            WitDbNativeTrace.Write("worker: started");
        }
    }

    public static T Run<T>(Func<T> work)
    {
        EnsureStarted();
        WaitUntilNotRunning();

        T? result = default;
        s_error = null;
        s_work = () => result = work();
        Volatile.Write(ref s_state, Running);
        s_signal.Set();
        SpinUntilComplete();

        if (Volatile.Read(ref s_state) == Failed)
        {
            Volatile.Write(ref s_state, Idle);
            throw s_error ?? new InvalidOperationException("witdb worker failed");
        }

        Volatile.Write(ref s_state, Idle);
        return result!;
    }

    private static void WorkerLoop()
    {
        while (true)
        {
            s_signal.WaitOne();
            WitDbNativeTrace.Write("worker: dispatch");
            try
            {
                s_work?.Invoke();
                Volatile.Write(ref s_state, Done);
            }
            catch (Exception ex)
            {
                s_error = ex;
                Volatile.Write(ref s_state, Failed);
                WitDbNativeTrace.Write($"worker: error {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    private static void WaitUntilNotRunning()
    {
        var spinner = new SpinWait();
        while (Volatile.Read(ref s_state) == Running)
        {
            spinner.SpinOnce();
        }
    }

    private static void SpinUntilComplete()
    {
        var spinner = new SpinWait();
        while (Volatile.Read(ref s_state) == Running)
        {
            spinner.SpinOnce();
        }
    }
}
