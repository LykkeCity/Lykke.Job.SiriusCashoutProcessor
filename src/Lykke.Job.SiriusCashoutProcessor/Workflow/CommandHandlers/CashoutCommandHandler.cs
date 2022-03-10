using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Common;
using Common.Log;
using Google.Protobuf.WellKnownTypes;
using JetBrains.Annotations;
using Lykke.Common.Log;
using Lykke.Cqrs;
using Lykke.Job.SiriusCashoutProcessor.Contract.Commands;
using Lykke.Job.SiriusCashoutProcessor.DomainServices;
using Swisschain.Extensions.Encryption;
using Swisschain.Sirius.Api.ApiClient;
using Swisschain.Sirius.Api.ApiContract.Account;
using Swisschain.Sirius.Api.ApiContract.User;
using Swisschain.Sirius.Api.ApiContract.WhitelistItems;
using Swisschain.Sirius.Api.ApiContract.Withdrawal;

namespace Lykke.Job.SiriusCashoutProcessor.Workflow.CommandHandlers
{
    public class CashoutCommandHandler
    {
        private readonly long _brokerAccountId;
        private readonly int _notEnoughBalanceRetryDelayInSeconds;
        private readonly IApiClient _siriusApiClient;
        private readonly PrivateKeyService _privateKeyService;
        private readonly ILog _log;
        private readonly AsymmetricEncryptionService _encryptionService;

        public CashoutCommandHandler(
            long brokerAccountId,
            IApiClient siriusApiClient,
            PrivateKeyService privateKeyService,
            ILogFactory logFactory,
            int notEnoughBalanceRetryDelayInSeconds)
        {
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

            WithdrawalDocument.WithdrawalDestinationTagType? tagType =
                !string.IsNullOrWhiteSpace(command.Tag)
                    ? (long.TryParse(command.Tag, out _)
                        ? WithdrawalDocument.WithdrawalDestinationTagType.Number
                        : WithdrawalDocument.WithdrawalDestinationTagType.Text)
                    : null;

            var document = new WithdrawalDocument
            {
                BrokerAccountId = _brokerAccountId,
                WithdrawalReferenceId = command.OperationId.ToString(),
                AssetId = command.SiriusAssetId,
                Amount = command.Amount,
                DestinationDetails = new WithdrawalDocument.WithdrawalDestinationDetails
                {
                    Address = command.Address,
                    Tag = tag,
                    TagType = tagType
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
                switch (result.Error.ErrorCode)
                {
                    case WithdrawalExecuteErrorResponseBody.Types.ErrorCode.IsNotAuthorized:
                    case WithdrawalExecuteErrorResponseBody.Types.ErrorCode.Unknown:
                        LogError(operationId, result.Error);
                        return CommandHandlingResult.Fail(TimeSpan.FromSeconds(10));
                    case WithdrawalExecuteErrorResponseBody.Types.ErrorCode.InvalidParameters:
                    case WithdrawalExecuteErrorResponseBody.Types.ErrorCode.NotFound:
                        LogError(operationId, result.Error);
                        return CommandHandlingResult.Ok(); // abort
                    case WithdrawalExecuteErrorResponseBody.Types.ErrorCode.NotEnoughBalance:
                        LogWarning(operationId, result.Error);
                        return CommandHandlingResult.Fail(TimeSpan.FromSeconds(_notEnoughBalanceRetryDelayInSeconds));
                    case WithdrawalExecuteErrorResponseBody.Types.ErrorCode.InvalidAddress:
                    case WithdrawalExecuteErrorResponseBody.Types.ErrorCode.AmountIsTooLow:
                        LogWarning(operationId, result.Error);
                        return CommandHandlingResult.Ok(); // abort
                }
            }

            _log.Info("Cashout sent to Sirius", context: new { operationId, withdrawal = result.Body.Withdrawal.ToJson()});

            return CommandHandlingResult.Ok();
        }

        private void LogWarning(string operationId, WithdrawalExecuteErrorResponseBody error)
        {
            _log.Warning(message: "Cashout to Sirius failed", context: new { operationId, error = error.ToJson() });
        }
        private void LogError(string operationId, WithdrawalExecuteErrorResponseBody error)
        {
            _log.Error(message: "Cashout to Sirius failed", context: new { operationId, error = error.ToJson() });
        }
    }
}
