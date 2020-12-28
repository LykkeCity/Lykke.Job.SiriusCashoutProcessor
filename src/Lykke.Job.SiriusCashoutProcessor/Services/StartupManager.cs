using System.Threading.Tasks;
using Common.Log;
using Lykke.Common.Log;
using Lykke.Cqrs;
using Lykke.Sdk;

namespace Lykke.Job.SiriusCashoutProcessor.Services
{
    public class StartupManager : IStartupManager
    {
        private readonly ICqrsEngine _cqrsEngine;
        private readonly ILog _log;

        public StartupManager(
            ILogFactory logFactory,
            ICqrsEngine cqrsEngine
        )
        {
            _cqrsEngine = cqrsEngine;
            _log = logFactory.CreateLog(this);
        }

        public async Task StartAsync()
        {
            _cqrsEngine.StartSubscribers();
            _cqrsEngine.StartProcesses();

            await Task.CompletedTask;
        }
    }
}
