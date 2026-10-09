using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;


namespace KerbalSteamProgram.UI
{
    /// <summary>
    /// Loads and holds references to Asset Bundled things
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class SystemSteamAssets : MonoBehaviour
    {
        public static GameObject OverlayPanelPrefab { get; private set; }
        public static GameObject ToolbarPanelPrefab { get; private set; }
        public static GameObject ToolbarPanelLoopPrefab { get; private set; }
        public static Dictionary<string, Sprite> Sprites { get; private set; }

        internal static string ASSET_PATH = "GameData/SteamPunk_KSP/UI/systemsteamui.dat";
        internal static string SPRITE_ATLAS_NAME = "system-steam-sprites-1";

        private void Awake()
        {
            Utils.Log("[SystemSteamAssets]: Loading Assets", LogType.UI);
            AssetBundle prefabs = AssetBundle.LoadFromFile(Path.Combine(KSPUtil.ApplicationRootPath, ASSET_PATH));

            // Get the Prefabs
            OverlayPanelPrefab = prefabs.LoadAsset("SystemInfo") as GameObject;
            ToolbarPanelPrefab = prefabs.LoadAsset("SystemSteamToolbar") as GameObject;
            ToolbarPanelLoopPrefab = prefabs.LoadAsset("SystemSteamLoopData") as GameObject;

            Utils.Log("[SystemSteamAssets]: Loaded UI Prefabs", LogType.UI);
            // Get the Sprite Atlas
            Sprite[] spriteSheet = prefabs.LoadAssetWithSubAssets<Sprite>(SPRITE_ATLAS_NAME);
            Sprites = new Dictionary<string, Sprite>();
            foreach (Sprite subSprite in spriteSheet)
            {
                Sprites.Add(subSprite.name, subSprite);
            }
            Utils.Log($"[SystemSteamAssets]: Loaded {Sprites.Count} sprites", LogType.UI);
        }

        // We can't load the asset bundle again - it is already loaded, and it would
        // instantiate the old types. Instead, we use reflection to read them out of
        // the old assembly. Hot-reloading will take care of replacing any widgets
        // within.
        private static void OnHotLoad(Assembly prev)
        {
            const BindingFlags Flags = BindingFlags.Public | BindingFlags.Static;

            var old = prev.GetType(typeof(SystemSteamAssets).FullName);
            OverlayPanelPrefab = GetPrefabProperty(old, nameof(OverlayPanelPrefab));
            ToolbarPanelPrefab = GetPrefabProperty(old, nameof(ToolbarPanelPrefab));
            ToolbarPanelLoopPrefab = GetPrefabProperty(old, nameof(ToolbarPanelLoopPrefab));
            Sprites = (Dictionary<string, Sprite>)old.GetProperty(nameof(Sprites), Flags).GetValue(null);
        }

        private static GameObject GetPrefabProperty(Type old, string name)
        {
            const BindingFlags Flags = BindingFlags.Public | BindingFlags.Static;
            return (GameObject)old.GetProperty(name, Flags).GetValue(null);
        }
    }
}
