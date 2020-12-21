namespace Lykke.Job.SiriusCashoutProcessor.Domain.Repositories
{
    public interface IRefund
    {
        string Id { get; }
        string OperationId { get; }
        string ClientId { get; set; }
        string AssetId { get; set; }
        long SiriusAssetId { get; set; }
        decimal Amount { get; set; }
        string State { get; set; }
    }
}
