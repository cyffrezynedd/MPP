using System;

namespace project.usecase
{
    public class WalletNotFoundException : Exception
    {
        public WalletNotFoundException(Guid id)
            : base($"Wallet with id '{id}' was not found.")
        {
        }
    }

    public class InsufficientFundsException : Exception
    {
        public InsufficientFundsException(Guid id, decimal balance, decimal requested)
            : base($"Wallet '{id}' has insufficient funds. Balance: {balance}, requested: {requested}.")
        {
        }
    }
}

