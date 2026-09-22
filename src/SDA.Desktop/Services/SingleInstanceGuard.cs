using System;
using System.Threading;

namespace SDA.Desktop.Services
{
    public sealed class SingleInstanceGuard : IDisposable
    {
        public const string DefaultMutexName = "SteamDesktopAuthenticator-macOS";

        private Mutex _mutex;
        private bool _disposed;

        private SingleInstanceGuard(Mutex mutex)
        {
            _mutex = mutex;
        }

        public static SingleInstanceGuard TryAcquire()
        {
            return TryAcquire(DefaultMutexName);
        }

        public static SingleInstanceGuard TryAcquire(string mutexName)
        {
            if (string.IsNullOrWhiteSpace(mutexName))
            {
                throw new ArgumentException("Mutex name is required.", nameof(mutexName));
            }

            Mutex mutex = null;
            try
            {
                mutex = new Mutex(true, mutexName, out bool createdNew);
                if (!createdNew)
                {
                    mutex.Dispose();
                    return null;
                }

                return new SingleInstanceGuard(mutex);
            }
            catch (AbandonedMutexException)
            {
                return new SingleInstanceGuard(mutex);
            }
            catch (Exception)
            {
                if (mutex != null)
                {
                    mutex.Dispose();
                }

                return new SingleInstanceGuard(null);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_mutex != null)
            {
                try
                {
                    _mutex.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                }

                _mutex.Dispose();
                _mutex = null;
            }
        }
    }
}
