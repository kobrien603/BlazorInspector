using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using BlazorInspector.SourceGen;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace BlazorInspector.Tests;

/// <summary>
/// Drives <see cref="ComponentSourceMapGenerator"/> in-process and asserts the generated map keys.
/// Exercises all three type-name resolution paths: per-file <c>@namespace</c>, <c>_Imports.razor</c>
/// inheritance, and the RootNamespace + folder convention fallback.
/// </summary>
public class ComponentSourceMapGeneratorTests
{
    [Fact]
    public void ResolvesNamespaceDirectiveImportsInheritanceAndConvention()
    {
        var files = new[]
        {
            // _Imports.razor governs everything under /proj/Components via @namespace.
            new InMemoryAdditionalText("/proj/Components/_Imports.razor", "@namespace My.Comp\n"),
            // No own @namespace -> inherits the _Imports namespace + relative sub-folder.
            new InMemoryAdditionalText("/proj/Components/Pages/Counter.razor", "@page \"/counter\"\n<h1>Counter</h1>\n"),
            // Per-file @namespace overrides everything, even though it's under Components.
            new InMemoryAdditionalText("/proj/Components/Widget.razor", "@namespace Override.Ns\n<div>w</div>\n"),
            // Not under any _Imports -> RootNamespace + folder convention.
            new InMemoryAdditionalText("/proj/Other/Card.razor", "<div>card</div>\n"),
        };

        var map = RunAndParseMap(files, rootNamespace: "MyApp", projectDir: "/proj/");

        Assert.Equal("/proj/Components/Pages/Counter.razor", map["My.Comp.Pages.Counter"]);
        Assert.Equal("/proj/Components/Widget.razor", map["Override.Ns.Widget"]);
        Assert.Equal("/proj/Other/Card.razor", map["MyApp.Other.Card"]);

        // _Imports.razor is not a component and must not appear as a type.
        Assert.DoesNotContain(map.Keys, k => k.Contains("_Imports"));
    }

    private static Dictionary<string, string> RunAndParseMap(
        InMemoryAdditionalText[] files, string rootNamespace, string projectDir)
    {
        var compilation = CSharpCompilation.Create(
            "TestAsm",
            syntaxTrees: System.Array.Empty<Microsoft.CodeAnalysis.SyntaxTree>(),
            references: new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(
            generators: new[] { new ComponentSourceMapGenerator().AsSourceGenerator() },
            additionalTexts: files,
            optionsProvider: new TestOptionsProvider(rootNamespace, projectDir));

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        Assert.Empty(diagnostics);

        var generated = output.SyntaxTrees
            .Single(t => t.FilePath.EndsWith("BlazorInspectorSourceMap.g.cs", System.StringComparison.Ordinal))
            .ToString();

        // Parse lines of the form: { "Type.Name", ("/path/file.razor", 3) },
        var map = new Dictionary<string, string>();
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(
                     generated, "\\{\\s*\"([^\"]+)\",\\s*\\(\"([^\"]+)\",\\s*\\d+\\)\\s*\\}"))
        {
            map[m.Groups[1].Value] = m.Groups[2].Value;
        }
        return map;
    }

    private sealed class InMemoryAdditionalText : AdditionalText
    {
        private readonly SourceText _text;
        public InMemoryAdditionalText(string path, string content)
        {
            Path = path;
            _text = SourceText.From(content);
        }
        public override string Path { get; }
        public override SourceText GetText(System.Threading.CancellationToken cancellationToken = default) => _text;
    }

    private sealed class TestOptionsProvider : AnalyzerConfigOptionsProvider
    {
        private readonly TestOptions _global;
        public TestOptionsProvider(string rootNamespace, string projectDir) =>
            _global = new TestOptions(new Dictionary<string, string>
            {
                ["build_property.RootNamespace"] = rootNamespace,
                ["build_property.ProjectDir"] = projectDir,
            });

        public override AnalyzerConfigOptions GlobalOptions => _global;
        public override AnalyzerConfigOptions GetOptions(Microsoft.CodeAnalysis.SyntaxTree tree) => TestOptions.Empty;
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => TestOptions.Empty;
    }

    private sealed class TestOptions : AnalyzerConfigOptions
    {
        public static readonly TestOptions Empty = new(new Dictionary<string, string>());
        private readonly Dictionary<string, string> _values;
        public TestOptions(Dictionary<string, string> values) => _values = values;
        public override bool TryGetValue(string key, out string value) => _values.TryGetValue(key, out value!);
    }
}
