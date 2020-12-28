using System;
using Autofac;
using AzureStorage.Tables;
using Common;
using JetBrains.Annotations;
using Lykke.Common.Log;
using Lykke.Job.SiriusCashoutProcessor.AzureRepositories;
using Lykke.Job.SiriusCashoutProcessor.Domain.Repositories;
using Lykke.Job.SiriusCashoutProcessor.Services;
using Lykke.Job.SiriusCashoutProcessor.Settings;
using Lykke.Sdk;
using Lykke.Sdk.Health;
using Lykke.Service.Assets.Client;
using Lykke.Service.Operations.Client;
using Lykke.SettingsReader;

namespace Lykke.Job.SiriusCashoutProcessor.Modules
{
    [UsedImplicitly]
    public class JobModule : Module
    {
        private readonly IReloadingManager<AppSettings> _settings;

        public JobModule(IReloadingManager<AppSettings> settings)
        {
            _settings = settings;
        }

        protected override void Load(ContainerBuilder builder)
        {
            builder.RegisterType<HealthService>()
                .As<IHealthService>()
                .SingleInstance();

            builder.RegisterType<StartupManager>()
                .As<IStartupManager>()
                .SingleInstance();

            builder.RegisterType<ShutdownManager>()
                .As<IShutdownManager>()
                .AutoActivate()
                .SingleInstance();

            builder.RegisterType<CashoutProcessorService>()
                .WithParameter(TypedParameter.From(_settings.CurrentValue.SiriusApiServiceClient.BrokerAccountId))
                .As<IStartable>()
                .As<IStopable>()
                .AutoActivate()
                .SingleInstance();

            builder.RegisterInstance(
                new Swisschain.Sirius.Api.ApiClient.ApiClient(_settings.CurrentValue.SiriusApiServiceClient.GrpcServiceUrl, _settings.CurrentValue.SiriusApiServiceClient.ApiKey)
            ).As<Swisschain.Sirius.Api.ApiClient.IApiClient>();

            builder.Register(ctx =>
                new LastCursorRepository(AzureTableStorage<CursorEntity>.Create(
                    _settings.ConnectionString(x => x.SiriusCashoutProcessorJob.Db.DataConnString),
                    "LastWithdrawalCursors", ctx.Resolve<ILogFactory>()))
            ).As<ILastCursorRepository>().SingleInstance();

            builder.Register(ctx =>
                new WithdrawalLogsRepository(AzureTableStorage<WithdrawalLogEntity>.Create(
                    _settings.ConnectionString(x => x.SiriusCashoutProcessorJob.Db.DataConnString),
                    "WithdrawalStateLogs", ctx.Resolve<ILogFactory>()))
            ).As<IWithdrawalLogsRepository>().SingleInstance();

            builder.Register(ctx =>
                new RefundsRepository(AzureTableStorage<RefundEntity>.Create(
                    _settings.ConnectionString(x => x.SiriusCashoutProcessorJob.Db.DataConnString),
                    "WithdrawalRefunds", ctx.Resolve<ILogFactory>()))
            ).As<IRefundsRepository>().SingleInstance();

            builder.RegisterMeClient(_settings.CurrentValue.MatchingEngineClient.IpEndpoint.GetClientIpEndPoint(), true);
            builder.RegisterAssetsClient(
                AssetServiceSettings.Create(
                    new Uri(_settings.CurrentValue.AssetsServiceClient.ServiceUrl),
                    _settings.CurrentValue.AssetsServiceClient.ExpirationPeriod));

            builder.RegisterOperationsClient(_settings.CurrentValue.OperationsServiceClient.ServiceUrl);
        }
    }
}
