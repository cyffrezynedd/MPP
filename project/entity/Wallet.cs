using System;

namespace project.entity
{
    public class Wallet
    {
        public Guid Id { get; }
        public string OwnerId { get; }
        public string Currency { get; }
        public decimal Balance { get; private set; }

        public Wallet(Guid id, string ownerId, string currency, decimal initialBalance)
        {
            if (string.IsNullOrWhiteSpace(ownerId))
                throw new ArgumentException("OwnerId is required.", nameof(ownerId));
            if (string.IsNullOrWhiteSpace(currency))
                throw new ArgumentException("Currency is required.", nameof(currency));
            if (initialBalance < 0)
                throw new ArgumentOutOfRangeException(nameof(initialBalance), "Initial balance cannot be negative.");

            Id = id;
            OwnerId = ownerId;
            Currency = currency;
            Balance = initialBalance;
        }

        public void Deposit(decimal amount)
        {
            if (amount <= 0)
                throw new ArgumentOutOfRangeException(nameof(amount), "Deposit amount must be positive.");

            Balance += amount;
        }

        public void Withdraw(decimal amount)
        {
            if (amount <= 0)
                throw new ArgumentOutOfRangeException(nameof(amount), "Withdraw amount must be positive.");
            if (amount > Balance)
                throw new InvalidOperationException("Insufficient funds.");

            Balance -= amount;
        }
    }
}

