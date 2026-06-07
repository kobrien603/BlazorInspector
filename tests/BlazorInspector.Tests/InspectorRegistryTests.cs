using System.Runtime.CompilerServices;
using Acme.App;
using BlazorInspector;

namespace BlazorInspector.Tests;

public class InspectorRegistryTests
{
    [Fact]
    public void TracksComponentsAndSnapshotsThem()
    {
        var registry = new InspectorRegistry();
        var a = new FakeComponent { Name = "a" };
        var b = new EmptyComponent();

        registry.Track(a);
        registry.Track(b);

        Assert.Equal(2, registry.Count);

        var snapshot = registry.Snapshot();
        Assert.Contains(snapshot, s => s.ComponentType == typeof(FakeComponent));
        Assert.Contains(snapshot, s => s.ComponentType == typeof(EmptyComponent));
    }

    [Fact]
    public void PrunesDeadWeakReferences()
    {
        var registry = new InspectorRegistry();
        TrackEphemeral(registry); // tracked instance becomes unreachable when this returns

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.Equal(0, registry.Prune());
    }

    // Separate non-inlined method so the tracked instance isn't rooted by the calling frame.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void TrackEphemeral(InspectorRegistry registry) => registry.Track(new FakeComponent());
}
