using System.Net.Http;
using JD.Worker.Abstractions;
using Microsoft.Extensions.Logging;

namespace JD.Worker.Connectors.Http;

public sealed class HttpConnectorFactory : IConnectorFactory
{
    private readonly IHttpClientFactory _clientFactory;
    private readonly ILoggerFactory _loggerFactory;

    public HttpConnectorFactory(IHttpClientFactory clientFactory, ILoggerFactory loggerFactory)
    {
        _clientFactory = clientFactory;
        _loggerFactory = loggerFactory;
    }

    public string TypeName => "Http";

    public ICncConnector Create(IReadOnlyDictionary<string, object?> settings)
    {
        var options = HttpConnectorOptions.FromSettings(settings);
        var client = _clientFactory.CreateClient();
        client.BaseAddress = options.BaseUri;
        return new HttpCncConnector(options, client, _loggerFactory.CreateLogger<HttpCncConnector>());
    }
}
