namespace PublishTool.Helpers;

public static class CriticalSection
{
    private static int _depth;

    public static bool IsActive => Volatile.Read(ref _depth) > 0;

    public static IDisposable Enter()
    {
        Interlocked.Increment(ref _depth);
        return new Scope();
    }

    private sealed class Scope : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                Interlocked.Decrement(ref _depth);
            }
        }
    }
}
