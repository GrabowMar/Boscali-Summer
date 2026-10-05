#if UNITY_EDITOR
using UnityEngine;

namespace BepInEx { public static class Paths { public static string ConfigPath { get; set; } = System.IO.Path.GetTempPath(); } }

namespace HarmonyLib
{
    public sealed class HarmonyPatch : System.Attribute
    {
        public HarmonyPatch(System.Type type, string method) { }
    }
    public sealed class HarmonyPrefix : System.Attribute { }
    public sealed class HarmonyPostfix : System.Attribute { }
}

namespace NuclearOption.UIStyleSystem
{
    public static class ThemeManager { public static Theme Active => throw new System.InvalidOperationException(); }
    public class Theme { public Palette ColorTheme; public TacticalScreenTheme TacScreenTheme; }
    public class TacticalScreenTheme { public System.Collections.Generic.List<TacticalTextStyle> TextStyles; }
    public class TacticalTextStyle { public TacticalStyle Style; }
    public class TacticalStyle { public TMPro.TMP_FontAsset Font; }
    public class Palette { public Color AllClear, MapIconFriendly, HudUnitFriendly, HudUnitNeutral, HudUnitHostile, HudUnitSelected, Warning, Alert; }
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
public class MFDScreen : MonoBehaviour { public TMPro.TextMeshProUGUI label; }
namespace BoscaliSummer.Core.Services
{
    internal static class ModuleServices
    {
        internal static bool TryGet<T>(out T service) where T : class
        {
            service = null;
            return false;
        }
    }
}
namespace BoscaliSummer.Core.Modules { }
namespace BoscaliSummer.Core.Ui { }

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    internal static class MfdRailPatch { internal static bool IsApplied => true; }
    internal static class ReliefNavigator
    {
        internal static void Tick(BoscaliSummer.Modules.Command.Domain.ReliefRig rig) { }
        internal static void Release() { }
        internal static void Reset() { }
        internal static bool Following => false;
        internal static bool Flying => false;
        internal static bool BlockIconClick() => false;
        internal static bool BlockContextClick() => false;
        internal static void Focus(GlobalPosition point, bool zoom) { }
        internal static void ToggleFly() { }
        internal static void Turn(float degrees) { }
        internal static void Tilt(float degrees) { }
        internal static void North() { }
        internal static void Fit() { }
        internal static void ToggleFollow() { }
    }
    internal static class MapUiPointer
    {
        internal static bool Contains(RectTransform rect, Vector2 point) => true;
        internal static bool OverControls() => false;
    }
}

public sealed class DynamicMap : MonoBehaviour
{
    public static bool mapMaximized = true;
    public GameObject mapImage;
    public GameObject iconLayer;
    public UnityEngine.UI.Image mapBackground;
    public Transform mapScaleCenter;
    public Transform mapScaleProxy;
    public Canvas maximizedMapCanvas;
    public GridLabels gridLabels;
    public System.Collections.Generic.List<MapIcon> selectedIcons = new System.Collections.Generic.List<MapIcon>();
    public System.Collections.Generic.List<MapIcon> mapIcons = new System.Collections.Generic.List<MapIcon>();
    public float mapDimension = 81920f;
    public float mapDisplayFactor = 900f / 81920f;
    private float zoom = 1f;
    public float GetZoomLevel() => zoom;
    public void SetZoomLevel(float value)
    {
        // Match the game's proxy/reparent zoom path. A direct image-scale stub hid
        // the cropped terrain seen in the expanded live map.
        mapScaleCenter.position = mapBackground.transform.position;
        mapScaleProxy.localScale = mapScaleCenter.localScale;
        mapScaleProxy.position = mapImage.transform.position;
        mapScaleProxy.SetParent(mapScaleCenter);
        mapScaleCenter.localScale = Vector3.one * value;
        mapScaleProxy.SetParent(mapBackground.transform);
        mapImage.transform.localScale = mapScaleProxy.localScale;
        mapImage.transform.position = mapScaleProxy.position;
        zoom = value;
    }
    public GlobalPosition GetCursorCoordinates() => default;
    public bool TryGetCursorCoordinates(out GlobalPosition point) { point = default; return true; }
    public void UnselectAll() => selectedIcons.Clear();
    public void DeselectIcon(Unit unit) => selectedIcons.RemoveAll(icon => icon is UnitMapIcon track && track.unit == unit);
    public void DisplayTooltip(MapIcon icon) { }
    public void HideTooltip() { }
    public void SelectIcon(Unit unit)
    {
        foreach (MapIcon icon in mapIcons)
            if (icon is UnitMapIcon track && track.unit == unit && !selectedIcons.Contains(icon))
                selectedIcons.Add(icon);
    }
    public static FactionMode GetFactionMode(FactionHQ hq = null, bool checkNoFactionBeforeSpectator = false) =>
        hq == null ? FactionMode.NoFaction : hq.Friendly ? FactionMode.Friendly : FactionMode.Enemy;
    public bool IsCursorInMapRectangle() => true;
    private void JumptoTarget() { }
}

