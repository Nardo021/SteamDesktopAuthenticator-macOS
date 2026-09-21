using System;
using System.Threading;
using System.Threading.Tasks;

namespace SDA.Desktop.Services
{
    public sealed class TimerService : IDisposable
    {
        private readonly PeriodicTimer _timer;
        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
        private SynchronizationContext _context;
        private int _started;

        public TimerService()
            : this(TimeSpan.FromSeconds(1))
        {
        }

        public TimerService(TimeSpan interval)
        {
            _timer = new PeriodicTimer(interval);
        }

        public event EventHandler Tick;

        public void Start(SynchronizationContext context)
        {
            if (Interlocked.Exchange(ref _started, 1) == 1)
            {
                return;
            }

            _context = context;
            Task.Run(RunAsync);
        }

        public void Dispose()
        {
            try
            {
                _cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            _timer.Dispose();
        }

        private async Task RunAsync()
        {
            try
            {
                while (await _timer.WaitForNextTickAsync(_cancellation.Token))
                {
                    EventHandler handler = Tick;
                    if (handler == null)
                    {
                        continue;
                    }

                    if (_context != null)
                    {
                        _context.Post(_ => handler(this, EventArgs.Empty), null);
                    }
                    else
                    {
                        handler(this, EventArgs.Empty);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }
}
