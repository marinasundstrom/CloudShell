namespace CloudShell.ControlPlane.ResourceManager;

public sealed class ResourceHealthRefreshCoordinator
{
    private readonly SemaphoreSlim refreshLock = new(1, 1);

    public async Task<IDisposable> EnterAsync(CancellationToken cancellationToken)
    {
        await refreshLock.WaitAsync(cancellationToken);
        return new Releaser(refreshLock);
    }

    public async Task<IDisposable?> TryEnterAsync(CancellationToken cancellationToken)
    {
        if (!await refreshLock.WaitAsync(0, cancellationToken))
        {
            return null;
        }

        return new Releaser(refreshLock);
    }

    private sealed class Releaser(SemaphoreSlim refreshLock) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            refreshLock.Release();
        }
    }
}
