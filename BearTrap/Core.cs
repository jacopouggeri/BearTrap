using BearTrap.ModBlockEntity;
using Vintagestory.API.Client;
using Vintagestory.API.Server;
using Vintagestory.API.Common;

namespace BearTrap;

public class Core : ModSystem
{
    public static ILogger Logger;
    public static string Modid;
    
    // Called on server and client
    // Useful for registering block/entity classes on both sides
    public override void Start(ICoreAPI api)
    {
        Modid = Mod.Info.ModID;
        Logger = Mod.Logger;
        //api.Logger.Notification("Hello from template mod: " + api.Side);
        api.RegisterBlockClass(Modid + ".beartrap", typeof(ModBlock.BearTrap));
        api.RegisterBlockEntityClass(Modid + ".blockentitybeartrap", typeof(BlockEntityBearTrap));
        api.RegisterMountable(Modid + ".beartrap", ModBlock.BearTrap.GetMountable);
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
    }
}