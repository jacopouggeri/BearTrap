using System;
using Conibear.BlockEntity;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using EnumTrapState = Conibear.BlockEntity.EnumTrapState;

namespace Conibear.Block
{

    public class ConibearTrap : Vintagestory.API.Common.Block
    {
        protected float rotInterval = GameMath.PIHALF / 4;

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
        }

        public override void OnEntityCollide(IWorldAccessor world, Entity entity, BlockPos pos, BlockFacing facing, Vec3d collideSpeed,
            bool isImpact)
        {
            var be = GetBlockEntity<BlockEntityConibearTrap>(pos);
            if (be != null &&
                be.TrapState != BlockEntity.EnumTrapState.Destroyed &&
                be.TrapState != EnumTrapState.Closed)
            {
                be.SnapClosed(entity);
            }
            base.OnEntityCollide(world, entity, pos, facing, collideSpeed, isImpact);
        }

        public override bool DoPlaceBlock(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ItemStack byItemStack)
        {
            bool val = base.DoPlaceBlock(world, byPlayer, blockSel, byItemStack);

            if (val)
            {
                var be = GetBlockEntity<BlockEntityConibearTrap>(blockSel.Position);
                if (be != null)
                {
                    BlockPos targetPos = blockSel.DidOffset ? blockSel.Position.AddCopy(blockSel.Face.Opposite) : blockSel.Position;
                    double dx = byPlayer.Entity.Pos.X - (targetPos.X + blockSel.HitPosition.X);
                    double dz = (float)byPlayer.Entity.Pos.Z - (targetPos.Z + blockSel.HitPosition.Z);
                    float angleHor = (float)Math.Atan2(dx, dz);

                    float roundRad = ((int)Math.Round(angleHor / rotInterval)) * rotInterval;

                    be.RotationYDeg = roundRad * GameMath.RAD2DEG;
                    be.MarkDirty(true);
                }
            }

            return val;
        }

        public override Cuboidf[] GetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
        {
            return new Cuboidf[] { new Cuboidf(0, 0, 0, 1, 0.05f, 1) };
        }

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            var be = GetBlockEntity<BlockEntityConibearTrap>(blockSel.Position);
            if (be != null) return be.Interact(byPlayer, blockSel) && base.OnBlockInteractStart(world, byPlayer, blockSel);
            return base.OnBlockInteractStart(world, byPlayer, blockSel);
        }

        public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
        {
            var be = GetBlockEntity<BlockEntityConibearTrap>(pos);
            if (be != null && be.TrapState == BlockEntity.EnumTrapState.Destroyed)
            {
                var material = this.Variant["metal"];
                api.Logger.Notification("Dropping bits of " + material + "!");
                return new ItemStack[] { new ItemStack(world.GetItem(new AssetLocation("game:item-metalbit-" + material)), 6 + world.Rand.Next(8)) };
            }

            return base.GetDrops(world, pos, byPlayer, dropQuantityMultiplier);
        }
        
        public override void GetDecal(IWorldAccessor world, BlockPos pos, ITexPositionSource decalTexSource, ref MeshData decalModelData, ref MeshData blockModelData)
        {
            var be = GetBlockEntity<BlockEntityBasketTrap>(pos);
            if (be != null)
            {
                blockModelData = be.GetCurrentMesh(null).Clone().Rotate(Vec3f.Half, 0, (be.RotationYDeg-90) * GameMath.DEG2RAD, 0);
                return;
            }

            base.GetDecal(world, pos, decalTexSource, ref decalModelData, ref blockModelData);

        }
    }
}