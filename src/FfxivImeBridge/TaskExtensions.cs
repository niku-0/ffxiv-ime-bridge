namespace FfxivImeBridge;

internal static class TaskExtensions
{
    /// <summary>For a task nobody awaits: <paramref name="handle"/> gets its exception if it faults, on the thread pool.</summary>
    public static void OnFault(this Task task, Action<Exception> handle) => task.ContinueWith(
        t => handle(t.Exception!.InnerException ?? t.Exception),
        CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
}
