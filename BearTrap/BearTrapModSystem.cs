using BearTrap.ModBlockEntity;
using Vintagestory;
using Vintagestory.API.Client;
using Vintagestory.API.Server;
using Vintagestory.API.Common;

namespace BearTrap;

public class BearTrapModSystem : ModSystem
{
    public Logger Logger;
    public static string Modid;
    
    // Called on server and client
    // Useful for registering block/entity classes on both sides
    public override void Start(ICoreAPI api)
    {
        //api.Logger.Notification("Hello from template mod: " + api.Side);
        api.RegisterBlockClass(Mod.Info.ModID + ".beartrap", typeof(ModBlock.BearTrap));
        api.RegisterBlockEntityClass(Mod.Info.ModID + ".blockentitybeartrap", typeof(BlockEntityBearTrap));
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        Modid = Mod.Info.ModID;
        Logger = (Logger)Mod.Logger;
        //api.Logger.Notification("Hello from template mod server side: " + Lang.Get("beartrap:hello"));
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        //api.Logger.Notification("Hello from template mod client side: " + Lang.Get("beartrap:hello"));
    }
}