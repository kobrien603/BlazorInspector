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
    /// DEBUG-ONLY: in Release builds this is a no-op. The inspector reflects over private framework
    /// internals that IL trimming / WASM AOT can strip, so it must never ship enabled in Release.
    /// </summary>
    public static IServiceCollection AddBlazorInspector(this IServiceCollection services) =>
        services.AddBlazorInspector(static _ => { });

    /// <inheritdoc cref="AddBlazorInspector(IServiceCollection)"/>
    public static IServiceCollection AddBlazorInspector(this IServiceCollection services, Action<InspectorOptions> configure)
    {
        var options = new InspectorOptions();
        configure(options);

        // The registry and options are always registered so the overlay can resolve them with a plain
        // [Inject] and never throw — but the overlay only goes live when Options.Enabled is true.
#if !DEBUG
        options.Enabled = false; // Release: dormant. No tracking activator is registered below either.
#endif
        services.AddSingleton(options);
        services.AddSingleton<InspectorRegistry>();

#if DEBUG
        // Capture whatever activator was registered before us (bUnit's, the framework default, etc.)
        // and wrap it. We register unconditionally so a later framework TryAddSingleton is a no-op.
        var existing = services.LastOrDefault(s => s.ServiceType == typeof(IComponentActivator));
        if (existing is not null)
            services.Remove(existing);

        services.AddSingleton<IComponentActivator>(sp =>
        {
            var registry = sp.GetRequiredService<InspectorRegistry>();
            var inner = existing is not null ? Materialize(existing, sp) as IComponentActivator : null;
            return new InspectorComponentActivator(inner, registry);
        });
#endif
        return services;
    }

#if DEBUG
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
#endif
}
