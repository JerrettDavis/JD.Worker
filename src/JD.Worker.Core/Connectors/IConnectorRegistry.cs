using System.Collections.Generic;
using JD.Worker.Abstractions;

namespace JD.Worker.Core;

public interface IConnectorRegistry
{
    void Register(ICncConnector connector);
    bool TryGet(string name, out ICncConnector connector);
    IReadOnlyCollection<ICncConnector> List();
}
