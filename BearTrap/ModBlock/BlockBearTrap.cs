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
                    be.MarkDirty(true);
                }
            }
            return val;
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
        
        public override void GetDecal(IWorldAccessor world, BlockPos pos, ITexPositionSource decalTexSource, ref MeshData decalModelData, ref MeshData blockModelData)
        {
            var be = GetBlockEntity<BlockEntityBearTrap>(pos);
            if (be != null)
            {
                api.Logger.Warning(decalTexSource.ToString());
                blockModelData = be.GetCurrentMesh(null).Clone().Rotate(Vec3f.Half, 0, (be.RotationYDeg-90) * GameMath.DEG2RAD, 0);
                decalModelData = be.GetCurrentMesh(decalTexSource).Clone().Rotate(Vec3f.Half, 0, (be.RotationYDeg-90) * GameMath.DEG2RAD, 0);

                return;
            }

            base.GetDecal(world, pos, decalTexSource, ref decalModelData, ref blockModelData);

        }
    }
}