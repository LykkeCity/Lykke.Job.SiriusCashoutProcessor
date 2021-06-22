using System;
using System.Linq;
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
using Swisschain.Sirius.Api.ApiContract.Account;
using Swisschain.Sirius.Api.ApiContract.User;
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

            var clientId = command.ClientId.ToString();
            var walletId = command.WalletId.HasValue ? command.WalletId.Value.ToString() : clientId;
            
             var accountSearchResponse = await _siriusApiClient.Accounts.SearchAsync(new AccountSearchRequest
            {
                BrokerAccountId = _brokerAccountId,
                UserNativeId = clientId,
                ReferenceId = walletId
            });

            if (accountSearchResponse.ResultCase == AccountSearchResponse.ResultOneofCase.Error)
            {
                var message = "Error fetching Sirius Account";
                _log.Warning(nameof(CashoutCommandHandler),
                    message,
                    context: new
                    {
                        error = accountSearchResponse.Error,
                        walletId,
                        clientId
                    });
                throw new Exception(message);
            }

            if (!accountSearchResponse.Body.Items.Any())
            {
                var accountRequestId = $"{_brokerAccountId}_{walletId}_account";
                var userRequestId = $"{clientId}_user";

                var userCreateResponse = await _siriusApiClient.Users.CreateAsync(new CreateUserRequest
                {
                    RequestId = userRequestId,
                    NativeId = clientId
                });

                if (userCreateResponse.BodyCase == CreateUserResponse.BodyOneofCase.Error)
                {
                    var message = "Error creating User in Sirius";
                    _log.Warning(nameof(CashoutCommandHandler),
                    message,
                    context: new
                    {
                        error = userCreateResponse.Error,
                        clientId,
                        requestId = userRequestId
                    });
                    throw new Exception(message);
                }

                var createResponse = await _siriusApiClient.Accounts.CreateAsync(new AccountCreateRequest
                {
                    RequestId = accountRequestId,
                    BrokerAccountId = _brokerAccountId,
                    UserId = userCreateResponse.User.Id,
                    ReferenceId = walletId
                });

                if (createResponse.ResultCase == AccountCreateResponse.ResultOneofCase.Error)
                {
                    var message = "Error creating user in Sirius";
                    _log.Warning(nameof(CashoutCommandHandler),
                    message,
                    context: new
                    {
                        error = createResponse.Error,
                        clientId,
                        requestId = accountRequestId
                    });
                    throw new Exception(message);
                }
            }
            
            var document = new WithdrawalDocument
            {
                BrokerAccountId = _brokerAccountId,
                WithdrawalReferenceId = command.OperationId.ToString(),
                AssetId = command.SiriusAssetId,
                Amount = command.Amount,
                DestinationDetails = new WithdrawalDocument.WithdrawalDestinationDetails
                {
                    Address = command.Address,
                    Tag = command.Tag ?? string.Empty
                },
                AccountReferenceId = walletId
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
