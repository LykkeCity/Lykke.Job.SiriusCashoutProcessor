using Swisschain.Sirius.Api.ApiContract.Withdrawal;

namespace Lykke.Job.SiriusCashoutProcessor.Domain
{
    public class WithdrawalDocument
    {
        public string Version { get; set; }

        public long BrokerAccountId { get; set; }

        public long? AccountId { get; set; }

        public string AccountReferenceId { get; set; }

        public string WithdrawalReferenceId { get; set; }

        public long AssetId { get; set; }

        public decimal Amount { get; set; }

        public DestinationDetails DestinationDetails { get; set; }
    }
}
