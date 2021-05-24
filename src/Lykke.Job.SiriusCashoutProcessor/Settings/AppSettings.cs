using Antares.Sdk.Settings;
using Lykke.Job.SiriusCashoutProcessor.Settings.JobSettings;

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
