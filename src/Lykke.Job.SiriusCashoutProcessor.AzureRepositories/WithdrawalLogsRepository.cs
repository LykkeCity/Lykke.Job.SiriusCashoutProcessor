using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AzureStorage;
using Lykke.Job.SiriusCashoutProcessor.Domain.Repositories;

namespace Lykke.Job.SiriusCashoutProcessor.AzureRepositories
{
    public class WithdrawalLogsRepository : IWithdrawalLogsRepository
    {
        private readonly INoSQLTableStorage<WithdrawalLogEntity> _tableStorage;

        public WithdrawalLogsRepository(INoSQLTableStorage<WithdrawalLogEntity> tableStorage)
        {
            _tableStorage = tableStorage;
        }

        public Task AddAsync(string withdrawalId, string message, string techData)
        {
            var now = DateTime.UtcNow;
            return _tableStorage.InsertAndGenerateRowKeyAsDateTimeAsync(
                WithdrawalLogEntity.Create(withdrawalId, message, techData, now), now);
        }

        public async Task<IReadOnlyList<IWithdrawalLog>> GetWithdrawalLogsAsync(string id)
        {
            return (await _tableStorage.GetDataAsync(WithdrawalLogEntity.GetPk(id))).ToList();
        }
    }
}
