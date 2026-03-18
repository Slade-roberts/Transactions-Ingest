using TransactionsIngest.Models;

namespace TransactionsIngest.Services.Interfaces;

public interface IFeedLoader
{
    Task<List<TransactionDto>> LoadAsync();
}
