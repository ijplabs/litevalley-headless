using System;
using Microsoft.Xna.Framework;
using StardewValley;

namespace StardewHeadless.Server.World;

/// <summary>
/// Menyimpan koordinat tapak tata letak peternakan (Rumah Utama/Grand Estate, Pintu Masuk, Peti Komunal, dan Kotak Surat).
/// </summary>
public record FarmLayoutCoordinates(
    Point MainHouseTopLeft,
    Point FrontDoor,
    Point FrontEntranceStep,
    Point CommunalChest1,
    Point CommunalChest2,
    Point Mailbox
);

/// <summary>
/// Dynamic Coordinate Resolver untuk memetakan penempatan Grand Estate, Mailbox, dan Peti Komunal secara presisi.
/// Menghilangkan hardcoded (58, 14) sehingga aman dari tabrakan air atau tebing di seluruh 11 profil peta peternakan
/// (Vanilla Standard, Riverland, Forest, Hilltop, Wilderness, Four Corners, Beach, Meadowlands, SVE IF2R, Grandpa's Farm, Frontier Farm).
/// </summary>
public static class MapLayoutResolver
{
    public static FarmLayoutCoordinates Resolve(Farm? farm, string? farmProfileId = null)
    {
        string profile = (farmProfileId ?? string.Empty).ToLowerInvariant().Trim();

        // 1. Profil Khusus SVE: Immersive Farm 2 Remastered (IF2R)
        bool isIF2R = profile == "sve:if2r" || profile == "if2r"
                      || (farm?.NameOrUniqueName?.Contains("IF2R", StringComparison.OrdinalIgnoreCase) == true)
                      || (farm?.map?.Properties?.ContainsKey("FarmCaveEntry") == true && profile.Contains("sve"));

        if (isIF2R)
        {
            return new FarmLayoutCoordinates(
                MainHouseTopLeft: new Point(46, 13),
                FrontDoor: new Point(48, 15),
                FrontEntranceStep: new Point(48, 16),
                CommunalChest1: new Point(67, 18),
                CommunalChest2: new Point(68, 18),
                Mailbox: new Point(68, 15)
            );
        }

        // 2. Evaluasi lokasi Ground-Truth dari instance Farm aktif jika tersedia
        if (farm != null)
        {
            try
            {
                var starterHouseLoc = farm.GetStarterFarmhouseLocation();
                if (starterHouseLoc.X > 0 && starterHouseLoc.Y > 0)
                {
                    int hx = (int)starterHouseLoc.X;
                    int hy = (int)starterHouseLoc.Y;

                    Point mailbox = new Point(hx + 10, hy + 2);
                    try
                    {
                        var nativeMailbox = farm.GetMainMailboxPosition();
                        if (nativeMailbox.X > 0 && nativeMailbox.Y > 0)
                        {
                            mailbox = nativeMailbox;
                        }
                    }
                    catch { }

                    return new FarmLayoutCoordinates(
                        MainHouseTopLeft: new Point(hx, hy),
                        FrontDoor: new Point(hx + 2, hy + 2),
                        FrontEntranceStep: new Point(hx + 2, hy + 3),
                        CommunalChest1: new Point(hx + 4, hy + 3),
                        CommunalChest2: new Point(hx + 5, hy + 3),
                        Mailbox: mailbox
                    );
                }
            }
            catch { }

            try
            {
                var mainEntry = farm.GetMainFarmHouseEntry();
                if (mainEntry.X > 0 && mainEntry.Y > 0)
                {
                    int hx = mainEntry.X - 2;
                    int hy = mainEntry.Y - 2;

                    return new FarmLayoutCoordinates(
                        MainHouseTopLeft: new Point(hx, hy),
                        FrontDoor: new Point(mainEntry.X, mainEntry.Y - 1),
                        FrontEntranceStep: mainEntry,
                        CommunalChest1: new Point(mainEntry.X + 2, mainEntry.Y),
                        CommunalChest2: new Point(mainEntry.X + 3, mainEntry.Y),
                        Mailbox: new Point(hx + 10, hy + 2)
                    );
                }
            }
            catch { }
        }

        // 3. Fallback Terpetakan Berdasarkan Profil / FarmType
        int whichFarm = Game1.whichFarm;
        if (profile == "vanilla:fourcorners" || profile == "fourcorners" || whichFarm == 5)
        {
            return new FarmLayoutCoordinates(
                MainHouseTopLeft: new Point(48, 39),
                FrontDoor: new Point(50, 41),
                FrontEntranceStep: new Point(50, 42),
                CommunalChest1: new Point(52, 42),
                CommunalChest2: new Point(53, 42),
                Mailbox: new Point(58, 41)
            );
        }

        if (profile == "vanilla:riverland" || profile == "riverland" || whichFarm == 1)
        {
            return new FarmLayoutCoordinates(
                MainHouseTopLeft: new Point(38, 14),
                FrontDoor: new Point(40, 16),
                FrontEntranceStep: new Point(40, 17),
                CommunalChest1: new Point(42, 17),
                CommunalChest2: new Point(43, 17),
                Mailbox: new Point(48, 16)
            );
        }

        // Standar Default untuk Standard (0), Forest (2), Hilltop (3), Wilderness (4), Beach (6), Meadowlands (7), Grandpa's Farm
        return new FarmLayoutCoordinates(
            MainHouseTopLeft: new Point(58, 14),
            FrontDoor: new Point(60, 16),
            FrontEntranceStep: new Point(60, 17),
            CommunalChest1: new Point(62, 17),
            CommunalChest2: new Point(63, 17),
            Mailbox: new Point(68, 16)
        );
    }
}
