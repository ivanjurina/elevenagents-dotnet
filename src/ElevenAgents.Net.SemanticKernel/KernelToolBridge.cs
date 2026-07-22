using System.Text.Json;
using ElevenAgents.Net.Realtime;
using Microsoft.SemanticKernel;

namespace ElevenAgents.Net.SemanticKernel;

/// <summary>
/// Bridges a Semantic Kernel <see cref="Kernel"/> to an <see cref="AgentConversation"/>:
/// every <see cref="KernelFunction"/> in the kernel's plugins becomes an ElevenLabs
/// client tool, so a voice agent can invoke your existing .NET code by name.
/// <para>
/// Configure the tools on the ElevenLabs agent side to match the kernel function names
/// (use <see cref="GetToolNames"/> to list them), then call <see cref="Register"/>.
/// </para>
/// </summary>
public static class KernelToolBridge
{
    /// <summary>
    /// Registers every kernel function as a client tool on the conversation.
    /// When the agent calls a tool, the matching kernel function runs with the
    /// tool's JSON parameters and its result is returned to the agent as a string.
    /// </summary>
    /// <returns>The names of the tools that were registered.</returns>
    public static IReadOnlyList<string> Register(AgentConversation conversation, Kernel kernel)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        ArgumentNullException.ThrowIfNull(kernel);

        var registered = new List<string>();

        foreach (var plugin in kernel.Plugins)
        foreach (var function in plugin)
        {
            // ElevenLabs tool names can't contain '.', so we flatten to a single token.
            // A bare function name is used when unambiguous; otherwise plugin_function.
            var toolName = function.Name;

            conversation.RegisterTool(toolName, async (parameters, cancellationToken) =>
            {
                var args = ToKernelArguments(parameters, function);
                var result = await kernel.InvokeAsync(function, args, cancellationToken).ConfigureAwait(false);
                return result.GetValue<object>()?.ToString() ?? string.Empty;
            });

            registered.Add(toolName);
        }

        return registered;
    }

    /// <summary>Lists the tool names the kernel would expose, for configuring the agent.</summary>
    public static IReadOnlyList<string> GetToolNames(Kernel kernel)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        return kernel.Plugins.SelectMany(p => p.Select(f => f.Name)).ToList();
    }

    private static KernelArguments ToKernelArguments(JsonElement parameters, KernelFunction function)
    {
        var args = new KernelArguments();
        if (parameters.ValueKind != JsonValueKind.Object)
            return args;

        // Map JSON parameters to the function's declared parameters by name.
        var declared = function.Metadata.Parameters.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var property in parameters.EnumerateObject())
        {
            object? value = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.Number => property.Value.TryGetInt64(out var l) ? l : property.Value.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => property.Value.GetRawText(),
            };

            // Prefer the declared parameter name casing when it matches.
            var name = declared.TryGetValue(property.Name, out var meta) ? meta.Name : property.Name;
            args[name] = value;
        }

        return args;
    }
}
