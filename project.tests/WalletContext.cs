using System.Threading;
using project.api;
using project.repository;

namespace project.tests
{
    public class WalletContext
    {
        private int _counter;

        public InMemoryWalletRepository Repository { get; } = new InMemoryWalletRepository();
        public WalletService Service { get; }

        public WalletContext()
        {
            Service = new WalletService(Repository);
        }

        public int Counter
        {
            get => Volatile.Read(ref _counter);
            set => Volatile.Write(ref _counter, value);
        }

        public void Bump() => Interlocked.Increment(ref _counter);
    }
}

