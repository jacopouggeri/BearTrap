using System;
using System.Collections.Generic;
using System.Text;
using BearTrap.ModBlockEntity;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using EnumTrapState = BearTrap.ModBlockEntity.EnumTrapState;

namespace BearTrap.ModBlock
{

    public class BearTrap : Block
    {
        private const float RotInterval = GameMath.PIHALF / 4;
        
        private Dictionary<string, int> _durabilityByType;
        private Dictionary<string, float> _snapDamageByType;
        
        public string MetalVariant => Variant["metal"];

        public int MaxDamage
        {
            get
            {
                InitializeAttributes();
                _durabilityByType.TryGetValue(MetalVariant, out var value);
                return value != 0 ? value : 50;
            }
        }
        
        public float SnapDamage
        {
            get
            {
                InitializeAttributes();
                _snapDamageByType.TryGetValue(MetalVariant, out var value);
                return value != 0 ? value : 10;
            }
        }

        public BearTrap()
        {
            // Load the attribute dictionaries from the json
            _snapDamageByType = Attributes?["snapDamageBy"].AsObject<Dictionary<string, float>>();
            _durabilityByType = Attributes?["durabilityBy"].AsObject<Dictionary<string, int>>();
        }
        
        private void InitializeAttributes()
        {
            if (Attributes == null) return;
            _durabilityByType ??= Attributes?["durabilityBy"].AsObject<Dictionary<string, int>>();
            _snapDamageByType ??= Attributes?["snapDamageBy"].AsObject<Dictionary<string, float>>();
        }

        public override void OnEntityInside(IWorldAccessor world, Entity entity, BlockPos pos)
        {
            var be = GetBlockEntity<BlockEntityBearTrap>(pos);
            be?.SnapClosed(entity);
            base.OnEntityInside(world, entity, pos);
        }

        public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection blockSel, IPlayer forPlayer)
        {
            var be = GetBlockEntity<BlockEntityBearTrap>(blockSel.Position);
            if (be != null)
            {
                WorldInteraction[] interactions = Array.Empty<WorldInteraction>();
                if (be.TrapState == EnumTrapState.Closed)
                {
                    interactions = interactions.Append(new WorldInteraction()
                    {
                        HotKeyCode = "shift",
                        ActionLangCode = "blockhelp-beartrap-open",
                        MouseButton = EnumMouseButton.Right,
                        RequireFreeHand = true
                    },
                    new WorldInteraction()
                    {
                        ActionLangCode = "blockhelp-behavior-rightclickpickup",
                        MouseButton = EnumMouseButton.Right,
                        RequireFreeHand = true
                    });
                }
                else if (be.TrapState == EnumTrapState.Open)
                {
                    interactions = interactions.Append(new WorldInteraction()
                    {
                        HotKeyCode = "shift",
                        ActionLangCode = "blockhelp-beartrap-bait",
                        MouseButton = EnumMouseButton.Right,
                        Itemstacks = Util.AssetUtils.GetItemStacks(world, new List<string>()
                        {
                            "game:redmeat-raw",
                            "game:fish-raw",
                            "game:bushmeat-raw",
                        })
                    });
                } else if (be.TrapState == EnumTrapState.Baited)
                {
                    interactions = interactions.Append(new WorldInteraction()
                    {
                        HotKeyCode = "shift",
                        ActionLangCode = "blockhelp-beartrap-pickbait",
                        MouseButton = EnumMouseButton.Right,
                        RequireFreeHand = true
                    });
                }

                return interactions;
            }
            return base.GetPlacedBlockInteractionHelp(world, blockSel, forPlayer);
        }

        public override bool DoPlaceBlock(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ItemStack byItemStack)
        {
            bool val = base.DoPlaceBlock(world, byPlayer, blockSel, byItemStack);

            if (val)
            {
                var be = GetBlockEntity<BlockEntityBearTrap>(blockSel.Position);
                if (be != null)
                {
                    BlockPos targetPos = blockSel.DidOffset ? blockSel.Position.AddCopy(blockSel.Face.Opposite) : blockSel.Position;
                    double dx = byPlayer.Entity.Pos.X - (targetPos.X + blockSel.HitPosition.X);
                    double dz = (float)byPlayer.Entity.Pos.Z - (targetPos.Z + blockSel.HitPosition.Z);
                    float angleHor = (float)Math.Atan2(dx, dz);

                    float roundRad = ((int)Math.Round(angleHor / RotInterval)) * RotInterval;

                    be.RotationYDeg = roundRad * GameMath.RAD2DEG;
                    var stack = byPlayer.InventoryManager.ActiveHotbarSlot.Itemstack;
                    be.Damage = MaxDamage - (int)stack.Attributes.GetDecimal("durability", GetMaxDurability(stack));
                    be.MarkDirty(true);
                }
            }
            return val;
        }
        
        public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
        {
            var be = world.BlockAccessor.GetBlockEntity<BlockEntityBearTrap>(pos);
            if (be != null)
            {
                var stack = new ItemStack(this);
                stack.Attributes.SetInt("durability", MaxDamage - be.Damage);
                return stack;
            }
            return base.OnPickBlock(world, pos);
        }

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            var be = GetBlockEntity<BlockEntityBearTrap>(blockSel.Position);
            if (be != null)
            {
                if (!byPlayer.Entity.Controls.ShiftKey && be.TrapState == EnumTrapState.Closed)
                {
                    return base.OnBlockInteractStart(world, byPlayer, blockSel);
                }
                return be.Interact(byPlayer, blockSel);
            }
            return base.OnBlockInteractStart(world, byPlayer, blockSel);
        }

        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            dsc.Append("Damage on snap: ").Append(SnapDamage).Append("%").Append('\n');
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
        }

        public override int GetMaxDurability(ItemStack itemstack)
        {
            return MaxDamage;
        }

        public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
        {
            var be = GetBlockEntity<BlockEntityBearTrap>(pos);
            if (be != null)
            {
                api.Logger.Warning(be.TrapState.ToString());
                api.Logger.Warning(be.Damage.ToString());
                if (be.TrapState == EnumTrapState.Destroyed)
                {
                    var itemCode = MetalVariant == "stainlesssteel" ? "ingot-stainlesssteel" : "metalbit-" + MetalVariant;
                    var quantity = MetalVariant == "stainlesssteel" ? 1 : 15 + world.Rand.Next(10);
                    return new[]
                    {
                        new ItemStack(world.GetItem(new AssetLocation(itemCode)), quantity)
                    };
                }
                
                var stack = new ItemStack(this);
                stack.Attributes.SetInt("durability", MaxDamage - be.Damage);
                return new[]
                {
                    stack
                };
            }

            return base.GetDrops(world, pos, byPlayer, dropQuantityMultiplier);
        }
        
        public override void GetDecal(IWorldAccessor world, BlockPos pos, ITexPositionSource decalTexSource, ref MeshData decalModelData, ref MeshData blockModelData)
        {
            var be = GetBlockEntity<BlockEntityBearTrap>(pos);
            if (be != null)
            {
                api.Logger.Warning(decalTexSource.ToString());
                blockModelData = be.GetCurrentMesh().Clone().Rotate(Vec3f.Half, 0, (be.RotationYDeg-90) * GameMath.DEG2RAD, 0);
                decalModelData = be.GetCurrentMesh(decalTexSource).Clone().Rotate(Vec3f.Half, 0, (be.RotationYDeg-90) * GameMath.DEG2RAD, 0);

                return;
            }

            base.GetDecal(world, pos, decalTexSource, ref decalModelData, ref blockModelData);

        }
    }
}