using System.Collections.Generic;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;

namespace CocoRelics;

public sealed class PreviewCombatVisuals : ICombatRoomVisuals
{
    public required EncounterModel Encounter { get; init; }

    public required IEnumerable<Creature> Enemies { get; init; }

    public required ActModel Act { get; init; }

    public IEnumerable<Creature> Allies => new List<Creature>();
}
