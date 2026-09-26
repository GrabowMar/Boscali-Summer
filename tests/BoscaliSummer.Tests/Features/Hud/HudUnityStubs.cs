#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Game adapters only. The HUD widgets, layout, settings and damage adapter are production sources.
public class SceneSingleton<T> { public static T i; }
public class FlightHud : MonoBehaviour
{
    public Transform statusAnchor;
    private Canvas canvas;
    private GameObject pitchCompassCenter;
    private RawImage compass;
    public static int Updates;
    public static Action Projection;
    private void Update() { Updates++; Projection?.Invoke(); }
    public static void EnableCanvas(bool value) { }
}
public class HeadMountedDisplay : MonoBehaviour { public static int Updates; private void Update() { Updates++; } }
public class HUDAppManager : MonoBehaviour { public static int Updates; private void Update() { Updates++; } }
public class CombatHUD : MonoBehaviour
{
    public Aircraft aircraft; public static int Updates; public HUDUnitMarker Selected;
    private TextMeshProUGUI targetInfo;
    public void SetLabel(TextMeshProUGUI label) => targetInfo=label;
    private bool ShowTargetInfo() { if(Selected==null) return false; targetInfo.transform.position=Selected.image.transform.position; return Selected.image.enabled; }
    private void LateUpdate() { Updates++; }
}
public class UnitPart : MonoBehaviour { public float hitPoints = 100; public bool detached; public bool IsDetached() => detached; }
public class Cockpit : UnitPart { public Rigidbody rb; }
public class Pilot { public Vector3 GetAccel() => Vector3.zero; }
public class ControlInputs { public float throttle = .72f; }
public class FactionHQ { }
public struct GlobalPosition { public float y; }
public static class PositionExtensions
{
    public static float GlobalY(this Vector3 position) => position.y + Datum.originPosition.y;
    public static GlobalPosition GlobalPosition(this Transform transform) => new GlobalPosition { y = transform.position.y };
}
public static class LevelInfo { public static float GetSpeedOfSound(float altitude) => 330; }
public class HUDApp : MonoBehaviour { }
public class SpeedGauge : HUDApp { private TMP_Text airspeedDisplay; private Image border; }
public class AoADisplay : HUDApp { private TMP_Text AoAText; }
public class Altitude : HUDApp { }
public class Climbrate : HUDApp { }
public class Bearing : HUDApp { }
public class ArtificialHorizon : HUDApp { }
public class FuelGauge : HUDApp { }
public class ThrottleGauge : HUDApp { }
public class GIndicators : HUDApp { }
public class MachIndicator : HUDApp { }
public class HUDUnitMarker
{
    public Unit unit; public Image image; public static Camera Camera; public int Updates;
    public void UpdatePosition(FactionHQ hq, GlobalPosition position, Vector3 forward)
    { Updates++; Vector3 point = Camera.WorldToScreenPoint(unit.transform.position); point.z = 0; image.transform.position = point; }
}
public class PilotOwner { public bool IsLocalPlayer = true; }
public static class Datum { public static Vector3 originPosition; }
namespace NuclearOption.MissionEditorScripts { public static class InputFieldChecker { public static bool InsideInputField; } }
namespace BoscaliSummer.Features.Hud.Runtime { internal class ThirdPersonFlightCamera { public int AppliedFrame { get; set; } = -1; public void Reset() { AppliedFrame = -1; } } }

