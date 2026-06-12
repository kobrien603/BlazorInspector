namespace BlazorInspector;

/// <summary>Corner of the viewport the floating button docks to.</summary>
public enum InspectorCorner
{
    BottomRight,
    BottomLeft,
    TopRight,
    TopLeft,
}

/// <summary>Configuration for the inspector. Pass to <c>AddBlazorInspector(o =&gt; ...)</c>.</summary>
public sealed class InspectorOptions
{
    /// <summary>
    /// Whether the overlay is live. <c>AddBlazorInspector</c> defaults this to true when the consuming app
    /// was built in Debug and false when it was built in Release — detected at runtime from the entry
    /// assembly, falling back to the assembly that called <c>AddBlazorInspector</c> (on MAUI
    /// Android / iOS / Mac Catalyst the entry assembly is unavailable). The overlay therefore renders
    /// nothing — and runs no reflection — in a Release app even if the tag is left in. Set it explicitly in
    /// <c>AddBlazorInspector(o =&gt; o.Enabled = ...)</c> to override.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>How often the panel auto-refreshes while open. Default 500ms.</summary>
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Where the floating button sits. Default bottom-right.</summary>
    public InspectorCorner Corner { get; set; } = InspectorCorner.BottomRight;

    /// <summary>Whether the panel starts open. Default false.</summary>
    public bool StartOpen { get; set; }
}
