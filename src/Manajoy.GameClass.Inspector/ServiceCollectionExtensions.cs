using Manajoy.GameClass.Inspector.Analysis;
using Manajoy.GameClass.Inspector.Configuration;
using Manajoy.GameClass.Inspector.Discovery;
using Manajoy.GameClass.Inspector.Discovery.Sources;
using Manajoy.GameClass.Inspector.Infrastructure;
using Manajoy.GameClass.Inspector.Infrastructure.Logging;
using Manajoy.GameClass.Inspector.Inspection;
using Manajoy.GameClass.Inspector.Output;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Manajoy.GameClass.Inspector;

internal static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInspector(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<InspectorOptions>().Bind(configuration.GetSection(InspectorOptions.SectionName));
        services.AddSingleton<IValidateOptions<InspectorOptions>, InspectorOptionsValidator>();

        services.AddSingleton<InspectionErrors>();
        services.AddSingleton<EnvironmentInfoProvider>();
        services.AddSingleton<SqlConnectionFactory>();
        services.AddSingleton<ReadOnlyQueryExecutor>();

        // Order matters: primary sources first, fallback candidates last.
        services.AddSingleton<ISqlInstanceSource, WindowsServiceSource>();
        services.AddSingleton<ISqlInstanceSource, RegistrySource>();
        services.AddSingleton<ISqlInstanceSource, ConfiguredServerSource>();
        services.AddSingleton<ISqlInstanceSource, CommonCandidateSource>();
        services.AddSingleton<SqlInstanceDiscovery>();
        services.AddSingleton<ConnectionProbe>();
        services.AddSingleton<DatabaseDiscovery>();

        services.AddSingleton<ColumnInspector>();
        services.AddSingleton<TableInspector>();
        services.AddSingleton<ViewInspector>();
        services.AddSingleton<RoutineInspector>();
        services.AddSingleton<SchemaInspector>();
        services.AddSingleton<GameClassCandidateScorer>();
        services.AddSingleton<SampleRowInspector>();

        services.AddSingleton<ConsoleReporter>();
        services.AddSingleton<JsonReporter>();
        services.AddSingleton<InspectionRunner>();
        return services;
    }

    public static ILoggingBuilder AddInspectorLogging(this ILoggingBuilder logging, IConfiguration configuration)
    {
        // Explicit providers only: the default EventLog provider would write to the Windows event log.
        logging.ClearProviders();
        logging.AddSimpleConsole(console =>
        {
            console.SingleLine = true;
            console.TimestampFormat = "HH:mm:ss ";
        });

        var logDirectory = configuration[$"{InspectorOptions.SectionName}:{nameof(InspectorOptions.LogDirectory)}"];
        logging.Services.AddSingleton<ILoggerProvider>(_ =>
            new FileLoggerProvider(string.IsNullOrWhiteSpace(logDirectory) ? new InspectorOptions().LogDirectory : logDirectory));
        return logging;
    }
}
