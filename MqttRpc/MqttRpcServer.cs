using MQTTnet.Diagnostics.Logger;
using MQTTnet.Protocol;
using MQTTnet.Server;
using System.Reflection;
using System.Text;
using System.Text.Json;
using static System.Net.Mime.MediaTypeNames;

namespace MqttRpc;

public class MqttRpcServer
{
    private MqttServer _mqttServer;
    public MqttRpcServer() {
        var mqttFactory = new MqttServerFactory();
        var mqttServerOptions = mqttFactory.CreateServerOptionsBuilder().Build();
        _mqttServer = mqttFactory.CreateMqttServer(mqttServerOptions);
        
        _mqttServer.InterceptingPublishAsync += async e =>
        {
            var topic = e.ApplicationMessage.Topic;
            if (topic.StartsWith("rpc/request/"))
            {
                Console.WriteLine($"Received request on topic: {topic}");

                var requestJson = Encoding.UTF8.GetString(e.ApplicationMessage.Payload);
                var request = JsonSerializer.Deserialize<RpcRequest>(requestJson);

                Console.WriteLine($"RequestId: {request.requestId}, Method: {request.method}");

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
        };


    }
}
public class RpcRequest
{
    public string requestId { get; set; } = string.Empty;
    public string method { get; set; } = string.Empty;
    public JsonElement payload { get; set; }
    public long timestamp { get; set; }
}
