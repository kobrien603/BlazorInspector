using System.Collections;
using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;

namespace BlazorInspector;

/// <summary>
/// The single home for every reflection access into ASP.NET Core framework internals. Member names
/// vary by .NET version; they live here as named constants so a runtime break has exactly one place
/// to fix (re-run the STEP 0 probe / <see cref="ProbeMembers"/> guard test when bumping the TFM).
///
/// Verified against ASP.NET Core 8.0, 9.0 and 10.0 (2026-06) — the guard test resolves every member
/// on all three runtimes. The names below are identical across all of them.
/// Everything is fail-soft: a missing member or throwing access returns null rather than propagating.
/// </summary>
internal static class RuntimeInternals
{
    // --- Verified member names (ASP.NET Core 8.0, 9.0 & 10.0) -------------------------------------
    /// <summary><c>private RenderHandle _renderHandle;</c> on <see cref="ComponentBase"/>.</summary>
    public const string RenderHandleField = "_renderHandle";
    /// <summary><c>private readonly Renderer? _renderer;</c> on <see cref="RenderHandle"/>.</summary>
    public const string RendererField = "_renderer";
    /// <summary><c>private readonly int _componentId;</c> on <see cref="RenderHandle"/> (a field, not a property).</summary>
    public const string ComponentIdField = "_componentId";
    /// <summary><c>private readonly Dictionary&lt;int, ComponentState&gt; _componentStateById</c> on <see cref="Renderer"/>.</summary>
    public const string ComponentStateByIdField = "_componentStateById";
    /// <summary><c>public int ComponentId { get; }</c> on the internal <c>ComponentState</c> type.</summary>
    public const string ComponentStateIdProperty = "ComponentId";
    /// <summary><c>public IComponent Component { get; }</c> on the internal <c>ComponentState</c> type.</summary>
    public const string ComponentStateComponentProperty = "Component";
    /// <summary><c>public ComponentState? ParentComponentState { get; }</c> on the internal <c>ComponentState</c> type.</summary>
    public const string ComponentStateParentProperty = "ParentComponentState";

    /// <summary>Full name of the internal ComponentState type, used to probe it without a hard reference.</summary>
    public const string ComponentStateTypeName = "Microsoft.AspNetCore.Components.Rendering.ComponentState";

    /// <summary><c>protected void StateHasChanged();</c> on <see cref="ComponentBase"/> — invoked to
    /// re-render a component after the inspector edits one of its values.</summary>
    public const string StateHasChangedMethod = "StateHasChanged";

    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    // Cached, lazily-resolved members. Resolved against concrete runtime types the first time we see one.
    private static FieldInfo? _renderHandleField;
    private static FieldInfo? _rendererField;
    private static FieldInfo? _componentIdField;
    private static FieldInfo? _statesByIdField;
    private static PropertyInfo? _stateIdProperty;
    private static PropertyInfo? _stateComponentProperty;
    private static PropertyInfo? _stateParentProperty;
    private static MethodInfo? _stateHasChanged;
    private static bool _stateHasChangedResolved;

