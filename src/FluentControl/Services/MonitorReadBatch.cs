namespace FluentControl.Services;

public static class MonitorReadBatch
{
    // A whole display is one work item: VCP requests for that display remain
    // serial. Bound concurrency for hubs/drivers, and join every worker before
    // the caller can destroy its physical monitor handles.
    public static void Run<T>(IEnumerable<T> devices, Func<T, string> identity, Action<T> read, Action<T, Exception> failed)
    {
        var groups = devices.GroupBy(identity, StringComparer.OrdinalIgnoreCase).ToArray();
        Parallel.ForEach(groups, new ParallelOptions { MaxDegreeOfParallelism = 2 }, group =>
        {
            foreach (var device in group)
            {
                try { read(device); }
                catch (Exception ex) { failed(device, ex); }
            }
        });
    }
}