public class MFDScreen : MonoBehaviour { public TMP_Text label; }
public class Unit : MonoBehaviour { public string unitName; public int persistentID; }
public class PersistentUnit { public Unit unit; public string unitName; }
public class Missile : Unit { public Unit owner; public int targetID; public string GetSeekerType() => "Radar"; }
public class MissileWarning { public List<Missile> knownMissiles = new List<Missile>(); }
public static class UnitRegistry
{
    public static List<Unit> allUnits = new List<Unit>();
    public static Dictionary<int, PersistentUnit> persistent = new Dictionary<int, PersistentUnit>();
    public static bool TryGetPersistentUnit(int id, out PersistentUnit unit) => persistent.TryGetValue(id, out unit);
}
public class AircraftParameters { public GameObject StatusDisplay; }
public class Aircraft : Unit
{
    public bool disabled;
    public PilotOwner Player = new PilotOwner();
    public Cockpit cockpit;
    public float speed = 260, radarAlt = 1400;
    public FactionHQ NetworkHQ = new FactionHQ();
    public Pilot[] pilots = { new Pilot() };
    public List<UnitPart> partLookup = new List<UnitPart>();
    public ControlInputs GetInputs() => new ControlInputs();
    public float GetFuelLevel() => .58f;
    public TargetCam targetCam;
    public AircraftParameters parameters = new AircraftParameters();
    public MissileWarning warnings = new MissileWarning();
    public Action<Unit> onDisableUnit;
    public bool HasEjected() => false;
    public AircraftParameters GetAircraftParameters() => parameters;
    public MissileWarning GetMissileWarningSystem() => warnings;
}
public class TargetCam : MonoBehaviour { public enum CamMode { landingMode, targetRear, targetFront } public Camera cam; }
public class DynamicMap : MonoBehaviour { public static bool mapMaximized; public static void EnableCanvas(bool value) { } }
public class GameplayUI { public static bool GameIsPaused; }
public class PlayerSettings { public static bool cinematicMode; }
public class CameraStateManager : MonoBehaviour { public Camera mainCamera; public object currentState, cockpitState, orbitState, chaseState; public Unit followingUnit; }
public static class GameManager { public static Aircraft aircraft; public static bool GetLocalAircraft(out Aircraft a) { a = aircraft; return a != null; } }
public static class UnitConverter { public static string ClimbRateReading(float v) => v.ToString("+0.0;-0.0;0.0") + " m/s"; public static string SpeedReading(float v) => (v * 3.6f).ToString("0") + " km/h"; public static string AltitudeReading(float v) => v.ToString("0") + " m"; public static string DistanceReading(float value) => (value / 1000f).ToString("0.0") + " km"; }
public struct OnReportDamage { public string failureMessage; }
public interface IReportDamage { event Action<OnReportDamage> onReportDamage; }
public class PartStatusDisplay { public Image partImage; public float displayCondition = 1; public void DamageUnsubscribe() { } }
public class StatusDisplay : MonoBehaviour
{
    private Aircraft aircraft;
    private Image aircraftBackground;
    private List<PartStatusDisplay> statusDisplays = new List<PartStatusDisplay>();
    private List<IReportDamage> damageReporters = new List<IReportDamage>();
    private AudioSource damageAlertSource;
    private Dictionary<string, GameObject> failureIndicatorsLookup = new Dictionary<string, GameObject>();
    public void Initialize(Aircraft aircraft)
    {
        this.aircraft = aircraft;
        aircraft.onDisableUnit += StatusDisplay_OnDisable;
        damageAlertSource = aircraft.gameObject.AddComponent<AudioSource>();
    }
    public void DisplayDamage() { enabled = true; }
    private void StatusDisplay_OnReportDamage(OnReportDamage report) { }
    private void StatusDisplay_OnDisable(Unit unit) { }
}
namespace NuclearOption.Networking { public class Player { } }
namespace BoscaliSummer.Framework.Lifecycle { internal interface ISceneService { void ResetForScene(); } }
namespace BoscaliSummer.Runtime
{
    internal static class NativeCamera
    {
        public static bool Available => true;
        public static TargetCam.CamMode ReadMode(TargetCam cam) => TargetCam.CamMode.targetFront;
        public static bool TryGet(Aircraft aircraft, out Camera camera, out string mode)
        { camera = aircraft.targetCam?.cam; mode = "FORWARD"; return camera != null; }
    }
    internal static class VanillaHudStyle
    {
        public struct CockpitStyle { public TMP_FontAsset Font; public Material FontMaterial; public Color Colour; }
        public struct Palette { public Color AllClear, Warning, Alert; }
        public static Palette Colours => new Palette { AllClear = Color.green, Warning = Color.yellow, Alert = Color.red };
        public static bool TryCockpit(out CockpitStyle style) { style = default; return false; }
        public static void Invalidate() { }
    }
}
namespace NuclearOption.UIStyleSystem
{
    public static class ThemeManager { public static Theme Active => throw new System.InvalidOperationException(); }
    public class Theme { public Palette ColorTheme; public TacticalScreenTheme TacScreenTheme; }
    public class TacticalScreenTheme { public List<TacticalTextStyle> TextStyles; }
    public class TacticalTextStyle { public TacticalStyle Style; }
    public class TacticalStyle { public TMP_FontAsset Font; }
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

namespace BoscaliSummer.Framework.Features { internal static class ModServices { public static bool TryGet<T>(out T service) { service=default; return false; } } }
#endif
