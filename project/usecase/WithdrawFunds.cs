using System;
using System.Threading.Tasks;
using project.repository;

namespace project.usecase
{
    public class WithdrawFunds
    {
        private readonly IWalletRepository _repository;

        public WithdrawFunds(IWalletRepository repository)
        {
            _repository = repository;
        }

        public async Task ExecuteAsync(Guid walletId, decimal amount)
        {
            var wallet = await _repository.GetByIdAsync(walletId)
                         ?? throw new WalletNotFoundException(walletId);

            if (amount > wallet.Balance)
            {
                throw new InsufficientFundsException(wallet.Id, wallet.Balance, amount);
            }

            wallet.Withdraw(amount);
            await _repository.UpdateAsync(wallet);
        }
    }
}

