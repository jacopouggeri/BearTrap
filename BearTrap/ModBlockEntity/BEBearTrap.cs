using System;
using System.Collections.Generic;
using System.Text;
using BearTrap.Util;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
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
        Baited,
        Destroyed
    }

    public class BlockEntityBearTrap : BlockEntityDisplay, IAnimalFoodSource
    {
        protected ICoreServerAPI Sapi;

        private InventoryGeneric _inv;
        public override InventoryBase Inventory => _inv;
        public override string InventoryClassName => "beartrap";
        public override int DisplayedItems => TrapState == EnumTrapState.Baited ? 1 : 0;
        public override string AttributeTransformCode => "beartrap";

        private int MaxDamage => ((ModBlock.BearTrap)Block).MaxDamage;

        private int _damage;

        public int Damage
        {
            get => _damage;
            set => _damage = Math.Min(value, MaxDamage); // Ensure Damage never exceeds MaxDamage
        }

        private Dictionary<EnumTrapState, AssetLocation> _shapeByState;


        public Vec3d Position => Pos.ToVec3d().Add(0.5, 0.25, 0.5);
        public string Type => _inv.Empty ? "nothing" : "food";

        float _rotationYDeg;
        float[] _rotMat;

        public float RotationYDeg
        {
            get { return _rotationYDeg; }
            set
            {
                _rotationYDeg = value;
                _rotMat = Matrixf.Create().Translate(0.5f, 0, 0.5f).RotateYDeg(_rotationYDeg - 90)
                    .Translate(-0.5f, 0, -0.5f).Values;
            }
        }

        public string MetalVariant => ((ModBlock.BearTrap)Block).MetalVariant;

        private EnumTrapState _trapState;

        public EnumTrapState TrapState
        {
            get { return _trapState; }
            set
            {
                _trapState = value;
                if (value != EnumTrapState.Baited) _inv[0].Itemstack = null;
                MarkDirty(true);
            }
        }

        public float SnapDamage => ((ModBlock.BearTrap)Block).SnapDamage;

        public BlockEntityBearTrap()
        {
            _inv = new InventoryGeneric(1, null, null);
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

            var shapeByStateString = Block.Attributes?["shapeBy"].AsObject<Dictionary<string, string>>();
            Dictionary<EnumTrapState, AssetLocation> shapeAssetLocations =
                new Dictionary<EnumTrapState, AssetLocation>();

            if (shapeByStateString != null)
            {
                foreach (var pair in shapeByStateString)
                {
                    if (Enum.TryParse(pair.Key, true, out EnumTrapState state))
                    {
                        shapeAssetLocations[state] = AssetLocation.Create(pair.Value, Block.Code.Domain)
                            .WithPathPrefixOnce("shapes/").WithPathAppendixOnce(".json");
                    }
                }
            }

            _shapeByState = shapeAssetLocations;

            Api.World.RegisterGameTickListener(TrapEntities, 50);
        }

        private Entity[] LoadTrappedEntities()
        {
            // Might want to use entity partitioning
            var entities = Api.World.GetEntitiesAround(Pos.ToVec3d(), 4, 2, e =>
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
                bool isEntityDeadOrPlayer = !entity.Alive || entity is EntityPlayer;
                if (isEntityDeadOrPlayer)
                {
                    ReleaseTrappedEntity();
                    return;
                }

                var trappedData = entity.WatchedAttributes.GetTreeAttribute("trappedData");
                var trappedPos = trappedData.GetBlockPos("trappedPos");

                if (trappedPos.Equals(Pos)) // TODO TRAP PLAYER
                {
                    Vec3d direction = Pos.ToVec3d().Add(0.5, 0, 0.5).Add(entity.ServerPos.XYZ.Mul(-1));
                    double distance = direction.Length();

                    bool motionCheck = entity.ServerPos.Motion.Length() > 0.01;
                    bool dieRoll = Api.World.Rand.NextDouble() < 0.02;

                    if (motionCheck && dieRoll)
                    {
                        DamageEntity(entity, SnapDamage * 0.1f);
                        BehaviorUtil.AddTiredness(entity, 5);
                        Damage += 1;

                        if (Damage > MaxDamage - 1)
                        {
                            SetDestroyed();
                            return;
                        }
                    }

                    direction.Normalize();
                    double scale = Math.Max(0, 1 - distance * 0.1);
                    Vec3d desiredMotion = direction.Mul(scale);

                    double interpolationFactor = 0.1;
                    Vec3d newMotion = entity.ServerPos.Motion.Mul(1 - interpolationFactor)
                        .Add(desiredMotion.Mul(interpolationFactor));

                    entity.ServerPos.Motion.Set(newMotion.X, newMotion.Y, newMotion.Z);
                }
            }
        }

        private void SetDestroyed()
        {
            TrapState = EnumTrapState.Destroyed;
            Damage = MaxDamage;
            Api.World.PlaySoundAt(new AssetLocation("game:sounds/effect/anvilhit3"), Pos.X + 0.5,
                Pos.Y + 0.25, Pos.Z + 0.5, null, true, 16);
            ReleaseTrappedEntity();
        }

        public bool Interact(IPlayer player, BlockSelection blockSel)
        {
            switch (TrapState)
            {
                case EnumTrapState.Destroyed:
                    return true;
                case EnumTrapState.Closed when player.Entity.Controls.Sneak:
                    TrapState = EnumTrapState.Open;
                    return true;
                // Damage players if they attempt to touch the trap without sneaking
                case EnumTrapState.Open when _inv[0].Empty:
                    return TryReadyTrap(player);
                case EnumTrapState.Open when !player.Entity.Controls.Sneak:
                case EnumTrapState.Baited when !player.Entity.Controls.Sneak:
                    DamageEntity(player.Entity, SnapDamage);
                    TrapState = EnumTrapState.Closed;
                    return true;
                case EnumTrapState.Baited when _inv[0].Itemstack != null:
                {
                    if (!player.InventoryManager.TryGiveItemstack(_inv[0].Itemstack))
                    {
                        Api.World.SpawnItemEntity(_inv[0].Itemstack, Pos.ToVec3d().Add(0.5, 0.2, 0.5));
                    }

                    TrapState = EnumTrapState.Open;
                    return true;
                }
                default:
                    return false;
            }
        }

        private bool TryReadyTrap(IPlayer player)
        {
            var heldSlot = player.InventoryManager.ActiveHotbarSlot;
            if (heldSlot.Empty) return false;

            var collobj = heldSlot.Itemstack.Collectible;
            if (!heldSlot.Empty && (collobj.NutritionProps != null || collobj.Attributes?["foodTags"].Exists == true))
            {
                _inv[0].Itemstack = heldSlot.TakeOut(1);
                TrapState = EnumTrapState.Baited;
                heldSlot.MarkDirty();
                return true;
            }

            return false;
        }

        public bool IsSuitableFor(Entity entity, CreatureDiet diet)
        {
            if (TrapState != EnumTrapState.Baited) return false;
            if (diet.FoodTags.Length == 0) return entity.IsCreature;
            bool dietMatches = diet.Matches(_inv[0].Itemstack);
            return dietMatches;
        }

        public float ConsumeOnePortion(Entity entity)
        {
            Sapi.Event.EnqueueMainThreadTask(() => SnapClosed(entity), "trapanimal");
            return 1f;
        }

        public void SnapClosed(Entity entity)
        {
            if (TrapState == EnumTrapState.Destroyed || TrapState == EnumTrapState.Closed) return;
            if (entity.IsCreature)
            {
                float trapChance = entity.Properties.Attributes["trapChance"].AsFloat();
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

                    DamageEntity(entity, SnapDamage);
                    BearTrapModSystem.Logger.Warning("Damage:" + Damage);
                    _inv[0].Itemstack = null;
                }
            }

            Damage += 1;
            TrapState = EnumTrapState.Closed;
            Api.World.PlaySoundAt(new AssetLocation("game:sounds/effect/anvilhit1"), Pos.X + 0.5, Pos.Y + 0.25,
                Pos.Z + 0.5, null, true, 16);
        }

        private void DamageEntity(Entity entity, float damage)
        {
            if (!entity.HasBehavior<EntityBehaviorHealth>())
            {
                return;
            }

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

        private void ReleaseTrappedEntity()
        {
            foreach (var entity in LoadTrappedEntities())
            {
                var trappedPos = entity.WatchedAttributes.GetTreeAttribute("trappedData").GetBlockPos("trappedPos");
                if (trappedPos.Equals(Pos))
                {
                    if (entity is EntityPlayer player)
                    {
                        SpeedUtil.RemoveSlowEffect(player);
                    }

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
            Damage = tree.GetInt("damage");
            if (Damage > MaxDamage - 1)
            {
                SetDestroyed();
            }
            else
            {
                TrapState = (EnumTrapState)tree.GetInt("trapState");
            }

            // Do this last
            RedrawAfterReceivingTreeAttributes(
                worldForResolving); // Redraw on client after we have completed receiving the update from server
        }


        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetFloat("rotationYDeg", _rotationYDeg);
            tree.SetInt("damage", _damage);
            tree.SetInt("trapState", (int)TrapState);
        }


        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
        {
            if (TrapState == EnumTrapState.Destroyed)
            {
                dsc.Append(Lang.Get(BearTrapModSystem.Modid + ":info-beartrap-destroyed"));
                return;
            }

            dsc.Append("Durability: " + (MaxDamage - Damage) + "/" + (MaxDamage) + "\n");
            if (TrapState == EnumTrapState.Baited)
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
            return ObjectCacheUtil.GetOrCreate(Api,
                "bearTrap-" + MetalVariant + loc + (texSource == null ? "-d" : "-t"), () =>
                {
                    var shape = Api.Assets.Get<Shape>(loc);
                    if (texSource == null)
                    {
                        texSource = new ShapeTextureSource(capi, shape, loc.ToShortString());
                    }

                    var block = Api.World.BlockAccessor.GetBlock(Pos);
                    ((ICoreClientAPI)Api).Tesselator.TesselateShape(block, Api.Assets.Get<Shape>(loc),
                        out var meshdata);
                    return meshdata;
                });
        }

        public MeshData GetCurrentMesh(ITexPositionSource texSource = null)
        {
            if (TrapState == EnumTrapState.Baited) return GetOrCreateMesh(_shapeByState[EnumTrapState.Open], texSource);
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