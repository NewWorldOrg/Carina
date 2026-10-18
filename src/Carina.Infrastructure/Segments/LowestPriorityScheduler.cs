using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Carina.Infrastructure.Segments;

/// <summary>
/// Runs work one piece at a time on a thread of its own, at the lowest priority the thread can be given: the
/// lowest the runtime names, and on Linux, where the runtime leaves the thread as it is, the highest nice
/// value a thread may take for itself. <see cref="Lowered"/> says which nice value the thread took, or null
/// when it could take none, and <see cref="Ran"/> how many pieces of work it has run.
/// </summary>
public sealed class LowestPriorityScheduler : TaskScheduler, IDisposable
{
    public const int LowestNice = 19;

    private const int ThisProcess = 0;

    private readonly BlockingCollection<Task> queued = [];

    private readonly TaskCompletionSource<int?> lowered = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly Thread thread;

    private long ran;

    public LowestPriorityScheduler()
    {
        thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "lowest priority",
            Priority = ThreadPriority.Lowest,
        };
        thread.Start();
    }

    public Task<int?> Lowered => lowered.Task;

    public long Ran => Interlocked.Read(ref ran);

    public override int MaximumConcurrencyLevel => 1;

    public Task<T> Run<T>(Func<T> work, CancellationToken cancellationToken)
        => Task.Factory.StartNew(work, cancellationToken, TaskCreationOptions.DenyChildAttach, this);

    public void Dispose() => queued.CompleteAdding();

    protected override void QueueTask(Task task) => queued.Add(task);

    protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
        => Thread.CurrentThread == thread && Execute(task);

    protected override IEnumerable<Task> GetScheduledTasks() => queued.ToArray();

    private void Run()
    {
        lowered.SetResult(LowerThisThread());

        foreach (Task task in queued.GetConsumingEnumerable())
        {
            Execute(task);
        }
    }

    private bool Execute(Task task)
    {
        Interlocked.Increment(ref ran);

        return TryExecuteTask(task);
    }

    private static int? LowerThisThread()
    {
        if (!OperatingSystem.IsLinux())
        {
            return null;
        }

        try
        {
            return SetPriority(ThisProcess, ThreadId(), LowestNice) == 0 ? LowestNice : null;
        }
        catch (Exception failure) when (failure is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    [DllImport("libc", EntryPoint = "setpriority", SetLastError = true)]
    private static extern int SetPriority(int which, int who, int value);

    [DllImport("libc", EntryPoint = "gettid")]
    private static extern int ThreadId();
}
