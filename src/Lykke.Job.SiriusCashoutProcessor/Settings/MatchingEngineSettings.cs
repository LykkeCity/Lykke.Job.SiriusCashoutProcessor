using JetBrains.Annotations;

namespace Lykke.Job.SiriusCashoutProcessor.Settings
{
    [UsedImplicitly]
    public class MatchingEngineSettings
    {
        [UsedImplicitly(ImplicitUseKindFlags.Assign)]
        public IpEndpointSettings IpEndpoint { get; set; }
    }
}
