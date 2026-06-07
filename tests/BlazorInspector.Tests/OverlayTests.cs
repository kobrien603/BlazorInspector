using BlazorInspector;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorInspector.Tests;

public class OverlayTests : IDisposable
{
    private readonly TestContext _ctx = new();

    public OverlayTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose; // picker JS calls are no-ops in tests
        // Long interval so the auto-refresh timer never fires during the test.
        _ctx.Services.AddSingleton(new InspectorOptions { RefreshInterval = TimeSpan.FromHours(1) });
        _ctx.Services.AddSingleton<InspectorRegistry>();
    }

    [Fact]
    public void RendersFloatingButton()
    {
        var cut = _ctx.RenderComponent<InspectorOverlay>();
        Assert.NotNull(cut.Find(".bi-fab"));
    }

    [Fact]
    public void OpensPanelOnClickAndShowsEmptyState()
    {
        var cut = _ctx.RenderComponent<InspectorOverlay>();

        cut.Find(".bi-fab").Click();

        Assert.NotNull(cut.Find(".bi-panel"));
        Assert.Contains("No components tracked yet", cut.Markup);
    }

    public void Dispose() => _ctx.Dispose();
}
