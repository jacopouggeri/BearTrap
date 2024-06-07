using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace Conibear.EntityBearTrap {
        
    // Create a new entity class for the bear trap
    public class EntityBearTrap : Entity
    {
        public EntityBearTrap(EntityProperties properties, ICoreAPI api, long InGameID) : base(properties, api, InGameID)
        {
        }
    }
}