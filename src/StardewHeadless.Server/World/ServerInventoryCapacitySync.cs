using StardewValley;

namespace StardewHeadless.Server.World;

/// <summary>
/// Memastikan farmer di server selalu memiliki kapasitas 58 slot (48 tas + 10 toolbelt),
/// sehingga NetFields tidak mismatch dengan client (mencegah BUG-06).
/// </summary>
public static class ServerInventoryCapacitySync
{
    public const int TotalCapacityWithToolbelt = 58;

    public static void EnsureCapacity(Farmer? farmer)
    {
        if (farmer?.Items == null) return;
        while (farmer.Items.Count < TotalCapacityWithToolbelt)
        {
            farmer.Items.Add(null);
        }
    }
}
