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

        services.AddBlazorInspector();

        using var provider = services.BuildServiceProvider();
        var activator = provider.GetRequiredService<IComponentActivator>();
        var registry = provider.GetRequiredService<InspectorRegistry>();

        Assert.IsType<InspectorComponentActivator>(activator);

        var instance = activator.CreateInstance(typeof(FakeComponent));

        Assert.IsType<FakeComponent>(instance);
        Assert.Contains(typeof(FakeComponent), preexisting.Created); // chained through DI
        Assert.Equal(1, registry.Count);
    }
}
