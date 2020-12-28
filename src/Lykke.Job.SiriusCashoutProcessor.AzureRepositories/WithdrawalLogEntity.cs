using System;
using Lykke.AzureStorage.Tables;
using Lykke.Job.SiriusCashoutProcessor.Domain.Repositories;

namespace Lykke.Job.SiriusCashoutProcessor.AzureRepositories
{
    public class WithdrawalLogEntity : AzureTableEntity, IWithdrawalLog
    {
        public DateTime CreatedAt { get; set; }
        public string WithdrawalId { get; set; }
        public string Message { get; set; }
        public string TechData { get; set; }

        public static string GetPk(string id) => id;

        public static WithdrawalLogEntity Create(string withdrawalId, string message, string techData, DateTime createdAt)
        {
            return new WithdrawalLogEntity
            {
                PartitionKey = GetPk(withdrawalId),
                WithdrawalId = withdrawalId,
                CreatedAt = createdAt,
                Message = message,
                TechData = techData
            };
        }
    }
}
