using System;
using System.Threading.Tasks;
using Common;
using Common.Log;
using JetBrains.Annotations;
using Lykke.Common.Log;
using Lykke.Cqrs;
using Lykke.Job.SiriusCashoutProcessor.Contract.Commands;
using Lykke.Job.SiriusCashoutProcessor.Domain;
using Swisschain.Sirius.Api.ApiClient;
using Swisschain.Sirius.Api.ApiContract.Withdrawal;

namespace Lykke.Job.SiriusCashoutProcessor.Workflow.CommandHandlers
{
    public class CashoutCommandHandler
    {
        private readonly long _brokerAccountId;
        private readonly IApiClient _siriusApiClient;
        private readonly ILog _log;

        public CashoutCommandHandler(
            long brokerAccountId,
            IApiClient siriusApiClient,
            ILogFactory logFactory
            )
        {
            _brokerAccountId = brokerAccountId;
            _siriusApiClient = siriusApiClient;
            _log = logFactory.CreateLog(this);
        }

        [UsedImplicitly]
        public async Task<CommandHandlingResult> Handle(StartCashoutCommand command, IEventPublisher eventPublisher)
        {
            _log.Info("Got cashout command", context: $"command: {command.ToJson()}" );

            var result = await _siriusApiClient.Withdrawals.ExecuteAsync(new WithdrawalExecuteRequest
            {
                RequestId = $"{_brokerAccountId}_{command.OperationId}",
                Document = new WithdrawalDocument
                {
                    BrokerAccountId = _brokerAccountId,
                    AccountReferenceId = command.ClientId.ToString(),
                    WithdrawalReferenceId = command.OperationId.ToString(),
                    AssetId = command.SiriusAssetId,
                    Amount = command.Amount,
                    DestinationDetails = new DestinationDetails
                    {
                        Address = command.Address,
                        Tag = command.Tag ?? string.Empty
                    }
                }.ToJson()
            });

            var techData = result.ResultCase == WithdrawalExecuteResponse.ResultOneofCase.Body
                ? result.Body.Withdrawal.ToJson()
                : result.Error.ToJson();

            _log.Info("Cashout sent to sirius", context: $"result: {techData}");

            return CommandHandlingResult.Ok();
        }
    }
}
