using Acme.App;
using BlazorInspector;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorInspector.Tests;

public class ActivatorAndDiTests
{
    [Fact]
    public void ActivatorChainsToInnerAndTracksInstance()
    {
        var registry = new InspectorRegistry();
        var inner = new RecordingActivator();
        var activator = new InspectorComponentActivator(inner, registry);

        var instance = activator.CreateInstance(typeof(FakeComponent));

        Assert.IsType<FakeComponent>(instance);
        Assert.Contains(typeof(FakeComponent), inner.Created); // chained
        Assert.Equal(1, registry.Count);                       // tracked
    }

    [Fact]
    public void ActivatorFallsBackToDefaultWhenNoInner()
    {
        var registry = new InspectorRegistry();
        var activator = new InspectorComponentActivator(inner: null, registry);

        var instance = activator.CreateInstance(typeof(EmptyComponent));

        Assert.IsType<EmptyComponent>(instance);
        Assert.Equal(1, registry.Count);
    }

    [Fact]
    public void ActivatorDoesNotTrackInspectorOwnComponents()
    {
        var registry = new InspectorRegistry();
        var activator = new InspectorComponentActivator(inner: null, registry);

        activator.CreateInstance(typeof(InspectorOverlay)); // in the BlazorInspector namespace

        Assert.Equal(0, registry.Count);
    }

    [Fact]
    public void IsInspectorTypeRecognizesOwnNamespaceOnly()
    {
        Assert.True(InspectorComponentActivator.IsInspectorType(typeof(InspectorOverlay)));
        Assert.False(InspectorComponentActivator.IsInspectorType(typeof(FakeComponent)));
    }

    [Fact]
    public void AddBlazorInspectorWrapsExistingActivatorAndRegistersRegistry()
    {
        var preexisting = new RecordingActivator();
        var services = new ServiceCollection();
        services.AddSingleton<IComponentActivator>(preexisting);

        // Force-enable: the test host's entry assembly isn't a representative Debug consumer, so the
        // activator registration is gated on an explicit Enabled rather than ambient build detection.
        services.AddBlazorInspector(o => o.Enabled = true);

        using var provider = services.BuildServiceProvider();
        var activator = provider.GetRequiredService<IComponentActivator>();
        var registry = provider.GetRequiredService<InspectorRegistry>();

        Assert.IsType<InspectorComponentActivator>(activator);

        var instance = activator.CreateInstance(typeof(FakeComponent));

        Assert.IsType<FakeComponent>(instance);
        Assert.Contains(typeof(FakeComponent), preexisting.Created); // chained through DI
        Assert.Equal(1, registry.Count);
    }

    [Fact]
    public void AddBlazorInspectorWhenDisabledLeavesHostActivatorUntouched()
    {
        // A Release consumer (Enabled=false) must register no component activator: the host keeps its own,
        // so nothing intercepts — or tracks — component creation. Guards against the registry's tracking
        // list growing unpruned while the overlay (the only thing that prunes it) never runs.
        var preexisting = new RecordingActivator();
        var services = new ServiceCollection();
        services.AddSingleton<IComponentActivator>(preexisting);

        services.AddBlazorInspector(o => o.Enabled = false);

        using var provider = services.BuildServiceProvider();

        // Host's activator is untouched — no InspectorComponentActivator wrapper inserted.
        Assert.Same(preexisting, provider.GetRequiredService<IComponentActivator>());
        // Registry and options still resolve so the (dormant) overlay can [Inject] them without throwing.
        Assert.NotNull(provider.GetRequiredService<InspectorRegistry>());
        Assert.False(provider.GetRequiredService<InspectorOptions>().Enabled);
    }
}
