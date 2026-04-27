using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using project.entity;

namespace project.repository
{
    public class InMemoryWalletRepository : IWalletRepository
    {
        private readonly ConcurrentDictionary<Guid, Wallet> _storage = new();

        public Task<Wallet?> GetByIdAsync(Guid id)
        {
            _storage.TryGetValue(id, out var wallet);
            return Task.FromResult(wallet);
        }

        public Task<IReadOnlyCollection<Wallet>> GetAllAsync()
        {
            var all = _storage.Values.ToList().AsReadOnly();
            return Task.FromResult((IReadOnlyCollection<Wallet>)all);
        }

        public Task AddAsync(Wallet wallet)
        {
            if (!_storage.TryAdd(wallet.Id, wallet))
            {
                throw new InvalidOperationException("Wallet with the same id already exists.");
            }

            return Task.CompletedTask;
        }

        public Task UpdateAsync(Wallet wallet)
        {
            _storage[wallet.Id] = wallet;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid id)
        {
            _storage.TryRemove(id, out _);
            return Task.CompletedTask;
        }
    }
}

