using System.Collections;
using System.Globalization;
using System.Reflection;
using Microsoft.AspNetCore.Components;

namespace BlazorInspector;

/// <summary>
/// Builds an expandable <see cref="ValueNode"/> tree for a component's parameters and state, letting
/// the detail pane drill into collections, dictionaries, and nested objects DevTools-style, and edit
/// writable scalar leaves in place (see <see cref="TrySetValue"/>). Walking is lazy: a node's
/// children are only materialized when its <c>path</c> is in the expanded set, so the cost scales
/// with what the user has opened, not the whole object graph.
///
/// Fully fail-soft (it must never throw into the host): throwing getters degrade to an
/// <c>&lt;error&gt;</c> leaf, and depth / breadth / reference-cycle guards bound the walk.
/// </summary>
internal static class ValueReader
{
    private const int MaxDepth = 6;
    private const int MaxChildren = 200;
    private const BindingFlags PublicInstance = BindingFlags.Instance | BindingFlags.Public;
    private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static readonly Dictionary<Type, MemberInfo[]> ObjectMembersCache = new();
    private static readonly object CacheLock = new();

    /// <summary>Top-level parameter and state nodes for <paramref name="component"/>, expanding only
    /// paths present in <paramref name="expanded"/>.</summary>
    public static (IReadOnlyList<ValueNode> Parameters, IReadOnlyList<ValueNode> State) BuildRoots(
        IComponent component, ISet<string> expanded)
    {
        var type = component.GetType();
        var paramNodes = new List<ValueNode>();
        foreach (var property in ParameterReader.GetParameterProperties(type))
        {
            var (value, error) = GuardedGet(() => property.GetValue(component));
            paramNodes.Add(BuildNode(property.Name, ParameterReader.Classify(property), value, error,
                "P:" + property.Name, expanded, NewAncestors(), 0, property.PropertyType, property.CanWrite));
        }

        var stateNodes = new List<ValueNode>();
        foreach (var member in ParameterReader.GetStateMembers(type))
        {
            var (value, error) = GuardedGet(() => GetMemberValue(member, component));
            var kind = member is FieldInfo ? "field" : "property";
            stateNodes.Add(BuildNode(member.Name, kind, value, error,
                "S:" + member.Name, expanded, NewAncestors(), 0, MemberType(member), IsWritable(member)));
        }

        return (paramNodes, stateNodes);
    }

    private static ValueNode BuildNode(string name, string kind, object? value, string? error, string path,
        ISet<string> expanded, HashSet<object> ancestors, int depth, Type? slotType, bool writableSlot)
    {
        if (error is not null)
            return Leaf(name, kind, error, path);

        var (display, expandable) = Describe(value, depth, ancestors);
        IReadOnlyList<ValueNode> children = Array.Empty<ValueNode>();
        if (expandable && expanded.Contains(path))
            children = BuildChildren(value!, path, expanded, ancestors, depth);

        var editable = !expandable && writableSlot && slotType is not null && IsEditableType(slotType);
        return new ValueNode
        {
            Name = name,
            Kind = kind,
            Display = display,
            Path = path,
            Expandable = expandable,
            Children = children,
            Editable = editable,
            EditText = editable ? RawEdit(value) : "",
        };
    }

    /// <summary>Summary string + whether the value can be drilled into.</summary>
    private static (string Display, bool Expandable) Describe(object? value, int depth, HashSet<object> ancestors)
    {
        switch (value)
        {
            case null:
                return ("null", false);
            case RenderFragment:
                return ("<fragment>", false);
            case string s:
                return (ParameterReader.Truncate(ParameterReader.Quote(s)), false);
            case Type t:
                return (FriendlyTypeName.Of(t), false);
            case Delegate:
                return ("<delegate>", false);
        }

        var type = value.GetType();
        if (ParameterReader.IsRenderFragmentOfT(type))
            return ("<fragment>", false);
        if (ParameterReader.IsEventCallback(type))
            return ("<event>", false);
        if (IsScalar(type, value))
            return (ParameterReader.Truncate(value.ToString() ?? ""), false);

        var cycle = !type.IsValueType && ancestors.Contains(value);
        var canDescend = !cycle && depth < MaxDepth;

        if (value is IDictionary dict)
        {
            var n = dict.Count;
            return ($"{FriendlyTypeName.Of(type)} ({n} {(n == 1 ? "entry" : "entries")})", n > 0 && canDescend);
        }
        if (value is IEnumerable enumerable)
        {
            var n = ParameterReader.CountItems(enumerable);
            return ($"{FriendlyTypeName.Of(type)} ({n} {(n == 1 ? "item" : "items")})", n > 0 && canDescend);
        }

        // A plain object: expandable if it exposes readable members.
        return (DescribeObject(value, type), GetObjectMembers(type).Length > 0 && canDescend);
    }

