using System;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace Conibear.BlockEntity
{
    public enum EnumTrapState
    {
        Open,
        Closed,
        Destroyed
    }

    public class BlockEntityConibearTrap : BlockEntityDisplay, IAnimalFoodSource
    {
        protected ICoreServerAPI sapi;

        InventoryGeneric inv;
        public override InventoryBase Inventory => inv;
        public override string InventoryClassName => "conibear";
        public override int DisplayedItems => baited ? 1 : 0;
        public override string AttributeTransformCode => "conibear";
        private Boolean baited = false;
        
        private long listenerId;

        public Vec3d Position => Pos.ToVec3d().Add(0.5, 0.1, 0.5);
        public string Type => inv.Empty ? "nothing" : "food";

        public EnumTrapState TrapState;
        float rotationYDeg;
        float[] rotMat;

        public float RotationYDeg
        {
            get { return rotationYDeg; }
            set { 
                rotationYDeg = value;
                rotMat = Matrixf.Create().Translate(0.5f, 0, 0.5f).RotateYDeg(rotationYDeg - 90).Translate(-0.5f, 0, -0.5f).Values;
            }
        }

        public BlockEntityConibearTrap()
        {
            inv = new InventoryGeneric(1, null, null);
        }
        
        public string ShapeState
        {
            get
            {
                Vintagestory.API.Common.Block block = Block;
                return block?.Variant["state"];
            }
        }

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            inv.LateInitialize("conibear-" + Pos, api);
            
            sapi = api as ICoreServerAPI;
            if (api.Side != EnumAppSide.Client)
            {
                sapi.ModLoader.GetModSystem<POIRegistry>().AddPOI(this);
            }

            Api.World.RegisterGameTickListener(new Action<float>(this.TrapEntities), 1);
        }

        private Entity[] LoadTrappedEntities()
        {
            var entities = Api.World.GetEntitiesAround(Pos.ToVec3d(), 5, 5, e => 
            {
                var trappedData = e.WatchedAttributes.GetTreeAttribute("trappedData");
                return trappedData != null && trappedData.GetBool("isTrapped") && e.Alive;
            });
            return entities;
        }
        
        private void TrapEntities(float deltaTime)
        {
            foreach (var entity in LoadTrappedEntities())
            {
                var trappedPos = entity.WatchedAttributes.GetTreeAttribute("trappedData").GetBlockPos("trappedPos");
                if (trappedPos.Equals(Pos))
                {
                    Vec3d direction = Pos.ToVec3d().Add(0.5, 0, 0.5).Add(entity.ServerPos.XYZ.Mul(-1));
                    double distance = direction.Length();
                    direction.Normalize();
                    double scale = Math.Max(0, 1 - distance * 0.1);
                    Vec3d desiredMotion = direction.Mul(scale);

                    // Interpolate between the current motion and the desired motion
                    double interpolationFactor = 0.1; // Adjust this value to change the speed of interpolation
                    Vec3d newMotion = entity.ServerPos.Motion.Mul(1 - interpolationFactor).Add(desiredMotion.Mul(interpolationFactor));

                    entity.ServerPos.Motion.Set(newMotion.X, newMotion.Y, newMotion.Z);
                }
            }
        }

        public bool Interact(IPlayer player, BlockSelection blockSel)
        {
            if (ShapeState == "destroyed") return true;
            
            if (inv[0].Empty)
            {
                var stack = new ItemStack(Block);

                if (ShapeState == "open") tryReadyTrap(player);
                else
                {
                    if (!player.InventoryManager.ActiveHotbarSlot.Empty) return true;

                    if (!player.InventoryManager.TryGiveItemstack(stack))
                    {
                        Api.World.SpawnItemEntity(stack, Pos.ToVec3d().Add(0.5, 0.2, 0.5));
                    }

                    Api.World.BlockAccessor.SetBlock(0, Pos);
                }
            } else
            {
                if (!player.InventoryManager.TryGiveItemstack(inv[0].Itemstack))
                {
                    Api.World.SpawnItemEntity(inv[0].Itemstack, Pos.ToVec3d().Add(0.5, 0.2, 0.5));
                }

                Api.World.BlockAccessor.SetBlock(0, Pos);
            }

            
            return true;
        }

        private void tryReadyTrap(IPlayer player)
        {
            var heldSlot = player.InventoryManager.ActiveHotbarSlot;
            if (heldSlot.Empty) return;

            var collobj = heldSlot?.Itemstack.Collectible;
            if (!heldSlot.Empty && (collobj.NutritionProps != null || collobj.Attributes?["foodTags"].Exists == true))
            {
                baited = true;
                inv[0].Itemstack = heldSlot.TakeOut(1);
                heldSlot.MarkDirty();
                MarkDirty(true);
            }
        }

        public bool IsSuitableFor(Entity entity, CreatureDiet diet)
        {
            if (!baited) return false;
            if (diet.FoodTags.Length == 0) return entity.IsCreature;
            bool dietMatches = diet.Matches(inv[0].Itemstack);
            return  dietMatches;
        }

        public float ConsumeOnePortion(Entity entity)
        {
            sapi.Event.EnqueueMainThreadTask(() => SnapClosed(entity), "trapanimal");
            return 1f;
        }

        public void SnapClosed(Entity entity)
        {
            if (entity.IsCreature)
            {
                float trapChance = entity.Properties.Attributes["trapChance"].AsFloat(0.5f);
                if (Api.World.Rand.NextDouble() < Double.Max(1 - trapChance - 0.05, 0))
                {
                    // Stop the entity from moving
                    ITreeAttribute trappedData = entity.WatchedAttributes.GetTreeAttribute("trappedData");
                    
                    if (trappedData == null)
                    {
                        trappedData = new TreeAttribute();
                        entity.WatchedAttributes["trappedData"] = trappedData;
                    }
                    
                    trappedData.SetBool("isTrapped", true);
                    trappedData.SetBlockPos("trappedPos", Pos);

                    Api.Logger.Warning("Trapped at: " + Pos);
                    Api.Logger.Warning("Trapped: " + trappedData.GetBool("isTrapped"));
                    Api.Logger.Warning("Trapped entity: " + entity.Code);

                    entity.ReceiveDamage(new DamageSource()
                        {
                            Source = EnumDamageSource.Block,
                            SourceBlock = this.Block,
                            Type = EnumDamageType.PiercingAttack,
                            SourcePos = this.Pos.ToVec3d()
                        },
                        damage: 5);
                    inv[0].Itemstack = null;
                }
                else
                {
                    inv[0].Itemstack = null;
                    float trapDestroyChance = entity.Properties.Attributes["trapDestroyChance"].AsFloat(0f);
                    if (Api.World.Rand.NextDouble() < trapDestroyChance)
                    {
                        TrapState = EnumTrapState.Destroyed;
                        ReplaceBlockWithState("destroyed");
                        MarkDirty(true);
                        Api.World.PlaySoundAt(new AssetLocation("game:sounds/effect/anvilhit3"), Pos.X + 0.5,
                            Pos.Y + 0.25, Pos.Z + 0.5, null, true, 16);
                        return;
                    }
                }
            }

            baited = false;
            TrapState = EnumTrapState.Closed;
            ReplaceBlockWithState("closed");
            MarkDirty(true);
            Api.World.PlaySoundAt(new AssetLocation("game:sounds/effect/anvilhit1"), Pos.X + 0.5, Pos.Y + 0.25, Pos.Z + 0.5, null, true, 16);
        }
        
        public void ReplaceBlockWithState(string state)
        {
                Vintagestory.API.Common.Block newBlock = GetBlockForState(state);
                Api.World.BlockAccessor.ExchangeBlock(newBlock.BlockId, Pos);
        }
        
        public Vintagestory.API.Common.Block GetBlockForState(string state)
        {
            string metal = this.Block.Variant["metal"]; // get the current block's metal variant
            string blockCodeString = $"conibear:conibear-{metal}-{state}";
            AssetLocation blockCode = new AssetLocation(blockCodeString);
            return Api.World.GetBlock(blockCode);
        }

        public override void OnBlockBroken(IPlayer byPlayer = null)
        {
            base.OnBlockBroken(byPlayer);
            foreach (var entity in LoadTrappedEntities())
            {
                var trappedPos = entity.WatchedAttributes.GetTreeAttribute("trappedData").GetBlockPos("trappedPos");
                if (trappedPos.Equals(Pos))
                {
                    entity.WatchedAttributes.RemoveAttribute("trappedData");
                }
            }
        }

        public override void OnBlockRemoved()
        {
            base.OnBlockRemoved();
            if (Api.Side == EnumAppSide.Server)
            {
                Api.ModLoader.GetModSystem<POIRegistry>().RemovePOI(this);
            }
        }

        public override void OnBlockUnloaded()
        {
            base.OnBlockUnloaded();

            if (Api?.Side == EnumAppSide.Server)
            {
                Api.ModLoader.GetModSystem<POIRegistry>().RemovePOI(this);
            }
        }
        
        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            base.FromTreeAttributes(tree, worldForResolving);

            TrapState = (EnumTrapState)tree.GetInt("trapState");
            RotationYDeg = tree.GetFloat("rotationYDeg");
            
            // Do this last
            RedrawAfterReceivingTreeAttributes(worldForResolving);     // Redraw on client after we have completed receiving the update from server
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetInt("trapState", (int)TrapState);
            tree.SetFloat("rotationYDeg", rotationYDeg);
        }
        
        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
        {
            if (TrapState == EnumTrapState.Closed)
            {
                dsc.Append("Snapped Closed!\n");
            }
            else
            {
                dsc.Append(BlockEntityShelf.PerishableInfoCompact(Api, inv[0], 0));
            }
        }

        protected override float[][] genTransformationMatrices()
        {
            tfMatrices = new float[1][];

            for (int i = 0; i < 1; i++)
            {
                tfMatrices[i] =
                    new Matrixf()
                    .Translate(0.5f, 0.1f, 0.5f)
                    .Scale(0.75f, 0.75f, 0.75f)
                    .Translate(-0.5f, 0, -0.5f)
                    .Values
                ;
            }

            return tfMatrices;
        }
    }
}