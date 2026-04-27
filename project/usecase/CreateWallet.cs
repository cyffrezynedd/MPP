using System;
using System.Threading.Tasks;
using project.entity;
using project.repository;

namespace project.usecase
{
    public class CreateWallet
    {
        private readonly IWalletRepository _repository;

        public CreateWallet(IWalletRepository repository)
        {
            _repository = repository;
        }

        public async Task<Wallet> ExecuteAsync(string ownerId, string currency, decimal initialBalance)
        {
            var wallet = new Wallet(Guid.NewGuid(), ownerId, currency, initialBalance);
            await _repository.AddAsync(wallet);
            return wallet;
        }
    }
}

