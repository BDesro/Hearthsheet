using Hearthsheet.Core.Application;
using Hearthsheet.Core.Content;
using Hearthsheet.Core.Domain;
using Hearthsheet.Core.Rules;
using Hearthsheet.Presentation;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hearthsheet.Tests;

[Collection("CommandRequery")]
public class CharacterViewModelTests
{
    private readonly Queue<Action> _posted = new();
    private readonly FakeDialogs _dialogs = new();
    private readonly CharacterViewModel _vm;

    public CharacterViewModelTests()
    {
        var character = CharacterFactory.Create(new NewCharacterOptions("Arannis", "Fighter", 3, "Elf", "Soldier"));
        _vm = new CharacterViewModel(new CharacterSession(character), RestService.CreateDefault(), _dialogs, NullLogger.Instance, _posted.Enqueue);
    }

    [Fact]
    public void BurstOfChanges_PostsOneRefresh()
    {
        var row = _vm.Abilities[0];
        var notified = 0;
        row.PropertyChanged += (_, _) => notified++;

        _vm.Character.Abilities.Set(Ability.Strength, 18);
        _vm.Character.Abilities.Set(Ability.Dexterity, 16);
        _vm.Character.Abilities.Set(Ability.Constitution, 14);

        Assert.Single(_posted);
        Assert.Equal(0, notified);
        _posted.Dequeue()();
        Assert.True(notified > 0);
        Assert.Equal(4, _vm.Abilities.Single(r => r.Ability == Ability.Strength).Modifier);

        _vm.Character.Abilities.Set(Ability.Wisdom, 12);
        Assert.Single(_posted);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RollAttack_LogsOnlyWhenDone(bool done)
    {
        var attack = new Attack { Name = "Club", Damage = "1d6 bludgeoning" };
        _vm.Character.Attacks.Add(attack);
        _dialogs.AttackRollAnswer = done;
        string? line = null;
        _dialogs.OnAttackRoll = vm =>
        {
            vm.Boxes[0].Input = "4";
            line = vm.LogLine;
        };

        _vm.Play.RollAttack.Execute(attack);
        await Task.Yield();

        Assert.Equal("Club: 4 bludgeoning", line);
        if (done) Assert.EndsWith(line, _vm.Play.ActivityLog[0]);
        else Assert.Empty(_vm.Play.ActivityLog);
    }

    private Resource AddResource(string name, int max, string roll = "", ResourceResult result = ResourceResult.JustShow)
    {
        var r = new Resource { Name = name, Maximum = max, Current = max, Roll = roll, Result = result };
        _vm.Character.Resources.Add(r);
        return r;
    }

    [Fact]
    public async Task Use_without_roll_logs_as_before()
    {
        var rage = AddResource("Rage", 3);

        _vm.Play.UseResource.Execute(rage);
        await Task.Yield();

        Assert.Equal(2, rage.Current);
        Assert.EndsWith("Used Rage (2/3 left).", _vm.Play.ActivityLog[0]);
        Assert.False(_dialogs.UseResourceOpened);
    }

    [Fact]
    public async Task Use_with_no_uses_left_skips_dialog()
    {
        var r = AddResource("Second Wind", 1, "1d10");
        r.Current = 0;

        _vm.Play.UseResource.Execute(r);
        await Task.Yield();

        Assert.EndsWith("Second Wind has no uses left.", _vm.Play.ActivityLog[0]);
        Assert.False(_dialogs.UseResourceOpened);
    }

    [Fact]
    public async Task Use_with_unreadable_roll_spends_and_logs()
    {
        var r = AddResource("Second Wind", 2, "1d7");

        _vm.Play.UseResource.Execute(r);
        await Task.Yield();

        Assert.Equal(1, r.Current);
        Assert.Contains("Couldn't read the roll \"1d7\".", _vm.Play.ActivityLog[0]);
        Assert.False(_dialogs.UseResourceOpened);
    }

    [Fact]
    public async Task Use_with_roll_cancel_spends_nothing()
    {
        var r = AddResource("Second Wind", 2, "1d10", ResourceResult.Heal);
        _dialogs.UseResourceAnswer = false;

        _vm.Play.UseResource.Execute(r);
        await Task.Yield();

        Assert.True(_dialogs.UseResourceOpened);
        Assert.Equal(2, r.Current);
        Assert.Empty(_vm.Play.ActivityLog);
    }

    [Fact]
    public async Task Use_with_roll_applies_result()
    {
        _vm.Character.Identity.Classes[0] = new ClassLevel { Name = "Fighter", Level = 11 };
        _vm.Character.Combat.MaxHp = 30;
        _vm.Character.Combat.CurrentHp = 5;
        var r = AddResource("Second Wind", 2, "1d10 + fighter level", ResourceResult.Heal);
        _dialogs.UseResourceAnswer = true;
        _dialogs.OnUseResource = vm => vm.Boxes[0].Input = "7";

        _vm.Play.UseResource.Execute(r);
        await Task.Yield();

        Assert.Equal(23, _vm.Character.Combat.CurrentHp);
        Assert.Equal(1, r.Current);
        Assert.Contains("rolled 18", _vm.Play.ActivityLog[0]);
    }

    [Fact]
    public async Task LongRest_DoesNothing_WhenNotConfirmed()
    {
        _dialogs.ConfirmAnswer = false;
        _vm.Character.TakeDamage(5);
        var hp = _vm.Character.Combat.CurrentHp;

        _vm.Play.LongRest.Execute(null);
        await Task.Yield();

        Assert.Equal(hp, _vm.Character.Combat.CurrentHp);
    }

    [Fact]
    public async Task LongRest_RestoresHp_WhenConfirmed()
    {
        _dialogs.ConfirmAnswer = true;
        _vm.Character.TakeDamage(5);

        _vm.Play.LongRest.Execute(null);
        await Task.Yield();

        Assert.Equal(_vm.MaxHitPoints, _vm.Character.Combat.CurrentHp);
    }

    private sealed class FakeDialogs : IDialogs
    {
        public bool ConfirmAnswer { get; set; }
        public Task<bool> ConfirmAsync(string message, string title = "Confirm") => Task.FromResult(ConfirmAnswer);
        public Task InfoAsync(string message) => Task.CompletedTask;
        public Task ErrorAsync(string message) => Task.CompletedTask;
        public Task<NewCharacterOptions?> NewCharacterAsync() => Task.FromResult<NewCharacterOptions?>(null);
        public Task<(string ClassName, LevelUpOptions Options)?> LevelUpAsync(Character c, string? className = null) =>
            Task.FromResult<(string ClassName, LevelUpOptions Options)?>(null);
        public Task<QuickAddResult?> QuickAddAsync(string title, IReadOnlyList<object>? kinds, object? defaultKind, bool withDescription) =>
            Task.FromResult<QuickAddResult?>(null);
        public Task<bool> ShortRestAsync(PlayViewModel play, Character character) => Task.FromResult(false);
        public bool AttackRollAnswer { get; set; }
        public Action<AttackRollViewModel>? OnAttackRoll { get; set; }
        public Task<bool> AttackRollAsync(AttackRollViewModel vm)
        {
            OnAttackRoll?.Invoke(vm);
            return Task.FromResult(AttackRollAnswer);
        }
        public bool UseResourceAnswer { get; set; }
        public bool UseResourceOpened { get; private set; }
        public Action<ResourceUseViewModel>? OnUseResource { get; set; }
        public Task<bool> UseResourceAsync(ResourceUseViewModel vm)
        {
            UseResourceOpened = true;
            OnUseResource?.Invoke(vm);
            return Task.FromResult(UseResourceAnswer);
        }
    }
}
