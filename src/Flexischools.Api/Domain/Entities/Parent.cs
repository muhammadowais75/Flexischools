namespace Flexischools.Api.Domain.Entities;

public class Parent
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public decimal WalletBalance { get; private set; }

    // EF Core navigation
    public ICollection<Student> Students { get; private set; } = new List<Student>();

    // Required by EF Core
    private Parent() { }

    public static Parent Create(string name, string email, decimal initialBalance)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Parent name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(email)) throw new ArgumentException("Parent email is required.", nameof(email));
        if (initialBalance < 0) throw new ArgumentException("Initial balance cannot be negative.", nameof(initialBalance));

        return new Parent
        {
            Id = Guid.NewGuid(),
            Name = name,
            Email = email,
            WalletBalance = initialBalance
        };
    }

    /// <summary>
    /// Debits the wallet. Throws if insufficient funds.
    /// </summary>
    public void DebitWallet(decimal amount)
    {
        if (amount <= 0) throw new ArgumentException("Debit amount must be positive.", nameof(amount));
        if (WalletBalance < amount)
            throw new Domain.Exceptions.InsufficientWalletBalanceException(WalletBalance, amount);

        WalletBalance -= amount;
    }

    /// <summary>
    /// Refunds an amount back to the wallet (used on cancellation).
    /// </summary>
    public void CreditWallet(decimal amount)
    {
        if (amount <= 0) throw new ArgumentException("Credit amount must be positive.", nameof(amount));
        WalletBalance += amount;
    }

    // Optimistic concurrency token
    public uint RowVersion { get; private set; }
}
