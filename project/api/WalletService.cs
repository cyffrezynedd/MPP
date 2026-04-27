using System;
using System.Threading.Tasks;
using project.repository;
using project.usecase;

namespace project.api
{
    public class WalletService
    {
        private readonly IWalletRepository _repository;

        public WalletService(IWalletRepository repository)
        {
            _repository = repository;
        }

        public static WalletService CreateInMemory()
        {
            var repo = new InMemoryWalletRepository();
            return new WalletService(repo);
        }

        public async Task<Guid> CreateWalletAsync(string ownerId, string currency, decimal initialBalance)
        {
            var usecase = new CreateWallet(_repository);
            var wallet = await usecase.ExecuteAsync(ownerId, currency, initialBalance);
            return wallet.Id;
        }

        public Task<decimal> GetBalanceAsync(Guid walletId)
        {
            var usecase = new GetBalance(_repository);
            return usecase.ExecuteAsync(walletId);
        }

        public Task DepositAsync(Guid walletId, decimal amount)
        {
            var usecase = new DepositFunds(_repository);
            return usecase.ExecuteAsync(walletId, amount);
        }

        public Task WithdrawAsync(Guid walletId, decimal amount)
        {
            var usecase = new WithdrawFunds(_repository);
            return usecase.ExecuteAsync(walletId, amount);
        }
    }
}
