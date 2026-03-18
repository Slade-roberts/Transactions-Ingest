namespace TransactionsIngest.Services.Interfaces;

public interface ITimeProvider
{
    DateTime UtcNow { get; }
}
