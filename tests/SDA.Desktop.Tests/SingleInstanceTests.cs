using SDA.Desktop.Services;
using System;
using Xunit;

namespace SDA.Desktop.Tests
{
    public class SingleInstanceTests
    {
        [Fact]
        public void TryAcquire_SecondCallerDoesNotGetGuard()
        {
            string name = "SDA-Test-" + Guid.NewGuid().ToString("N");
            using (SingleInstanceGuard first = SingleInstanceGuard.TryAcquire(name))
            {
                Assert.NotNull(first);
                SingleInstanceGuard second = SingleInstanceGuard.TryAcquire(name);
                Assert.Null(second);
            }

            using (SingleInstanceGuard afterRelease = SingleInstanceGuard.TryAcquire(name))
            {
                Assert.NotNull(afterRelease);
            }
        }
    }
}
