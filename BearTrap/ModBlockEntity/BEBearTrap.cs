#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
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

    public class BlockEntityBearTrap : BlockEntityDisplay, IAnimalFoodSource, IMountable
    {
        protected ICoreServerAPI Sapi;
        private readonly object _lock = new object();
        private InventoryGeneric _inv = new(1, null, null);
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
            set { 
                _rotationYDeg = value;
                _rotMat = Matrixf.Create().Translate(0.5f, 0, 0.5f).RotateYDeg(_rotationYDeg - 90).Translate(-0.5f, 0, -0.5f).Values;
            }
        }

        public string MetalVariant => ((ModBlock.BearTrap)Block).MetalVariant;

        private EnumTrapState _trapState;

        public EnumTrapState TrapState
        {
            get { return _trapState;}
            set
            {
                _trapState = value;
                if (value != EnumTrapState.Baited) _inv[0].Itemstack = null;
                if (value != EnumTrapState.Closed) UnmountEntity("openTrap");
                MarkDirty(true);
            }
        }
        
        public float SnapDamage => ((ModBlock.BearTrap)Block).SnapDamage;

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
            
            this._controls.OnAction = this.OnControls;
            
            EntityAgent? entityAgent;
            if (this._mountedByPlayerUid == null)
            {
                entityAgent = api.World.GetEntityById(this._mountedByEntityId) as EntityAgent;
            }
            else
            {
                IPlayer player = api.World.PlayerByUid(this._mountedByPlayerUid);
                entityAgent = player?.Entity;
            }
            MountEntity(entityAgent, "init");
            api.World.RegisterGameTickListener(SlowTick, 50);
        }
        
        private Entity? LoadTrappedEntity()
        {
            var entities = Api.World.GetEntitiesAround(Pos.ToVec3d(), 5, 5, e => e.WatchedAttributes != null && e.WatchedAttributes.GetBool(Core.Modid + "trapped") && e.Alive);
            return entities != null && entities.Any() ? entities[0] : null;
        }
        
        private void SlowTick(float deltaTime)
        {
            if (TrapState != EnumTrapState.Closed) return;
            if (MountedBy is { Alive: false })
            {
                UnmountEntity("dead");
            }
            else if (MountedBy is { WatchedAttributes: not null } && MountedBy.WatchedAttributes.GetBool(Core.Modid + ":trapped"))
            {
                if (LoadTrappedEntity() is EntityAgent entityAgent)
                {
                    MountEntity(entityAgent);
                }
            }
        }

        private void SetDestroyed()
        {
            TrapState = EnumTrapState.Destroyed;
            Damage = MaxDamage;
            Api.World.PlaySoundAt(new AssetLocation("game:sounds/effect/anvilhit3"), Pos.X + 0.5,
                Pos.Y + 0.25, Pos.Z + 0.5, null, true, 16);
            UnmountEntity("destroyed");
        }
        
        public void MountEntity(EntityAgent? entityAgent, string? dsc = null) 
        {
            Core.Logger.Warning("MountingMethod: {0}", dsc);
            if (entityAgent == null) return;
            entityAgent.TryMount(this);
            MountedBy = entityAgent;
            entityAgent?.WatchedAttributes?.SetBool(Core.Modid + ":trapped", true);
            if (MountedBy is EntityPlayer entityPlayer)
            {
                entityPlayer.Stats.Set("walkspeed", Core.Modid + "trapped", -1);
            }
        }

        public void UnmountEntity(String dsc = null)
        {
            Core.Logger.Warning("UnmountingMethod: {0}", dsc);
            if (MountedBy is EntityPlayer entityPlayer)
            {
                entityPlayer.Stats.Remove("walkspeed", Core.Modid + "trapped");
            }
            this.MountedBy?.WatchedAttributes?.SetBool(Core.Modid + ":trapped", false);
            this.MountedBy?.TryUnmount();
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
                case EnumTrapState.Open when !player.Entity.Controls.Sneak:
                case EnumTrapState.Baited when !player.Entity.Controls.Sneak:
                    SnapClosed(player.Entity);
                    return true;
                case EnumTrapState.Open when _inv[0].Empty:
                    return TryReadyTrap(player);
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
            if (TrapState is EnumTrapState.Destroyed or EnumTrapState.Closed) return;
            if (entity is EntityAgent entityAgent)
            {
                TrapState = EnumTrapState.Closed;
                Core.Logger.Warning("Snap Damage");
                DamageEntity(entityAgent, SnapDamage);
                Core.Logger.Warning("SnapMount: {0}", entityAgent.MountedOn);
                if (entityAgent.MountedOn == null) MountEntity(entityAgent, "snapclosed");
                Core.Logger.Warning("SnapMount2: {0}", entityAgent.MountedOn);
                _inv[0].Itemstack = null;
            }

            Damage += 1;
            Api.World.PlaySoundAt(new AssetLocation("game:sounds/effect/anvilhit1"), Pos.X + 0.5, Pos.Y + 0.25,
                Pos.Z + 0.5, null, true, 16);
        }
        
        private void DamageEntity(Entity entity, float damage)
        {
            if (!entity.HasBehavior<EntityBehaviorHealth>()) { return;}
            entity.ReceiveDamage(new DamageSource()
                {
                    Source = EnumDamageSource.Block,
                    SourceBlock = this.Block,
                    Type = EnumDamageType.PiercingAttack,
                    SourcePos = this.Pos.ToVec3d()
                },
                damage: damage);
        }
        
        public override void OnBlockBroken(IPlayer byPlayer = null)
        {
            base.OnBlockBroken(byPlayer);
            if (Api.Side == EnumAppSide.Server)
            {
                Api.ModLoader.GetModSystem<POIRegistry>().RemovePOI(this);
            }
        }

        public override void OnBlockRemoved()
        {
            base.OnBlockRemoved();
            if (Api.Side == EnumAppSide.Server)
            {
                Api.ModLoader.GetModSystem<POIRegistry>().RemovePOI(this);
                UnmountEntity("blockremoved");
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
            this._mountedByEntityId = tree.GetLong("mountedByEntityId");
            this._mountedByPlayerUid = tree.GetString("mountedByPlayerUid");

            // Do this last
            RedrawAfterReceivingTreeAttributes(worldForResolving);     // Redraw on client after we have completed receiving the update from server
        }


        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetFloat("rotationYDeg", _rotationYDeg);
            tree.SetInt("damage", _damage);
            tree.SetInt("trapState", (int)TrapState);
            tree.SetLong("mountedByEntityId", this._mountedByEntityId);
            tree.SetString("mountedByPlayerUid", this._mountedByPlayerUid);
        }

        
        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
        {
            if (TrapState == EnumTrapState.Destroyed)
            {
                dsc.Append(Lang.Get(Core.Modid + ":info-beartrap-destroyed"));
                return;
            }
            dsc.Append("Durability: " + (MaxDamage - Damage) + "/" + (MaxDamage) + "\n");
            if (TrapState == EnumTrapState.Baited)
            {
                dsc.Append(BlockEntityShelf.PerishableInfoCompact(Api, _inv[0], 0));
            }
            dsc.Append(TrapState);
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
                
                var block = Api.World.BlockAccessor.GetBlock(Pos);
                ((ICoreClientAPI)Api).Tesselator.TesselateShape(block, Api.Assets.Get<Shape>(loc), out var meshdata);
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
        
        // IMountable

        public void MountableToTreeAttributes(TreeAttribute tree)
        {
            tree.SetString("className", Core.Modid + "beartrap");
            tree.SetInt("posx", this.Pos.X);
            tree.SetInt("posy", this.Pos.InternalY);
            tree.SetInt("posz", this.Pos.Z);
        }

        public void DidUnmount(EntityAgent entityAgent)
        {
            Core.Logger.Warning("DidUnmount: {0}", this._mountedByPlayerUid);
            Core.Logger.Warning("DidUnmount: {0}", this._mountedByEntityId);
            this.MountedBy = null;
            this._mountedByEntityId = 0L;
            this._mountedByPlayerUid = null;
            this.LocalEyePos = null;
        }

        public void DidMount(EntityAgent entityAgent)
        {
            if (this.MountedBy == entityAgent)
                return;
            this.MountedBy = entityAgent;
            this._mountedByPlayerUid = entityAgent is EntityPlayer entityPlayer ? entityPlayer.PlayerUID : null;
            this._mountedByEntityId = this.MountedBy.EntityId;
            this.LocalEyePos = entityAgent.LocalEyePos.ToVec3f();
            Core.Logger.Warning("DidMount: {0}", this._mountedByPlayerUid);
            Core.Logger.Warning("DidMount: {0}", this._mountedByEntityId);
        }
        
        private EntityControls _controls = new EntityControls();
        public EntityControls Controls => this._controls;
        public EntityAgent MountedBy;
        public bool CanControl => false;
        Entity IMountable.MountedBy => this.MountedBy;
        public IMountableSupplier MountSupplier => null;
        private EntityPos _mountPos = new EntityPos();
        private long _mountedByEntityId;
        private string _mountedByPlayerUid;
        public EntityPos MountPosition
        {
            get
            {
                this._mountPos.SetPos(this.Pos);
                this._mountPos.Add(0.5f, 0f, 0.5f);
                return this._mountPos;
            }
        }
        
        private void OnControls(EnumEntityAction action, bool on, ref EnumHandling handled)
        {
            this._controls.StopAllMovement();
            if (on && action is not (EnumEntityAction.Backward or EnumEntityAction.Forward or EnumEntityAction.Right
                or EnumEntityAction.Left or EnumEntityAction.Up)) return;
            if (MountedBy != null)
            {
                DamageEntityAndTrap();
            }
            handled = EnumHandling.PreventSubsequent;
        }

        private double lastTrapDamageTime;
        private double trapDamageCooldown = 0.02;
        
        private void DamageEntityAndTrap()
        {
            double currentTime = Api.World.Calendar.TotalHours;
            if (currentTime - lastTrapDamageTime >= trapDamageCooldown)
            {
                lastTrapDamageTime = currentTime;
                Damage += 1;
                MarkDirty();
                if (Damage > MaxDamage - 1)
                {
                    SetDestroyed();
                }
            }
            DamageEntity(MountedBy, SnapDamage * 0.1f);
            BehaviorUtil.AddTiredness(MountedBy, 1f);
        }

        public EnumMountAngleMode AngleMode => EnumMountAngleMode.Unaffected;
        public string SuggestedAnimation => "stand";
        public Vec3f LocalEyePos { get; private set; }
    }
}