    private static IReadOnlyList<ValueNode> BuildChildren(object value, string path, ISet<string> expanded,
        HashSet<object> ancestors, int depth)
    {
        var type = value.GetType();
        var track = !type.IsValueType;
        if (track)
            ancestors.Add(value);
        try
        {
            var children = new List<ValueNode>();
            var nextDepth = depth + 1;

            if (value is IDictionary dict)
            {
                var writable = !dict.IsReadOnly;
                var valueType = DictValueType(type);
                var i = 0;
                foreach (DictionaryEntry entry in dict)
                {
                    if (i >= MaxChildren) { children.Add(Truncated(dict.Count - MaxChildren, path)); break; }
                    var (v, error) = GuardedGet(() => entry.Value);
                    children.Add(BuildNode(KeyName(entry.Key), "entry", v, error, $"{path}/{i}", expanded,
                        ancestors, nextDepth, valueType ?? v?.GetType(), writable));
                    i++;
                }
            }
            else if (value is IEnumerable enumerable)
            {
                var settable = value is IList list && !list.IsReadOnly;
                var elementType = ElementType(type);
                var i = 0;
                foreach (var item in enumerable)
                {
                    if (i >= MaxChildren) { children.Add(Truncated(-1, path)); break; }
                    children.Add(BuildNode($"[{i}]", "item", item, null, $"{path}/{i}", expanded,
                        ancestors, nextDepth, elementType ?? item?.GetType(), settable));
                    i++;
                }
            }
            else
            {
                foreach (var member in GetObjectMembers(type))
                {
                    var (v, error) = GuardedGet(() => GetMemberValue(member, value));
                    var kind = member is FieldInfo ? "field" : "property";
                    children.Add(BuildNode(member.Name, kind, v, error, $"{path}/{member.Name}", expanded,
                        ancestors, nextDepth, MemberType(member), IsWritable(member)));
                }
            }

            return children;
        }
        finally
        {
            if (track)
                ancestors.Remove(value);
        }
    }

    // ----- editing -------------------------------------------------------------------------------

    /// <summary>
    /// Writes <paramref name="raw"/> (a string from the edit box) to the value addressed by
    /// <paramref name="path"/> on the live <paramref name="component"/>, re-resolving the path against
    /// current data and converting to the target type. Returns <c>(false, reason)</c> on any failure;
    /// never throws.
    /// </summary>
    public static (bool Ok, string? Error) TrySetValue(IComponent component, string path, string raw)
    {
        try
        {
            var segments = path.Split('/');
            var head = segments[0];
            if (head.Length < 3 || head[1] != ':' || (head[0] != 'P' && head[0] != 'S'))
                return (false, "bad path");

            var topName = head.Substring(2);
            var fromState = head[0] == 'S';
            var type = component.GetType();

            // Single segment: set a top-level parameter/state member directly on the component.
            if (segments.Length == 1)
            {
                var member = FindTopLevelMember(type, topName, fromState);
                return member is null ? (false, "member not found") : SetMember(component, member, raw);
            }

            // Otherwise navigate to the parent container of the final segment, then set there.
            var top = FindTopLevelMember(type, topName, fromState);
            if (top is null)
                return (false, "member not found");

            object? container = GetMemberValue(top, component);
            for (var i = 1; i < segments.Length - 1; i++)
            {
                if (container is null)
                    return (false, "path no longer resolves");
                container = Navigate(container, segments[i]);
            }
            return container is null ? (false, "path no longer resolves") : SetOnContainer(container, segments[^1], raw);
        }
        catch (Exception ex)
        {
            return (false, ex.GetType().Name);
        }
    }

