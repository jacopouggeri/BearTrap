using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace BearTrap.Util;

public class BehaviorUtil
{
    public static void AddTiredness(Entity entity, float value)
    {
        if (entity.HasBehavior<EntityBehaviorTiredness>())
        {
            entity.GetBehavior<EntityBehaviorTiredness>().Tiredness += value;
        }
    }
}