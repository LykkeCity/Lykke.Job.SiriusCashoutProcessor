using System;
using System.Threading.Tasks;
using AzureStorage;
using Lykke.Job.SiriusCashoutProcessor.Domain.Repositories;

namespace Lykke.Job.SiriusCashoutProcessor.AzureRepositories
{
    public class RefundsRepository : IRefundsRepository
    {
        private readonly INoSQLTableStorage<RefundEntity> _tableStorage;

        public RefundsRepository(INoSQLTableStorage<RefundEntity> tableStorage)
        {
            _tableStorage = tableStorage;
        }

        public async Task<IRefund> AddAsync(string id, string clientId, string assetId, long siriusAssetId, decimal amount)
        {
            var entity = RefundEntity.Create(id, clientId, assetId, siriusAssetId, amount);
            await _tableStorage.InsertOrReplaceAsync(entity);
            return entity;
        }

        public Task UpdateAsync(string clientId, string id, string state)
        {
            return _tableStorage.MergeAsync(RefundEntity.GetPk(clientId), RefundEntity.GetRk(id), entity =>
            {
                entity.State = state;
                entity.UpdatedAt = DateTime.UtcNow;
                return entity;
            });
        }

        public async Task<IRefund> GetAsync(string clientId, string id)
        {
            return await _tableStorage.GetDataAsync(RefundEntity.GetPk(clientId), RefundEntity.GetRk(id));
        }
    }
}
