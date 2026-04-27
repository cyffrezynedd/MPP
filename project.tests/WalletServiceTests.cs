using System;
using System.Threading.Tasks;
using project.api;
using project.usecase;
using TestRunner;

namespace project.tests
{
    [TestClass(Category = "Wallet", Priority = 10)]
    [SharedContext(typeof(WalletContext))]
    public class WalletServiceTests
    {
        private WalletContext _context = null!;

        [ClassInitialize]
        public void ClassInit(WalletContext context)
        {
            Assert.IsNotNull(context);
            _context = context;
            _context.Counter = 0;
        }

        [ClassCleanup]
        public void ClassCleanup(WalletContext context)
        {
            Assert.GreaterThan(context.Counter, 0, "Counter should be incremented by tests.");
        }

        [TestInitialize]
        public void TestInit(WalletContext context)
        {
            context.Bump();
        }

        [TestCleanup]
        public void TestCleanup()
        {
        }

        [TestMethod("Create wallet and get balance", Description = "Happy path create/get")]
        public async Task CreateWallet_ThenGetBalance_ReturnsInitialBalance(WalletContext context)
        {
            decimal initial = 100m;
            var id = await context.Service.CreateWalletAsync("user1", "USD", initial);

            var balance = await context.Service.GetBalanceAsync(id);

            Assert.AreEqual(initial, balance);
            Assert.IsTrue(balance > 0);
            Assert.GreaterThan(balance, 50m);
            Assert.LessThan(balance, 200m);
        }

        [TestMethod("Deposit increases balance")]
        public async Task Deposit_IncreasesBalance()
        {
            var service = _context.Service;
            var id = await service.CreateWalletAsync("user2", "EUR", 0m);

            await service.DepositAsync(id, 50m);
            await service.DepositAsync(id, 25m);

            var balance = await service.GetBalanceAsync(id);
            Assert.AreEqual(75m, balance);
        }

        [TestMethod("Withdraw decreases balance")]
        public async Task Withdraw_DecreasesBalance()
        {
            var service = _context.Service;
            var id = await service.CreateWalletAsync("user3", "EUR", 100m);

            await service.WithdrawAsync(id, 40m);

            var balance = await service.GetBalanceAsync(id);
            Assert.AreEqual(70m, balance);
            Assert.IsFalse(balance == 100m);
        }

        [TestMethod("Withdraw with insufficient funds throws")]
        public async Task Withdraw_InsufficientFunds_Throws()
        {
            var service = _context.Service;
            var id = await service.CreateWalletAsync("user4", "EUR", 10m);

            await AssertAsync.ThrowsAsync<InsufficientFundsException>(async () =>
            {
                await service.WithdrawAsync(id, 20m);
            });
        }

        [TestMethod("Get balance for missing wallet throws")]
        public async Task GetBalance_WalletNotFound_Throws()
        {
            var service = _context.Service;
            var missingId = Guid.NewGuid();

            await AssertAsync.ThrowsAsync<WalletNotFoundException>(async () =>
            {
                await service.GetBalanceAsync(missingId);
            });
        }

        [TestMethod("Null and not null checks", Category = "Wallet", Priority = 5, Author = "demo")]
        public void NullChecks()
        {
            WalletService? service = null;
            Assert.IsNull(service);

            service = WalletService.CreateInMemory();
            Assert.IsNotNull(service);
        }

        [Timeout(300)]
        [TestMethod("Completes within timeout")]
        public async Task FastEnough_ForTimeoutAttribute()
        {
            await Task.Delay(50);
            Assert.IsTrue(true);
        }
    }

    public static class AssertAsync
    {
        public static async Task ThrowsAsync<TException>(Func<Task> action)
            where TException : Exception
        {
            try
            {
                await action();
            }
            catch (TException)
            {
                return;
            }
            catch (Exception ex)
            {
                throw new AssertionFailedException(
                    $"Expected exception {typeof(TException).Name}, but got {ex.GetType().Name}");
            }

            throw new AssertionFailedException(
                $"Expected exception {typeof(TException).Name}, but no exception was thrown.");
        }
    }
}

