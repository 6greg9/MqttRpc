using System.Text.Json;

namespace MqttRpc;

/// <summary>
/// Represents an expected application fault that a responder returns to the caller.
/// </summary>
public sealed class MqttRpcFaultException : Exception
{
    /// <summary>
    /// Initializes an application fault.
    /// </summary>
    /// <param name="code">A stable, machine-readable error code.</param>
    /// <param name="message">A caller-safe error message.</param>
    /// <param name="retryable">Whether retrying the operation may succeed.</param>
    /// <param name="details">Optional structured error details.</param>
    public MqttRpcFaultException(
        string code,
        string message,
        bool retryable = false,
        JsonElement? details = null)
        : base(message)
    {
        Code = code;
        Retryable = retryable;
        Details = details;
    }

    /// <summary>
    /// Gets the stable, machine-readable error code.
    /// </summary>
    public string Code { get; }

    /// <summary>
    /// Gets optional structured error details.
    /// </summary>
    public JsonElement? Details { get; }

    /// <summary>
    /// Gets whether retrying the operation may succeed.
    /// </summary>
    public bool Retryable { get; }
}

/// <summary>
/// Represents a malformed or incomplete MqttRpc protocol message.
/// </summary>
public sealed class MqttRpcProtocolException : Exception
{
    /// <summary>
    /// Initializes a protocol exception.
    /// </summary>
    /// <param name="message">The protocol error message.</param>
    public MqttRpcProtocolException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a protocol exception caused by another exception.
    /// </summary>
    /// <param name="message">The protocol error message.</param>
    /// <param name="innerException">The exception that caused the protocol failure.</param>
    public MqttRpcProtocolException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Represents an error returned by a remote MqttRpc handler.
/// </summary>
public sealed class MqttRpcRemoteException : Exception
{
    /// <summary>
    /// Initializes a remote exception from its wire-level error payload.
    /// </summary>
    /// <param name="error">The remote error payload.</param>
    public MqttRpcRemoteException(MqttRpcError error)
        : base(error.Message)
    {
        Error = error;
    }

    /// <summary>
    /// Gets the error returned by the remote responder.
    /// </summary>
    public MqttRpcError Error { get; }
}
