using Vintagestory.API.Common;

namespace BearTrap.Util;

public static class SpeedUtil
{
    public static void ApplySlowEffect(EntityPlayer playerEntity, float speed = 0.1f)
    {
        // Only set the original walk speed if it hasn't been set yet
        if (!playerEntity.WatchedAttributes.HasAttribute("beartrap:previousWalkSpeed"))
        {
            playerEntity.WatchedAttributes.SetFloat("beartrap:previousWalkSpeed", playerEntity.walkSpeed);
        }
        playerEntity.Api.Logger.Warning("Setting walk speed to " + speed);
        playerEntity.walkSpeed = speed;
        playerEntity.WatchedAttributes.MarkPathDirty("beartrap:previousWalkSpeed");
    }

    public static void RemoveSlowEffect(EntityPlayer playerEntity)
    {
        var previousWalkSpeed = playerEntity.WatchedAttributes.GetFloat("beartrap:previousWalkSpeed");
        playerEntity.walkSpeed = previousWalkSpeed;
        playerEntity.WatchedAttributes.RemoveAttribute("beartrap:previousWalkSpeed");
        playerEntity.WatchedAttributes.MarkPathDirty("beartrap:previousWalkSpeed");
    }
}