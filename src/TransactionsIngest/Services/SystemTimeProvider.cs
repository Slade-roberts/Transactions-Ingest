using TransactionsIngest.Services.Interfaces;

namespace TransactionsIngest.Services;

public class SystemTimeProvider : ITimeProvider
{
    public DateTime UtcNow => DateTime.UtcNow;
}
