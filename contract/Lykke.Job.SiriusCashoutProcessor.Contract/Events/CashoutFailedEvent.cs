using MessagePack;

namespace Lykke.Job.SiriusCashoutProcessor.Contract.Events
{
    [MessagePackObject(keyAsPropertyName: true)]
    public class CashoutFailedEvent
    {
        public string OperationId { get; set; }
        public string RefundId { get; set; }
        public string Status { get; set; }
        public string Error { get; set; }
    }
}
