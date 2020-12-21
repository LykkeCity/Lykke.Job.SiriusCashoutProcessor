using System.Threading.Tasks;

namespace Lykke.Job.SiriusCashoutProcessor.Domain.Repositories
{
    public interface ILastCursorRepository
    {
        Task<long?> GetAsync(long brokerAccountId);
        Task AddAsync(long brokerAccountId, long cursor);
    }
}