    /// <summary>Reads <c>ComponentBase._renderHandle</c> as a boxed <see cref="RenderHandle"/>; null if unavailable.</summary>
    private static object? GetRenderHandle(IComponent component)
    {
        try
        {
            if (component is not ComponentBase)
                return null;
            _renderHandleField ??= FindField(component.GetType(), RenderHandleField);
            return _renderHandleField?.GetValue(component);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The component id assigned by the renderer, or null if the component isn't attached yet.</summary>
    public static int? TryGetComponentId(IComponent component)
    {
        try
        {
            var handle = GetRenderHandle(component);
            if (handle is null)
                return null;
            _componentIdField ??= FindField(handle.GetType(), ComponentIdField);
            return _componentIdField?.GetValue(handle) is int id ? id : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The owning <c>Renderer</c> (boxed) for a tracked component, or null.</summary>
    public static object? TryGetRenderer(IComponent component)
    {
        try
        {
            var handle = GetRenderHandle(component);
            if (handle is null)
                return null;
            _rendererField ??= FindField(handle.GetType(), RendererField);
            return _rendererField?.GetValue(handle);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Enumerates the renderer's live <c>ComponentState</c> objects (boxed). Read each via
    /// <see cref="GetStateComponentId"/>, <see cref="GetStateComponent"/>, <see cref="GetStateParent"/>.
    /// </summary>
    public static IReadOnlyList<object> TryGetComponentStates(object renderer)
    {
        try
        {
            _statesByIdField ??= FindField(renderer.GetType(), ComponentStateByIdField);
            if (_statesByIdField?.GetValue(renderer) is not IDictionary dict)
                return Array.Empty<object>();

            var result = new List<object>(dict.Count);
            foreach (var value in dict.Values)
            {
                if (value is not null)
                    result.Add(value);
            }
            return result;
        }
        catch
        {
            return Array.Empty<object>();
        }
    }

    /// <summary>
    /// Forces a re-render of <paramref name="component"/> by invoking the protected
    /// <c>ComponentBase.StateHasChanged()</c>, so the host UI reflects a value the inspector just
    /// wrote. Returns false (no-op) for non-<see cref="ComponentBase"/> components or if unavailable.
    /// </summary>
    public static bool TryInvokeStateHasChanged(IComponent component)
    {
        try
        {
            if (component is not ComponentBase)
                return false;
            if (!_stateHasChangedResolved)
            {
                _stateHasChanged = typeof(ComponentBase).GetMethod(
                    StateHasChangedMethod, BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                _stateHasChangedResolved = true;
            }
            if (_stateHasChanged is null)
                return false;
            _stateHasChanged.Invoke(component, null);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static int? GetStateComponentId(object componentState)
    {
        try
        {
            _stateIdProperty ??= componentState.GetType().GetProperty(ComponentStateIdProperty, Flags);
            return _stateIdProperty?.GetValue(componentState) is int id ? id : null;
        }
        catch
        {
            return null;
        }
    }

    public static IComponent? GetStateComponent(object componentState)
    {
        try
        {
            _stateComponentProperty ??= componentState.GetType().GetProperty(ComponentStateComponentProperty, Flags);
            return _stateComponentProperty?.GetValue(componentState) as IComponent;
        }
        catch
        {
            return null;
        }
    }

    public static object? GetStateParent(object componentState)
    {
        try
        {
            _stateParentProperty ??= componentState.GetType().GetProperty(ComponentStateParentProperty, Flags);
            return _stateParentProperty?.GetValue(componentState);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Finds a field by name, walking up the type hierarchy (GetField doesn't see base-class private fields).</summary>
    private static FieldInfo? FindField(Type? type, string name)
    {
        for (var t = type; t is not null; t = t.BaseType)
        {
            var field = t.GetField(name, Flags);
            if (field is not null)
                return field;
        }
        return null;
    }

    /// <summary>
    /// Diagnostic used by the STEP 0 guard test: confirms every reflected member still resolves on the
    /// real framework types. Returns member-key → resolved. Any false means the runtime internals drifted.
    /// </summary>
    public static IReadOnlyDictionary<string, bool> ProbeMembers()
    {
        var results = new Dictionary<string, bool>();

        results[$"ComponentBase.{RenderHandleField}"] =
            FindField(typeof(ComponentBase), RenderHandleField) is not null;
        results[$"RenderHandle.{RendererField}"] =
            FindField(typeof(RenderHandle), RendererField) is not null;
        results[$"RenderHandle.{ComponentIdField}"] =
            FindField(typeof(RenderHandle), ComponentIdField) is not null;
        results[$"Renderer.{ComponentStateByIdField}"] =
            FindField(typeof(Renderer), ComponentStateByIdField) is not null;

        var stateType = typeof(ComponentBase).Assembly.GetType(ComponentStateTypeName);
        results[$"{ComponentStateTypeName}"] = stateType is not null;
        results[$"ComponentState.{ComponentStateIdProperty}"] =
            stateType?.GetProperty(ComponentStateIdProperty, Flags) is not null;
        results[$"ComponentState.{ComponentStateComponentProperty}"] =
            stateType?.GetProperty(ComponentStateComponentProperty, Flags) is not null;
        results[$"ComponentState.{ComponentStateParentProperty}"] =
            stateType?.GetProperty(ComponentStateParentProperty, Flags) is not null;

        return results;
    }
}
