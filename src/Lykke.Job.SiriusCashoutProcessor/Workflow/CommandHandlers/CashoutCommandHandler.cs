using System;
using System.Text;
using System.Threading.Tasks;
using Common;
using Common.Log;
using JetBrains.Annotations;
using Lykke.Common.Log;
using Lykke.Cqrs;
using Lykke.Job.SiriusCashoutProcessor.Contract.Commands;
using Lykke.Job.SiriusCashoutProcessor.DomainServices;
using Swisschain.Extensions.Encryption;
using Swisschain.Sirius.Api.ApiClient;
using Swisschain.Sirius.Api.ApiContract.Withdrawal;

namespace Lykke.Job.SiriusCashoutProcessor.Workflow.CommandHandlers
{
    public class CashoutCommandHandler
    {
        private readonly long _brokerAccountId;
        private readonly IApiClient _siriusApiClient;
        private readonly PrivateKeyService _privateKeyService;
        private readonly ILog _log;
        private readonly AsymmetricEncryptionService _encryptionService;

        public CashoutCommandHandler(
            long brokerAccountId,
            IApiClient siriusApiClient,
            PrivateKeyService privateKeyService,
            ILogFactory logFactory
            )
        {
            _brokerAccountId = brokerAccountId;
            _siriusApiClient = siriusApiClient;
            _privateKeyService = privateKeyService;
            _log = logFactory.CreateLog(this);
            _encryptionService = new AsymmetricEncryptionService();
        }

        [UsedImplicitly]
        public async Task<CommandHandlingResult> Handle(StartCashoutCommand command, IEventPublisher eventPublisher)
        {
            _log.Info("Got cashout command", context: $"command: {command.ToJson()}" );

            var document = new WithdrawalDocument
            {
                BrokerAccountId = _brokerAccountId,
                UserNativeId = command.ClientId.ToString(),
                WithdrawalReferenceId = command.OperationId.ToString(),
                AssetId = command.SiriusAssetId,
                Amount = command.Amount,
                DestinationDetails = new WithdrawalDocument.WithdrawalDestinationDetails
                {
                    Address = command.Address,
                    Tag = command.Tag ?? string.Empty
                }
            }.ToJson();

            var signatureBytes = _encryptionService.GenerateSignature(Encoding.UTF8.GetBytes(document),  _privateKeyService.GetPrivateKey());
            var signature = Convert.ToBase64String(signatureBytes);

            var result = await _siriusApiClient.Withdrawals.ExecuteAsync(new WithdrawalExecuteRequest
            {
                RequestId = $"{_brokerAccountId}_{command.OperationId}",
                Document = document,
                Signature = signature
            });

            if (result.ResultCase == WithdrawalExecuteResponse.ResultOneofCase.Error)
            {
                _log.Warning("Cashout to Sirius failed", context: $"result: {result.Error.ToJson()}");
                
                return CommandHandlingResult.Fail(TimeSpan.FromSeconds(5));
            }
            else
            {
                _log.Info("Cashout sent to Sirius", context: $"result: {result.Body.Withdrawal.ToJson()}");
                
                return CommandHandlingResult.Ok();
            }
        }
    }
}
