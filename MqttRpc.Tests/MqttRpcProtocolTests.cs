using MqttRpc;

namespace MqttRpc.Tests;

public sealed class MqttRpcProtocolTests
{
    [Fact]
    public void RequestTopicRoundTripsMethod()
    {
        var topic = MqttRpcProtocol.BuildRequestTopic("device/getStatus");

        var matched = MqttRpcProtocol.TryGetMethod(topic, out var method);

        Assert.True(matched);
        Assert.Equal("device/getStatus", method);
    }

    [Theory]
    [InlineData("device//status")]
    [InlineData("device/+/status")]
    [InlineData("device/#")]
    [InlineData("device/status value")]
    public void InvalidMethodIsRejected(string method)
    {
        Assert.Throws<ArgumentException>(() => MqttRpcProtocol.BuildRequestTopic(method));
    }

    [Fact]
    public void ReplyTopicMustBelongToSingleClient()
    {
        Assert.True(MqttRpcProtocol.IsReplyTopic("mqttrpc/v1/reply/web-a1"));
        Assert.False(MqttRpcProtocol.IsReplyTopic("mqttrpc/v1/reply/+"));
        Assert.False(MqttRpcProtocol.IsReplyTopic("other/reply/web-a1"));
    }
}
