using System.Collections.Concurrent;
using System.Text.Json;

namespace MqttRpc;

/// <summary>
/// Maps RPC method names to typed handlers and converts their outcomes into stable results.
/// </summary>
public sealed class MqttRpcRouter
{
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ConcurrentDictionary<string, RpcHandler> _routes =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a router.
    /// </summary>
    /// <param name="jsonOptions">
    /// Optional serializer settings shared by parameter deserialization and result serialization.
    /// </param>
    public MqttRpcRouter(JsonSerializerOptions? jsonOptions = null)
    {
        _jsonOptions = jsonOptions ?? new JsonSerializerOptions(JsonSerializerDefaults.Web);
    }

    /// <summary>
    /// Registers one typed handler for an RPC method.
    /// </summary>
    /// <typeparam name="TRequest">The request parameters type.</typeparam>
    /// <typeparam name="TResponse">The handler result type.</typeparam>
    /// <param name="method">The slash-separated RPC method name.</param>
    /// <param name="handler">The asynchronous handler to invoke.</param>
    /// <returns>This router, allowing registrations to be chained.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the method is already registered.
    /// </exception>
    public MqttRpcRouter Map<TRequest, TResponse>(
        string method,
        Func<TRequest, CancellationToken, Task<TResponse>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var normalizedMethod = MqttRpcProtocol.NormalizeMethod(method);

        async Task<object?> InvokeAsync(JsonElement parameters, CancellationToken cancellationToken)
        {
            var request = parameters.Deserialize<TRequest>(_jsonOptions);
            if (request is null)
            {
                throw new JsonException($"Parameters for '{normalizedMethod}' cannot be null.");
            }

            return await handler(request, cancellationToken).ConfigureAwait(false);
        }

        if (!_routes.TryAdd(normalizedMethod, InvokeAsync))
        {
            throw new InvalidOperationException($"RPC method '{normalizedMethod}' is already mapped.");
        }

        return this;
    }

    /// <summary>
    /// Dispatches a decoded invocation to its registered handler.
    /// </summary>
    /// <param name="invocation">The method and JSON parameters to dispatch.</param>
    /// <param name="cancellationToken">Cancels handler execution.</param>
    /// <returns>A successful result or a stable protocol/application error.</returns>
    public async Task<MqttRpcResult> DispatchAsync(
        MqttRpcInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        string method;
        try
        {
            method = MqttRpcProtocol.NormalizeMethod(invocation.Method);
        }
        catch (ArgumentException)
        {
            return MqttRpcResult.Failure(new MqttRpcError(
                MqttRpcErrorCodes.InvalidRequest,
                "The RPC method name is invalid."));
        }

        if (!_routes.TryGetValue(method, out var handler))
        {
            return MqttRpcResult.Failure(new MqttRpcError(
                MqttRpcErrorCodes.MethodNotFound,
                $"RPC method '{method}' is not registered."));
        }

        try
        {
            var result = await handler(invocation.Parameters, cancellationToken).ConfigureAwait(false);
            return MqttRpcResult.Success(result);
        }
        catch (JsonException exception)
        {
            return MqttRpcResult.Failure(new MqttRpcError(
                MqttRpcErrorCodes.InvalidParams,
                exception.Message));
        }
        catch (MqttRpcFaultException exception)
        {
            return MqttRpcResult.Failure(new MqttRpcError(
                exception.Code,
                exception.Message,
                exception.Retryable,
                exception.Details));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return MqttRpcResult.Failure(new MqttRpcError(
                MqttRpcErrorCodes.InternalError,
                "The RPC handler failed."));
        }
    }

    private delegate Task<object?> RpcHandler(
        JsonElement parameters,
        CancellationToken cancellationToken);
}
