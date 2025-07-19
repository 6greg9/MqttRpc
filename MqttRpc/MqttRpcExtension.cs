using MQTTnet.Server;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace MqttRpc;

public class MqttRpcExtension
{
    public static void HandleRpcRequest(InterceptingPublishEventArgs e)
    {
        var topic = e.ApplicationMessage.Topic;
        if (topic.StartsWith("rpc/request/"))
        {
            Console.WriteLine($"Received request on topic: {topic}");

            var requestJson = Encoding.UTF8.GetString(e.ApplicationMessage.Payload);
            var request = JsonSerializer.Deserialize<RpcRequest>(requestJson);

            Console.WriteLine($"RequestId: {request.requestId}, Method: {request.method}");

            // 呼叫對應 Handler
           // var response = await _rpcDispatcher.InvokeAsync(request.Method, request.Params);
            // 模擬處理不同方法
            object responseData = request.method switch
            {
                "device/getStatus" => new { status = "OK", temperature = 23.4 },
                "device/reboot" => new { status = "Rebooting" },
                _ => new { error = "Unknown method" }
            };

            var responseJson = JsonSerializer.Serialize(responseData);

            // 回傳 topic
            var responseTopic = $"rpc/response/{request.requestId}";

            e.ApplicationMessage.Topic = responseTopic;

            e.ApplicationMessage.PayloadSegment = Encoding.UTF8.GetBytes(responseJson);


            Console.WriteLine($"Response sent to {responseTopic}");
        }
    }
}
