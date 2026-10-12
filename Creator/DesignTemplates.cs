using System.Numerics;
using Unrendered.Core;

namespace Unrendered.Creator;

/// <summary>
/// What a new design starts as. A creature starts the way Spore's does — a short spine with
/// nothing on it, waiting for legs — and a vehicle starts as one chassis block to bolt things
/// to. The game's own characters are not built here: they are copied exactly from the game's
/// models by <see cref="Rendering.GameCapture"/>.
/// </summary>
public static class DesignTemplates
{
    public static Design Blank(DesignKind kind)
    {
        Design d;
        if (kind == DesignKind.Creature) d = Spine.NewCreature();
        else
        {
            d = new Design { Name = "NEW VEHICLE", Kind = DesignKind.Vehicle, BaseClass = PlayerClass.Tank };
            d.Parts.Add(new Part
            {
                Id = 0, Name = "CHASSIS", Role = PartRole.Body,
                Blocks = { new Block { Center = new Vector3(0, 0.55f, 0), Size = new Vector3(1.4f, 0.5f, 2.2f), Swatch = 1 } },
            });
        }
        AutoAnim.Rebuild(d);
        return d;
    }
}
