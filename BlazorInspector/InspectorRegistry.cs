using Microsoft.AspNetCore.Components;

namespace BlazorInspector;

/// <summary>
/// Holds weak references to every tracked component instance and produces snapshots / the live tree.
/// Weak references keep the inspector from extending component lifetimes; dead refs are pruned lazily.
/// Registered as a singleton by <c>AddBlazorInspector()</c>. All members are thread-safe.
/// </summary>
public sealed class InspectorRegistry
{
    private readonly List<WeakReference<IComponent>> _tracked = new();
    private readonly object _lock = new();

    /// <summary>Called by <see cref="InspectorComponentActivator"/> for every created component instance.</summary>
    internal void Track(IComponent component)
    {
        lock (_lock)
        {
            _tracked.Add(new WeakReference<IComponent>(component));
        }
    }

    /// <summary>Live instances still reachable, pruning dead weak references as a side effect.</summary>
    private List<IComponent> GetLiveInstances()
    {
        lock (_lock)
        {
            var live = new List<IComponent>(_tracked.Count);
            for (var i = _tracked.Count - 1; i >= 0; i--)
            {
                if (_tracked[i].TryGetTarget(out var component))
                    live.Add(component);
                else
                    _tracked.RemoveAt(i); // prune
            }
            live.Reverse(); // restore creation order
            return live;
        }
    }

    /// <summary>Drops dead weak references and returns the number of live components remaining.</summary>
    public int Prune() => GetLiveInstances().Count;

    /// <summary>Count of live tracked components (accurate after pruning).</summary>
    public int Count => GetLiveInstances().Count;

    /// <summary>Phase 1: a flat list of every live tracked component with its current parameter values.</summary>
    public IReadOnlyList<ComponentSnapshot> Snapshot()
    {
        var live = GetLiveInstances();
        var result = new List<ComponentSnapshot>(live.Count);
        foreach (var component in live)
        {
            result.Add(new ComponentSnapshot(
                RuntimeInternals.TryGetComponentId(component),
                component.GetType(),
                ParameterReader.Read(component)));
        }
        return result;
    }

    /// <summary>
    /// Phase 2: the authoritative parent/child hierarchy, read from the renderer's component-state map.
    /// Walks <c>ComponentState.ParentComponentState</c> to link nodes by component id. Tracked components
    /// not present in any renderer map (e.g. not yet attached) are appended as extra roots.
    /// </summary>
    public IReadOnlyList<ComponentNode> BuildTree()
    {
        var live = GetLiveInstances();
        if (live.Count == 0)
            return Array.Empty<ComponentNode>();

        // Collect every reachable renderer from the tracked components (usually just one).
        var renderers = new List<object>();
        foreach (var component in live)
        {
            var renderer = RuntimeInternals.TryGetRenderer(component);
            if (renderer is not null && !renderers.Any(r => ReferenceEquals(r, renderer)))
                renderers.Add(renderer);
        }

        var nodesById = new Dictionary<int, ComponentNode>();
        var parentIdByChildId = new Dictionary<int, int?>();

        foreach (var renderer in renderers)
        {
            foreach (var state in RuntimeInternals.TryGetComponentStates(renderer))
            {
                var id = RuntimeInternals.GetStateComponentId(state);
                var component = RuntimeInternals.GetStateComponent(state);
                if (id is null || component is null)
                    continue;

                // Don't surface the inspector's own components in the tree.
                if (InspectorComponentActivator.IsInspectorType(component.GetType()))
                    continue;

                if (nodesById.ContainsKey(id.Value))
                    continue;

                nodesById[id.Value] = new ComponentNode
                {
                    ComponentId = id.Value,
                    ComponentType = component.GetType(),
                    Parameters = ParameterReader.Read(component),
                    State = ParameterReader.ReadState(component),
                    Instance = component,
                };

                int? parentId = null;
                var parentState = RuntimeInternals.GetStateParent(state);
                if (parentState is not null)
                    parentId = RuntimeInternals.GetStateComponentId(parentState);
                parentIdByChildId[id.Value] = parentId;
            }
        }

        // Link children to parents; anything without a known parent becomes a root.
        var roots = new List<ComponentNode>();
        foreach (var (childId, node) in nodesById)
        {
            if (parentIdByChildId.TryGetValue(childId, out var parentId) &&
                parentId is int pid &&
                nodesById.TryGetValue(pid, out var parent))
            {
                parent.Children.Add(node);
            }
            else
            {
                roots.Add(node);
            }
        }

        // Fallback: tracked components the renderer map didn't cover (id known, not yet in a tree).
        foreach (var component in live)
        {
            if (InspectorComponentActivator.IsInspectorType(component.GetType()))
                continue;
            var id = RuntimeInternals.TryGetComponentId(component);
            if (id is int cid && !nodesById.ContainsKey(cid))
            {
                var node = new ComponentNode
                {
                    ComponentId = cid,
                    ComponentType = component.GetType(),
                    Parameters = ParameterReader.Read(component),
                    State = ParameterReader.ReadState(component),
                    Instance = component,
                };
                nodesById[cid] = node;
                roots.Add(node);
            }
        }

        // Stable ordering by component id for a predictable display.
        SortRecursive(roots);
        return roots;
    }

    private static void SortRecursive(List<ComponentNode> nodes)
    {
        nodes.Sort((a, b) => a.ComponentId.CompareTo(b.ComponentId));
        foreach (var node in nodes)
            SortRecursive(node.Children);
    }
}
