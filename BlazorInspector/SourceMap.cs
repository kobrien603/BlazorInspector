using System.Collections;
using System.Reflection;

namespace BlazorInspector;

/// <summary>
/// Reads the <c>BlazorInspector.Generated.BlazorInspectorSourceMap.Map</c> dictionary that the source
/// generator emits into each consuming assembly, by reflection — so the RCL has no compile-time
/// dependency on the generated symbol. Maps a component's full type name to its <c>.razor</c> file and
/// line, and renders a <c>vscode://file/...</c> link.
/// </summary>
internal static class SourceMap
{
    private const string GeneratedTypeName = "BlazorInspector.Generated.BlazorInspectorSourceMap";

    private static Dictionary<string, (string File, int Line)>? _map;
    private static readonly object Lock = new();

    private static Dictionary<string, (string File, int Line)> Map
    {
        get
        {
            if (_map is not null)
                return _map;

            lock (Lock)
            {
                _map ??= BuildMap();
            }
            return _map;
        }
    }

    private static Dictionary<string, (string File, int Line)> BuildMap()
    {
        var map = new Dictionary<string, (string File, int Line)>(StringComparer.Ordinal);

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var type = assembly.GetType(GeneratedTypeName);
                var field = type?.GetField("Map", BindingFlags.Public | BindingFlags.Static);
                if (field?.GetValue(null) is not IDictionary dict)
                    continue;

                foreach (DictionaryEntry entry in dict)
                {
                    if (entry.Key is not string key)
                        continue;
                    if (TryReadTuple(entry.Value, out var file, out var line))
                        map[key] = (file, line);
                }
            }
            catch
            {
                // Reflection-only / dynamic assemblies can throw; skip them.
            }
        }

        return map;
    }

    /// <summary>Reads the emitted <c>(string File, int Line)</c> value tuple via its public fields.</summary>
    private static bool TryReadTuple(object? value, out string file, out int line)
    {
        file = string.Empty;
        line = 1;
        if (value is null)
            return false;

        var type = value.GetType();
        var item1 = type.GetField("Item1")?.GetValue(value) as string;
        if (item1 is null)
            return false;

        file = item1;
        if (type.GetField("Item2")?.GetValue(value) is int l)
            line = l;
        return true;
    }

    /// <summary>The (file, line) source location for a component type, or null if unmapped.</summary>
    public static (string File, int Line)? Lookup(string fullTypeName) =>
        Map.TryGetValue(fullTypeName, out var location) ? location : null;

    /// <summary>A <c>vscode://file/{absolutePath}:{line}</c> link for a component type, or null if unmapped.</summary>
    public static string? VsCodeLink(string fullTypeName)
    {
        if (Lookup(fullTypeName) is not { } location)
            return null;

        var path = location.File.Replace('\\', '/');
        if (!path.StartsWith('/'))
            path = "/" + path; // vscode://file/ expects a leading slash before a Windows drive too
        return $"vscode://file{path}:{location.Line}";
    }
}
