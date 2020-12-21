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

                try
                {
                    var request = new WithdrawalUpdateSearchRequest
                    {
                        BrokerAccountId = _brokerAccountId,
                        Cursor = _lastCursor
                    };

                    _log.Info("Getting updates...", context: $"request: {request.ToJson()}");

                    var updates = _apiClient.Withdrawals.GetUpdates(request);

                    while (await updates.ResponseStream.MoveNext(_cancellationTokenSource.Token).ConfigureAwait(false))
                    {
                        WithdrawalUpdateArrayResponse update = updates.ResponseStream.Current;

                        foreach (var item in update.Items)
                        {
                            if (item.WithdrawalUpdateId <= _lastCursor)
                                continue;

                            if (string.IsNullOrEmpty(item.Withdrawal.AccountReferenceId))
                                continue;

                            _log.Info("Withdrawal update", context: $"withdrawal: {item.ToJson()}");

                            string assetId = assets.FirstOrDefault(x => x.SiriusAssetId == item.Withdrawal.AssetId)?.Id;

                            if (string.IsNullOrEmpty(assetId))
                            {
                                _log.Warning("Lykke asset not found", context: new {siriusAssetId = item.Withdrawal.AssetId, withdrawalId = item.Withdrawal.Id});
                                continue;
                            }

                            await _withdrawalLogsRepository.AddAsync(item.Withdrawal.TransferContext.WithdrawalReferenceId, $"Withdrawal update (state: {item.Withdrawal.State.ToString()})",
                                new
                                {
                                    siriusWithdrawalId = item.Withdrawal.Id,
                                    clientId = item.Withdrawal.TransferContext.AccountReferenceId,
                                    fees = item.Withdrawal.Fee.ToJson(),
                                    item.Withdrawal.State,
                                    TransactionHash = item.Withdrawal.TransactionInfo.TransactionId
                                }.ToJson()
                            );

                            switch (item.Withdrawal.State)
                            {
                                case WithdrawalState.Completed:
                                    _cqrsEngine.PublishEvent(new CashoutCompletedEvent
                                    {
                                        OperationId = Guid.Parse(item.Withdrawal.TransferContext.WithdrawalReferenceId),
                                        ClientId = Guid.Parse(item.Withdrawal.AccountReferenceId),
                                        AssetId = assetId,
                                        Amount = Convert.ToDecimal(item.Withdrawal.Amount.Value),
                                        Address = item.Withdrawal.DestinationDetails.Address,
                                        Tag = item.Withdrawal.DestinationDetails.Tag,
                                        TransactionHash = item.Withdrawal.TransactionInfo.TransactionId,
                                        Timestamp = item.Withdrawal.UpdatedAt.ToDateTime().ToUniversalTime(),
                                    }, SiriusCashoutProcessorBoundedContext.Name);

                                    await _lastCursorRepository.AddAsync(_brokerAccountId, item.WithdrawalUpdateId);
                                    _lastCursor = item.WithdrawalUpdateId;
                                    break;
                                case WithdrawalState.Failed:
                                case WithdrawalState.Rejected:
                                {
                                    var document = JsonConvert.DeserializeObject<WithdrawalDocument>(item.Withdrawal.TransferContext.Document);

                                    var asset = assets.FirstOrDefault(x => x.SiriusAssetId == document.AssetId);

                                    if (asset == null)
                                    {
                                        _log.Error(message: $"Can't find the asset for sirius assetId = {document.AssetId}");
                                        return;
                                    }

                                    await _withdrawalLogsRepository.AddAsync(
                                        item.Withdrawal.TransferContext.WithdrawalReferenceId,
                                        "Withdrawal failed, processing refund in ME",
                                        new {WithdrawalError = item.Withdrawal.Error?.ToJson()}.ToJson());

                                    var refund = await _refundsRepository.GetAsync(item.Withdrawal.AccountReferenceId,
                                                     item.Withdrawal.TransferContext.WithdrawalReferenceId) ??
                                                 await _refundsRepository.AddAsync(
                                                     item.Withdrawal.TransferContext.WithdrawalReferenceId,
                                                     item.Withdrawal.AccountReferenceId,
                                                     asset.Id,
                                                     asset.SiriusAssetId,
                                                     Convert.ToDecimal(item.Withdrawal.Amount.Value));

                                    var policy = Policy
                                        .Handle<TaskCanceledException>(exception =>
                                        {
                                            _log.Warning($"Retry on TaskCanceledException", context: $"clientId = {refund.ClientId}, assetId = {assetId}, amount = {refund.Amount}");
                                            return true;
                                        })
                                        .OrResult<MeResponseModel>(r =>
                                        {
                                            _log.Warning($"Response from ME: {(r == null ? "null" : r.ToJson())}");
                                            _withdrawalLogsRepository.AddAsync(item.Withdrawal.TransferContext.WithdrawalReferenceId, "Response from ME",
                                                new {refund.ClientId, refund.OperationId, assetId, refund.Amount, meResponse = r == null ? "null" : r.ToJson()}.ToJson()).GetAwaiter().GetResult();
                                            return r == null || r.Status == MeStatusCodes.Runtime;
                                        })
                                        .WaitAndRetryAsync(5, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));

                                    var result = await policy.ExecuteAsync(async() =>
                                    {
                                        await _withdrawalLogsRepository.AddAsync(refund.Id, "Send refund to ME",
                                            new {refund.ClientId, refund.OperationId, assetId, refund.Amount}.ToJson()
                                        );

                                        var res = await _meClient.CashInOutAsync(refund.OperationId,
                                            refund.ClientId,
                                            assetId,
                                            Convert.ToDouble(refund.Amount)
                                        );

                                        return res;
                                    });

                                    if (result != null && (result.Status == MeStatusCodes.Ok || result.Status == MeStatusCodes.Duplicate))
                                    {
                                        _log.Info("Refund processed", context: $"operationId = {refund.OperationId}");

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
                                        _log.Info("Refund failed", context: $"operationId = {refund.OperationId}");

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

                    _log.Info("End of stream");
                }
                catch (RpcException ex)
                {
                    if (ex.StatusCode == StatusCode.ResourceExhausted)
                    {
                        _log.Warning($"Rate limit has been reached. Waiting 1 minute...", ex);
                        await Task.Delay(60000);
                    }
                    else
                    {
                        _log.Warning($"RpcException. {ex.Status}; {ex.StatusCode}", ex);
                    }
                }
                catch (Exception ex)
                {
                    _log.Error(ex);
                }

                await Task.Delay(5000);
            }
        }
    }
}
