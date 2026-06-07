using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace BlazorInspector.SourceGen;

/// <summary>
/// Emits an <c>internal static class BlazorInspectorSourceMap</c> into the compiling assembly that
/// maps each component's full type name to its <c>.razor</c> source file and a representative line.
/// The map is keyed by full type-name <see cref="string"/> (not <see cref="System.Type"/>) so the
/// inspector RCL can read it via reflection without a hard reference to the generated symbol.
/// </summary>
/// <remarks>
/// Type-name resolution mirrors the Razor compiler's own rules as closely as a convention map can:
/// a per-file <c>@namespace</c> directive wins; otherwise the nearest ancestor <c>_Imports.razor</c>
/// that declares <c>@namespace</c> governs (plus the relative sub-folder path); otherwise it falls
/// back to <c>RootNamespace</c> + folder path. (A true <c>#line</c>-based mapping off the Razor
/// generated output is not reachable here: source generators cannot see each other's outputs within
/// one compilation, so this generator never sees the Razor-generated component classes. Honoring
/// <c>@namespace</c> directives is the equivalent fix for the cases the plain convention misses —
/// components whose namespace differs from their folder, the most common real-world miss.)
/// </remarks>
[Generator]
public sealed class ComponentSourceMapGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Root namespace + project directory drive the convention-based type-name mapping.
        var options = context.AnalyzerConfigOptionsProvider.Select(static (provider, _) =>
        {
            provider.GlobalOptions.TryGetValue("build_property.RootNamespace", out var rootNs);
            provider.GlobalOptions.TryGetValue("build_property.ProjectDir", out var projectDir);
            return (RootNamespace: rootNs ?? string.Empty, ProjectDir: projectDir ?? string.Empty);
        });

        // Every .razor file fed in via <AdditionalFiles>, carrying its first content line and any
        // explicit @namespace directive (needed for both per-file overrides and _Imports.razor).
        var razorFiles = context.AdditionalTextsProvider
            .Where(static f => f.Path.EndsWith(".razor", System.StringComparison.OrdinalIgnoreCase))
            .Select(static (f, ct) =>
            {
                var text = f.GetText(ct);
                return new RazorFile(f.Path, FirstComponentLine(text), ReadNamespaceDirective(text));
            });

        var combined = razorFiles.Collect().Combine(options);

        context.RegisterSourceOutput(combined, static (spc, data) =>
        {
            var (files, opts) = data;
            spc.AddSource("BlazorInspectorSourceMap.g.cs", BuildSource(files, opts.RootNamespace, opts.ProjectDir));
        });
    }

    private readonly struct RazorFile
    {
        public RazorFile(string path, int line, string? @namespace)
        {
            Path = path;
            Line = line;
            Namespace = @namespace;
        }

        public string Path { get; }
        public int Line { get; }
        public string? Namespace { get; }
    }

    /// <summary>First line that isn't blank or a Razor directive (lines starting with '@'); 1-based.</summary>
    private static int FirstComponentLine(SourceText? text)
    {
        if (text is null)
            return 1;

        var lineNumber = 0;
        foreach (var line in text.Lines)
        {
            lineNumber++;
            var trimmed = line.ToString().TrimStart();
            if (trimmed.Length == 0 || trimmed[0] == '@')
                continue;
            return lineNumber;
        }

        return 1;
    }

    /// <summary>Value of an <c>@namespace Foo.Bar</c> directive, or null if the file has none.</summary>
    private static string? ReadNamespaceDirective(SourceText? text)
    {
        if (text is null)
            return null;

        foreach (var line in text.Lines)
        {
            var trimmed = line.ToString().Trim();
            if (!trimmed.StartsWith("@namespace", System.StringComparison.Ordinal))
                continue;
            var rest = trimmed.Substring("@namespace".Length).Trim();
            return rest.Length == 0 ? null : rest;
        }

        return null;
    }

    private static string BuildSource(ImmutableArray<RazorFile> files, string rootNamespace, string projectDir)
    {
        // Folders governed by an _Imports.razor @namespace, longest (most specific) path first so the
        // nearest ancestor wins when resolving a component's namespace.
        var importsNamespaces = files
            .Where(f => IsImportsFile(f.Path) && f.Namespace is not null)
            .Select(f => (Folder: NormalizeFolder(DirectoryOf(f.Path)), Namespace: f.Namespace!))
            .OrderByDescending(e => e.Folder.Length)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("namespace BlazorInspector.Generated");
        sb.AppendLine("{");
        sb.AppendLine("    internal static class BlazorInspectorSourceMap");
        sb.AppendLine("    {");
        sb.AppendLine("        // fullTypeName -> (absolute .razor path, 1-based line)");
        sb.AppendLine("        public static readonly System.Collections.Generic.Dictionary<string, (string File, int Line)> Map =");
        sb.AppendLine("            new System.Collections.Generic.Dictionary<string, (string File, int Line)>");
        sb.AppendLine("            {");

        var seen = new HashSet<string>();
        foreach (var file in files)
        {
            if (IsImportsFile(file.Path))
                continue; // _Imports.razor is not a component type.

            var typeName = ToTypeName(file, rootNamespace, projectDir, importsNamespaces);
            if (typeName is null || !seen.Add(typeName))
                continue;

            sb.Append("                { ")
              .Append(Quote(typeName))
              .Append(", (")
              .Append(Quote(file.Path))
              .Append(", ")
              .Append(file.Line)
              .AppendLine(") },");
        }

        sb.AppendLine("            };");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    /// <summary>
    /// Resolves a component's full type name: per-file <c>@namespace</c> wins; else the nearest
    /// ancestor <c>_Imports.razor</c> namespace plus the relative sub-folder; else RootNamespace +
    /// folder convention. The class name is always the sanitized file name.
    /// </summary>
    private static string? ToTypeName(
        RazorFile file, string rootNamespace, string projectDir,
        List<(string Folder, string Namespace)> importsNamespaces)
    {
        var className = Sanitize(FileNameWithoutExtension(file.Path));
        if (className.Length == 0)
            return null;

        // 1. Explicit per-file @namespace directive.
        if (file.Namespace is { } own)
            return own + "." + className;

        var fileFolder = NormalizeFolder(DirectoryOf(file.Path));

        // 2. Nearest ancestor _Imports.razor that declares @namespace.
        foreach (var (folder, ns) in importsNamespaces)
        {
            if (!IsUnderFolder(fileFolder, folder))
                continue;
            var sub = RelativeNamespaceSuffix(fileFolder, folder);
            var nsFull = sub.Length == 0 ? ns : ns + "." + sub;
            return nsFull + "." + className;
        }

        // 3. Convention: RootNamespace + folder path relative to the project directory.
        var folderNs = ConventionFolderNamespace(file.Path, rootNamespace, projectDir);
        return folderNs.Length == 0 ? className : folderNs + "." + className;
    }

    /// <summary>RootNamespace + sanitized folder segments between the project dir and the file.</summary>
    private static string ConventionFolderNamespace(string path, string rootNamespace, string projectDir)
    {
        var relative = path;
        if (!string.IsNullOrEmpty(projectDir) && path.StartsWith(projectDir, System.StringComparison.OrdinalIgnoreCase))
            relative = path.Substring(projectDir.Length);
        relative = relative.Replace('\\', '/').TrimStart('/');

        var lastSlash = relative.LastIndexOf('/');
        var folderPart = lastSlash < 0 ? string.Empty : relative.Substring(0, lastSlash);

        var segments = new List<string>();
        if (!string.IsNullOrEmpty(rootNamespace))
            segments.Add(rootNamespace);
        foreach (var part in folderPart.Split(new[] { '/' }, System.StringSplitOptions.RemoveEmptyEntries))
        {
            var ident = Sanitize(part);
            if (ident.Length != 0)
                segments.Add(ident);
        }
        return string.Join(".", segments);
    }

    private static bool IsImportsFile(string path) =>
        FileNameWithoutExtension(path).Equals("_Imports", System.StringComparison.OrdinalIgnoreCase);

    private static string DirectoryOf(string path)
    {
        var normalized = path.Replace('\\', '/');
        var slash = normalized.LastIndexOf('/');
        return slash < 0 ? string.Empty : normalized.Substring(0, slash);
    }

    private static string FileNameWithoutExtension(string path)
    {
        var normalized = path.Replace('\\', '/');
        var slash = normalized.LastIndexOf('/');
        var name = slash < 0 ? normalized : normalized.Substring(slash + 1);
        var dot = name.LastIndexOf('.');
        return dot < 0 ? name : name.Substring(0, dot);
    }

    private static string NormalizeFolder(string folder) =>
        folder.Replace('\\', '/').TrimEnd('/');

    private static bool IsUnderFolder(string fileFolder, string ancestor)
    {
        if (ancestor.Length == 0)
            return true;
        if (fileFolder.Equals(ancestor, System.StringComparison.OrdinalIgnoreCase))
            return true;
        return fileFolder.StartsWith(ancestor + "/", System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Dotted, sanitized sub-folder path from <paramref name="ancestor"/> down to the file.</summary>
    private static string RelativeNamespaceSuffix(string fileFolder, string ancestor)
    {
        var rel = ancestor.Length == 0
            ? fileFolder
            : fileFolder.Substring(System.Math.Min(ancestor.Length, fileFolder.Length));
        rel = rel.TrimStart('/');
        if (rel.Length == 0)
            return string.Empty;

        var parts = rel.Split(new[] { '/' }, System.StringSplitOptions.RemoveEmptyEntries)
                       .Select(Sanitize)
                       .Where(p => p.Length != 0);
        return string.Join(".", parts);
    }

    private static string Sanitize(string segment)
    {
        var sb = new StringBuilder(segment.Length);
        foreach (var ch in segment)
            sb.Append(char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_');
        if (sb.Length > 0 && char.IsDigit(sb[0]))
            sb.Insert(0, '_');
        return sb.ToString();
    }

    private static string Quote(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
