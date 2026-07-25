namespace MqttRpc;

/// <summary>
/// Validates names and constructs topics for the MqttRpc v1 wire protocol.
/// </summary>
public static class MqttRpcProtocol
{
    /// <summary>
    /// The default root used by MqttRpc v1 topics.
    /// </summary>
    public const string DefaultTopicRoot = "mqttrpc/v1";

    /// <summary>
    /// Builds the dedicated reply topic for a client.
    /// </summary>
    /// <param name="clientId">The client identifier used as one topic segment.</param>
    /// <param name="topicRoot">The protocol topic root.</param>
    /// <returns>The validated reply topic.</returns>
    public static string BuildReplyTopic(string clientId, string topicRoot = DefaultTopicRoot)
    {
        ValidateSegment(clientId, nameof(clientId));
        return $"{NormalizeTopicRoot(topicRoot)}/reply/{clientId}";
    }

    /// <summary>
    /// Builds a request topic for an RPC method.
    /// </summary>
    /// <param name="method">The slash-separated RPC method name.</param>
    /// <param name="topicRoot">The protocol topic root.</param>
    /// <returns>The validated request topic.</returns>
    public static string BuildRequestTopic(string method, string topicRoot = DefaultTopicRoot)
    {
        return $"{NormalizeTopicRoot(topicRoot)}/request/{NormalizeMethod(method)}";
    }

    /// <summary>
    /// Determines whether a topic is a valid reply topic under the supplied root.
    /// </summary>
    /// <param name="topic">The MQTT topic to inspect.</param>
    /// <param name="topicRoot">The protocol topic root.</param>
    /// <returns><see langword="true"/> when the topic is a valid reply topic.</returns>
    public static bool IsReplyTopic(string topic, string topicRoot = DefaultTopicRoot)
    {
        if (string.IsNullOrWhiteSpace(topic))
        {
            return false;
        }

        var prefix = $"{NormalizeTopicRoot(topicRoot)}/reply/";
        if (!topic.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var clientId = topic[prefix.Length..];
        return IsValidSegment(clientId);
    }

    /// <summary>
    /// Trims and validates an RPC method name.
    /// </summary>
    /// <param name="method">The slash-separated method name.</param>
    /// <returns>The normalized method name.</returns>
    public static string NormalizeMethod(string method)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);

        var normalized = method.Trim('/');
        if (normalized.Length is 0 or > 160)
        {
            throw new ArgumentException("RPC method must contain between 1 and 160 characters.", nameof(method));
        }

        foreach (var segment in normalized.Split('/'))
        {
            ValidateSegment(segment, nameof(method));
        }

        return normalized;
    }

    /// <summary>
    /// Trims and validates a protocol topic root.
    /// </summary>
    /// <param name="topicRoot">The slash-separated topic root.</param>
    /// <returns>The normalized topic root.</returns>
    public static string NormalizeTopicRoot(string topicRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topicRoot);

        var normalized = topicRoot.Trim('/');
        foreach (var segment in normalized.Split('/'))
        {
            ValidateSegment(segment, nameof(topicRoot));
        }

        return normalized;
    }

    /// <summary>
    /// Extracts a validated RPC method from a request topic.
    /// </summary>
    /// <param name="topic">The MQTT request topic.</param>
    /// <param name="method">Receives the normalized method when parsing succeeds.</param>
    /// <param name="topicRoot">The protocol topic root.</param>
    /// <returns><see langword="true"/> when the topic is a valid request topic.</returns>
    public static bool TryGetMethod(
        string topic,
        out string method,
        string topicRoot = DefaultTopicRoot)
    {
        method = string.Empty;
        if (string.IsNullOrWhiteSpace(topic))
        {
            return false;
        }

        var prefix = $"{NormalizeTopicRoot(topicRoot)}/request/";
        if (!topic.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            method = NormalizeMethod(topic[prefix.Length..]);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool IsValidSegment(string segment)
    {
        return segment.Length is > 0 and <= 64
            && segment.All(character =>
                char.IsAsciiLetterOrDigit(character)
                || character is '-' or '_' or '.');
    }

    private static void ValidateSegment(string segment, string parameterName)
    {
        if (!IsValidSegment(segment))
        {
            throw new ArgumentException(
                "Topic segments may only contain ASCII letters, numbers, '.', '_' and '-'.",
                parameterName);
        }
    }
}
