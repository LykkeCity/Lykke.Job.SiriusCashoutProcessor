using System;
using System.Collections.Generic;
using Autofac;
using JetBrains.Annotations;
using Lykke.Common.Log;
using Lykke.Cqrs;
using Lykke.Cqrs.Configuration;
using Lykke.Cqrs.Middleware.Logging;
using Lykke.Job.SiriusCashoutProcessor.Contract;
using Lykke.Job.SiriusCashoutProcessor.Contract.Commands;
using Lykke.Job.SiriusCashoutProcessor.Contract.Events;
using Lykke.Job.SiriusCashoutProcessor.Settings;
using Lykke.Job.SiriusCashoutProcessor.Workflow.CommandHandlers;
using Lykke.Messaging;
using Lykke.Messaging.Contract;
using Lykke.Messaging.RabbitMq;
using Lykke.Messaging.Serialization;
using Lykke.SettingsReader;

namespace Lykke.Job.SiriusCashoutProcessor.Modules
{
    [UsedImplicitly]
    public class CqrsModule : Module
    {
        private readonly IReloadingManager<AppSettings> _settings;

        public CqrsModule(IReloadingManager<AppSettings> settings)
        {
            _settings = settings;
        }

        protected override void Load(ContainerBuilder builder)
        {
            MessagePackSerializerFactory.Defaults.FormatterResolver = MessagePack.Resolvers.ContractlessStandardResolver.Instance;
            var rabbitMqSettings = new RabbitMQ.Client.ConnectionFactory { Uri = new Uri(_settings.CurrentValue.SiriusCashoutProcessorJob.Cqrs.RabbitConnectionString) };

            builder.Register(context => new AutofacDependencyResolver(context)).As<IDependencyResolver>();
            builder.RegisterType<CashoutCommandHandler>()
                .WithParameter(TypedParameter.From(_settings.CurrentValue.SiriusApiServiceClient.BrokerAccountId))
                .SingleInstance();

            builder.Register(ctx => new MessagingEngine(ctx.Resolve<ILogFactory>(),
                new TransportResolver(new Dictionary<string, TransportInfo>
                {
                    {
                        "RabbitMq",
                        new TransportInfo(rabbitMqSettings.Endpoint.ToString(), rabbitMqSettings.UserName,
                            rabbitMqSettings.Password, "None", "RabbitMq")
                    }
                }),
                new RabbitMqTransportFactory(ctx.Resolve<ILogFactory>()))).As<IMessagingEngine>().SingleInstance();

            const string environment = "lykke";

            builder.Register(ctx =>
            {
                var engine = new CqrsEngine(ctx.Resolve<ILogFactory>(),
                    ctx.Resolve<IDependencyResolver>(),
                    ctx.Resolve<IMessagingEngine>(),
                    new DefaultEndpointProvider(),
                    true,
                    Register.DefaultEndpointResolver(new RabbitMqConventionEndpointResolver(
                        "RabbitMq",
                        SerializationFormat.MessagePack,
                        environment: environment)),

                    Register.EventInterceptors(new DefaultEventLoggingInterceptor(ctx.Resolve<ILogFactory>())),
                    Register.CommandInterceptors(new DefaultCommandLoggingInterceptor(ctx.Resolve<ILogFactory>())),

                    Register.BoundedContext(SiriusCashoutProcessorBoundedContext.Name)
                        .ListeningCommands(typeof(StartCashoutCommand))
                        .On("commands")
                        .WithCommandsHandler<CashoutCommandHandler>()
                        .PublishingEvents(
                            typeof (CashoutCompletedEvent),
                            typeof (CashoutFailedEvent)
                            )
                        .With("events")
                );

                engine.StartPublishers();

                return engine;
            })
            .As<ICqrsEngine>()
            .SingleInstance()
            .AutoActivate();
        }
    }
}
