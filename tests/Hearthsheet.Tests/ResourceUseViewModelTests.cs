using System.Globalization;
using Hearthsheet.Core.Domain;
using Hearthsheet.Core.Rules;
using Hearthsheet.Presentation;

namespace Hearthsheet.Tests;

public class ResourceUseViewModelTests
{
    private static Character Fighter()
    {
        var c = new Character();
        c.Identity.Classes[0] = new ClassLevel { Name = "Fighter", Level = 11 };
        c.Abilities.Set(Ability.Wisdom, 14);
        return c;
    }

    private static ResourceUseViewModel Make(string roll, ResourceResult result = ResourceResult.Heal)
    {
        var c = Fighter();
        var r = new Resource { Name = "Second Wind", Maximum = 1, Current = 1, Roll = roll, Result = result };
        return new ResourceUseViewModel(c, r, ResourceFormula.TryResolve(c, roll)!, new Random(1));
    }

    [Fact]
    public void Groups_boxes_by_die_size()
    {
        var vm = Make("1d6 + 2d6 + 1d8 + 3");

        Assert.Equal(["3d6", "1d8"], vm.Boxes.Select(b => b.Label));
        Assert.Equal((3, 18), (vm.Boxes[0].Min, vm.Boxes[0].Max));
        Assert.Equal((1, 8), (vm.Boxes[1].Min, vm.Boxes[1].Max));
    }

    [Fact]
    public void Total_null_until_every_box_valid()
    {
        var vm = Make("1d10 + fighter level");
        Assert.Null(vm.Total);
        Assert.False(vm.CanUse);

        vm.Boxes[0].Input = "7";
        Assert.Equal(18, vm.Total);
        Assert.Equal("Heal 18", vm.ResultText);
        Assert.True(vm.CanUse);

        vm.Boxes[0].Input = "11";
        Assert.Null(vm.Total);
        Assert.Equal("Between 1 and 10", vm.Boxes[0].Error);
        Assert.False(vm.CanUse);
    }

    [Fact]
    public void Flat_only_is_ready_at_once()
    {
        var vm = Make("wis");

        Assert.Empty(vm.Boxes);
        Assert.Equal(2, vm.Total);
        Assert.True(vm.CanUse);
    }

    [Fact]
    public void Change_notifications_fire_when_a_box_changes()
    {
        var vm = Make("1d10");
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.Boxes[0].Input = "4";

        Assert.Contains(nameof(ResourceUseViewModel.Total), raised);
        Assert.Contains(nameof(ResourceUseViewModel.ResultText), raised);
        Assert.Contains(nameof(ResourceUseViewModel.CanUse), raised);
    }

    [Fact]
    public void Roll_for_me_fills_only_empty()
    {
        var vm = Make("1d6 + 1d8");
        vm.Boxes[0].Input = "6";

        vm.RollForMe.Execute(null);

        Assert.Equal("6", vm.Boxes[0].Input);
        Assert.InRange(int.Parse(vm.Boxes[1].Input, CultureInfo.InvariantCulture), 1, 8);
        Assert.True(vm.CanUse);
    }

    [Theory]
    [InlineData(ResourceResult.TemporaryHp, "Gain 9 temporary HP")]
    [InlineData(ResourceResult.JustShow, "Total 9")]
    public void Result_text_forms(ResourceResult result, string expected)
    {
        var vm = Make("1d10 + 4", result);
        vm.Boxes[0].Input = "5";
        Assert.Equal(expected, vm.ResultText);
    }

    [Fact]
    public void Heal_with_negative_total_shows_zero()
    {
        var vm = Make("1d4 - 5");
        vm.Boxes[0].Input = "4";
        Assert.Equal(-1, vm.Total);
        Assert.Equal("Heal 0", vm.ResultText);
    }

    [Fact]
    public void Labels_and_header()
    {
        var vm = Make("1d10 + fighter level");

        Assert.Equal("Second Wind", vm.Name);
        Assert.Equal("1 / 1 left", vm.UsesText);
        Assert.Equal("1d10 + 11 (fighter level)", vm.FormulaText);
        Assert.Equal("1d10", vm.Boxes[0].Label);
    }

    [Theory]
    [InlineData("Once per rest", "", ResourceResult.JustShow, "Once per rest")]
    [InlineData("", "1d10 + fighter level", ResourceResult.Heal, "Roll: 1d10 + fighter level (Heal me)")]
    [InlineData("Note", "1d10", ResourceResult.TemporaryHp, "Note\nRoll: 1d10 (Temporary HP)")]
    [InlineData("", "1d10", ResourceResult.JustShow, "Roll: 1d10")]
    [InlineData("  ", " ", ResourceResult.Heal, null)]
    public void Tooltip_text(string notes, string roll, ResourceResult result, string? expected) =>
        Assert.Equal(expected, Lookups.ResourceTooltip(notes, roll, result));
}
