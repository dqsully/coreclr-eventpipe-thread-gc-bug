using System.Diagnostics;
using System.Diagnostics.Tracing;

namespace eventpipe_leak;

class ClrEventListener : EventListener
{
    public EventSource? EventSource;

    public int EventCount;

    public ClrEventListener(bool enabled)
        : base()
    {

        if (enabled)
        {
            if (EventSource == null) {
                throw new Exception("couldn't find CLR event source");
            }

            Console.WriteLine("Enabling CLR EventPipe\n");
            EnableEvents(EventSource, EventLevel.Informational, (EventKeywords) 0x8000); // 0x8000 means watch exceptions
        }
    }

    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        if (eventSource.Name.Equals("Microsoft-Windows-DotNETRuntime"))
        {
            EventSource = eventSource;
        }
    }

    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        Interlocked.Increment(ref EventCount);
        base.OnEventWritten(eventData);
    }
}

class Program
{
    private static int threadCount;

    private static long lastUtime;
    private static TimeSpan lastMeasurement;
    private static Stopwatch stopwatch = new Stopwatch();

    private static int exceptions = 0;

    static void CollectUsageLinux(bool print)
    {
        // Assuming sysconf(_SC_CLK_TCK) is 100 and page size is 4096, most common settings on Linux
        string[] stats = File.ReadAllText("/proc/self/stat").Split(" ");
        TimeSpan measuredAt = stopwatch.Elapsed;

        double rss = double.Parse(stats[23]) / 1024 / 1024 * 4096;
        long utime = long.Parse(stats[13]);

        if (print)
        {
            Console.WriteLine("RSS: {0}MiB, user CPU: {1}%", rss, (utime - lastUtime) / (measuredAt - lastMeasurement).TotalSeconds);
        }

        lastUtime = utime;
        lastMeasurement = measuredAt;
    }

    static void EndTest(ClrEventListener listener, bool print) {
        if (print)
        {
            CollectUsageLinux(true);
        }

        Thread.Sleep(100); // Allow EventPipe to catch up

        if (print)
        {
            Console.WriteLine("{0} EventPipe events received, {1} exceptions thrown\n", listener.EventCount, exceptions);
        }

        listener.EventCount = exceptions = 0;
        CollectUsageLinux(false);
    }

    static void DoNothing()
    {
        Interlocked.Increment(ref threadCount);

        // Just to trigger EventPipe
        try {
            throw new Exception();
        } catch (Exception) {
            Interlocked.Increment(ref exceptions);
        }
    }

    static void ThrowCatchExceptions(int numExceptions) {
        for (int i = 0; i < numExceptions; i++) {
            try {
                throw new Exception();
            } catch (Exception) {
                exceptions++;
            }
        }
    }

    static void Main(string[] args)
    {
        stopwatch.Start();

        if (args.Length < 2) {
            Console.WriteLine("must provide 2-3 CLI arguments: # of threads, # of exceptions to throw, and optionally '--disabled' to run without CLR EventPipe session");
            return;
        }

        int numThreads = int.Parse(args[0]);
        int numExceptions = int.Parse(args[1]);
        bool eventPipeEnabled = true;

        if (args.Length > 2 && args[2] == "--disabled") {
            eventPipeEnabled = false;
        }

        ClrEventListener listener = new ClrEventListener(eventPipeEnabled);
        Thread thread;


        // Warm up JIT in case it affects results
        Console.WriteLine("Throwing and catching 1000 exceptions (warm up JIT)\n");

        ThrowCatchExceptions(1000);

        EndTest(listener, false);


        // Measurement 1 - should work normally, no issues
        Console.WriteLine("Throwing and catching {0} exceptions", numExceptions);

        ThrowCatchExceptions(numExceptions);

        EndTest(listener, true);


        // Spawn threads to trigger EventPipe leak. Will also show fewer events,
        // more CPU as threads increase due to EventPipe
        Console.WriteLine("Spawning {0} short-lived threads", numThreads);

        for (int i = 0; i < numThreads; i++)
        {
            thread = new Thread(DoNothing);
            thread.Start();
            thread.Join();
        }

        EndTest(listener, true);


        // Measurement 2 - should show fewer events, more CPU as threads increase
        Console.WriteLine("Throwing and catching {0} exceptions", numExceptions);

        ThrowCatchExceptions(numExceptions);

        EndTest(listener, true);


        if (eventPipeEnabled && listener.EventSource != null)
        {
            // Create new, identical EventPipe session to reset performance
            Console.WriteLine("Reconfiguring EventPipe with identical settings\n", numExceptions);
            listener.EnableEvents(listener.EventSource, EventLevel.Informational, (EventKeywords) 0x8000); // 0x8000 means watch exceptions
            CollectUsageLinux(false);


            // Measurement 3 - should work normally, no issues
            Console.WriteLine("Throwing and catching {0} exceptions", numExceptions);

            ThrowCatchExceptions(numExceptions);

            EndTest(listener, true);
        }
    }
}
