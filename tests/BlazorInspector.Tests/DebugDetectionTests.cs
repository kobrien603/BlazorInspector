using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;

namespace BlazorInspector.Tests;

/// <summary>
/// Tests the Debug-vs-Release detection that gates the inspector, against synthetic assemblies so the
/// results don't depend on how this test host itself was built. The shapes mirror what the C# compiler
/// actually emits: Debug builds get a <see cref="DebuggableAttribute"/> with the Default (JIT tracking)
/// and DisableOptimizations bits; Release builds get only IgnoreSymbolStoreSequencePoints; a build with
/// &lt;DebugType&gt;none&lt;/DebugType&gt; gets no attribute at all.
/// </summary>
public class DebugDetectionTests
{
    [Fact]
    public void NullAssemblyReadsAsRelease()
    {
        // No resolvable host assembly (e.g. GetEntryAssembly and GetCallingAssembly both unavailable):
        // stay dormant rather than risk running live in a Release app.
        Assert.False(ServiceCollectionExtensions.AssemblyBuiltInDebug(null));
    }

    [Fact]
    public void DebugShapedAttributeReadsAsDebug()
    {
        var asm = BuildAssembly(DebuggableAttribute.DebuggingModes.Default
                                | DebuggableAttribute.DebuggingModes.DisableOptimizations);

        Assert.True(ServiceCollectionExtensions.AssemblyBuiltInDebug(asm));
    }

    [Fact]
    public void ReleaseShapedAttributeReadsAsRelease()
    {
        var asm = BuildAssembly(DebuggableAttribute.DebuggingModes.IgnoreSymbolStoreSequencePoints);

        Assert.False(ServiceCollectionExtensions.AssemblyBuiltInDebug(asm));
    }

    [Fact]
    public void MissingAttributeReadsAsRelease()
    {
        var asm = BuildAssembly(modes: null);

        Assert.False(ServiceCollectionExtensions.AssemblyBuiltInDebug(asm));
    }

    /// <summary>A throwaway in-memory assembly, optionally stamped with a DebuggableAttribute.</summary>
    private static Assembly BuildAssembly(DebuggableAttribute.DebuggingModes? modes)
    {
        var builder = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName($"DebugDetectionProbe_{(int?)modes ?? -1}"),
            AssemblyBuilderAccess.Run);

        if (modes is { } m)
        {
            var ctor = typeof(DebuggableAttribute).GetConstructor(new[] { typeof(DebuggableAttribute.DebuggingModes) })!;
            builder.SetCustomAttribute(new CustomAttributeBuilder(ctor, new object[] { m }));
        }

        return builder;
    }
}
