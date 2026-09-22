using System;
using System.Threading;
using System.Threading.Tasks;

namespace SDA.Desktop.Services
{
    public sealed class ManifestMutationGate
    {
        public static readonly ManifestMutationGate Shared = new ManifestMutationGate();

        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

        public T Run<T>(Func<T> work)
        {
            _gate.Wait();
            try
            {
                return work();
            }
            finally
            {
                _gate.Release();
            }
        }

        public void Run(Action work)
        {
            Run<object>(() =>
            {
                work();
                return null;
            });
        }

        public async Task<T> RunAsync<T>(Func<Task<T>> work)
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                return await work().ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }
    }
}
