using JD.Worker.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace JD.Worker.Connectors.Http;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddHttpConnector(this IServiceCollection services)
    {
        services.AddHttpClient();
        services.AddSingleton<IConnectorFactory, HttpConnectorFactory>();
        return services;
    }
}
