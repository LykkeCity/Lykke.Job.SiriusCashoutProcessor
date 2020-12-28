namespace Lykke.Job.SiriusCashoutProcessor.Settings.JobSettings
{
    public class SiriusCashoutProcessorJobSettings
    {
        public DbSettings Db { get; set; }
        public CqrsSettings Cqrs { get; set; }
    }
}
