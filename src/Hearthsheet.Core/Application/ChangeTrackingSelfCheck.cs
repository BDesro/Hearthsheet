using Hearthsheet.Core.Content;

namespace Hearthsheet.Core.Application;

/// <summary>
/// Startup check for builds where trimming could strip what <see cref="ChangeTracker"/> reads by reflection:
/// a damage hit must fire <see cref="CharacterSession.Changed"/>.
/// </summary>
public static class ChangeTrackingSelfCheck
{
    public static bool Passes()
    {
        var character = CharacterFactory.Create(new NewCharacterOptions("Check", "Fighter", 1, "Human", "Soldier"));
        using var session = new CharacterSession(character);
        var fired = false;
        session.Changed += (_, _) => fired = true;
        character.TakeDamage(1);
        return fired;
    }
}
