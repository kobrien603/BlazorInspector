using System.Collections;
using System.Reflection;
using Microsoft.AspNetCore.Components;

namespace BlazorInspector;

/// <summary>
/// Reads <c>[Parameter]</c> and <c>[CascadingParameter]</c> values off a component and formats them
/// safely for display. Never throws into the caller: a throwing getter degrades to a placeholder.
/// Rich values (fragments, callbacks, collections) are summarized rather than dumped via ToString().
/// </summary>
internal static class ParameterReader
{
    private const int MaxValueLength = 200;
    private const BindingFlags PropFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private const BindingFlags DeclaredInstance =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    // Cache member lists per component type — they never change for a given type.
    private static readonly Dictionary<Type, PropertyInfo[]> ParameterPropertiesCache = new();
    private static readonly Dictionary<Type, MemberInfo[]> StateMembersCache = new();
    private static readonly object CacheLock = new();

    public static IReadOnlyList<ParameterValue> Read(IComponent component)
    {
        PropertyInfo[] properties;
        try
        {
            properties = GetParameterProperties(component.GetType());
        }
        catch
        {
            return Array.Empty<ParameterValue>();
        }

        var values = new List<ParameterValue>(properties.Length);
        foreach (var property in properties)
        {
            var kind = Classify(property);
            string formatted;
            try
            {
                var raw = property.GetValue(component);
                formatted = Format(raw);
            }
            catch (Exception ex)
            {
                // A throwing getter must never crash the panel.
                formatted = $"<error: {ex.GetType().Name}>";
            }

            values.Add(new ParameterValue(property.Name, kind, formatted));
        }

        return values;
    }

    internal static PropertyInfo[] GetParameterProperties(Type type)
    {
        lock (CacheLock)
        {
            if (ParameterPropertiesCache.TryGetValue(type, out var cached))
                return cached;

            var props = type
                .GetProperties(PropFlags)
                .Where(p => p.CanRead &&
                            (p.IsDefined(typeof(ParameterAttribute), inherit: true) ||
                             p.IsDefined(typeof(CascadingParameterAttribute), inherit: true)))
                .ToArray();

            ParameterPropertiesCache[type] = props;
            return props;
        }
    }

    internal static string Classify(PropertyInfo property) =>
        property.IsDefined(typeof(CascadingParameterAttribute), inherit: true) ? "cascading" : "parameter";

    /// <summary>
    /// Reads the component's own state — non-parameter instance fields and properties declared on the
    /// component type (and any user-defined base types, up to but excluding <see cref="ComponentBase"/>).
    /// This is what surfaces things like a page's private <c>currentCount</c> field. Compiler-generated
    /// members, injected services, and framework internals are excluded; values are formatted safely.
    /// </summary>
    public static IReadOnlyList<ParameterValue> ReadState(IComponent component)
    {
        MemberInfo[] members;
        try
        {
            members = GetStateMembers(component.GetType());
        }
        catch
        {
            return Array.Empty<ParameterValue>();
        }

        var values = new List<ParameterValue>(members.Length);
        foreach (var member in members)
        {
            string formatted;
            try
            {
                var raw = member is FieldInfo field
                    ? field.GetValue(component)
                    : ((PropertyInfo)member).GetValue(component);
                formatted = Format(raw);
            }
            catch (Exception ex)
            {
                formatted = $"<error: {ex.GetType().Name}>";
            }

            var kind = member is FieldInfo ? "field" : "property";
            values.Add(new ParameterValue(member.Name, kind, formatted));
        }

        return values;
    }

    internal static MemberInfo[] GetStateMembers(Type type)
    {
        lock (CacheLock)
        {
            if (StateMembersCache.TryGetValue(type, out var cached))
                return cached;

            var members = new List<MemberInfo>();
            for (var t = type; t is not null && t != typeof(object) && !IsFrameworkComponentType(t); t = t.BaseType)
            {
                foreach (var field in t.GetFields(DeclaredInstance))
                {
                    if (field.IsStatic || field.IsLiteral)
                        continue;
                    if (IsCompilerGenerated(field) || field.Name.Contains('<')) // skip auto-property backing fields etc.
                        continue;
                    members.Add(field);
                }

                foreach (var property in t.GetProperties(DeclaredInstance))
                {
                    if (!property.CanRead || property.GetIndexParameters().Length > 0)
                        continue;
                    if (IsCompilerGenerated(property))
                        continue;
                    if (property.IsDefined(typeof(ParameterAttribute), inherit: true) ||
                        property.IsDefined(typeof(CascadingParameterAttribute), inherit: true) ||
                        property.IsDefined(typeof(InjectAttribute), inherit: true))
                        continue; // shown in the Parameters section or injected dependencies, not state
                    members.Add(property);
                }
            }

            var array = members.ToArray();
            StateMembersCache[type] = array;
            return array;
        }
    }

    // Stop the hierarchy walk at framework component types (ComponentBase, OwningComponentBase, …) so we
    // only report the user's own state, never framework internals like _renderHandle.
    private static bool IsFrameworkComponentType(Type type) =>
        type.Namespace is { } ns && ns.StartsWith("Microsoft.AspNetCore.Components", StringComparison.Ordinal);

    internal static bool IsCompilerGenerated(MemberInfo member) =>
        member.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), inherit: false);

    private static string Format(object? value)
    {
        switch (value)
        {
            case null:
                return "null";

            case RenderFragment:
                return "<fragment>";

            case string s:
                return Truncate(Quote(s));
        }

        var type = value.GetType();

        // RenderFragment<T> is a delegate type; EventCallback / EventCallback<T> are structs.
        if (IsRenderFragmentOfT(type))
            return "<fragment>";
        if (IsEventCallback(type))
            return "<event>";

        // Collections: show type + count, never the contents.
        if (value is IEnumerable enumerable and not string)
            return $"{FriendlyTypeName.Of(type)} ({CountItems(enumerable)} items)";

        if (type.IsPrimitive || value is decimal || value is Guid || value is DateTime || value is DateTimeOffset || value is TimeSpan || type.IsEnum)
            return Truncate(value.ToString() ?? "");

        // Anything else: a guarded ToString, prefixed with the type so it stays readable.
        try
        {
            var text = value.ToString();
            if (text is null || text == type.FullName || text == type.ToString())
                return FriendlyTypeName.Of(type);
            return $"{FriendlyTypeName.Of(type)}: {Truncate(text)}";
        }
        catch (Exception ex)
        {
            return $"{FriendlyTypeName.Of(type)} <ToString threw: {ex.GetType().Name}>";
        }
    }

    internal static int CountItems(IEnumerable enumerable)
    {
        try
        {
            if (enumerable is ICollection collection)
                return collection.Count;

            var count = 0;
            foreach (var _ in enumerable)
            {
                if (++count >= 10_000) // guard against infinite / huge sequences
                    break;
            }
            return count;
        }
        catch
        {
            return 0;
        }
    }

    internal static bool IsEventCallback(Type type)
    {
        if (type == typeof(EventCallback))
            return true;
        return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(EventCallback<>);
    }

    internal static bool IsRenderFragmentOfT(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(RenderFragment<>);

    internal static string Quote(string s) => $"\"{s}\"";

    internal static string Truncate(string s) =>
        s.Length <= MaxValueLength ? s : s[..MaxValueLength] + "…";
}
