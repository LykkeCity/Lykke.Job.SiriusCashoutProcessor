using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Common;
using Common.Log;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using JetBrains.Annotations;
using Lykke.Common.Log;
using Lykke.Cqrs;
using Lykke.Job.SiriusCashoutProcessor.Contract.Commands;
using Lykke.Job.SiriusCashoutProcessor.DomainServices;
using Lykke.Job.SiriusCashoutProcessor.Services;
using Swisschain.Extensions.Encryption;
using Swisschain.Sirius.Api.ApiClient;
using Swisschain.Sirius.Api.ApiClient.Utils.Builders.V2;
using Swisschain.Sirius.Api.ApiContract.Account;
using Swisschain.Sirius.Api.ApiContract.Common;
using Swisschain.Sirius.Api.ApiContract.User;
using Swisschain.Sirius.Api.ApiContract.WhitelistItems;
using Swisschain.Sirius.Api.ApiContract.V2.Withdrawals;
using WithdrawalDocument = Swisschain.Sirius.Api.ApiContract.V2.Withdrawals.WithdrawalDocument;

namespace Lykke.Job.SiriusCashoutProcessor.Workflow.CommandHandlers
{
    public class CashoutCommandHandler
    {
        private readonly BlockedCashoutsManager _blockedWithdrawalsManager;
        private readonly long _brokerAccountId;
        private readonly int _notEnoughBalanceRetryDelayInSeconds;
        private readonly IApiClient _siriusApiClient;
        private readonly PrivateKeyService _privateKeyService;
        private readonly ILog _log;
        private readonly AsymmetricEncryptionService _encryptionService;

        public CashoutCommandHandler(
            BlockedCashoutsManager blockedWithdrawalsManager,
            long brokerAccountId,
            IApiClient siriusApiClient,
            PrivateKeyService privateKeyService,
            ILogFactory logFactory,
            int notEnoughBalanceRetryDelayInSeconds)
        {
            _blockedWithdrawalsManager = blockedWithdrawalsManager;
            _brokerAccountId = brokerAccountId;
            _siriusApiClient = siriusApiClient;
            _privateKeyService = privateKeyService;
            _notEnoughBalanceRetryDelayInSeconds = notEnoughBalanceRetryDelayInSeconds;
            _log = logFactory.CreateLog(this);
            _encryptionService = new AsymmetricEncryptionService();
        }

