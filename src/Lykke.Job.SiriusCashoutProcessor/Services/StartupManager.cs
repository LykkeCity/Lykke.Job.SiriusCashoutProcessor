using System.Threading.Tasks;
using Antares.Sdk.Services;
using Common.Log;
using Lykke.Common.Log;
using Lykke.Cqrs;
using Lykke.Job.SiriusCashoutProcessor.DomainServices;

namespace Lykke.Job.SiriusCashoutProcessor.Services
{
    public class StartupManager : IStartupManager
    {
        private readonly PrivateKeyService _privateKeyService;
        private readonly ICqrsEngine _cqrsEngine;
        private readonly ILog _log;

        public StartupManager(
            ILogFactory logFactory,
            PrivateKeyService privateKeyService,
            ICqrsEngine cqrsEngine
        )
        {
            _privateKeyService = privateKeyService;
            _cqrsEngine = cqrsEngine;
            _log = logFactory.CreateLog(this);
        }

        public async Task StartAsync()
        {
            _cqrsEngine.StartSubscribers();
            _cqrsEngine.StartProcesses();

            await _privateKeyService.InitAsync();
        }
    }
}
