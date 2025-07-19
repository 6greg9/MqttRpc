using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace MqttRpc;

public class RpcDispatcher
{
    private readonly Dictionary<string, IRpcMethodHandler> _handlers;

    public RpcDispatcher(IEnumerable<IRpcMethodHandler> handlers)
    {
        _handlers = handlers.ToDictionary(h => h.MethodName);
    }

    public async Task<object> InvokeAsync(string method, JsonElement @params)
    {
        if (_handlers.TryGetValue(method, out var handler))
        {
            return await handler.HandleAsync(@params);
        }

        return new { error = "Unknown method" };
    }
}

