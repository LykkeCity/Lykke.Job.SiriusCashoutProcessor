using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Common;
using Common.Log;
using Grpc.Core;
using Lykke.Common.Log;
using Lykke.Cqrs;
using Lykke.Job.SiriusCashoutProcessor.Contract;
using Lykke.Job.SiriusCashoutProcessor.Contract.Events;
using Lykke.Job.SiriusCashoutProcessor.Domain;
using Lykke.Job.SiriusCashoutProcessor.Domain.Repositories;
using Lykke.MatchingEngine.Connector.Abstractions.Services;
using Lykke.MatchingEngine.Connector.Models.Api;
using Lykke.Service.Assets.Client;
using Lykke.Service.Assets.Client.Models;
using Lykke.Service.Operations.Client;
using Newtonsoft.Json;
using Polly;
using Swisschain.Sirius.Api.ApiClient;
using Swisschain.Sirius.Api.ApiContract.Withdrawal;

namespace Lykke.Job.SiriusCashoutProcessor.Services
{
    public class CashoutProcessorService : IStartable, IStopable
    {
        private readonly ILastCursorRepository _lastCursorRepository;
        private readonly IWithdrawalLogsRepository _withdrawalLogsRepository;
        private readonly IRefundsRepository _refundsRepository;
        private readonly IMatchingEngineClient _meClient;
        private readonly IAssetsServiceWithCache _assetsService;
        private readonly IApiClient _apiClient;
        private readonly IOperationsClient _operationsClient;
        private readonly ICqrsEngine _cqrsEngine;
        private readonly long _brokerAccountId;
        private long? _lastCursor;
        private readonly ILog _log;
        private CancellationTokenSource _cancellationTokenSource;

        public CashoutProcessorService(
            ILastCursorRepository lastCursorRepository,
            IWithdrawalLogsRepository withdrawalLogsRepository,
            IRefundsRepository refundsRepository,
            IMatchingEngineClient meClient,
            IAssetsServiceWithCache assetsService,
            IApiClient apiClient,
            IOperationsClient operationsClient,
            ICqrsEngine cqrsEngine,
            long brokerAccountId,
            ILogFactory logFactory
            )
        {
            _lastCursorRepository = lastCursorRepository;
            _withdrawalLogsRepository = withdrawalLogsRepository;
            _refundsRepository = refundsRepository;
            _meClient = meClient;
            _assetsService = assetsService;
            _apiClient = apiClient;
            _operationsClient = operationsClient;
            _cqrsEngine = cqrsEngine;
            _brokerAccountId = brokerAccountId;
            _log = logFactory.CreateLog(this);
        }

        public void Start()
        {
            Task.Run(async () => await ProcessCashoutsAsync());
        }

        public void Dispose()
        {
            _cancellationTokenSource?.Cancel();
        }

        public void Stop()
        {
            _cancellationTokenSource?.Cancel();
        }

