using Acme.App;
using BlazorInspector;
using Microsoft.AspNetCore.Components;

namespace BlazorInspector.Tests;

public class ParameterReaderTests
{
    private static ParameterValue Get(IReadOnlyList<ParameterValue> values, string name) =>
        values.Single(v => v.Name == name);

    [Fact]
    public void ReadsParameterAndCascadingParameterButNotPlainProperties()
    {
        var values = ParameterReader.Read(new FakeComponent());

        Assert.Contains(values, v => v.Name == nameof(FakeComponent.Name) && v.Kind == "parameter");
        Assert.Contains(values, v => v.Name == nameof(FakeComponent.Theme) && v.Kind == "cascading");
        Assert.DoesNotContain(values, v => v.Name == nameof(FakeComponent.NotAParameter));
    }

    [Fact]
    public void FormatsNullStringsNumbersFragmentsCallbacksAndCollections()
    {
        var component = new FakeComponent
        {
            Name = "hello",
            Count = 42,
            ChildContent = builder => { },
            Template = _ => builder => { },
            Items = new[] { 1, 2, 3 },
        };

        var values = ParameterReader.Read(component);

        Assert.Equal("\"hello\"", Get(values, nameof(FakeComponent.Name)).Value);
        Assert.Equal("42", Get(values, nameof(FakeComponent.Count)).Value);
        Assert.Equal("<fragment>", Get(values, nameof(FakeComponent.ChildContent)).Value);
        Assert.Equal("<fragment>", Get(values, nameof(FakeComponent.Template)).Value);
        Assert.Equal("<event>", Get(values, nameof(FakeComponent.OnClick)).Value);
        Assert.Equal("<event>", Get(values, nameof(FakeComponent.OnValue)).Value);
        Assert.Contains("(3 items)", Get(values, nameof(FakeComponent.Items)).Value);
        Assert.Equal("null", Get(values, nameof(FakeComponent.Theme)).Value);
    }

    [Fact]
    public void ThrowingGetterDoesNotCrashAndIsReportedAsError()
    {
        var values = ParameterReader.Read(new FakeComponent());

        var throwing = Get(values, nameof(FakeComponent.Throws));
        Assert.StartsWith("<error:", throwing.Value);
    }

    [Fact]
    public void ComponentWithNoParametersReturnsEmpty()
    {
        Assert.Empty(ParameterReader.Read(new EmptyComponent()));
    }

    [Fact]
    public void ReadStateSurfacesPrivateFieldsAndNonParameterProperties()
    {
        var component = new FakeComponent();
        component.SetLocalState(42);

        var state = ParameterReader.ReadState(component);

        var field = state.Single(v => v.Name == "_localState");
        Assert.Equal("field", field.Kind);
        Assert.Equal("42", field.Value);

        Assert.Contains(state, v => v.Name == nameof(FakeComponent.NotAParameter) && v.Kind == "property");

        // Parameters must not be duplicated into the state section.
        Assert.DoesNotContain(state, v => v.Name == nameof(FakeComponent.Name));
        Assert.DoesNotContain(state, v => v.Name == nameof(FakeComponent.Theme));
    }
}
