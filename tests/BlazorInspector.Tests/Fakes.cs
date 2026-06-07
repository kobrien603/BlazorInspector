using Microsoft.AspNetCore.Components;

// Deliberately NOT under the BlazorInspector.* namespace — the activator skips its own namespace,
// so fake components used to exercise tracking must look like ordinary host components.
namespace Acme.App;

/// <summary>A component exercising every parameter shape the reader must handle safely.</summary>
internal sealed class FakeComponent : IComponent
{
    [Parameter] public string? Name { get; set; }
    [Parameter] public int Count { get; set; }
    [Parameter] public RenderFragment? ChildContent { get; set; }
    [Parameter] public RenderFragment<int>? Template { get; set; }
    [Parameter] public EventCallback OnClick { get; set; }
    [Parameter] public EventCallback<int> OnValue { get; set; }
    [Parameter] public IEnumerable<int>? Items { get; set; }
    [CascadingParameter] public string? Theme { get; set; }

    [Parameter]
    public object? Throws
    {
        get => throw new InvalidOperationException("boom");
        set { }
    }

    /// <summary>Not a parameter — ignored by the parameter reader, surfaced by the state reader.</summary>
    public string NotAParameter { get; set; } = "ignored";

    /// <summary>Private state (like a page's currentCount) — surfaced only by the state reader.</summary>
    private int _localState = 7;

    public void SetLocalState(int value) => _localState = value;

    public void Attach(RenderHandle renderHandle) { }
    public Task SetParametersAsync(ParameterView parameters) => Task.CompletedTask;
}

/// <summary>A second component type with no parameters.</summary>
internal sealed class EmptyComponent : IComponent
{
    public void Attach(RenderHandle renderHandle) { }
    public Task SetParametersAsync(ParameterView parameters) => Task.CompletedTask;
}

/// <summary>Records which types it created so the activator-chaining path can be asserted.</summary>
internal sealed class RecordingActivator : IComponentActivator
{
    public List<Type> Created { get; } = new();

    public IComponent CreateInstance(Type componentType)
    {
        Created.Add(componentType);
        return (IComponent)Activator.CreateInstance(componentType)!;
    }
}