        private async Task ProcessCashoutsAsync()
        {
            _cancellationTokenSource = new CancellationTokenSource();

            while (!_cancellationTokenSource.IsCancellationRequested)
            {
                _lastCursor = await _lastCursorRepository.GetAsync(_brokerAccountId);
                var assets = await _assetsService.GetAllAssetsAsync(false, _cancellationTokenSource.Token);

                var streamId = Guid.NewGuid().ToString();
                try
                {
                    var request = new WithdrawalUpdateSearchRequest
                    {
                        StreamId = streamId,
                        BrokerAccountId = _brokerAccountId,
                        Cursor = _lastCursor
                    };

                    _log.Info("Getting updates...", context: new
                    {
                        StreamId = request.StreamId,
                        BrokerAccountId = request.BrokerAccountId,
                        Cursor = request.Cursor,
                    });

                    var updates = _apiClient.Withdrawals.GetUpdates(request);

                    while (await updates.ResponseStream.MoveNext(_cancellationTokenSource.Token).ConfigureAwait(false))
                    {
                        WithdrawalUpdateArrayResponse update = updates.ResponseStream.Current;

                        if (!update.Items.Any())
                        {
                            _log.Warning("Empty collection of update items:", context: new
                            {
                                StreamId = request.StreamId,
                                BrokerAccountId = request.BrokerAccountId,
                                Cursor = request.Cursor,
                            });
                        }
                        
                        foreach (var item in update.Items)
                        {
                            if (item.WithdrawalUpdateId <= _lastCursor)
                                continue;

                            
                            if (string.IsNullOrWhiteSpace(item.Withdrawal.GetUserNativeId()))
                            {
                                _log.Warning("UserNativeId is empty", context: new
                                {
                                    StreamId = request.StreamId,
                                    BrokerAccountId = request.BrokerAccountId,
                                    Cursor = request.Cursor,
                                });
                                _log.Warning(message: "Withdrawal update body", context: new
                                    {
                                       Item = item.Withdrawal.ToJson(),
                                       StreamId = request.StreamId,
                                       BrokerAccountId = request.BrokerAccountId,
                                       Cursor = request.Cursor,
                                    });
                                continue;
                            }

                            _log.Info("Withdrawal update", context: new
                            {
                                Withdrawal = item.ToJson(),
                                StreamId = request.StreamId,
                                BrokerAccountId = request.BrokerAccountId,
                                Cursor = request.Cursor,
                            });

                            Asset asset = assets.FirstOrDefault(x => x.SiriusAssetId == item.Withdrawal.AssetId);

                            if (asset == null)
                            {
                                _log.Warning(
                                    "Lykke asset not found", 
                                    context: new
                                    {
                                        siriusAssetId = item.Withdrawal.AssetId,
                                        withdrawalId = item.Withdrawal.Id,
                                        StreamId = request.StreamId,
                                        BrokerAccountId = request.BrokerAccountId,
                                        Cursor = request.Cursor,
                                    });
                                continue;
                            }

                            await _withdrawalLogsRepository.AddAsync(item.Withdrawal.TransferContext.WithdrawalReferenceId, $"Withdrawal update (state: {item.Withdrawal.State.ToString()})",
                                new
                                {
                                    siriusWithdrawalId = item.Withdrawal.Id,
                                    clientId = item.Withdrawal.GetUserNativeId(),
                                    walletId = item.Withdrawal.AccountReferenceId == item.Withdrawal.GetUserNativeId() ? item.Withdrawal.GetUserNativeId() : item.Withdrawal.AccountReferenceId,
                                    fees = item.Withdrawal.ActualFees.ToJson(),
                                    item.Withdrawal.State,
                                    TransactionHash = item.Withdrawal.TransactionInfo?.TransactionId
                                }.ToJson()
                            );

                            if (!Guid.TryParse(item.Withdrawal.TransferContext.WithdrawalReferenceId, out var operationId))
                            {
                                operationId = Guid.Empty;
                            }

                            Guid? walletId = item.Withdrawal.AccountReferenceId == item.Withdrawal.GetUserNativeId() ? null : Guid.Parse(item.Withdrawal.AccountReferenceId);

                            switch (item.Withdrawal.State)
                            {
                                case WithdrawalState.Completed:
                                    _cqrsEngine.PublishEvent(new CashoutCompletedEvent
                                    {
                                        OperationId = operationId,
                                        ClientId = Guid.Parse(item.Withdrawal.GetUserNativeId()),
                                        WalletId = walletId,
                                        AssetId = asset.Id,
                                        Amount = Convert.ToDecimal(item.Withdrawal.Amount.Value),
                                        Address = item.Withdrawal.DestinationDetails.Address,
                                        Tag = item.Withdrawal.DestinationDetails.Tag,
                                        TransactionHash = item.Withdrawal.TransactionInfo?.TransactionId,
                                        Timestamp = item.Withdrawal.UpdatedAt.ToDateTime().ToUniversalTime(),
                                    }, SiriusCashoutProcessorBoundedContext.Name);

                                    await _lastCursorRepository.AddAsync(_brokerAccountId, item.WithdrawalUpdateId);
                                    _lastCursor = item.WithdrawalUpdateId;
                                    break;
                                case WithdrawalState.Failed:
                                    await _withdrawalLogsRepository.AddAsync(
                                        item.Withdrawal.TransferContext.WithdrawalReferenceId,
                                        "Withdrawal failed, finishing without Refund",
                                        null);
                                    await _lastCursorRepository.AddAsync(_brokerAccountId, item.WithdrawalUpdateId);
                                    _lastCursor = item.WithdrawalUpdateId;
                                    break;
                                case WithdrawalState.Rejected:
                                case WithdrawalState.Refunded:
                                {
                                    await _withdrawalLogsRepository.AddAsync(
                                        item.Withdrawal.TransferContext.WithdrawalReferenceId,
                                        "Withdrawal failed, processing refund in ME",
                                        new {WithdrawalError = item.Withdrawal.Error?.ToJson()}.ToJson());

                                    decimal amount = Convert.ToDecimal(item.Withdrawal.Amount.Value);

                                    var operation = await _operationsClient.Get(operationId);

                                    var operationContext = JsonConvert.DeserializeObject<OperationContext>(operation.ContextJson);

                                    decimal fee = operationContext.Fee.Type == "Absolute"
                                        ? operationContext.Fee.Size.TruncateDecimalPlaces(asset.Accuracy, true)
                                        : (amount * operationContext.Fee.Size).TruncateDecimalPlaces(asset.Accuracy, true);

                                    var refund = await _refundsRepository.GetAsync(item.Withdrawal.GetUserNativeId(),
                                                     item.Withdrawal.TransferContext.WithdrawalReferenceId) ??
                                                 await _refundsRepository.AddAsync(
                                                     item.Withdrawal.TransferContext.WithdrawalReferenceId,
                                                     item.Withdrawal.GetUserNativeId(),
                                                     walletId?.ToString() ?? item.Withdrawal.GetUserNativeId(),
                                                     operationContext.GlobalSettings.FeeSettings.TargetClients.Cashout,
                                                     asset.Id,
                                                     asset.SiriusAssetId,
                                                     amount, fee);

                                    if (refund.FeeAmount > 0)
                                    {
                                        var feeReturnResult = await ReturnFeeAsync(refund);

                                        if (feeReturnResult != null && (feeReturnResult.Status == MeStatusCodes.Ok || feeReturnResult.Status == MeStatusCodes.Duplicate))
                                        {
                                            _log.Info("Fee cashed out from fee wallet", new
                                            {
                                                OperationId = refund.FeeOperationId,
                                                StreamId = request.StreamId,
                                                BrokerAccountId = request.BrokerAccountId,
                                                Cursor = request.Cursor,
                                            });

                                            await _withdrawalLogsRepository.AddAsync(refund.Id, "Fee cashed out from fee wallet",
                                                new {refund.FeeOperationId, refund.FeeClientId, refund.FeeAmount, refund.AssetId}.ToJson());
                                        }
                                        else
                                        {
                                            _log.Info("Can't cashout fee from fee wallet", context: new
                                            {
                                                OperationId = refund.FeeOperationId,
                                                StreamId = request.StreamId,
                                                BrokerAccountId = request.BrokerAccountId,
                                                Cursor = request.Cursor,
                                            });

                                            await _withdrawalLogsRepository.AddAsync(refund.Id, "Can't cashout fee from fee wallet",
                                                new {refund.FeeOperationId, refund.FeeClientId, refund.FeeAmount, refund.AssetId,
                                                    error = feeReturnResult == null
                                                    ? "response from ME is null"
                                                    : $"{feeReturnResult.Status}: {feeReturnResult.Message}"}.ToJson());
                                        }
                                    }

                                    var result = await RefundAsync(refund);

                                    if (result != null && (result.Status == MeStatusCodes.Ok || result.Status == MeStatusCodes.Duplicate))
                                    {
                                        _log.Info("Refund processed", context: new
                                        {
                                            OperationId = refund.OperationId,
                                            StreamId = request.StreamId,
                                            BrokerAccountId = request.BrokerAccountId,
                                            Cursor = request.Cursor,
                                        });

                                        await _refundsRepository.UpdateAsync(refund.ClientId, refund.Id, WithdrawalState.Completed.ToString());

                                        await _withdrawalLogsRepository.AddAsync(refund.Id, "Refund processed in ME",
                                            new {result.Status, refund.OperationId}.ToJson());

                                        _cqrsEngine.PublishEvent(new CashoutFailedEvent
                                        {
                                            OperationId = refund.Id,
                                            RefundId = refund.OperationId,
                                            Status = MeStatusCodes.Ok.ToString()
                                        }, SiriusCashoutProcessorBoundedContext.Name);
                                        await _lastCursorRepository.AddAsync(_brokerAccountId, item.WithdrawalUpdateId);
                                        _lastCursor = item.WithdrawalUpdateId;
                                    }
                                    else
                                    {
                                        _log.Info("Refund failed", context: new
                                        {
                                            OperationId = refund.OperationId,
                                            StreamId = request.StreamId,
                                            BrokerAccountId = request.BrokerAccountId,
                                            Cursor = request.Cursor,
                                        });

                                        await _refundsRepository.UpdateAsync(refund.ClientId, refund.Id, WithdrawalState.Failed.ToString());

                                        await _withdrawalLogsRepository.AddAsync(refund.Id, "Refund in ME failed",
                                            new
                                            {
                                                Error = result == null
                                                    ? "response from ME is null"
                                                    : $"{result.Status}: {result.Message}",
                                                OperationId = refund.Id
                                            }.ToJson());

                                        _cqrsEngine.PublishEvent(new CashoutFailedEvent
                                        {
                                            OperationId = refund.Id,
                                            RefundId = refund.OperationId,
                                            Status = result?.Status.ToString(),
                                            Error = result == null
                                                ? "response from ME is null"
                                                : result.Message
                                        }, SiriusCashoutProcessorBoundedContext.Name);

                                        await _lastCursorRepository.AddAsync(_brokerAccountId, item.WithdrawalUpdateId);
                                        _lastCursor = item.WithdrawalUpdateId;
                                    }

                                    break;
                                }
                            }
                        }
                    }

                    _log.Info("End of stream", context: new { request.StreamId });
                }
                catch (RpcException ex)
                {
                    if (ex.StatusCode == StatusCode.ResourceExhausted)
                    {
                        _log.Warning($"Rate limit has been reached. Waiting 1 minute...", ex, context: new
                        {
                            StreamId = streamId
                        });
                        await Task.Delay(60000);
                    }
                    else
                    {
                        _log.Warning($"RpcException. {ex.Status}; {ex.StatusCode}", ex, context: new
                        {
                            StreamId = streamId
                        });
                    }
                }
                catch (Exception ex)
                {
                    _log.Error(ex);
                }

                await Task.Delay(5000);
            }
        }

