using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using project.entity;

namespace project.repository
{
    public interface IWalletRepository
    {
        Task<Wallet?> GetByIdAsync(Guid id);
        Task<IReadOnlyCollection<Wallet>> GetAllAsync();
        Task AddAsync(Wallet wallet);
        Task UpdateAsync(Wallet wallet);
        Task DeleteAsync(Guid id);
    }
}