    private static object? Navigate(object container, string segment)
    {
        switch (container)
        {
            case IDictionary dict:
                return NthDictValue(dict, ParseIndex(segment));
            case IList list:
            {
                var i = ParseIndex(segment);
                return i >= 0 && i < list.Count ? list[i] : null;
            }
            case IEnumerable enumerable and not string:
                return NthItem(enumerable, ParseIndex(segment));
            default:
                var member = FindObjectMember(container.GetType(), segment);
                return member is null ? null : GetMemberValue(member, container);
        }
    }

    private static (bool Ok, string? Error) SetOnContainer(object container, string segment, string raw)
    {
        switch (container)
        {
            case IDictionary dict:
            {
                if (dict.IsReadOnly)
                    return (false, "read-only");
                var key = NthDictKey(dict, ParseIndex(segment));
                if (key is null)
                    return (false, "key not found");
                var valueType = DictValueType(dict.GetType()) ?? dict[key]?.GetType() ?? typeof(string);
                dict[key] = Convert(raw, valueType);
                return (true, null);
            }
            case IList list:
            {
                if (list.IsReadOnly)
                    return (false, "read-only");
                var i = ParseIndex(segment);
                if (i < 0 || i >= list.Count)
                    return (false, "index out of range");
                var elementType = ElementType(list.GetType()) ?? list[i]?.GetType() ?? typeof(string);
                list[i] = Convert(raw, elementType);
                return (true, null);
            }
            default:
            {
                var member = FindObjectMember(container.GetType(), segment);
                return member is null ? (false, "member not found") : SetMember(container, member, raw);
            }
        }
    }

    private static (bool Ok, string? Error) SetMember(object target, MemberInfo member, string raw)
    {
        switch (member)
        {
            case PropertyInfo p when p.CanWrite:
                p.SetValue(target, Convert(raw, p.PropertyType));
                return (true, null);
            case FieldInfo f when !f.IsInitOnly && !f.IsLiteral:
                f.SetValue(target, Convert(raw, f.FieldType));
                return (true, null);
            default:
                return (false, "read-only");
        }
    }

    /// <summary>Parses the edit-box string back into the slot's declared type. Throws on bad input
    /// (caught by <see cref="TrySetValue"/> and surfaced to the user).</summary>
    private static object? Convert(string raw, Type targetType)
    {
        var underlying = Nullable.GetUnderlyingType(targetType);
        if (underlying is not null)
        {
            if (raw.Length == 0 || raw == "null")
                return null;
            targetType = underlying;
        }

        if (targetType == typeof(string)) return raw;
        if (targetType.IsEnum) return Enum.Parse(targetType, raw, ignoreCase: true);
        if (targetType == typeof(bool)) return bool.Parse(raw);
        if (targetType == typeof(Guid)) return Guid.Parse(raw);
        if (targetType == typeof(char)) return raw.Length == 1 ? raw[0] : throw new FormatException("expected a single character");
        if (targetType == typeof(DateTime)) return DateTime.Parse(raw, CultureInfo.InvariantCulture);
        if (targetType == typeof(DateTimeOffset)) return DateTimeOffset.Parse(raw, CultureInfo.InvariantCulture);
        if (targetType == typeof(DateOnly)) return DateOnly.Parse(raw, CultureInfo.InvariantCulture);
        if (targetType == typeof(TimeOnly)) return TimeOnly.Parse(raw, CultureInfo.InvariantCulture);
        if (targetType == typeof(TimeSpan)) return TimeSpan.Parse(raw, CultureInfo.InvariantCulture);
        return System.Convert.ChangeType(raw, targetType, CultureInfo.InvariantCulture);
    }