        private async Task<MeResponseModel> RefundAsync(IRefund refund)
        {
            var policy = Policy
                .Handle<TaskCanceledException>(exception =>
                {
                    _log.Warning($"Retry on TaskCanceledException", context: $"clientId = {refund.ClientId}, assetId = {refund.AssetId}, amount = {refund.Amount + refund.FeeAmount}");
                    return true;
                })
                .OrResult<MeResponseModel>(r =>
                {
                    _log.Warning($"Response from ME: {(r == null ? "null" : r.ToJson())}");
                    _withdrawalLogsRepository.AddAsync(refund.Id, "Response from ME",
                        new {refund.ClientId, refund.OperationId, refund.AssetId, refund.Amount, refund.FeeAmount, meResponse = r == null ? "null" : r.ToJson()}.ToJson()).GetAwaiter().GetResult();
                    return r == null || r.Status == MeStatusCodes.Runtime;
                })
                .WaitAndRetryAsync(5, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));

            var result = await policy.ExecuteAsync(async() =>
            {
                await _withdrawalLogsRepository.AddAsync(refund.Id, "Send refund to ME",
                    new {refund.ClientId, refund.OperationId, refund.AssetId, refund.Amount}.ToJson()
                );

                var res = await _meClient.CashInOutAsync(refund.OperationId,
                    refund.WalletId ?? refund.ClientId,
                    refund.AssetId,
                    Convert.ToDouble(refund.Amount + refund.FeeAmount)
                );

                return res;
            });

