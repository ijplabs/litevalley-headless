namespace StardewHeadless.Server.World;

/// <summary>
/// Koleksi konstanta kunci modData resmi yang digunakan oleh ekosistem LiteValley Headless Server.
/// Mencegah desinkronisasi string magic lintas modul server, anticheat, dan bridge client.
/// </summary>
public static class ModDataKeys
{
    // Chest & Storage Keys
    public const string ChestId = "svheadless/chest_id";
    public const string CommunityStorage = "svheadless/community_storage";
    public const string IsCommunal = "svheadless/is_communal";
    public const string ChestLabel = "svheadless/chest_label";
    public const string ChestOwner = "svheadless/chest_owner";
    public const string OwnerId = "svheadless/owner_id";
    public const string PrivateOwner = "svheadless/private_owner";
    public const string LockedBy = "svheadless/locked_by";

    // Player & Profession Keys
    public const string Profession = "svheadless/profession";

    // Crop Protection Keys
    public const string PlanterId = "svheadless/planter_id";
    public const string PlanterName = "svheadless/planter_name";
    public const string PublicCrop = "svheadless/public_crop";

    // Building & Cabin Keys
    public const string MainEstateHouse = "svheadless/main_estate_house";

    /// <summary>
    /// Kumpulan alias kunci yang merepresentasikan kepemilikan privat pada peti (chest).
    /// </summary>
    public static readonly string[] ChestOwnerAliases = new[]
    {
        ChestOwner,
        OwnerId,
        PrivateOwner,
        LockedBy
    };

    /// <summary>
    /// Kumpulan alias kunci yang menandai peti sebagai penyimpanan publik / komunal.
    /// </summary>
    public static readonly string[] CommunalStorageAliases = new[]
    {
        CommunityStorage,
        IsCommunal
    };
}
