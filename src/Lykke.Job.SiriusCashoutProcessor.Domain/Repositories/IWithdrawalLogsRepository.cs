using System.Collections.Generic;
using System.Threading.Tasks;

namespace Lykke.Job.SiriusCashoutProcessor.Domain.Repositories
{
    public interface IWithdrawalLogsRepository
    {
        Task AddAsync(string withdrawalId, string message, string techData);
        Task<IReadOnlyList<IWithdrawalLog>> GetWithdrawalLogsAsync(string id);
    }
}
