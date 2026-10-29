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
    }
}
