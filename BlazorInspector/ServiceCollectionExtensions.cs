using System.Diagnostics;
using System.Reflection;
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
    public static IServiceCollection AddBlazorInspector(this IServiceCollection services) =>
        services.AddBlazorInspector(static _ => { });

    /// <inheritdoc cref="AddBlazorInspector(IServiceCollection)"/>
    public static IServiceCollection AddBlazorInspector(this IServiceCollection services, Action<InspectorOptions> configure)
    {
        // Enable by default only when the CONSUMING app was built in Debug. This MUST be a runtime check on
        // the entry assembly, never an `#if DEBUG` in this library: a library's `#if DEBUG` is evaluated when
        // the library itself is compiled — and the NuGet package is built in Release — so a compile-time gate
        // here would strip the inspector out of the shipped package and leave it permanently dormant in every
        // consumer, regardless of how the consumer is built. The explicit Enabled option still overrides this.
        var options = new InspectorOptions { Enabled = EntryAssemblyBuiltInDebug() };
        configure(options);

        // Registry and options are always registered so the overlay can resolve them with a plain [Inject]
        // and never throw — the overlay itself only goes live when Options.Enabled is true.
        services.AddSingleton(options);
        services.AddSingleton<InspectorRegistry>();

        // Capture whatever activator was registered before us (bUnit's, the framework default, etc.) and
        // wrap it. Registered unconditionally so a later framework TryAddSingleton is a no-op. The wrapper
        // only does cheap weak-reference tracking on the creation path; it runs no framework-internals
        // reflection, so it is inert (beyond the tracking list) in a Release app where Enabled is false.
        var existing = services.LastOrDefault(s => s.ServiceType == typeof(IComponentActivator));
        if (existing is not null)
            services.Remove(existing);

        services.AddSingleton<IComponentActivator>(sp =>
        {
            var registry = sp.GetRequiredService<InspectorRegistry>();
            var inner = existing is not null ? Materialize(existing, sp) as IComponentActivator : null;
            return new InspectorComponentActivator(inner, registry);
        });

        return services;
    }

    /// <summary>
    /// True when the entry (consuming app) assembly was compiled in Debug. The C# compiler emits a
    /// <see cref="DebuggableAttribute"/> with JIT tracking enabled / optimizations disabled for Debug builds
    /// and omits that flag for Release builds, so this distinguishes a Debug consumer from a Release one at
    /// runtime — exactly the signal a library cannot get from its own <c>#if DEBUG</c>.
    /// </summary>
    private static bool EntryAssemblyBuiltInDebug()
    {
        try
        {
            var entry = Assembly.GetEntryAssembly();
            var attr = entry?.GetCustomAttribute<DebuggableAttribute>();
            return attr is not null && attr.IsJITTrackingEnabled;
        }
        catch
        {
            return false; // Unknown host: stay dormant rather than risk running live in a Release app.
        }
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
