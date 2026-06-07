using System.Collections.Generic;
using System.Linq;
using BlazorInspector;
using Microsoft.AspNetCore.Components;

namespace BlazorInspector.Tests;

/// <summary>
/// Exercises the expandable value tree: collections, arrays, dictionaries, nested objects, cycles,
/// throwing getters, lazy expansion, and breadth truncation.
/// </summary>
public class ValueReaderTests
{
    private sealed class Row
    {
        public string Name { get; set; } = "";
        public int Temp { get; set; }
    }

    private sealed class SelfRef
    {
        public string Label { get; set; } = "";
        public SelfRef? Self { get; set; }
    }

    private sealed class DataComponent : IComponent
    {
        [Parameter] public List<Row>? Rows { get; set; }
        [Parameter] public int[]? Numbers { get; set; }
        [Parameter] public Dictionary<string, int>? Map { get; set; }
        [Parameter] public Row? Single { get; set; }
        [Parameter] public SelfRef? Cyclic { get; set; }
        [Parameter] public string? Text { get; set; }

        [Parameter]
        public object? Throws
        {
            get => throw new InvalidOperationException("boom");
            set { }
        }

        // Non-parameter members surface as State.
        public int Counter = 5;            // writable field
        public int Doubled => Counter * 2; // read-only computed property

        public void Attach(RenderHandle renderHandle) { }
        public Task SetParametersAsync(ParameterView parameters) => Task.CompletedTask;
    }

    private static ValueNode Param(DataComponent component, string name, params string[] expanded)
    {
        var roots = ValueReader.BuildRoots(component, new HashSet<string>(expanded));
        return roots.Parameters.Single(n => n.Name == name);
    }

    [Fact]
    public void CollectionIsLazy_SummaryWhenCollapsed_ItemsWhenExpanded()
    {
        var c = new DataComponent { Rows = new() { new Row { Name = "a", Temp = 1 }, new Row { Name = "b", Temp = 2 } } };

        var collapsed = Param(c, "Rows");
        Assert.True(collapsed.Expandable);
        Assert.Contains("2 items", collapsed.Display);
        Assert.Empty(collapsed.Children); // lazy: no children until the path is expanded

        var rows = Param(c, "Rows", "P:Rows");
        Assert.Equal(2, rows.Children.Count);
        Assert.Equal("[0]", rows.Children[0].Name);
        Assert.Equal("[1]", rows.Children[1].Name);
        Assert.True(rows.Children[0].Expandable); // each Row is an object that can be drilled into
    }

    [Fact]
    public void CollectionItemExposesItsProperties()
    {
        var c = new DataComponent { Rows = new() { new Row { Name = "a", Temp = 42 } } };

        var rows = Param(c, "Rows", "P:Rows", "P:Rows/0");
        var row0 = rows.Children[0];

        Assert.Contains(row0.Children, n => n.Name == "Name" && n.Display.Contains("\"a\""));
        Assert.Contains(row0.Children, n => n.Name == "Temp" && n.Display == "42");
    }

    [Fact]
    public void ArrayExpandsToIndexedScalars()
    {
        var c = new DataComponent { Numbers = new[] { 10, 20, 30 } };

        var nums = Param(c, "Numbers", "P:Numbers");
        Assert.Equal(3, nums.Children.Count);
        Assert.Equal("[2]", nums.Children[2].Name);
        Assert.Equal("30", nums.Children[2].Display);
        Assert.False(nums.Children[2].Expandable); // scalar leaf
    }

    [Fact]
    public void DictionaryEntriesAreKeyedAndExpandable()
    {
        var c = new DataComponent { Map = new() { ["x"] = 1, ["y"] = 2 } };

        var collapsed = Param(c, "Map");
        Assert.Contains("2 entries", collapsed.Display);

        var map = Param(c, "Map", "P:Map");
        Assert.Equal(2, map.Children.Count);
        Assert.Contains(map.Children, n => n.Name == "\"x\"" && n.Display == "1");
        Assert.Contains(map.Children, n => n.Name == "\"y\"" && n.Display == "2");
    }

    [Fact]
    public void NestedObjectExpandsToMembers()
    {
        var c = new DataComponent { Single = new Row { Name = "solo", Temp = 7 } };

        var single = Param(c, "Single");
        Assert.True(single.Expandable);

        var expanded = Param(c, "Single", "P:Single");
        Assert.Contains(expanded.Children, n => n.Name == "Name" && n.Display.Contains("\"solo\""));
        Assert.Contains(expanded.Children, n => n.Name == "Temp" && n.Display == "7");
    }

    [Fact]
    public void ScalarsAndStringsAreLeaves()
    {
        var c = new DataComponent { Text = "hello" };
        var text = Param(c, "Text");
        Assert.False(text.Expandable);
        Assert.Equal("\"hello\"", text.Display);
    }

