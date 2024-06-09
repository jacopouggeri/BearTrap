using System;
using System.Collections.Generic;
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

        public override void OnEntityInside(IWorldAccessor world, Entity entity, BlockPos pos)
        {
            var be = GetBlockEntity<BlockEntityBearTrap>(pos);
            if (be != null &&
                be.TrapState == EnumTrapState.Open)
            {
                be.SnapClosed(entity);
            }
            base.OnEntityInside(world, entity, pos);
        }

        public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection blockSel, IPlayer forPlayer)
        {
            var be = GetBlockEntity<BlockEntityBearTrap>(blockSel.Position);
            if (be != null)
            {
                WorldInteraction[] interactions = new WorldInteraction[]
                {
                    new WorldInteraction()
                    {
                        ActionLangCode = "blockhelp-behavior-rightclickpickup",
                        MouseButton = EnumMouseButton.Right,
                        RequireFreeHand = true
                    }
                };
                if (be.TrapState == EnumTrapState.Closed)
                {
                    interactions = interactions.Append(new WorldInteraction()
                    {
                        HotKeyCode = "shift",
                        ActionLangCode = "blockhelp-beartrap-open",
                        MouseButton = EnumMouseButton.Right,
                        RequireFreeHand = true
                    });
                }
                if (be.TrapState == EnumTrapState.Open)
                {
                    interactions = interactions.Append(new WorldInteraction()
                    {
                        ActionLangCode = "blockhelp-beartrap-bait",
                        MouseButton = EnumMouseButton.Right,
                        Itemstacks = Util.AssetUtils.GetItemStacks(world, new List<string>()
                        {
                            "game:redmeat-raw",
                            "game:fish-raw",
                            "game:bushmeat-raw",
                        })
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
                    be.MarkDirty(true);
                }
            }

            return val;
        }

        public override Cuboidf[] GetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
        {
            return new[] { new Cuboidf(0, 0, 0, 0.1, 0, 1) };
        }

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            var be = GetBlockEntity<BlockEntityBearTrap>(blockSel.Position);
            if (be != null) return be.Interact(byPlayer, blockSel) && base.OnBlockInteractStart(world, byPlayer, blockSel);
            return base.OnBlockInteractStart(world, byPlayer, blockSel);
        }

        public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
        {
            var be = GetBlockEntity<BlockEntityBearTrap>(pos);
            if (be != null && be.TrapState == EnumTrapState.Destroyed)
            {
                var material = this.Variant["metal"];
                api.Logger.Notification("Dropping bits of " + material + "!");
                return new[] { new ItemStack(world.GetItem(new AssetLocation("game:item-metalbit-" + material)), 6 + world.Rand.Next(8)) };
            }

            return base.GetDrops(world, pos, byPlayer, dropQuantityMultiplier);
        }
    }
}