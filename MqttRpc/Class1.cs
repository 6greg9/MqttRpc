namespace MqttRpc
{
    public class Class1
    {
        public async Task Test()
        {
            var rpc = new MqttRpcClient("broker.hivemq.com");
            await rpc.ConnectAsync();

            var response = await rpc.CallAsync("echo", new { message = "Hello" }, TimeSpan.FromSeconds(5));
            Console.WriteLine(response);
        }
    }
}
