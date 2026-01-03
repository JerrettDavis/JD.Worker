using System.Collections.Concurrent;
using System.Collections.Generic;
using JD.Worker.Abstractions;

namespace JD.Worker.Core;

public sealed class InMemoryConnectorRegistry : IConnectorRegistry
{
    private readonly ConcurrentDictionary<string, ICncConnector> _connectors = new(StringComparer.OrdinalIgnoreCase);

    public void Register(ICncConnector connector)
    {
        _connectors[connector.Name] = connector;
    }

    public bool TryGet(string name, out ICncConnector connector) =>
        _connectors.TryGetValue(name, out connector!);

    public IReadOnlyCollection<ICncConnector> List() => _connectors.Values.ToList();
}
