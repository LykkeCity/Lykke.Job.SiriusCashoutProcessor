using JetBrains.Annotations;
using Lykke.Job.SiriusCashoutProcessor.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Antares.Sdk;
using Autofac;
using Lykke.SettingsReader;
using Microsoft.Extensions.Configuration;

namespace Lykke.Job.SiriusCashoutProcessor
{
    [UsedImplicitly]
    public class Startup
    {
        private readonly LykkeSwaggerOptions _swaggerOptions = new LykkeSwaggerOptions
        {
            ApiTitle = "SiriusCashoutProcessorJob API",
            ApiVersion = "v1"
        };

        private IReloadingManagerWithConfiguration<AppSettings> _settings;
        private LykkeServiceOptions<AppSettings> _lykkeOptions;

        [UsedImplicitly]
        public void ConfigureServices(IServiceCollection services)
        {
            (_lykkeOptions, _settings) = services.ConfigureServices<AppSettings>(options =>
            {
                options.SwaggerOptions = _swaggerOptions;

                options.Logs = logs =>
                {
                    logs.AzureTableName = "SiriusCashoutProcessorJobLog";
                    logs.AzureTableConnectionStringResolver = settings => settings.SiriusCashoutProcessorJob.Db.LogsConnString;
                };
            });
        }

        [UsedImplicitly]
        public void Configure(IApplicationBuilder app)
        {
            app.UseLykkeConfiguration(options =>
            {
                options.SwaggerOptions = _swaggerOptions;
            });
        }

        [UsedImplicitly]
        public void ConfigureContainer(ContainerBuilder builder)
        {
            var configurationRoot = new ConfigurationBuilder()
                .AddEnvironmentVariables()
                .Build();

            builder.ConfigureContainerBuilder(_lykkeOptions, configurationRoot, _settings);
        }
    }
}