    private static bool IsEditableType(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type.IsEnum)
            return true;
        return type.IsPrimitive
            || type == typeof(string) || type == typeof(decimal) || type == typeof(Guid)
            || type == typeof(DateTime) || type == typeof(DateTimeOffset)
            || type == typeof(DateOnly) || type == typeof(TimeOnly) || type == typeof(TimeSpan);
    }

    private static string RawEdit(object? value) =>
        value switch { null => "", string s => s, _ => value.ToString() ?? "" };

    // ----- shared helpers ------------------------------------------------------------------------

    /// <summary>Friendly type name, plus a meaningful <c>ToString()</c> when the type overrides it.</summary>
    private static string DescribeObject(object value, Type type)
    {
        var friendly = FriendlyTypeName.Of(type);
        try
        {
            var text = value.ToString();
            if (string.IsNullOrEmpty(text) || text == type.FullName || text == type.ToString())
                return friendly;
            return $"{friendly}: {ParameterReader.Truncate(text)}";
        }
        catch (Exception ex)
        {
            return $"{friendly} <ToString threw: {ex.GetType().Name}>";
        }
    }

    private static MemberInfo[] GetObjectMembers(Type type)
    {
        lock (CacheLock)
        {
            if (ObjectMembersCache.TryGetValue(type, out var cached))
                return cached;

            var members = new List<MemberInfo>();
            foreach (var property in type.GetProperties(PublicInstance))
            {
                if (property.CanRead && property.GetIndexParameters().Length == 0 &&
                    !ParameterReader.IsCompilerGenerated(property))
                    members.Add(property);
            }
            foreach (var field in type.GetFields(PublicInstance))
            {
                if (!field.IsLiteral && !ParameterReader.IsCompilerGenerated(field))
                    members.Add(field);
            }

            var array = members.ToArray();
            ObjectMembersCache[type] = array;
            return array;
        }
    }

    private static MemberInfo? FindTopLevelMember(Type type, string name, bool fromState) =>
        fromState
            ? Array.Find(ParameterReader.GetStateMembers(type), m => m.Name == name)
            : Array.Find(ParameterReader.GetParameterProperties(type), p => p.Name == name);

    private static MemberInfo? FindObjectMember(Type type, string name) =>
        (MemberInfo?)type.GetProperty(name, AnyInstance) ?? type.GetField(name, AnyInstance);

    private static object? GetMemberValue(MemberInfo member, object target) =>
        member is FieldInfo field ? field.GetValue(target) : ((PropertyInfo)member).GetValue(target);

    private static Type MemberType(MemberInfo member) =>
        member is FieldInfo field ? field.FieldType : ((PropertyInfo)member).PropertyType;

    private static bool IsWritable(MemberInfo member) => member switch
    {
        PropertyInfo p => p.CanWrite,
        FieldInfo f => !f.IsInitOnly && !f.IsLiteral,
        _ => false,
    };

    private static Type? ElementType(Type collectionType)
    {
        if (collectionType.IsArray)
            return collectionType.GetElementType();
        foreach (var iface in collectionType.GetInterfaces())
            if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                return iface.GetGenericArguments()[0];
        return null;
    }

    private static Type? DictValueType(Type dictType)
    {
        foreach (var iface in dictType.GetInterfaces())
            if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IDictionary<,>))
                return iface.GetGenericArguments()[1];
        return null;
    }

    private static object? NthItem(IEnumerable source, int n)
    {
        if (n < 0) return null;
        var i = 0;
        foreach (var item in source)
            if (i++ == n) return item;
        return null;
    }

    private static object? NthDictValue(IDictionary dict, int n)
    {
        if (n < 0) return null;
        var i = 0;
        foreach (DictionaryEntry entry in dict)
            if (i++ == n) return entry.Value;
        return null;
    }

    private static object? NthDictKey(IDictionary dict, int n)
    {
        if (n < 0) return null;
        var i = 0;
        foreach (DictionaryEntry entry in dict)
            if (i++ == n) return entry.Key;
        return null;
    }

    private static int ParseIndex(string segment) => int.TryParse(segment, out var i) ? i : -1;

    private static bool IsScalar(Type type, object value) =>
        type.IsPrimitive || type.IsEnum ||
        value is decimal or Guid or DateTime or DateTimeOffset or DateOnly or TimeOnly or TimeSpan;

    private static string KeyName(object? key) =>
        key switch
        {
            null => "(null)",
            string s => ParameterReader.Truncate(ParameterReader.Quote(s)),
            _ => ParameterReader.Truncate(key.ToString() ?? "(null)"),
        };

    private static (object? Value, string? Error) GuardedGet(Func<object?> get)
    {
        try { return (get(), null); }
        catch (Exception ex) { return (null, $"<error: {ex.GetType().Name}>"); }
    }

    private static HashSet<object> NewAncestors() => new(ReferenceEqualityComparer.Instance);

    private static ValueNode Leaf(string name, string kind, string display, string path) =>
        new() { Name = name, Kind = kind, Display = display, Path = path, Expandable = false };

    private static ValueNode Truncated(int remaining, string path) =>
        Leaf("…", "info", remaining > 0 ? $"… {remaining} more (truncated)" : "… more (truncated)", path + "/…");
}
