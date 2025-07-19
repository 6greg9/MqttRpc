using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace MqttRpc;

public interface IRpcMethodHandler
{
    string MethodName { get; }
    Task<object> HandleAsync(JsonElement @params);
}
public class GetStatusHandler : IRpcMethodHandler
{
    public string MethodName => "device/getStatus";

    public Task<object> HandleAsync(JsonElement @params)
    {
        var result = new { status = "OK", temperature = 23.4 };
        return Task.FromResult((object)result);
    }
}

public class RebootHandler : IRpcMethodHandler
{
    public string MethodName => "device/reboot";

    public Task<object> HandleAsync(JsonElement @params)
    {
        var result = new { status = "Rebooting" };
        return Task.FromResult((object)result);
    }
}

