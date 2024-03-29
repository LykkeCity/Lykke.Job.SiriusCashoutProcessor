﻿using System;
using Lykke.AzureStorage.Tables;
using Lykke.AzureStorage.Tables.Entity.Annotation;
using Lykke.AzureStorage.Tables.Entity.ValueTypesMerging;
using Lykke.Job.SiriusCashoutProcessor.Domain.Repositories;

namespace Lykke.Job.SiriusCashoutProcessor.AzureRepositories
{
    [ValueTypeMergingStrategy(ValueTypeMergingStrategy.UpdateAlways)]
    public class RefundEntity : AzureTableEntity, IRefund
    {
        public string Id { get; set; }
        public string OperationId { get; set; }
        public string FeeOperationId { get; set; }
        public string ClientId { get; set; }
        public string FeeClientId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string AssetId { get; set; }
        public long SiriusAssetId { get; set; }
        public decimal Amount { get; set; }
        public decimal FeeAmount { get; set; }
        public string State { get; set; }
        public string WalletId { get; set; }

        public static string GetPk(string clientId) => clientId;
        public static string GetRk(string id) => id;

        public static RefundEntity Create(string id, string clientId, string walletId, string feeClientId, string assetId, long siriusAssetId, decimal amount, decimal feeAmount)
        {
            string operationId = Guid.NewGuid().ToString();
            string feeOperationId = Guid.NewGuid().ToString();
            var now = DateTime.UtcNow;

            return new RefundEntity
            {
                PartitionKey = GetPk(clientId),
                RowKey = GetRk(id),
                Id = id,
                OperationId = operationId,
                FeeOperationId = feeOperationId,
                ClientId = clientId,
                FeeClientId = feeClientId,
                CreatedAt = now,
                UpdatedAt = now,
                AssetId = assetId,
                SiriusAssetId = siriusAssetId,
                Amount = amount,
                FeeAmount = feeAmount,
                WalletId = walletId,
                State = "Created"
            };
        }
    }
}
