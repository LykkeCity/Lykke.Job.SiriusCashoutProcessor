using System;
using MessagePack;

namespace Lykke.Job.SiriusCashoutProcessor.Contract.Commands
{
    [MessagePackObject(true)]
    public class StartCashoutCommand
    {
        public Guid OperationId { get; set; }
        public string AssetId { get; set; }
        public long SiriusAssetId { get; set; }
        public decimal Amount { get; set; }
        public string Address { get; set; }
        public string Tag { get; set; }
        public Guid ClientId { get; set; }
    }
}
