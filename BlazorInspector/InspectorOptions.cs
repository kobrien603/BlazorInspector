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
    /// Whether the overlay is live. Set true in DEBUG by <c>AddBlazorInspector</c> and false in Release,
    /// so the overlay renders nothing (and runs no reflection) in Release even if the tag is left in.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>How often the panel auto-refreshes while open. Default 500ms.</summary>
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Where the floating button sits. Default bottom-right.</summary>
    public InspectorCorner Corner { get; set; } = InspectorCorner.BottomRight;

    /// <summary>Whether the panel starts open. Default false.</summary>
    public bool StartOpen { get; set; }
}
