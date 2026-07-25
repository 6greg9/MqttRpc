using System.Text.Json;

namespace MqttRpc;

/// <summary>
/// Defines the built-in MqttRpc error codes.
/// </summary>
public static class MqttRpcErrorCodes
{
    /// <summary>The handler failed with an unexpected exception.</summary>
    public const string InternalError = "INTERNAL_ERROR";

    /// <summary>The request parameters could not be deserialized.</summary>
    public const string InvalidParams = "INVALID_PARAMS";

    /// <summary>The request did not conform to the wire protocol.</summary>
    public const string InvalidRequest = "INVALID_REQUEST";

    /// <summary>No handler is registered for the requested method.</summary>
    public const string MethodNotFound = "METHOD_NOT_FOUND";
}

/// <summary>
/// Describes a stable error returned over the MqttRpc wire protocol.
/// </summary>
/// <param name="Code">A stable, machine-readable error code.</param>
/// <param name="Message">A caller-safe error message.</param>
/// <param name="Retryable">Whether retrying the operation may succeed.</param>
/// <param name="Details">Optional structured error details.</param>
public sealed record MqttRpcError(
    string Code,
    string Message,
    bool Retryable = false,
    JsonElement? Details = null);

/// <summary>
/// Describes a method invocation after its request envelope has been decoded.
/// </summary>
/// <param name="Method">The normalized RPC method name.</param>
/// <param name="Parameters">The JSON parameters supplied by the caller.</param>
public sealed record MqttRpcInvocation(string Method, JsonElement Parameters);

/// <summary>
/// Represents the transport-independent result of routing an invocation.
/// </summary>
/// <param name="Ok">Whether the invocation succeeded.</param>
/// <param name="Result">The handler result when <paramref name="Ok"/> is true.</param>
/// <param name="Error">The stable error when <paramref name="Ok"/> is false.</param>
public sealed record MqttRpcResult(bool Ok, object? Result, MqttRpcError? Error)
{
    /// <summary>
    /// Creates a failed routing result.
    /// </summary>
    /// <param name="error">The error returned to the caller.</param>
    /// <returns>A failed result.</returns>
    public static MqttRpcResult Failure(MqttRpcError error) => new(false, null, error);

    /// <summary>
    /// Creates a successful routing result.
    /// </summary>
    /// <param name="result">The handler result.</param>
    /// <returns>A successful result.</returns>
    public static MqttRpcResult Success(object? result) => new(true, result, null);
}

/// <summary>
/// Represents an RPC method that accepts no request fields.
/// </summary>
public sealed record MqttRpcEmptyRequest;

internal sealed record MqttRpcRequestEnvelope(JsonElement Params);

internal sealed record MqttRpcRequestEnvelope<TRequest>(TRequest Params);

internal sealed record MqttRpcResponseEnvelope(
    bool Ok,
    object? Result,
    MqttRpcError? Error);

internal sealed record MqttRpcResponseEnvelope<TResponse>(
    bool Ok,
    TResponse? Result,
    MqttRpcError? Error);
