using System;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace BearTrap.ModBlockEntity
{
    public enum EnumTrapState
    {
        Closed,
        Open,
        Destroyed
    }

    public class BlockEntityBearTrap : BlockEntityDisplay, IAnimalFoodSource
    {
        protected ICoreServerAPI Sapi;

        InventoryGeneric _inv = new(1, null, null);
        public override InventoryBase Inventory => _inv;
        public override string InventoryClassName => "beartrap";
        public override int DisplayedItems => HasBait ? 1 : 0;
        public override string AttributeTransformCode => "beartrap";
        public Boolean HasBait;
        private float MaxDamage
        { 
            get
            {
                _durabilityByType.TryGetValue(MetalVariant, out var value);
                return value != 0 ? value : 50;
            }
        }

        private float _damage;
        private float Damage 
        { 
            get => _damage;
            set => _damage = Math.Min(value, MaxDamage); // Ensure Damage never exceeds MaxDamage
        }
        private Dictionary<string, float> _durabilityByType;

        private Dictionary<string, float> _snapDamageByType;
        private Dictionary<EnumTrapState, AssetLocation> _shapeByState;

        public Vec3d Position => Pos.ToVec3d().Add(0.5, 0.25, 0.5);
        public string Type => _inv.Empty ? "nothing" : "food";
        
        float _rotationYDeg;
        float[] _rotMat;


        public float RotationYDeg
        {
            get { return _rotationYDeg; }
            set { 
                _rotationYDeg = value;
                _rotMat = Matrixf.Create().Translate(0.5f, 0, 0.5f).RotateYDeg(_rotationYDeg - 90).Translate(-0.5f, 0, -0.5f).Values;
            }
        }

        public string MetalVariant
        {
            get
            {
                Block block = Block;
                return block?.Variant["metal"];
            }
        }

        public EnumTrapState TrapState;
        
        public float SnapDamage
        {
            get
            {
                _snapDamageByType.TryGetValue(MetalVariant, out var value);
                return value != 0 ? value : 10;
            }
        }

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            _inv.LateInitialize("beartrap-" + Pos, api);
            
            Sapi = api as ICoreServerAPI;
            if (api.Side != EnumAppSide.Client)
            {
                Sapi?.ModLoader.GetModSystem<POIRegistry>().AddPOI(this);
            }
            
            // Load the attribute dictionaries from the json
            _durabilityByType = Block.Attributes?["durabilityBy"].AsObject<Dictionary<string, float>>();
            _snapDamageByType = Block.Attributes?["snapDamageBy"].AsObject<Dictionary<string, float>>();
            
            var shapeByStateString = Block.Attributes?["shapeBy"].AsObject<Dictionary<string, string>>();
            Dictionary<EnumTrapState, AssetLocation> shapeAssetLocations = new Dictionary<EnumTrapState, AssetLocation>();

            if (shapeByStateString != null)
            {
                foreach (var pair in shapeByStateString)
                {
                    if (Enum.TryParse(pair.Key, true, out EnumTrapState state))
                    {
                        shapeAssetLocations[state] = AssetLocation.Create(pair.Value, Block.Code.Domain).WithPathPrefixOnce("shapes/").WithPathAppendixOnce(".json");
                    }
                }
            }

            _shapeByState = shapeAssetLocations;

            Api.World.RegisterGameTickListener(this.TrapEntities, 10);
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
            if (TrapState != EnumTrapState.Closed) return;
            foreach (var entity in LoadTrappedEntities())
            {
                Api.Logger.Warning("Trapping entity");
                Api.Logger.Warning("Entity: " + entity.Code);
                if (!entity.Alive) {ReleaseTrappedEntity(); return;}
                var trappedPos = entity.WatchedAttributes.GetTreeAttribute("trappedData").GetBlockPos("trappedPos");
                if (trappedPos.Equals(Pos))
                {
                    if (entity.ServerPos.Motion.Length() > 0.01 && Api.World.Rand.NextDouble() < 0.2)
                    {
                        DamageEntityPercent(entity, SnapDamage*0.1f);
                        if (entity.HasBehavior<EntityBehaviorTiredness>())
                        {
                            entity.GetBehavior<EntityBehaviorTiredness>().Tiredness += 1.5f;
                        }
                        Damage += 1;
                        if (Math.Abs(Damage - MaxDamage) < 0.001)
                        {
                            SetDestroyed();
                            return;
                        }
                    }
                    
                    
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

        private void SetDestroyed()
        {
            TrapState = EnumTrapState.Destroyed;
            MarkDirty(true);
            Api.World.PlaySoundAt(new AssetLocation("game:sounds/effect/anvilhit3"), Pos.X + 0.5,
                Pos.Y + 0.25, Pos.Z + 0.5, null, true, 16);
            ReleaseTrappedEntity();
        }
        
        public bool Interact(IPlayer player, BlockSelection blockSel)
        {
            Api.Logger.Warning("Interacting with bear trap");
            Api.Logger.Warning("Trap state: " + TrapState);
            if (TrapState == EnumTrapState.Destroyed) return true;

            if (TrapState == EnumTrapState.Closed)
            {
                if (player.Entity.Controls.Sneak)
                {
                    Api.Logger.Warning("Trying to open trap");
                    TrapState = EnumTrapState.Open;
                    MarkDirty(true);
                    return true;
                }

                PickupBlock(player);
                return true;
            }
            
            // Damage players if they attempt to touch the trap without sneaking
            if (!player.Entity.Controls.Sneak)
            {
                Api.Logger.Warning("Player not sneaking");
                DamageEntityPercent(player.Entity, 5);
                TrapState = EnumTrapState.Closed;
            }
            
            if (_inv[0].Empty)
            {
                Api.Logger.Warning("Trying to bait trap");
                if (!HasBait) TryReadyTrap(player);
                else
                {
                    PickupBlock(player);
                }
            }
            else
            {
                Api.Logger.Warning("Trying to take bait");
                if (!player.InventoryManager.TryGiveItemstack(_inv[0].Itemstack))
                {
                    Api.World.SpawnItemEntity(_inv[0].Itemstack, Pos.ToVec3d().Add(0.5, 0.2, 0.5));
                }
                Api.World.BlockAccessor.SetBlock(0, Pos);
            }
            return true;
        }

        private void PickupBlock(IPlayer player)
        {
            var stack = new ItemStack(GetBlockForState(EnumTrapState.Closed.ToString()));
                    
            if (!player.InventoryManager.ActiveHotbarSlot.Empty) return;
            if (!player.InventoryManager.TryGiveItemstack(stack))
            {
                Api.World.SpawnItemEntity(stack, Pos.ToVec3d().Add(0.5, 0.2, 0.5));
            }
            Api.World.PlaySoundAt(new AssetLocation("game:sounds/effect/anvilhit2"), Pos.X + 0.5, Pos.Y + 0.25, Pos.Z + 0.5, null, true, 16);
            Api.World.BlockAccessor.SetBlock(0, Pos);
        }

        private void TryReadyTrap(IPlayer player)
        {
            var heldSlot = player.InventoryManager.ActiveHotbarSlot;
            if (heldSlot.Empty) return;

            var collobj = heldSlot.Itemstack.Collectible;
            if (!heldSlot.Empty && (collobj.NutritionProps != null || collobj.Attributes?["foodTags"].Exists == true))
            {
                _inv[0].Itemstack = heldSlot.TakeOut(1);
                HasBait = true;
                heldSlot.MarkDirty();
                MarkDirty(true);
            }
        }

        public bool IsSuitableFor(Entity entity, CreatureDiet diet)
        {
            if (!HasBait) return false;
            if (diet.FoodTags.Length == 0) return entity.IsCreature;
            bool dietMatches = diet.Matches(_inv[0].Itemstack);
            return  dietMatches;
        }

        public float ConsumeOnePortion(Entity entity)
        {
            Sapi.Event.EnqueueMainThreadTask(() => SnapClosed(entity), "trapanimal");
            return 1f;
        }

        public void SnapClosed(Entity entity)
        {
            if (TrapState != EnumTrapState.Open) return;
            if (entity.IsCreature)
            {
                Api.Logger.Notification("Snap!");
                Api.Logger.Notification("Entity: " + entity.Code);
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

                    DamageEntityPercent(entity, SnapDamage);
                    _inv[0].Itemstack = null;
                }
            }
            
            TrapState = EnumTrapState.Closed;
            MarkDirty(true);
            Api.World.PlaySoundAt(new AssetLocation("game:sounds/effect/anvilhit1"), Pos.X + 0.5, Pos.Y + 0.25, Pos.Z + 0.5, null, true, 16);
        }
        
        private void DamageEntityPercent(Entity entity, float dmg)
        {
            if (!entity.HasBehavior<EntityBehaviorHealth>()) { return;}
            var damage = entity.GetBehavior<EntityBehaviorHealth>().MaxHealth * dmg/100f;
            bool shouldRelease = entity.GetBehavior<EntityBehaviorHealth>().Health - damage <= 0 &&
                                 entity is EntityPlayer;
            entity.ReceiveDamage(new DamageSource()
                {
                    Source = EnumDamageSource.Block,
                    SourceBlock = this.Block,
                    Type = EnumDamageType.PiercingAttack,
                    SourcePos = this.Pos.ToVec3d()
                },
                damage: damage);
            if (shouldRelease) ReleaseTrappedEntity();
        }
        
        public void ReplaceBlockWithState(string state)
        {
                Block newBlock = GetBlockForState(state);
                Api.World.BlockAccessor.ExchangeBlock(newBlock.BlockId, Pos);
                MarkDirty(true);
        }
        
        public Block GetBlockForState(string state)
        {
            string metal = this.Block.Variant["metal"]; // get the current block's metal variant
            string blockCodeString = $"beartrap:beartrap-{metal}-{state}";
            AssetLocation blockCode = new AssetLocation(blockCodeString);
            return Api.World.GetBlock(blockCode);
        }

        private void ReleaseTrappedEntity()
        {
            Api.Logger.Warning("Releasing trapped entity");
            foreach (var entity in LoadTrappedEntities())
            {
                var trappedPos = entity.WatchedAttributes.GetTreeAttribute("trappedData").GetBlockPos("trappedPos");
                if (trappedPos.Equals(Pos))
                {
                    entity.WatchedAttributes.RemoveAttribute("trappedData");
                }
            }
        }
        
        public override void OnBlockBroken(IPlayer byPlayer = null)
        {
            base.OnBlockBroken(byPlayer);
            if (Api.Side == EnumAppSide.Server)
            {
                Api.ModLoader.GetModSystem<POIRegistry>().RemovePOI(this);
            }
            ReleaseTrappedEntity();
        }

        public override void OnBlockRemoved()
        {
            base.OnBlockRemoved();
            if (Api.Side == EnumAppSide.Server)
            {
                Api.ModLoader.GetModSystem<POIRegistry>().RemovePOI(this);
            }
            ReleaseTrappedEntity();
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
            RotationYDeg = tree.GetFloat("rotationYDeg");
            TrapState = (EnumTrapState)tree.GetInt("trapState");

            // Do this last
            RedrawAfterReceivingTreeAttributes(worldForResolving);     // Redraw on client after we have completed receiving the update from server
        }


        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetFloat("rotationYDeg", _rotationYDeg);
            tree.SetInt("trapState", (int)TrapState);
        }

        
        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
        {
            if (TrapState == EnumTrapState.Destroyed)
            {
                dsc.Append("This trap was destroyed\n");
                return;
            }
            dsc.Append("Durability: " + (MaxDamage - Damage) + "/" + (MaxDamage) + "\n");
            if (HasBait)
            {
                dsc.Append(BlockEntityShelf.PerishableInfoCompact(Api, _inv[0], 0));
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
        
        public MeshData GetOrCreateMesh(AssetLocation loc, ITexPositionSource texSource = null)
        {
            return ObjectCacheUtil.GetOrCreate(Api, "bearTrap-" + MetalVariant + loc + (texSource == null ? "-d" : "-t"), () =>
            {
                var shape = Api.Assets.Get<Shape>(loc);
                if (texSource == null)
                {
                    texSource = new ShapeTextureSource(capi, shape, loc.ToShortString());
                }

                Vec3f meshRotationDeg = new Vec3f(0, RotationYDeg, 0);
                var block = Api.World.BlockAccessor.GetBlock(Pos);
                ((ICoreClientAPI)Api).Tesselator.TesselateShape(block, Api.Assets.Get<Shape>(loc), out var meshdata, meshRotationDeg);
                return meshdata;
            });
        }
        
        public MeshData GetCurrentMesh(ITexPositionSource texSource = null)
        {
            return GetOrCreateMesh(_shapeByState[TrapState], texSource);
        }
        
        public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
        {

            bool skip = base.OnTesselation(mesher, tessThreadTesselator);
            if (!skip)
            {
                mesher.AddMeshData(GetCurrentMesh(this), _rotMat);
            }
            return true;
        }
    }
}