            return result;
        }

        private async Task<MeResponseModel> ReturnFeeAsync(IRefund refund)
        {
            var policy = Policy
                .Handle<TaskCanceledException>(exception =>
                {
                    _log.Warning("Retry on TaskCanceledException", context: $"feeClientId = {refund.FeeClientId}, assetId = {refund.AssetId}, amount = {refund.FeeAmount}");
                    return true;
                })
                .OrResult<MeResponseModel>(r =>
                {
                    _log.Warning($"Response from ME: {(r == null ? "null" : r.ToJson())}");
                    _withdrawalLogsRepository.AddAsync(refund.Id, "Response from ME",
                        new {refund.FeeOperationId, refund.AssetId, refund.FeeAmount, meResponse = r == null ? "null" : r.ToJson()}.ToJson()).GetAwaiter().GetResult();
                    return r == null || r.Status == MeStatusCodes.Runtime;
                })
                .WaitAndRetryAsync(5, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));

            var result = await policy.ExecuteAsync(async() =>
            {
                await _withdrawalLogsRepository.AddAsync(refund.Id, "Send cash out for fee to ME",
                    new {refund.FeeClientId, refund.FeeOperationId, refund.AssetId, refund.FeeAmount}.ToJson()
                );

                var res = await _meClient.CashInOutAsync(
                    refund.FeeOperationId,
                    refund.FeeClientId,
                    refund.AssetId,
                    -Convert.ToDouble(refund.FeeAmount)
                );

                return res;
            });

            return result;
        }
    }
}
