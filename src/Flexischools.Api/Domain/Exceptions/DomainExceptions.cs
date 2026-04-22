namespace Flexischools.Api.Domain.Exceptions;

/// <summary>Base class for all domain-level validation failures.</summary>
public abstract class DomainException : Exception
{
    protected DomainException(string message) : base(message) { }
}

public class OrderCutOffException : DomainException
{
    public OrderCutOffException(string canteenName, TimeSpan cutOff, DateOnly fulfilmentDate)
        : base($"Orders for '{canteenName}' on {fulfilmentDate:yyyy-MM-dd} must be placed before {cutOff:hh\\:mm}.") { }
}

public class InsufficientStockException : DomainException
{
    public InsufficientStockException(string itemName, int available, int requested)
        : base($"Insufficient stock for '{itemName}': requested {requested}, available {available}.") { }
}

public class InsufficientWalletBalanceException : DomainException
{
    public InsufficientWalletBalanceException(decimal balance, decimal required)
        : base($"Wallet balance ${balance:F2} is insufficient for order total ${required:F2}.") { }
}

public class AllergenConflictException : DomainException
{
    public AllergenConflictException(string studentName, string itemName, IEnumerable<string> allergens)
        : base($"Menu item '{itemName}' contains allergen(s) [{string.Join(", ", allergens)}] recorded for student '{studentName}'.") { }
}

public class NotFoundException : DomainException
{
    public NotFoundException(string entityName, object id)
        : base($"{entityName} with id '{id}' was not found.") { }
}
