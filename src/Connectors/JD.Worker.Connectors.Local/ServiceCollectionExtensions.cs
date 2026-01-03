using JD.Worker.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace JD.Worker.Connectors.Local;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLocalConnector(this IServiceCollection services)
    {
        services.AddSingleton<IConnectorFactory, LocalConnectorFactory>();
        return services;
    }
}
