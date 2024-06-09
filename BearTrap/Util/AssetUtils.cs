using System.Collections.Generic;
using Vintagestory.API.Common;

namespace BearTrap.Util;

public class AssetUtils
{
    public static ItemStack[] GetItemStacks(IWorldAccessor world, List<string> itemCodes)
    {
        List<ItemStack> itemStacks = new List<ItemStack>();

        foreach (string itemCode in itemCodes)
        {
            Item item = world.GetItem(new AssetLocation(itemCode));
            if (item != null)
            {
                itemStacks.Add(new ItemStack(item, 1));
            }
        }

        return itemStacks.ToArray();
    }
}