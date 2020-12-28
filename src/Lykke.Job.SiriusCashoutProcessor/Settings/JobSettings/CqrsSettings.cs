using JetBrains.Annotations;
using Lykke.SettingsReader.Attributes;

namespace Lykke.Job.SiriusCashoutProcessor.Settings.JobSettings
{
    [UsedImplicitly]
    public class CqrsSettings
    {
        [AmqpCheck]
        public string RabbitConnectionString { get; set; }
    }
}
