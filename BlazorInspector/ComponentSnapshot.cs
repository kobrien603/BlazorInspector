using Microsoft.AspNetCore.Components;

namespace BlazorInspector;

/// <summary>A flat view of one tracked component: its id, type, and current parameter values.</summary>
public sealed record ComponentSnapshot(
    int? ComponentId,
    Type ComponentType,
    IReadOnlyList<ParameterValue> Parameters)
{
    /// <summary>Short, display-friendly type name (no namespace, generic arity stripped).</summary>
    public string DisplayName => FriendlyTypeName.Of(ComponentType);
}

/// <summary>One parameter and its safely-formatted value (see <see cref="ParameterReader"/>).</summary>
public sealed record ParameterValue(string Name, string Kind, string Value);

/// <summary>
/// One node in an expandable value tree for the detail pane (see <see cref="ValueReader"/>). A value
/// can be a leaf (scalar/string/fragment) or expandable (collection items, dictionary entries, or an
/// object's properties/fields). <see cref="Children"/> is populated only when the node's
/// <see cref="Path"/> is currently expanded, so deep/large graphs are walked lazily.
/// </summary>
public sealed class ValueNode
{
    /// <summary>Member name, <c>[index]</c> for collection items, or the key for dictionary entries.</summary>
    public required string Name { get; init; }
    /// <summary>parameter | cascading | field | property | item | entry | info.</summary>
    public required string Kind { get; init; }
    /// <summary>Safe, truncated summary of the value (type + count for collections; the type for objects).</summary>
    public required string Display { get; init; }
    /// <summary>Stable path used as the expansion key and render key (e.g. <c>P:Items/2/Name</c>).</summary>
    public required string Path { get; init; }
    /// <summary>True when the value can be drilled into (non-empty collection / object with members).</summary>
    public bool Expandable { get; init; }
    /// <summary>True when this leaf is a writable scalar the inspector can edit in place.</summary>
    public bool Editable { get; init; }
    /// <summary>Raw current value seeded into the edit box (unquoted, untruncated); empty for null.</summary>
    public string EditText { get; init; } = "";
    /// <summary>Child nodes — present only when this node's <see cref="Path"/> is expanded.</summary>
    public IReadOnlyList<ValueNode> Children { get; init; } = Array.Empty<ValueNode>();
}

/// <summary>A node in the live component tree (see <see cref="InspectorRegistry.BuildTree"/>).</summary>
public sealed class ComponentNode
{
    public required int ComponentId { get; init; }
    public required Type ComponentType { get; init; }
    public required IReadOnlyList<ParameterValue> Parameters { get; init; }
    public IReadOnlyList<ParameterValue> State { get; init; } = Array.Empty<ParameterValue>();
    public List<ComponentNode> Children { get; } = new();

    /// <summary>
    /// The live component instance, used by the detail pane to build the expandable value tree on
    /// demand. Internal: not part of the public inspection surface, and never held beyond a refresh.
    /// </summary>
    internal IComponent? Instance { get; init; }

    public string DisplayName => FriendlyTypeName.Of(ComponentType);

    /// <summary>Full type name, used as the source-map key for jump-to-code.</summary>
    public string FullName => ComponentType.FullName ?? ComponentType.Name;
}

internal static class FriendlyTypeName
{
    public static string Of(Type type)
    {
        var name = type.Name;
        var tick = name.IndexOf('`');
        return tick >= 0 ? name[..tick] : name;
    }
}
