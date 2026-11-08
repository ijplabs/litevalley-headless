using System;
using StardewValley;

namespace StardewHeadless.Server.World;

/// <summary>
/// Helper utilitas terpusat untuk operasi saldo (Wallet / Money) yang aman untuk Stardew Valley 1.6 Dedicated Server.
/// Di 1.6, setter langsung `farmer.Money = ...` melempar Exception jika `Game1.player != this`.
/// Kelas ini mengarahkan mutasi ke `Game1.player.team.SetIndividualMoney` / `AddIndividualMoney` secara aman.
/// </summary>
public static class FarmerWalletHelper
{
    public static int GetMoney(Farmer? who)
    {
        if (who == null) return 0;

        try
        {
            if (Game1.player?.team != null)
            {
                return Game1.player.team.GetIndividualMoney(who);
            }
        }
        catch
        {
        }

        return who.Money;
    }

    public static void SetMoney(Farmer? who, int amount)
    {
        if (who == null) return;
        amount = Math.Max(0, amount);

        try
        {
            if (Game1.player?.team != null)
            {
                Game1.player.team.SetIndividualMoney(who, amount);
                return;
            }
        }
        catch
        {
        }

        if (ReferenceEquals(Game1.player, who))
        {
            who.Money = amount;
        }
    }

    public static void AddMoney(Farmer? who, int delta)
    {
        if (who == null) return;

        try
        {
            if (Game1.player?.team != null)
            {
                int current = Game1.player.team.GetIndividualMoney(who);
                int target = Math.Max(0, current + delta);
                Game1.player.team.SetIndividualMoney(who, target);
                return;
            }
        }
        catch
        {
        }

        if (ReferenceEquals(Game1.player, who))
        {
            who.Money = Math.Max(0, who.Money + delta);
        }
    }

    public static void DeductMoney(Farmer? who, int amount)
    {
        if (amount <= 0) return;
        AddMoney(who, -amount);
    }
}
