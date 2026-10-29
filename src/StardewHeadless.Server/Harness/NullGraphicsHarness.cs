using System.IO;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using StardewHeadless.Protocol.Logging;
using StardewValley;
using StardewValley.Audio;

namespace StardewHeadless.Server.Harness;

/// <summary>
/// Harness untuk mem-bypass 100% dependensi grafis (OpenGL/DirectX/GPU) dan audio (XACT/OpenAL)
/// pada MonoGame dan Stardew Valley menggunakan Harmony runtime IL patching.
/// </summary>
public static class NullGraphicsHarness
{
    private static Harmony? _harmony;
    private static StructuredLogger? _logger;
    private static string _gameInstallDir = @"D:\SteamLibrary\steamapps\common\Stardew Valley";

    public static void Apply(StructuredLogger logger, string? gameDir = null)
    {
        _logger = logger;
        if (!string.IsNullOrWhiteSpace(gameDir))
        {
            _gameInstallDir = gameDir;
        }
        else if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("STARDEW_GAME_DIR")))
        {
            _gameInstallDir = Environment.GetEnvironmentVariable("STARDEW_GAME_DIR")!;
        }

        _logger?.Info("Harness", "Mengaplikasikan NullGraphicsHarness untuk pure headless execution (Zero-GPU / Zero-Window)...");

        _harmony = new Harmony("com.stardewheadless.nullgraphics");

        PatchMonoGamePlatform();
        PatchGraphicsDevice();
        PatchTextures();
        PatchAudio();
        PatchGameLoopBarriers();
        PatchContentStorage();
        PatchDataLoader();

        _logger?.Info("Harness", "NullGraphicsHarness berhasil diaplikasikan ke seluruh platform hooks.");
    }

    private static void PatchMonoGamePlatform()
    {
        var monoGameAsm = typeof(Game).Assembly;
        var sdlPlatformType = monoGameAsm.GetType("Microsoft.Xna.Framework.SdlGamePlatform");

        // 1. SdlGamePlatform.BeforeDraw: selalu return false agar Game.DoDraw tidak memanggil BeginDraw/Draw/EndDraw
        if (sdlPlatformType != null)
        {
            var beforeDrawMethod = AccessTools.Method(sdlPlatformType, "BeforeDraw");
            if (beforeDrawMethod != null)
            {
                var prefix = AccessTools.Method(typeof(NullGraphicsHarness), nameof(BeforeDraw_Prefix));
                _harmony!.Patch(beforeDrawMethod, prefix: new HarmonyMethod(prefix));
                _logger?.Debug("Harness", "Patched SdlGamePlatform.BeforeDraw -> selalu false");
            }

            // 1a. SdlGamePlatform.RunLoop: intercept untuk menjalankan headless 60 TPS loop tanpa SDL event polling
            var runLoopMethod = AccessTools.Method(sdlPlatformType, "RunLoop");
            if (runLoopMethod != null)
            {
                var prefix = AccessTools.Method(typeof(NullGraphicsHarness), nameof(RunLoop_Prefix));
                _harmony!.Patch(runLoopMethod, prefix: new HarmonyMethod(prefix));
                _logger?.Debug("Harness", "Patched SdlGamePlatform.RunLoop -> delegated to ServerHost");
            }
        }

        // 1b. Sdl.Window.Create & Sdl.Window.Show: paksa window selalu tersembunyi (Hidden) dan no-op Show
        var sdlWindowType = monoGameAsm.GetType("Sdl+Window") ?? monoGameAsm.GetType("Microsoft.Xna.Framework.Sdl+Window");
        if (sdlWindowType != null)
        {
            var windowCreate = AccessTools.Method(sdlWindowType, "Create");
            if (windowCreate != null)
            {
                var createPrefix = AccessTools.Method(typeof(NullGraphicsHarness), nameof(SdlWindow_Create_Prefix));
                _harmony!.Patch(windowCreate, prefix: new HarmonyMethod(createPrefix));
                _logger?.Debug("Harness", "Patched Sdl.Window.Create -> forced SDL_WINDOW_HIDDEN");
            }

            var showField = AccessTools.Field(sdlWindowType, "Show");
            if (showField != null)
            {
                var delType = showField.FieldType;
                var noOpMethod = typeof(NullGraphicsHarness).GetMethod(nameof(NoOpWindowShow), BindingFlags.Public | BindingFlags.Static);
                if (noOpMethod != null)
                {
                    var del = Delegate.CreateDelegate(delType, noOpMethod);
                    showField.SetValue(null, del);
                    _logger?.Debug("Harness", "Replaced Sdl.Window.Show delegate -> NoOp");
                }
            }
        }
    }

    public static void SdlWindow_Create_Prefix(ref int flags)
    {
        // SDL: Shown = 4, Hidden = 8
        flags = (flags & ~4) | 8;
    }

    public static void NoOpWindowShow(IntPtr window)
    {
        // No-op: jangan pernah tampilkan window ke monitor
    }

    public static bool BeforeDraw_Prefix(ref bool __result)
    {
        __result = false;
        return false; // Skip original BeforeDraw and cancel DoDraw
    }

    public static bool RunLoop_Prefix()
    {
        ServerHost.RunHeadlessLoop();
        return false; // Skip vanilla SdlGamePlatform.RunLoop
    }

    private static void PatchGraphicsDevice()
    {
        // 2. GraphicsDevice.PlatformSetup: bypass GL.CreateContext
        var setupMethod = AccessTools.Method(typeof(GraphicsDevice), "PlatformSetup");
        if (setupMethod != null)
        {
            var prefix = AccessTools.Method(typeof(NullGraphicsHarness), nameof(PlatformSetup_Prefix));
            _harmony!.Patch(setupMethod, prefix: new HarmonyMethod(prefix));
            _logger?.Debug("Harness", "Patched GraphicsDevice.PlatformSetup -> bypass GL.CreateContext");
        }

        // 3. GraphicsDevice.PlatformInitialize: bypass FramebufferHelper and GL states
        var initMethod = AccessTools.Method(typeof(GraphicsDevice), "PlatformInitialize");
        if (initMethod != null)
        {
            var prefix = AccessTools.Method(typeof(NullGraphicsHarness), nameof(GraphicsDevice_PlatformInitialize_Prefix));
            _harmony!.Patch(initMethod, prefix: new HarmonyMethod(prefix));
            _logger?.Debug("Harness", "Patched GraphicsDevice.PlatformInitialize -> bypass GL states");
        }

        // 3a. GraphicsDevice.PlatformSetViewport: bypass GL.Viewport
        var setVpMethod = AccessTools.Method(typeof(GraphicsDevice), "PlatformSetViewport");
        if (setVpMethod != null)
        {
            var prefix = AccessTools.Method(typeof(NullGraphicsHarness), nameof(PlatformSetViewport_Prefix));
            _harmony!.Patch(setVpMethod, prefix: new HarmonyMethod(prefix));
            _logger?.Debug("Harness", "Patched GraphicsDevice.PlatformSetViewport -> bypass GL.Viewport");
        }

        // 3b. GraphicsDevice.PlatformClear: bypass GL.Clear
        var clearMethod = AccessTools.Method(typeof(GraphicsDevice), "PlatformClear");
        if (clearMethod != null)
        {
            var prefix = AccessTools.Method(typeof(NullGraphicsHarness), nameof(PlatformClear_Prefix));
            _harmony!.Patch(clearMethod, prefix: new HarmonyMethod(prefix));
            _logger?.Debug("Harness", "Patched GraphicsDevice.PlatformClear -> bypass GL.Clear");
        }

        // 3c. GraphicsDevice.PlatformApplyDefaultRenderTarget: bypass framebufferHelper
        var applyRtMethod = AccessTools.Method(typeof(GraphicsDevice), "PlatformApplyDefaultRenderTarget");
        if (applyRtMethod != null)
        {
            var prefix = AccessTools.Method(typeof(NullGraphicsHarness), nameof(PlatformApplyDefaultRenderTarget_Prefix));
            _harmony!.Patch(applyRtMethod, prefix: new HarmonyMethod(prefix));
            _logger?.Debug("Harness", "Patched GraphicsDevice.PlatformApplyDefaultRenderTarget -> bypass framebufferHelper");
        }

        // 3d. GraphicsDevice.PlatformDispose: bypass GL Context.Dispose()
        var disposeMethod = AccessTools.Method(typeof(GraphicsDevice), "PlatformDispose");
        if (disposeMethod != null)
        {
            var prefix = AccessTools.Method(typeof(NullGraphicsHarness), nameof(PlatformDispose_Prefix));
            _harmony!.Patch(disposeMethod, prefix: new HarmonyMethod(prefix));
            _logger?.Debug("Harness", "Patched GraphicsDevice.PlatformDispose -> bypass GL Context.Dispose");
        }

        // 3e. GraphicsDevice.OnPresentationChanged: bypass GL Context.MakeCurrent
        var presMethod = AccessTools.Method(typeof(GraphicsDevice), "OnPresentationChanged");
        if (presMethod != null)
        {
            var prefix = AccessTools.Method(typeof(NullGraphicsHarness), nameof(OnPresentationChanged_Prefix));
            _harmony!.Patch(presMethod, prefix: new HarmonyMethod(prefix));
            _logger?.Debug("Harness", "Patched GraphicsDevice.OnPresentationChanged -> bypass GL Context.MakeCurrent");
        }

        // 4. GraphicsCapabilities.PlatformInitialize: bypass GL.Extensions
        var capType = AccessTools.TypeByName("Microsoft.Xna.Framework.Graphics.GraphicsCapabilities");
        if (capType != null)
        {
            var capInit = AccessTools.Method(capType, "PlatformInitialize", new[] { typeof(GraphicsDevice) });
            if (capInit != null)
            {
                var prefix = AccessTools.Method(typeof(NullGraphicsHarness), nameof(GraphicsCapabilities_PlatformInitialize_Prefix));
                _harmony!.Patch(capInit, prefix: new HarmonyMethod(prefix));
                _logger?.Debug("Harness", "Patched GraphicsCapabilities.PlatformInitialize -> dummy capabilities");
            }
        }

        // 5. GraphicsAdapter.SupportedDisplayModes: fallback 1280x720 display mode untuk headless linux tanpa GPU/X11
        var getDisplayModes = AccessTools.PropertyGetter(typeof(GraphicsAdapter), nameof(GraphicsAdapter.SupportedDisplayModes));
        if (getDisplayModes != null)
        {
            var prefix = AccessTools.Method(typeof(NullGraphicsHarness), nameof(SupportedDisplayModes_Prefix));
            _harmony!.Patch(getDisplayModes, prefix: new HarmonyMethod(prefix));
            _logger?.Debug("Harness", "Patched GraphicsAdapter.SupportedDisplayModes -> headless 1280x720 fallback");
        }

        // 6. Options.setToDefaults: tangani exception jika terjadi saat inisialisasi display mode headless
        var setDefaults = AccessTools.Method(typeof(Options), nameof(Options.setToDefaults));
        if (setDefaults != null)
        {
            var finalizer = AccessTools.Method(typeof(NullGraphicsHarness), nameof(Options_SetToDefaults_Finalizer));
            _harmony!.Patch(setDefaults, finalizer: new HarmonyMethod(finalizer));
            _logger?.Debug("Harness", "Patched Options.setToDefaults -> finalizer exception suppression");
        }
    }

    private static DisplayModeCollection? _cachedDummyModes;

    public static bool SupportedDisplayModes_Prefix(ref DisplayModeCollection __result)
    {
        if (_cachedDummyModes == null)
        {
            try
            {
                var dmCtor = AccessTools.Constructor(typeof(DisplayMode), new[] { typeof(int), typeof(int), typeof(SurfaceFormat) });
                var dmcCtor = AccessTools.Constructor(typeof(DisplayModeCollection), new[] { typeof(List<DisplayMode>) });
                if (dmCtor != null && dmcCtor != null)
                {
                    var mode = (DisplayMode)dmCtor.Invoke(new object[] { 1280, 720, SurfaceFormat.Color });
                    var list = new List<DisplayMode> { mode };
                    _cachedDummyModes = (DisplayModeCollection)dmcCtor.Invoke(new object[] { list });
                }
            }
            catch (Exception ex)
            {
                _logger?.Warn("Harness", $"Gagal membuat dummy DisplayModeCollection: {ex.Message}");
            }
        }

        if (_cachedDummyModes != null)
        {
            __result = _cachedDummyModes;
            return false;
        }

        return true;
    }

    public static Exception? Options_SetToDefaults_Finalizer(Options __instance, Exception? __exception)
    {
        if (__exception != null)
        {
            _logger?.Warn("Harness", $"Suppressed Options.setToDefaults exception dalam mode headless: {__exception.Message}");
            __instance.preferredResolutionX = 1280;
            __instance.preferredResolutionY = 720;
            return null; // Suppress exception
        }
        return null;
    }

    public static bool PlatformSetup_Prefix(GraphicsDevice __instance)
    {
        // Tetapkan property & field internal dummy agar GraphicsDevice tidak crash
        AccessTools.Field(typeof(GraphicsDevice), "MaxTextureSlots")?.SetValue(__instance, 16);
        AccessTools.Field(typeof(GraphicsDevice), "_maxTextureSize")?.SetValue(__instance, 4096);
        AccessTools.Field(typeof(GraphicsDevice), "MaxVertexAttributes")?.SetValue(__instance, 16);
        AccessTools.Field(typeof(GraphicsDevice), "_maxVertexBufferSlots")?.SetValue(__instance, 16);
        AccessTools.Field(typeof(GraphicsDevice), "_newEnabledVertexAttributes")?.SetValue(__instance, new bool[16]);
        return false; // Skip original PlatformSetup
    }

    public static bool GraphicsDevice_PlatformInitialize_Prefix(GraphicsDevice __instance)
    {
        var pp = __instance.PresentationParameters;
        int width = pp?.BackBufferWidth ?? 1280;
        int height = pp?.BackBufferHeight ?? 720;
        var vp = new Viewport(0, 0, width, height);
        AccessTools.Field(typeof(GraphicsDevice), "_viewport")?.SetValue(__instance, vp);

        var enabledAttribs = AccessTools.Field(typeof(GraphicsDevice), "_enabledVertexAttributes")?.GetValue(__instance);
        enabledAttribs?.GetType().GetMethod("Clear")?.Invoke(enabledAttribs, null);

        var maxSlots = (int)(AccessTools.Field(typeof(GraphicsDevice), "_maxVertexBufferSlots")?.GetValue(__instance) ?? 16);
        var bufferInfoType = AccessTools.TypeByName("Microsoft.Xna.Framework.Graphics.BufferBindingInfo");
        if (bufferInfoType != null)
        {
            var array = Array.CreateInstance(bufferInfoType, maxSlots);
            AccessTools.Field(typeof(GraphicsDevice), "_bufferBindingInfos")?.SetValue(__instance, array);
        }

        return false; // Skip original PlatformInitialize
    }

    public static bool PlatformSetViewport_Prefix() => false;
    public static bool PlatformClear_Prefix() => false;
    public static bool PlatformApplyDefaultRenderTarget_Prefix() => false;
    public static bool PlatformDispose_Prefix() => false;
    public static bool OnPresentationChanged_Prefix() => false;

    public static bool GraphicsCapabilities_PlatformInitialize_Prefix(object __instance)
    {
        var type = __instance.GetType();
        void SetProp(string name, object val) => AccessTools.Property(type, name)?.SetValue(__instance, val);

        SetProp("SupportsNonPowerOfTwo", true);
        SetProp("SupportsTextureFilterAnisotropic", false);
        SetProp("SupportsDepth24", true);
        SetProp("SupportsPackedDepthStencil", true);
        SetProp("SupportsDepthNonLinear", false);
        SetProp("SupportsTextureMaxLevel", true);
        SetProp("SupportsS3tc", true);
        SetProp("SupportsDxt1", true);
        SetProp("SupportsPvrtc", false);
        SetProp("SupportsEtc1", false);
        SetProp("SupportsAtitc", false);
        SetProp("SupportsFramebufferObjectARB", true);
        SetProp("SupportsFramebufferObjectEXT", false);
        SetProp("SupportsSRgb", false);
        SetProp("SupportsFloatTextures", true);
        SetProp("SupportsHalfFloatTextures", true);
        SetProp("SupportsNormalized", false);
        SetProp("SupportsTextureArrays", false);
        SetProp("SupportsDepthClamp", false);
        SetProp("SupportsVertexTextures", false);
        SetProp("SupportsInstancing", false);
        SetProp("SupportsBaseIndexInstancing", false);
        SetProp("SupportsSeparateBlendStates", false);

        return false; // Skip original PlatformInitialize
    }

    private static void PatchTextures()
    {
        // 5. Texture2D.PlatformConstruct: bypass GL texture allocation
        var texConstruct = AccessTools.Method(typeof(Texture2D), "PlatformConstruct");
        if (texConstruct != null)
        {
            var prefix = AccessTools.Method(typeof(NullGraphicsHarness), nameof(Texture2D_PlatformConstruct_Prefix));
            _harmony!.Patch(texConstruct, prefix: new HarmonyMethod(prefix));
            _logger?.Debug("Harness", "Patched Texture2D.PlatformConstruct -> bypass GL texture allocation");
        }

        // 7. RenderTarget2D.PlatformConstruct: bypass render target GL creation
        var rtConstruct = AccessTools.Method(typeof(RenderTarget2D), "PlatformConstruct");
        if (rtConstruct != null)
        {
            var prefix = AccessTools.Method(typeof(NullGraphicsHarness), nameof(RenderTarget2D_PlatformConstruct_Prefix));
            _harmony!.Patch(rtConstruct, prefix: new HarmonyMethod(prefix));
            _logger?.Debug("Harness", "Patched RenderTarget2D.PlatformConstruct -> bypass render target GL creation");
        }

        // 7b. Texture2D.SetData<T>, PlatformSetData<T>, GetData<T>, PlatformGetData<T>: bypass GL pixel transfer
        var commonTypes = new[] { typeof(Color), typeof(byte), typeof(int), typeof(uint), typeof(Vector4) };
        foreach (var m in typeof(Texture2D).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            if ((m.Name == "SetData" || m.Name.StartsWith("PlatformSetData") || m.Name == "GetData" || m.Name.StartsWith("PlatformGetData")) && m.IsGenericMethodDefinition)
            {
                foreach (var t in commonTypes)
                {
                    try
                    {
                        var closed = m.MakeGenericMethod(t);
                        var prefix = AccessTools.Method(typeof(NullGraphicsHarness), nameof(Texture2D_Data_Prefix));
                        _harmony!.Patch(closed, prefix: new HarmonyMethod(prefix));
                    }
                    catch
                    {
                        // Ignore incompatible generic constraints
                    }
                }
            }
        }
        _logger?.Debug("Harness", "Patched Texture2D.SetData/GetData<T> -> bypass GL pixel transfer");

        // 8. Texture2DReader.Read: seek stream past pixel data, return dummy sized Texture2D
        var texReaderType = AccessTools.TypeByName("Microsoft.Xna.Framework.Content.Texture2DReader");
        if (texReaderType != null)
        {
            var readMethod = AccessTools.Method(texReaderType, "Read", new[] { typeof(ContentReader), typeof(Texture2D) });
            if (readMethod != null)
            {
                var prefix = AccessTools.Method(typeof(NullGraphicsHarness), nameof(Texture2DReader_Read_Prefix));
                _harmony!.Patch(readMethod, prefix: new HarmonyMethod(prefix));
                _logger?.Debug("Harness", "Patched Texture2DReader.Read -> zero-memory mipmap seeker");
            }
        }
    }

    public static bool Texture2D_PlatformConstruct_Prefix() => false;
    public static bool RenderTarget2D_PlatformConstruct_Prefix() => false;
    public static bool Texture2D_Data_Prefix() => false;

    public static bool Texture2DReader_Read_Prefix(ContentReader reader, Texture2D existingInstance, ref Texture2D __result)
    {
        var surfaceFormat = (SurfaceFormat)reader.ReadInt32();
        uint num = reader.ReadUInt32();
        uint num2 = reader.ReadUInt32();

        int width = (num & 0xFFFF0000u) == 0 ? (int)num : (int)(num & 0xFFFF);
        int height = (num2 & 0xFFFF0000u) == 0 ? (int)num2 : (int)(num2 & 0xFFFF);
        int levelCount = reader.ReadInt32();

        // LzxDecoderStream tidak mendukung Seek(), jadi kita drain stream dengan buffer 8KB agar memory tetap < 250MB
        Span<byte> skipBuffer = stackalloc byte[8192];
        for (int i = 0; i < levelCount; i++)
        {
            int byteCount = reader.ReadInt32();
            int remaining = byteCount;
            while (remaining > 0)
            {
                int toRead = Math.Min(remaining, skipBuffer.Length);
                int read = reader.Read(skipBuffer.Slice(0, toRead));
                if (read <= 0) break;
                remaining -= read;
            }
        }

        var device = reader.GetGraphicsDevice();
        __result = existingInstance ?? new Texture2D(device, Math.Max(1, width), Math.Max(1, height), false, surfaceFormat);
        return false; // Skip original Read
    }

    private static void PatchAudio()
    {
        // 9. Game1.InitializeSounds: gunakan DummyAudioEngine & DummySoundBank bawaan Stardew Valley
        var initSoundsMethod = AccessTools.Method(typeof(Game1), "InitializeSounds");
        if (initSoundsMethod != null)
        {
            var prefix = AccessTools.Method(typeof(NullGraphicsHarness), nameof(Game1_InitializeSounds_Prefix));
            _harmony!.Patch(initSoundsMethod, prefix: new HarmonyMethod(prefix));
            _logger?.Debug("Harness", "Patched Game1.InitializeSounds -> DummyAudioEngine & DummySoundBank");
        }

        // 9b. Game1.updateMusic: bypass music updates pada headless server
        var updateMusicMethod = AccessTools.Method(typeof(Game1), "updateMusic");
        if (updateMusicMethod != null)
        {
            var prefix = AccessTools.Method(typeof(NullGraphicsHarness), nameof(Game1_updateMusic_Prefix));
            _harmony!.Patch(updateMusicMethod, prefix: new HarmonyMethod(prefix));
            _logger?.Debug("Harness", "Patched Game1.updateMusic -> no-op");
        }

        // 9c. Game1.initializeVolumeLevels: bypass audio volume setup pada headless server
        var initVolMethod = AccessTools.Method(typeof(Game1), "initializeVolumeLevels");
        if (initVolMethod != null)
        {
            var prefix = AccessTools.Method(typeof(NullGraphicsHarness), nameof(Game1_initializeVolumeLevels_Prefix));
            _harmony!.Patch(initVolMethod, prefix: new HarmonyMethod(prefix));
            _logger?.Debug("Harness", "Patched Game1.initializeVolumeLevels -> no-op");
        }
    }

    public static bool Game1_updateMusic_Prefix() => false;
    public static bool Game1_initializeVolumeLevels_Prefix() => false;

    public static bool Game1_InitializeSounds_Prefix()
    {
        var dummyEngineType = AccessTools.TypeByName("StardewValley.Audio.DummyAudioEngine");
        var dummyBankType = AccessTools.TypeByName("StardewValley.Audio.DummySoundBank");
        if (dummyEngineType != null && dummyBankType != null)
        {
            var engine = (IAudioEngine)Activator.CreateInstance(dummyEngineType)!;
            var bank = (ISoundBank)Activator.CreateInstance(dummyBankType)!;
            Game1.audioEngine = engine;
            Game1.soundBank = bank;
            Game1.musicCategory = engine.GetCategory("Music");
            Game1.soundCategory = engine.GetCategory("Sound");
            Game1.ambientCategory = engine.GetCategory("Ambient");
            Game1.footstepCategory = engine.GetCategory("Footsteps");
        }

        AccessTools.Field(typeof(Game1), "FinishedFirstInitSounds")?.SetValue(null, true);
        return false; // Skip original InitializeSounds
    }

    private static void PatchGameLoopBarriers()
    {
        // 10. SaveGameMenu.update: set hasDrawn = true agar save malam hari tidak deadlock karena ketiadaan Draw()
        var saveMenuUpdate = AccessTools.Method(typeof(StardewValley.Menus.SaveGameMenu), nameof(StardewValley.Menus.SaveGameMenu.update));
        if (saveMenuUpdate != null)
        {
            var prefix = AccessTools.Method(typeof(NullGraphicsHarness), nameof(SaveGameMenu_Update_Prefix));
            _harmony!.Patch(saveMenuUpdate, prefix: new HarmonyMethod(prefix));
            _logger?.Debug("Harness", "Patched SaveGameMenu.update -> hasDrawn = true auto-set");
        }

        // 10b. GameLocation.IsOutOfBounds: cegah NullReferenceException saat map interior unvisited building belum dimuat
        var isOutOfBoundsMethod = AccessTools.Method(typeof(GameLocation), nameof(GameLocation.IsOutOfBounds));
        if (isOutOfBoundsMethod != null)
        {
            var prefix = AccessTools.Method(typeof(NullGraphicsHarness), nameof(GameLocation_IsOutOfBounds_Prefix));
            _harmony!.Patch(isOutOfBoundsMethod, prefix: new HarmonyMethod(prefix));
            _logger?.Debug("Harness", "Patched GameLocation.IsOutOfBounds -> safe null-map handling");
        }

        // 10c. GameLocation.isCollidingPosition: cegah NullReferenceException pada RequireLayer saat map interior belum dimuat
        foreach (var method in typeof(GameLocation).GetMethods().Where(m => m.Name == nameof(GameLocation.isCollidingPosition)))
        {
            var prefix = AccessTools.Method(typeof(NullGraphicsHarness), nameof(GameLocation_IsCollidingPosition_Prefix));
            _harmony!.Patch(method, prefix: new HarmonyMethod(prefix));
        }
        _logger?.Debug("Harness", "Patched GameLocation.isCollidingPosition -> safe null-map handling");

        // 10d. LocalMultiplayer.GetStaticFieldsAndDefaults: tangani ReflectionTypeLoadException jika ada assembly luar dengan missing types
        var getStaticFields = AccessTools.Method(typeof(LocalMultiplayer), "GetStaticFieldsAndDefaults");
        if (getStaticFields != null)
        {
            var finalizer = AccessTools.Method(typeof(NullGraphicsHarness), nameof(LocalMultiplayer_GetStaticFields_Finalizer));
            _harmony!.Patch(getStaticFields, finalizer: new HarmonyMethod(finalizer));
            _logger?.Debug("Harness", "Patched LocalMultiplayer.GetStaticFieldsAndDefaults -> finalizer exception suppression");
        }
    }

    public static Exception? LocalMultiplayer_GetStaticFields_Finalizer(Exception? __exception)
    {
        if (__exception != null)
        {
            _logger?.Warn("Harness", $"Suppressed LocalMultiplayer.GetStaticFieldsAndDefaults exception: {__exception.Message}");
            return null;
        }
        return null;
    }

    private static void PatchContentStorage()
    {
        // 11. TitleContainer.Location: redirect ke instalasi game Stardew Valley agar Content/*.xnb dimuat
        AccessTools.Property(typeof(TitleContainer), "Location")?.SetValue(null, _gameInstallDir);

        var platformInitMethod = AccessTools.Method(typeof(TitleContainer), "PlatformInit");
        if (platformInitMethod != null)
        {
            var prefix = AccessTools.Method(typeof(NullGraphicsHarness), nameof(TitleContainer_PlatformInit_Prefix));
            _harmony!.Patch(platformInitMethod, prefix: new HarmonyMethod(prefix));
            _logger?.Debug("Harness", $"Patched TitleContainer.Location -> redirected to '{_gameInstallDir}'");
        }

        // 11b. Program.GetAppDataFolder: redirect ke /data/config agar tidak menulis ke /data/game (read-only volume)
        var getAppDataFolder = AccessTools.Method(typeof(StardewValley.Program), "GetAppDataFolder");
        if (getAppDataFolder != null)
        {
            var prefix = AccessTools.Method(typeof(NullGraphicsHarness), nameof(GetAppDataFolder_Prefix));
            _harmony!.Patch(getAppDataFolder, prefix: new HarmonyMethod(prefix));
            _logger?.Debug("Harness", "Patched Program.GetAppDataFolder -> redirected to writable config folder");
        }
    }

    public static bool GetAppDataFolder_Prefix(string? subfolder, bool createIfMissing, ref string __result)
    {
        if (string.Equals(subfolder, "Saves", StringComparison.OrdinalIgnoreCase))
        {
            var saveDirEnv = Environment.GetEnvironmentVariable("STARDEW_SAVE_DIR");
            if (!string.IsNullOrWhiteSpace(saveDirEnv))
            {
                if (createIfMissing && !Directory.Exists(saveDirEnv))
                {
                    Directory.CreateDirectory(saveDirEnv);
                }
                __result = saveDirEnv;
                return false;
            }
        }

        var baseDir = Path.Combine(_gameInstallDir, "config");
        var configDirEnv = Environment.GetEnvironmentVariable("STARDEW_CONFIG_DIR");
        if (!string.IsNullOrWhiteSpace(configDirEnv))
        {
            baseDir = configDirEnv;
        }
        else if (Directory.Exists("/data/config"))
        {
            baseDir = "/data/config";
        }

        var path = string.IsNullOrEmpty(subfolder) ? baseDir : Path.Combine(baseDir, subfolder);
        if (createIfMissing && !Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }

        __result = path;
        return false;
    }

    public static bool TitleContainer_PlatformInit_Prefix()
    {
        AccessTools.Property(typeof(TitleContainer), "Location")?.SetValue(null, _gameInstallDir);
        return false; // Skip original PlatformInit
    }

    private static void PatchDataLoader()
    {
        var dataLoaderType = typeof(DataLoader);
        var methods = dataLoaderType.GetMethods(BindingFlags.Public | BindingFlags.Static);
        foreach (var m in methods)
        {
            if (m.ReturnType != typeof(void) && !m.IsGenericMethod)
            {
                var finalizer = AccessTools.Method(typeof(NullGraphicsHarness), nameof(DataLoader_Generic_Finalizer));
                try
                {
                    _harmony!.Patch(m, finalizer: new HarmonyMethod(finalizer));
                }
                catch
                {
                }
            }
        }
        _logger?.Debug("Harness", "Patched DataLoader methods with safe headless fallback finalizers");
    }

    public static Exception? DataLoader_Generic_Finalizer(MethodBase __originalMethod, Exception? __exception, ref object __result)
    {
        if (__exception != null && __originalMethod is MethodInfo mi)
        {
            _logger?.Warn("Harness", $"Suppressed DataLoader.{mi.Name} exception dalam mode headless: {__exception.Message}. Using fallback instance.");
            try
            {
                __result = Activator.CreateInstance(mi.ReturnType) ?? __result;
            }
            catch
            {
            }
            return null; // Suppress exception
        }
        return null;
    }

    public static void SaveGameMenu_Update_Prefix(StardewValley.Menus.SaveGameMenu __instance)
    {
        __instance.hasDrawn = true;
    }

    public static bool GameLocation_IsOutOfBounds_Prefix(GameLocation __instance, ref bool __result)
    {
        if (__instance.map == null || __instance.map.Layers == null || __instance.map.Layers.Count == 0)
        {
            __result = false;
            return false;
        }
        return true;
    }

    public static bool GameLocation_IsCollidingPosition_Prefix(GameLocation __instance, ref bool __result)
    {
        if (__instance.map == null || __instance.map.Layers == null || __instance.map.Layers.Count == 0)
        {
            __result = false;
            return false;
        }
        return true;
    }
}
