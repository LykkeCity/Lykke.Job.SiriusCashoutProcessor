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

        public async Task<IRefund> AddAsync(string id, string clientId, string walletId, string feeClientId, string assetId, long siriusAssetId, decimal amount, decimal feeAmount)
        {
            var entity = RefundEntity.Create(id, clientId, walletId, feeClientId, assetId, siriusAssetId, amount, feeAmount);
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
