using System;
using System.Threading.Tasks;
using project.repository;

namespace project.usecase
{
    public class DepositFunds
    {
        private readonly IWalletRepository _repository;

        public DepositFunds(IWalletRepository repository)
        {
            _repository = repository;
        }

        public async Task ExecuteAsync(Guid walletId, decimal amount)
        {
            var wallet = await _repository.GetByIdAsync(walletId)
                         ?? throw new WalletNotFoundException(walletId);

            wallet.Deposit(amount);
            await _repository.UpdateAsync(wallet);
        }
    }
}

