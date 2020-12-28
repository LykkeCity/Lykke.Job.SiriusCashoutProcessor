using System.Threading.Tasks;
using AzureStorage;
using Lykke.Job.SiriusCashoutProcessor.Domain.Repositories;

namespace Lykke.Job.SiriusCashoutProcessor.AzureRepositories
{
    public class LastCursorRepository : ILastCursorRepository
    {
        private readonly INoSQLTableStorage<CursorEntity> _tableStorage;

        public LastCursorRepository(INoSQLTableStorage<CursorEntity> tableStorage)
        {
            _tableStorage = tableStorage;
        }

        public async Task<long?> GetAsync(long brokerAccountId)
        {
            var entity = await _tableStorage.GetDataAsync(CursorEntity.GetPk(brokerAccountId), CursorEntity.GetRk());
            return entity?.Cursor;
        }

        public Task AddAsync(long brokerAccountId, long cursor)
        {
            return _tableStorage.InsertOrReplaceAsync(new CursorEntity
            {
                PartitionKey = brokerAccountId.ToString(), RowKey = CursorEntity.GetRk(), Cursor = cursor
            });
        }
    }
}
