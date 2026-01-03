using JD.Worker.Abstractions;
using Microsoft.Extensions.Logging;

namespace JD.Worker.Connectors.Local;

public sealed class LocalConnectorFactory : IConnectorFactory
{
    private readonly ILoggerFactory _loggerFactory;

    public LocalConnectorFactory(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public string TypeName => "Local";

    public ICncConnector Create(IReadOnlyDictionary<string, object?> settings)
    {
        var options = LocalConnectorOptions.FromSettings(settings);
        return new LocalCncConnector(options, _loggerFactory.CreateLogger<LocalCncConnector>());
    }
}
