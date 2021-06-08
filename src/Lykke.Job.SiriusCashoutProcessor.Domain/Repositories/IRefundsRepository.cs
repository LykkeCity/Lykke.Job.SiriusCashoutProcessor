using System.Threading.Tasks;

namespace Lykke.Job.SiriusCashoutProcessor.Domain.Repositories
{
    public interface IRefundsRepository
    {
        Task<IRefund> AddAsync(string id, string clientId, string walletId, string feeClientId, string assetId, long siriusAssetId, decimal amount, decimal feeAmount);
        Task UpdateAsync(string clientId, string id, string state);
        Task<IRefund> GetAsync(string clientId, string id);
    }
}