        [UsedImplicitly]
        public async Task<CommandHandlingResult> Handle(StartCashoutCommand command, IEventPublisher eventPublisher)
        {
            var operationId = command.OperationId.ToString();

            _log.Info("Got cashout command", context: new { operationId, command = command.ToJson() });

            if (_blockedWithdrawalsManager.IsBlocked(command.OperationId))
            {
                _log.Warning("Cashout is blocked it will be skipped", context: new { operationId, command = command.ToJson() });
                return CommandHandlingResult.Ok();
            }

            var clientId = command.ClientId.ToString();
            long? accountId = null;

            string walletId;

            if (command.WalletId.HasValue)
            {
                walletId = command.WalletId.Value == Guid.Empty
                    ? clientId
                    : command.WalletId.Value.ToString();
            }
            else
            {
                walletId = clientId;
            }

            var walletType = !string.IsNullOrWhiteSpace(walletId) ? "API" : "Trading";

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
                        clientId,
                        operationId
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
                        requestId = userRequestId,
                        operationId
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
                        requestId = accountRequestId,
                        operationId
                    });
                    throw new Exception(message);
                }

                accountId = createResponse.Body.Account.Id;
            }
            else
            {
                accountId = accountSearchResponse.Body.Items.FirstOrDefault()?.Id;
            }

            var whitelistingRequestId = $"lykke:trading_wallet:{clientId}";

            var whitelistItemCreateResponse = await _siriusApiClient.WhitelistItems.CreateAsync(new WhitelistItemCreateRequest
            {
                Name = "Trading Wallet Whitelist",
                Scope = new WhitelistItemScope
                {
                    BrokerAccountId = _brokerAccountId,
                    AccountId = accountId,
                    UserNativeId = clientId
                },
                Details = new WhitelistItemDetails
                {
                    TransactionType = WhitelistTransactionType.Any,
                    TagType = new  NullableWhitelistItemTagType
                    {
                        Null = NullValue.NullValue
                    }
                },
                Lifespan = new WhitelistItemLifespan
                {
                    StartsAt = Timestamp.FromDateTime(DateTime.UtcNow)
                },
                RequestId = whitelistingRequestId
            });

            if (whitelistItemCreateResponse.BodyCase == WhitelistItemCreateResponse.BodyOneofCase.Error)
            {
                _log.Warning(nameof(CashoutCommandHandler), "Error creating Whitelist item",
                    context: new
                    {
                        error = whitelistItemCreateResponse.Error,
                        clientId,
                        requestId = whitelistingRequestId,
                        operationId
                    });

                throw new Exception("Error creating Whitelist item");
            }

            var tag = !string.IsNullOrWhiteSpace(command.Tag) ? command.Tag : string.Empty;
            var destinationTag = !string.IsNullOrWhiteSpace(command.Tag) ? 
                new DestinationTag 
                {
                    Type = long.TryParse(command.Tag, out _) ? TagType.Number : TagType.Text,
                    Value = tag
                }
                : null;
            
            var withdrawalDocument = new WithdrawalDocument
            {
                BrokerAccountId = _brokerAccountId,
                AssetId = command.SiriusAssetId,
                Amount = command.Amount,
                DestinationDetails = new ()
                {
                    Address = command.Address,
                    DestinationTag = destinationTag,
                },
                Properties =
                {
                    { KnownProperties.UserId, command.ClientId.ToString() }, 
                    { KnownProperties.WalletId, walletId },
                    { WithdrawalProperties.WithdrawalId, command.OperationId.ToString()},
                    { "WalletType", walletType }
                }
            };
            
            var idempotencyId = $"withdrawal_${_brokerAccountId}_{command.OperationId}";
            var documentBuilder = WithdrawalV2DocumentBuilder.Create();
            documentBuilder.SetBrokerAccountId(withdrawalDocument.BrokerAccountId);
            documentBuilder.SetAssetId(withdrawalDocument.AssetId);
            documentBuilder.SetAmount(withdrawalDocument.Amount);
            documentBuilder.SetDestinationAddress(withdrawalDocument.DestinationDetails.Address);
            documentBuilder.SetIdempotencyId(idempotencyId);
            foreach (var property in withdrawalDocument.Properties)
            {
                documentBuilder.SetProperty(property.Key, property.Value);
            }

            var document = documentBuilder.Build();

            var signatureBytes = _encryptionService.GenerateSignature(Encoding.UTF8.GetBytes(document),  _privateKeyService.GetPrivateKey());

            _log.Info($"Withdrawal document: [{document}]", new 
            { 
                operationId
            });

            var result = await _siriusApiClient.WithdrawalsV2.ExecuteAsync(new()
            {
                IdempotencyId = idempotencyId,
                Signature = ByteString.CopyFrom(signatureBytes),
                Document = withdrawalDocument,
            });
            
            if (result.Error != null)
            {
                switch (result.Error.Code)
                {
                    case WithdrawalExecuteError.Types.ErrorCode.NotAuthorized:
                    case WithdrawalExecuteError.Types.ErrorCode.Unknown:
                        LogError(operationId, result.Error);
                        return CommandHandlingResult.Fail(TimeSpan.FromSeconds(10));
                    case WithdrawalExecuteError.Types.ErrorCode.InvalidParameters:
                    case WithdrawalExecuteError.Types.ErrorCode.DomainProblem:
                        LogError(operationId, result.Error);
                        return CommandHandlingResult.Ok(); // abort
                    case WithdrawalExecuteError.Types.ErrorCode.NotEnoughBalance:
                        LogWarning(operationId, result.Error);
                        return CommandHandlingResult.Fail(TimeSpan.FromSeconds(_notEnoughBalanceRetryDelayInSeconds));
                }
            }

            _log.Info("Cashout sent to Sirius", context: new { operationId, withdrawalId = result.Payload.Id});

            return CommandHandlingResult.Ok();
        }

        private void LogWarning(string operationId, WithdrawalExecuteError error)
        {
            _log.Warning(message: "Cashout to Sirius failed", context: new { operationId, error = error.ToJson() });
        }
        private void LogError(string operationId, WithdrawalExecuteError error)
        {
            _log.Error(message: "Cashout to Sirius failed", context: new { operationId, error = error.ToJson() });
        }
    }
}
