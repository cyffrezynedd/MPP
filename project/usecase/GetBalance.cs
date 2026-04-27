using System;
using System.Threading.Tasks;
using project.repository;

namespace project.usecase
{
    public class GetBalance
    {
        private readonly IWalletRepository _repository;

        public GetBalance(IWalletRepository repository)
        {
            _repository = repository;
        }

        public async Task<decimal> ExecuteAsync(Guid walletId)
        {
            var wallet = await _repository.GetByIdAsync(walletId)
                         ?? throw new WalletNotFoundException(walletId);

            return wallet.Balance;
        }
    }
}