public sealed class GridLabels : MonoBehaviour { }

public sealed class MapSettings : MonoBehaviour { public Vector2 MapSize = new Vector2(81920f, 81920f); }
public class MapIcon : MonoBehaviour
{
    public enum ClickSource { Mouse }
    public UnityEngine.UI.Image iconImage;
    protected Vector3 globalPosition;
}
public enum FactionMode { NoFaction = 0, Spectator = 1, Friendly = 2, Enemy = 4 }
public sealed class MapOptions : MonoBehaviour { public float iconSize = .8f; }
public sealed class FactionHQ { public bool Friendly; }
public sealed class TargetListSelector { public bool CheckExclusions(Unit unit) => false; }
public class Unit
{
    public UnitDefinition definition;
    public bool disabled;
    public FactionHQ NetworkHQ;
}
public sealed class Aircraft : Unit { }
public sealed class Ship : Unit { }
public sealed class GroundVehicle : Unit { }
public sealed class Building : Unit { }
public enum VehicleType { TRUCK, UGV, LCV, AFV, MBT, ART, AAA, IR_SAM, R_SAM, RDR }
public class VehicleDefinition : UnitDefinition { public VehicleType vehicleType; }
public sealed class Missile : Unit { }
public static class GameManager
{
    public static Aircraft LocalAircraft;
    public static bool GetLocalAircraft(out Aircraft aircraft)
    {
        aircraft = LocalAircraft;
        return aircraft != null;
    }
}
public sealed class UnitMapIcon : MapIcon
{
    public Unit unit;
    public void UpdateIcon(float factor, float inverse, Transform parent, bool large) { }
    public string GetInfoText() => "TEST TRACK";
}
public sealed class TestUnit : Unit { }
public class UnitDefinition { public bool mapOrient; public float mapIconSize = 1f; public Sprite mapIcon; }
public sealed class TestDefinition : UnitDefinition { }
public sealed class AirbaseMapIcon : MapIcon
{
    public Unit airbase = new Unit();
    public int NativeClicks;
    public void ClickIcon(ClickSource source) { NativeClicks++; }
    public void UpdateIcon(float factor, float inverse, Transform parent, bool large) { }
    public string GetInfoText() => "TEST AIRBASE";
}
public sealed class ObjectiveMarker : MonoBehaviour { public void UpdateMarker() { } }
public sealed class TargetMarker : MonoBehaviour { public UnitMapIcon Icon; private void Update() { } }
public sealed class GameAssets { public static GameAssets i = new GameAssets(); public PhysicMaterial terrainMaterial; }
public enum PhysicsLayers { StaticsMask = 1 }
public static class SceneSingleton<T> where T : class { public static T i; }
public struct GlobalPosition
{
    public float x, y, z;
    public GlobalPosition(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    public Vector3 ToLocalPosition() => new Vector3(x, y, z);
}
public static class GlobalPositionExtensions
{
    public static GlobalPosition ToGlobalPosition(this Vector3 point) =>
        new GlobalPosition(point.x, point.y, point.z);
}
public sealed class TestEntry<T> { public T Value; public TestEntry(T value) { Value = value; } }
public sealed class TestCommandSettings
{
    public TestEntry<bool> MapRelief3D = new TestEntry<bool>(true);
    public TestEntry<bool> MapSymbology = new TestEntry<bool>(true);
    public TestEntry<bool> MapTerrainImage = new TestEntry<bool>(true);
    public TestEntry<bool> FrontlinesOverlay = new TestEntry<bool>(true);
    public TestEntry<bool> ThreatHeat = new TestEntry<bool>(true);
    public TestEntry<float> MapTerrainOpacity = new TestEntry<float>(1f);
}
public sealed class TestSettings { public TestCommandSettings Command = new TestCommandSettings(); }
public sealed class TestLogger { public void LogWarning(string message) => Debug.LogWarning(message); }
public static class Plugin
{
    public static TestSettings Settings = new TestSettings();
    public static TestLogger Logger = new TestLogger();
}
#endif
