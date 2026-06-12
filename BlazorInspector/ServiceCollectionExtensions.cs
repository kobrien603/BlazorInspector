using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorInspector;

/// <summary>
/// DI entry point for the inspector. Call <c>builder.Services.AddBlazorInspector()</c> and drop a
/// <c>&lt;InspectorOverlay /&gt;</c> into your layout — that is the entire integration.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the inspector: a singleton <see cref="InspectorRegistry"/> and an
    /// <see cref="IComponentActivator"/> that wraps any previously-registered one and tracks every
    /// component instance.
    ///
    /// The overlay enables itself only when the <em>consuming app</em> was built in Debug (see
    /// <c>Options.Enabled</c>); in a Release app it stays dormant and runs none of the framework-internals
    /// reflection that IL trimming / WASM AOT can strip.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)] // inlined into the consumer, GetCallingAssembly would skip a frame
    public static IServiceCollection AddBlazorInspector(this IServiceCollection services)
    {
        // GetCallingAssembly must be captured in EACH public overload: if this one delegated to the
        // configure overload, the calling assembly observed there would be BlazorInspector itself.
        Assembly? caller = null;
        try { caller = Assembly.GetCallingAssembly(); }
        catch { /* net8.0 NativeAOT throws PlatformNotSupportedException (dotnet/runtime#94200) — stay dormant */ }
        return AddBlazorInspectorCore(services, static _ => { }, caller);
    }

    /// <inheritdoc cref="AddBlazorInspector(IServiceCollection)"/>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static IServiceCollection AddBlazorInspector(this IServiceCollection services, Action<InspectorOptions> configure)
    {
        Assembly? caller = null;
        try { caller = Assembly.GetCallingAssembly(); }
        catch { }
        return AddBlazorInspectorCore(services, configure, caller);
    }

    private static IServiceCollection AddBlazorInspectorCore(
        IServiceCollection services, Action<InspectorOptions> configure, Assembly? caller)
    {
        // Enable by default only when the CONSUMING app was built in Debug. This MUST be a runtime check on
        // the consumer's assembly, never an `#if DEBUG` in this library: a library's `#if DEBUG` is evaluated
        // when the library itself is compiled — and the NuGet package is built in Release — so a compile-time
        // gate here would strip the inspector out of the shipped package and leave it permanently dormant in
        // every consumer, regardless of how the consumer is built. The explicit Enabled option still overrides.
        var options = new InspectorOptions { Enabled = HostBuiltInDebug(caller) };
        configure(options);

        // Registry and options are always registered so the overlay can resolve them with a plain [Inject]
        // and never throw — the overlay itself only goes live when Options.Enabled is true.
        services.AddSingleton(options);
        services.AddSingleton<InspectorRegistry>();

        // Only take over component creation when the inspector is actually live. In a dormant (Release)
        // consumer we register nothing functional: the host keeps its own default activator, so there is
        // zero per-component overhead and no tracking list that would grow unpruned while the overlay —
        // the only thing that ever reads or prunes the registry — never runs. Gated on the post-configure
        // Enabled so an explicit `o => o.Enabled = ...` override is honored either way.
        if (options.Enabled)
        {
            // Capture whatever activator was registered before us (bUnit's, the framework default, etc.)
            // and wrap it. Registered as a concrete singleton so a later framework TryAddSingleton is a
            // no-op. The wrapper only does cheap weak-reference tracking on the creation path.
            var existing = services.LastOrDefault(s => s.ServiceType == typeof(IComponentActivator));
            if (existing is not null)
                services.Remove(existing);

            services.AddSingleton<IComponentActivator>(sp =>
            {
                var registry = sp.GetRequiredService<InspectorRegistry>();
                var inner = existing is not null ? Materialize(existing, sp) as IComponentActivator : null;
                return new InspectorComponentActivator(inner, registry);
            });
        }

        return services;
    }

    /// <summary>
    /// True when the consuming app's assembly was compiled in Debug. Uses the entry assembly when available,
    /// falling back to the assembly that called <c>AddBlazorInspector</c> — on MAUI Android / iOS /
    /// Mac Catalyst, <see cref="Assembly.GetEntryAssembly"/> returns null because managed code is launched
    /// from native startup with no managed Main (dotnet/android#9960), and the caller (the consumer's
    /// MauiProgram) is the app assembly there.
    /// </summary>
    private static bool HostBuiltInDebug(Assembly? caller)
    {
        try
        {
            return AssemblyBuiltInDebug(Assembly.GetEntryAssembly() ?? caller);
        }
        catch
        {
            return false; // Unknown host: stay dormant rather than risk running live in a Release app.
        }
    }

    /// <summary>
    /// True when <paramref name="asm"/> was compiled in Debug. The C# compiler emits a
    /// <see cref="DebuggableAttribute"/> with JIT tracking enabled / optimizations disabled for Debug builds
    /// and omits those flags for Release builds, so this distinguishes a Debug consumer from a Release one at
    /// runtime — exactly the signal a library cannot get from its own <c>#if DEBUG</c>. Note a build with
    /// <c>&lt;DebugType&gt;none&lt;/DebugType&gt;</c> emits no attribute at all and reads as Release here.
    /// </summary>
    internal static bool AssemblyBuiltInDebug(Assembly? asm)
    {
        var attr = asm?.GetCustomAttribute<DebuggableAttribute>();
        return attr is not null && attr.IsJITTrackingEnabled;
    }

    /// <summary>Builds the service described by an existing descriptor (instance / factory / type).</summary>
    private static object? Materialize(ServiceDescriptor descriptor, IServiceProvider provider)
    {
        if (descriptor.ImplementationInstance is not null)
            return descriptor.ImplementationInstance;
        if (descriptor.ImplementationFactory is not null)
            return descriptor.ImplementationFactory(provider);
        if (descriptor.ImplementationType is not null)
            return ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType);
        return null;
    }
}
