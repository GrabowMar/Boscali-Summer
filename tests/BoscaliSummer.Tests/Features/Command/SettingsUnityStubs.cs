#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
public class SceneSingleton<T> { public static T i; }
public class DynamicMap : MonoBehaviour { public static bool mapMaximized; public Canvas maximizedMapCanvas; }
public class VirtualMFD : MonoBehaviour { }
public class MFDScreen : MonoBehaviour
{
    public string shortName; public GameObject displayPanel; public bool aircraftOnly; public bool isActive;
    public TextMeshProUGUI label; public Image highlight;
}
namespace BoscaliSummer.Framework.Contracts
{
    internal interface IThirdPersonHud
    {
        bool IsEnabled { get; }
        void Toggle();
        bool HidePitchLadder { get; set; }
        bool CameraFeedEnabled { get; set; }
        bool FlightCameraEnabled { get; set; }
    }
}
namespace BoscaliSummer.Framework.Features { internal static class ModServices { public static bool TryGet<T>(out T service) { service=default; return false; } } }
namespace BoscaliSummer.Framework.Lifecycle { public interface ISceneService { void ResetForScene(); } }
namespace BoscaliSummer.Runtime
{
    public static class MfdSlots { public const string Set = "SET"; }
    public static class MfdBezel
    {
        public static bool TryClaim(string id, bool preferLeft, VirtualMFD mfd, out List<Button> buttons,
            out List<MFDScreen> screens, out int slot, out bool left)
        { buttons = null; screens = null; slot = 0; left = false; return false; }
        public static MFDScreen FindTemplate(List<MFDScreen> screens) => null;
        public static MFDScreen FindTemplate(VirtualMFD mfd) => null;
        public static void Release(string id) { }
        public static bool Bind(VirtualMFD mfd, List<Button> buttons, List<MFDScreen> screens, int slot, bool left, MFDScreen screen) => false;
    }
}
namespace BoscaliSummer.Features.Command.Presentation
{
    public class ComMapOverlay { public static ComMapOverlay Instance; public void SyncSettings() { } }
}
namespace BoscaliSummer.Features.Command.Presentation.MapUi
{
    public static class MfdMapDeck
    {
        public static string WallpaperStatus => "No images found.";
        public static int DiscoveredWallpaperCount => 0;
        public static void Configure(object o) { }
        public static void ApplyAppearance(object o) { }
        public static string GetCurrentWallpaperFileName() => "NONE FOUND";
        public static void CycleCustomWallpaper(int d) { }
        public static void RescanWallpapers() { }
    }
    public static class MfdRailPatch
    {
        public static Vector2 AppliedCanvasSize => Vector2.zero;
        public static void OnStructureChanged(DynamicMap map) { }
        public static void Refresh(DynamicMap map) { }
        public static void Reconcile() { }
    }
    internal static class MfdNewsTicker { public static void Ensure(Canvas c, MfdLayout.Columns columns, object settings) { } }
}
namespace NuclearOption.UIStyleSystem
{
    public static class ThemeManager { public static Theme Active => throw new System.InvalidOperationException(); }
    public class Theme { public Palette ColorTheme; }
    public class Palette { public Color AllClear, MapIconFriendly, HudUnitFriendly, Warning, Alert; }
}
namespace Rewired
{
    public sealed class Keyboard { public bool enabled; }
    public sealed class Controllers { public Keyboard Keyboard => null; }
    public static class ReInput
    {
        public static bool isReady => false;
        public static Controllers controllers => null;
    }
}
#endif
