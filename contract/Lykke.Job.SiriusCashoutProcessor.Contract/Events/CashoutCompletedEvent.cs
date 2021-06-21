using System;
using MessagePack;

namespace Lykke.Job.SiriusCashoutProcessor.Contract.Events
{
    [MessagePackObject(keyAsPropertyName: true)]
    public class CashoutCompletedEvent
    {
        public Guid OperationId { get; set; }
        public Guid ClientId { get; set; }
        public Guid? WalletId { set; get; } // populated if API wallet cashout, null if Trading wallet cashout
        public string AssetId { get; set; }
        public decimal Amount { get; set; }
        public string Address { get; set; }
        public string Tag { get; set; }
        public string TransactionHash { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
