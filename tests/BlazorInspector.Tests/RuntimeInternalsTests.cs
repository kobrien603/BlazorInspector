using BlazorInspector;

namespace BlazorInspector.Tests;

/// <summary>
/// STEP 0 guard: every reflected framework member must still resolve against the live runtime.
/// If this fails after a TFM bump, the private member names drifted — fix them in RuntimeInternals.
/// </summary>
public class RuntimeInternalsTests
{
    [Fact]
    public void AllReflectedMembersResolve()
    {
        var probe = RuntimeInternals.ProbeMembers();

        Assert.NotEmpty(probe);
        var unresolved = probe.Where(kv => !kv.Value).Select(kv => kv.Key).ToList();
        Assert.True(unresolved.Count == 0, "Unresolved framework members: " + string.Join(", ", unresolved));
    }
}
