namespace Lykke.Job.SiriusCashoutProcessor.Domain
{
    public class OperationContext
    {
        public GlobalSettings GlobalSettings { get; set; }
        public FeeInfo Fee { get; set; }
    }

    public class GlobalSettings
    {
        public FeeSettings FeeSettings { get; set; }
    }

    public class FeeSettings
    {
        public TargetClients TargetClients { get; set; }
    }

    public class TargetClients
    {
        public string Cashout { get; set; }
    }

    public class FeeInfo
    {
        public string AssetId { get; set; }
        public decimal Size { get; set; }
        public string Type { get; set; }
    }
}
