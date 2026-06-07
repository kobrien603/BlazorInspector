using Microsoft.JSInterop;

namespace BlazorInspector;

/// <summary>
/// Bridge for the Phase 3 element picker. The JS module (<c>BlazorInspector.lib.module.js</c>) calls
/// <see cref="OnPick"/> with the component id under the cursor; the overlay subscribes to
/// <see cref="Picked"/> to select that node in the tree.
/// </summary>
public static class InspectorInterop
{
    /// <summary>Raised (component id) when the user clicks an element while the picker is active.</summary>
    public static event Action<int>? Picked;

    /// <summary>Invoked from JS: <c>DotNet.invokeMethodAsync('BlazorInspector', 'OnPick', id)</c>.</summary>
    [JSInvokable]
    public static void OnPick(int componentId) => Picked?.Invoke(componentId);
}