    [Fact]
    public void CycleIsDetected_DoesNotRecurseOrThrow()
    {
        var node = new SelfRef { Label = "root" };
        node.Self = node; // self-reference
        var c = new DataComponent { Cyclic = node };

        // Ask to expand well past the cycle point; must return without hanging or throwing.
        var root = Param(c, "Cyclic", "P:Cyclic", "P:Cyclic/Self", "P:Cyclic/Self/Self");
        var self = root.Children.Single(n => n.Name == "Self");

        Assert.False(self.Expandable);  // already-visited reference is not expandable again
        Assert.Empty(self.Children);
    }

    [Fact]
    public void ThrowingGetterBecomesErrorLeaf_DoesNotCrash()
    {
        var c = new DataComponent();
        var throws = Param(c, "Throws");
        Assert.False(throws.Expandable);
        Assert.StartsWith("<error:", throws.Display);
    }

    [Fact]
    public void NullValueIsLeaf()
    {
        var c = new DataComponent { Rows = null };
        var rows = Param(c, "Rows");
        Assert.False(rows.Expandable);
        Assert.Equal("null", rows.Display);
    }

    [Fact]
    public void LargeCollectionIsTruncated()
    {
        var c = new DataComponent { Numbers = Enumerable.Range(0, 250).ToArray() };
        var nums = Param(c, "Numbers", "P:Numbers");

        // 200 items + one truncation marker.
        Assert.Equal(201, nums.Children.Count);
        Assert.Contains("truncated", nums.Children[^1].Display);
    }

    // ----- inline editing -----------------------------------------------------------------------

    private static ValueNode State(DataComponent component, string name, params string[] expanded)
    {
        var roots = ValueReader.BuildRoots(component, new HashSet<string>(expanded));
        return roots.State.Single(n => n.Name == name);
    }

    [Fact]
    public void WritableScalarsAreEditable_ReadOnlyAndComputedAreNot()
    {
        var c = new DataComponent { Text = "hello", Single = new Row { Name = "n", Temp = 3 } };

        var text = Param(c, "Text");
        Assert.True(text.Editable);
        Assert.Equal("hello", text.EditText); // raw, unquoted

        Assert.True(State(c, "Counter").Editable);   // writable field
        Assert.False(State(c, "Doubled").Editable);  // computed, no setter

        var single = Param(c, "Single", "P:Single");
        Assert.All(single.Children, n => Assert.True(n.Editable)); // Name + Temp both writable
    }

    [Fact]
    public void SetTopLevelStateField()
    {
        var c = new DataComponent();
        var (ok, error) = ValueReader.TrySetValue(c, "S:Counter", "42");
        Assert.True(ok, error);
        Assert.Equal(42, c.Counter);
    }

    [Fact]
    public void SetNestedObjectProperty()
    {
        var row = new Row { Name = "a", Temp = 1 };
        var c = new DataComponent { Rows = new() { row } };

        var (ok, error) = ValueReader.TrySetValue(c, "P:Rows/0/Temp", "99");
        Assert.True(ok, error);
        Assert.Equal(99, row.Temp); // the real object was mutated
    }

    [Fact]
    public void SetListElementAndDictionaryValue()
    {
        var c = new DataComponent
        {
            Numbers = new[] { 10, 20, 30 },
            Map = new() { ["x"] = 1, ["y"] = 2 },
        };

        Assert.True(ValueReader.TrySetValue(c, "P:Numbers/1", "77").Ok);
        Assert.Equal(77, c.Numbers![1]);

        Assert.True(ValueReader.TrySetValue(c, "P:Map/0", "5").Ok); // first entry = "x"
        Assert.Equal(5, c.Map!["x"]);
    }

    [Fact]
    public void SetStringAndConvertsTypes()
    {
        var c = new DataComponent { Single = new Row { Name = "old", Temp = 0 } };
        Assert.True(ValueReader.TrySetValue(c, "P:Single/Name", "new").Ok);
        Assert.Equal("new", c.Single!.Name);
    }

    [Fact]
    public void BadInputReturnsError_DoesNotMutate()
    {
        var c = new DataComponent { Counter = 7 };
        var (ok, error) = ValueReader.TrySetValue(c, "S:Counter", "not-a-number");
        Assert.False(ok);
        Assert.NotNull(error);
        Assert.Equal(7, c.Counter); // unchanged
    }

    [Fact]
    public void SettingReadOnlyMemberFails()
    {
        var c = new DataComponent();
        var (ok, _) = ValueReader.TrySetValue(c, "S:Doubled", "100");
        Assert.False(ok);
    }
}
