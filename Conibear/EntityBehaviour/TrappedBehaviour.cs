using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Conibear.EntityBehaviour
{
    public class TrappedBehaviour : EntityBehavior
    {
        private long listenerId;
        public TrappedBehaviour(Entity entity) : base(entity)
        {
        }
        
        public BlockPos TrappedPos
        {
            get => this.entity.WatchedAttributes.GetTreeAttribute("trappedData").GetBlockPos("trappedPos");
            set
            {
                this.entity.WatchedAttributes.GetTreeAttribute("trappedData").SetBlockPos("trappedPos", value);
                this.entity.WatchedAttributes.MarkPathDirty("trappedData");
            }
        }
        
        public bool IsTrapped
        {
            get
            {
                ITreeAttribute treeAttribute = this.entity.WatchedAttributes.GetTreeAttribute("trappedData");
                return treeAttribute != null && treeAttribute.GetBool("isTrapped");
            }
            set
            {
                this.entity.WatchedAttributes.GetTreeAttribute("trappedData").SetBool("isTrapped", value);
                this.entity.WatchedAttributes.MarkPathDirty("trappedData");
            }
        }
        
        public override void Initialize(EntityProperties properties, JsonObject typeAttributes)
        {
            // Initialize the trappedData attributes
            if (entity.WatchedAttributes.GetTreeAttribute("trappedData") == null)
            {
                ITreeAttribute treeAttribute = new TreeAttribute();
                entity.WatchedAttributes.SetAttribute("trappedData", treeAttribute);
                TrappedPos = typeAttributes["trappedPos"].AsObject<BlockPos>();
            }
            this.listenerId = this.entity.World.RegisterGameTickListener(new Action<float>(this.Tick), 5);
        }

        public override string PropertyName()
        {
            return "conibear:trapped";
        }
        
        public override void OnEntityDespawn(EntityDespawnData despawn)
        {
            base.OnEntityDespawn(despawn);
            this.entity.World.UnregisterGameTickListener(this.listenerId);
        }
        
        private void Tick(float deltaTime)
        {
            // Check if the entity is trapped
            if (this.IsTrapped && entity.Alive)
            {
                
                entity.Api.Logger.Warning("Trapped at: " + TrappedPos);

                // Calculate the displacement vector from the entity's position to the trap's position
                Vec3d displacement = this.TrappedPos.ToVec3d().Add(0.5, 0.0, 0.5).Sub(entity.ServerPos.XYZ);

                entity.Api.Logger.Warning("Displacement: " + displacement);
                entity.Api.Logger.Warning("Trapped Pos " + TrappedPos.ToVec3d().Add(0.5, 0.0, 0.5));
                
                // If the entity is more than 0.1 blocks away from the trap's position, move the entity towards the trap's position
                if (displacement.Length() > 0.1)
                {
                    Vec3d fractionOfDisplacement = displacement.Mul(0.1); // Adjust the fraction as needed
                    entity.ServerPos.SetPos(entity.ServerPos.XYZ.Add(fractionOfDisplacement));
                    entity.AnimManager.StartAnimation("walk");
                }
                
                if (entity.ServerPos.Motion.Length() > 0 && entity.World.Rand.NextDouble() < 0.01)
                {
                    entity.ReceiveDamage(new DamageSource()
                    {
                        Source = EnumDamageSource.Internal,
                        Type = EnumDamageType.PiercingAttack
                    }, entity.Properties.Weight * 0.1f);
                }
            }
        }
    }
}