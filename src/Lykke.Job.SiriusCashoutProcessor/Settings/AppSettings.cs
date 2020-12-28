using Lykke.Job.SiriusCashoutProcessor.Settings.JobSettings;
using Lykke.Sdk.Settings;

namespace Lykke.Job.SiriusCashoutProcessor.Settings
{
    public class AppSettings : BaseAppSettings
    {
        public SiriusCashoutProcessorJobSettings SiriusCashoutProcessorJob { get; set; }
        public SiriusApiServiceClientSettings SiriusApiServiceClient { get; set; }
        public MatchingEngineSettings MatchingEngineClient { get; set; }
        public AssetsServiceClientSettings AssetsServiceClient { get; set; }
        public OperationsServiceClientSettings OperationsServiceClient { get; set; }
    }
}
