using Lykke.AzureStorage.Tables;

namespace Lykke.Job.SiriusCashoutProcessor.AzureRepositories
{
    public class CursorEntity : AzureTableEntity
    {
        public long Cursor { get; set; }

        public static string GetPk(long brokerAccountId) => brokerAccountId.ToString();
        public static string GetRk() => "Cursor";
    }
}
