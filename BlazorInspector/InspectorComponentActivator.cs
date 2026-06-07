using Microsoft.AspNetCore.Components;

namespace BlazorInspector;

/// <summary>
/// Observes every component instance creation by acting as the DI-registered <see cref="IComponentActivator"/>.
/// It chains to whatever activator was registered before it (e.g. bUnit's, or the framework default) so
/// creation behavior is preserved, and it tracks each created component in the <see cref="InspectorRegistry"/>.
///
/// Critical invariant: this MUST always return a valid instance — registering an activator makes this
/// library responsible for creating ALL components, so a null/throw here breaks the host app's rendering.
/// </summary>
internal sealed class InspectorComponentActivator : IComponentActivator
{
    private const string InspectorNamespace = "BlazorInspector";

    private readonly IComponentActivator? _inner;
    private readonly InspectorRegistry _registry;

    public InspectorComponentActivator(IComponentActivator? inner, InspectorRegistry registry)
    {
        _inner = inner;
        _registry = registry;
    }

    public IComponent CreateInstance(Type componentType)
    {
        // Reproduce default creation, chaining to any pre-existing activator first.
        var instance = _inner?.CreateInstance(componentType)
                       ?? (IComponent)Activator.CreateInstance(componentType)!;

        // Never track the inspector's own components (avoids recursion / noise in the tree).
        if (!IsInspectorType(componentType))
        {
            try
            {
                _registry.Track(instance);
            }
            catch
            {
                // Tracking must never affect the host app — swallow and return the instance.
            }
        }

        return instance;
    }

    /// <summary>True for the inspector's own components, which are excluded from tracking and the tree.</summary>
    public static bool IsInspectorType(Type type) =>
        type.Namespace is { } ns &&
        (ns == InspectorNamespace || ns.StartsWith(InspectorNamespace + ".", StringComparison.Ordinal));
}
