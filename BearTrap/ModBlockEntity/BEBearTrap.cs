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

    public class BlockEntityBearTrap : BlockEntityDisplay, IAnimalFoodSource, IMountable
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
            this._controls.OnAction = this.OnControls;
            
            EntityAgent entityAgent;
            if (this._mountedByPlayerUid == null)
            {
                entityAgent = api.World.GetEntityById(this._mountedByEntityId) as EntityAgent;
            }
            else
            {
                IPlayer player = api.World.PlayerByUid(this._mountedByPlayerUid);
                entityAgent = player?.Entity;
            }
            entityAgent?.TryMount(this);
            
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
            Api.World.RegisterGameTickListener(UnMountIfDead, 50);
        }
        
        private void UnMountIfDead(float deltaTime)
        {
            if (TrapState != EnumTrapState.Closed) this.MountedBy?.TryUnmount();
                if (MountedBy is { Alive: false })
                {
                    this.MountedBy.TryUnmount();
                }

                if (TrapState == EnumTrapState.Closed)
                {
                    if (this.Api.World.Side.IsServer()) this.MountedBy?.ServerPos.SetPos(MountPosition);
                    else this.MountedBy?.Pos.SetPos(MountPosition);
                }
        }

        private void SetDestroyed()
        {
            TrapState = EnumTrapState.Destroyed;
            Damage = MaxDamage;
            Api.World.PlaySoundAt(new AssetLocation("game:sounds/effect/anvilhit3"), Pos.X + 0.5,
                Pos.Y + 0.25, Pos.Z + 0.5, null, true, 16);
            this.MountedBy?.TryUnmount();
        }
        
        public bool Interact(IPlayer player, BlockSelection blockSel)
        {
            switch (TrapState)
            {
                case EnumTrapState.Destroyed:
                    return true;
                case EnumTrapState.Closed when player.Entity.Controls.Sneak:
                    MountedBy?.TryUnmount();
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
                Core.Logger.Warning("Snap Damage");
                DamageEntity(entityAgent, SnapDamage);
                entityAgent.TryUnmount();
                entityAgent.TryMount(this);
                _inv[0].Itemstack = null;
            }
            Damage += 1;
            TrapState = EnumTrapState.Closed;
            Api.World.PlaySoundAt(new AssetLocation("game:sounds/effect/anvilhit1"), Pos.X + 0.5, Pos.Y + 0.25, Pos.Z + 0.5, null, true, 16);
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
            Core.Logger.Warning("DidUnmount: {}", this._mountedByPlayerUid);
            Core.Logger.Warning("DidUnmount: {}", this._mountedByEntityId);
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
            Core.Logger.Warning("DidMount: {}", this._mountedByPlayerUid);
            Core.Logger.Warning("DidMount: {}", this._mountedByEntityId);
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

        private void DamageEntityAndTrap()
        {
            DamageEntity(MountedBy, SnapDamage * 0.1f);
            BehaviorUtil.AddTiredness(MountedBy, 2f);
            Damage += 1;
            MarkDirty();
            if (Damage > MaxDamage - 1)
            {
                SetDestroyed();
            }
        }

        public EnumMountAngleMode AngleMode => EnumMountAngleMode.Unaffected;
        public string SuggestedAnimation => "sitflooridle";
        public Vec3f LocalEyePos { get; private set; }
    }
}