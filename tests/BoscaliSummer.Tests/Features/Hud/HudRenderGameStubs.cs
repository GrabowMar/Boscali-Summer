#if UNITY_EDITOR
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Native data seams for the production render/visibility owners. Projection uses real Unity
// Camera/Transform/Canvas objects; this fixture does not reproduce native combat input or weapons.
public class SceneSingleton<T> : MonoBehaviour where T : MonoBehaviour { public static T i; }
public class CameraBaseState { }
public class CameraStateManager : SceneSingleton<CameraStateManager>
{
    public Camera mainCamera;
    public CameraBaseState orbitState = new CameraBaseState(), chaseState = new CameraBaseState(), cockpitState = new CameraBaseState();
    public CameraBaseState currentState;
    public Unit followingUnit;
}
public class Player { public bool IsLocalPlayer = true; }
public class FactionHQ
{
    public bool Known = true;
    public bool TryGetKnownPosition(Unit unit, out GlobalPosition position)
    { position = unit.GlobalPosition(); return Known; }
}
public class Unit : MonoBehaviour { }
public class Cockpit : MonoBehaviour { public bool detached; public bool IsDetached() => detached; }
public class Aircraft : Unit
{
    public bool disabled, ejected, gearDeployed;
    public float radarAlt = 200f;
    public Pilot[] pilots = { new Pilot() };
    public Player Player = new Player();
    public Cockpit cockpit;
    public Rigidbody rb;
    public FactionHQ NetworkHQ = new FactionHQ();
    public bool HasEjected() => ejected;
    public Rigidbody CockpitRB() => rb;
}
public class Pilot { public FlightInfo flightInfo = new FlightInfo(); }
public class FlightInfo { public bool HasTakenOff = true; }
public readonly struct GlobalPosition
{
    public readonly Vector3 Position;
    public GlobalPosition(Vector3 position) { Position = position; }
    public Vector3 ToLocalPosition() => Position + HudCoordinates.Origin;
    public static Vector3 operator -(GlobalPosition left, GlobalPosition right) => left.Position - right.Position;
}
public static class HudCoordinates
{
    public static Vector3 Origin;
    public static GlobalPosition GlobalPosition(this Transform transform) => new GlobalPosition(transform.position - Origin);
    public static GlobalPosition GlobalPosition(this Unit unit) => unit.transform.GlobalPosition();
}
public class FlightHud : SceneSingleton<FlightHud>
{
    private Canvas canvas;
    public Transform center;
    public Image velocityVector;
    public void Initialize(Canvas value) { canvas = value; }
    public Transform GetHUDCenter() => center;
    public static void EnableCanvas(bool enabled) => i.canvas.gameObject.SetActive(enabled);
}
public class CombatHUD : SceneSingleton<CombatHUD>
{
    public Aircraft aircraft;
    private readonly List<HUDUnitMarker> markers = new List<HUDUnitMarker>();
    private float jamAccumulation;
    private HUDWeaponState weaponState;
    private ObjectiveOverlayManager objectiveOverlay;
    private TextMeshProUGUI targetInfo, targetText;
    private Image targetArrow;
    private Transform targetArrowTail;
    private readonly List<Unit> targetList = new List<Unit>();
    public int HitRefreshes, NativeUpdates;
    public Transform hitMarker;
    public Unit hitUnit;
    public void Add(HUDUnitMarker marker) => markers.Add(marker);
    public void Jam(float value) { jamAccumulation = value; }
    public void Objectives(ObjectiveOverlayManager value) { objectiveOverlay = value; }
    public void Captions(TextMeshProUGUI info, TextMeshProUGUI text, Transform tail)
    {
        targetInfo = info; targetText = text; targetArrowTail = tail;
        targetArrow = new GameObject("Native target arrow", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        targetArrow.transform.SetParent(tail.parent, false);
        tail.SetParent(targetArrow.transform, true);
        targetArrow.enabled = false;
    }
    public void Target(Unit target)
    {
        targetList.Clear();
        if (target != null) targetList.Add(target);
        foreach (HUDUnitMarker marker in markers) marker.selected = marker.unit == target;
    }
    public void Weapon(HUDWeaponState value, Unit target) { weaponState = value; Target(target); }
    public List<Unit> GetTargetList() => targetList;
    public void SetTargetArrow(bool show, Vector3 point)
    {
        targetArrow.enabled = targetText.enabled = show;
        targetText.transform.position = targetArrowTail.position;
        if (show) targetArrow.transform.position = point;
    }
    private bool ShowTargetInfo()
    {
        if (targetList.Count == 0 || targetArrow.enabled) return false;
        HUDUnitMarker marker = markers.Find(item => item.unit == targetList[0]);
        if (marker == null || !aircraft.NetworkHQ.TryGetKnownPosition(marker.unit, out _)) return false;
        targetInfo.transform.position = marker.image.transform.position;
        targetInfo.text = marker.unit.name;
        marker.image.color = Color.cyan;
        return true;
    }
    private void UpdateHitMarkers()
    {
        HitRefreshes++;
        Vector3 point = CameraStateManager.i.mainCamera.WorldToScreenPoint(hitUnit.transform.position);
        hitMarker.position = new Vector3(point.x, point.y, 0f);
    }
    private void LateUpdate() { NativeUpdates++; }
}
public class MissionPosition
{
    public struct PositionResult { public GlobalPosition Position; }
}
public class ObjectiveOverlayManager : MonoBehaviour
{
    private Aircraft aircraft;
    private readonly List<ObjectiveOverlay> overlays = new List<ObjectiveOverlay>();
    private readonly List<MissionPosition.PositionResult> resultCache = new List<MissionPosition.PositionResult>();
    public int MissionQueries, SmoothingUpdates;
    public void Configure(Aircraft ownship, ObjectiveOverlay overlay, GlobalPosition position)
    {
        aircraft = ownship;
        overlays.Add(overlay);
        resultCache.Add(new MissionPosition.PositionResult { Position = position });
    }
}
public class TextNoOverlap
{
    public TextMeshProUGUI Text;
    public Vector2 TargetPosition, PreviousPosition, NudgeOffset;
    public bool AutomaticlalySetPosition;
    public void SetTarget(Vector2 point)
    {
        TargetPosition = point;
        if (AutomaticlalySetPosition) Text.transform.position = point;
    }
}
public class ObjectiveOverlay : MonoBehaviour
{
    public Transform Pointer;
    public TextNoOverlap TextNoOverlap;
    public int Refreshes;
    public void UpdateOverlay(MissionPosition.PositionResult result)
    {
        Refreshes++;
        Vector3 point = CameraStateManager.i.mainCamera.WorldToScreenPoint(result.Position.ToLocalPosition());
        point.z = 0f;
        Pointer.position = point;
        TextNoOverlap.SetTarget(point - Vector3.up * 25f);
    }
}
public class Airbase : MonoBehaviour
{
    public Transform center;
    public class Runway
    {
        public Transform Start;
        public readonly struct RunwayUsage
        {
            public readonly Runway Runway;
            public RunwayUsage(Runway runway) { Runway = runway; }
            public Transform GetStart() => Runway.Start;
        }
    }
}
public static class HUDFunctions
{
    public static bool PinToScreenEdge(Vector3 point, out Vector3 position, out float angle)
    {
        position = CameraStateManager.i.mainCamera.WorldToScreenPoint(point);
        position.z = 0f;
        angle = 0f;
        return false;
    }
}
public class AirbaseOverlay : MonoBehaviour
{
    private Airbase nearestAirbase;
    private Airbase.Runway.RunwayUsage? runwayUsage;
    private bool landing, taxiingToRunway, reachedRunway;
    private Image airbaseMarker, glideslope, glideslopeAimPoint;
    private TextMeshProUGUI airbaseLabel;
    public Image Border;
    public int BorderRefreshes, GlideRefreshes, RegistrationUpdates;
    public bool Reached => reachedRunway;
    public void Configure(Airbase airbase, Airbase.Runway.RunwayUsage usage, Image marker, TextMeshProUGUI label, Image glide, Image aim)
    {
        nearestAirbase = airbase; runwayUsage = usage; airbaseMarker = marker;
        airbaseLabel = label; glideslope = glide; glideslopeAimPoint = aim;
    }
    public void Flags(bool isLanding, bool isTaxiing, bool hasReached)
    { landing = isLanding; taxiingToRunway = isTaxiing; reachedRunway = hasReached; }
    private void DrawRunwayBorders(Airbase.Runway runway)
    {
        BorderRefreshes++;
        HUDFunctions.PinToScreenEdge(runway.Start.position, out Vector3 point, out _);
        Border.transform.position = point;
    }
    private bool DrawGlideslope(Aircraft aircraft, Airbase.Runway.RunwayUsage usage)
    {
        GlideRefreshes++;
        Vector3 world = usage.GetStart().position;
        Camera camera = CameraStateManager.i.mainCamera;
        if (aircraft.radarAlt < 1f || Vector3.Dot(world - camera.transform.position, camera.transform.forward) < 0f) return false;
        HUDFunctions.PinToScreenEdge(world, out Vector3 point, out _);
        glideslope.transform.position = point;
        HUDFunctions.PinToScreenEdge(world + Vector3.up * 10f, out point, out _);
        glideslopeAimPoint.transform.position = point;
        return true;
    }
}
public class AllyInfo : MonoBehaviour
{
    private TextMeshProUGUI hoveredAllyInfo;
    private HUDUnitMarker hoveredAllyMarker;
    public void Configure(TextMeshProUGUI info, HUDUnitMarker marker)
    { hoveredAllyInfo = info; hoveredAllyMarker = marker; }
}
public class HUDUnitMarker : MonoBehaviour
{
    public Image image;
    public Unit unit;
    public bool selected;
    public int Positions, Distortions;
    public GlobalPosition View;
    public Vector3 Forward;
    public void UpdatePosition(FactionHQ hq, GlobalPosition viewPosition, Vector3 cameraForward)
    {
        Positions++;
        View = viewPosition;
        Forward = cameraForward;
        Vector3 point = CameraStateManager.i.mainCamera.WorldToScreenPoint(unit.transform.position);
        transform.position = new Vector3(point.x, point.y, 0f);
        if (selected)
        {
            bool offscreen = point.z <= 0f || point.x < 0f || point.x > Screen.width || point.y < 0f || point.y > Screen.height;
            CombatHUD.i.SetTargetArrow(offscreen, transform.position);
            image.enabled = !offscreen;
        }
        Color colour = image.color;
        colour.a = 1f;
        image.color = colour;
    }
    public void JammingDistortion(float strength)
    {
        Distortions++;
        transform.position += new Vector3(1f, 2f, 0f) * strength;
        Color colour = image.color;
        colour.a = .625f;
        image.color = colour;
    }
}
public class DynamicMap : SceneSingleton<DynamicMap>
{
    public static bool mapMaximized;
    public static void EnableCanvas(bool enabled) => i.gameObject.SetActive(enabled);
}
public static class GameplayUI { public static bool GameIsPaused; }
public static class PlayerSettings { public static bool cinematicMode, lagPip; }
public static class Datum { public static Transform origin; }
public class HUDWeaponState : MonoBehaviour { public int SolverUpdates; }
public class ControlsFilter
{
    protected class AimAssist
    {
        private GlobalPosition? accurateAimpoint;
        public AimAssist(GlobalPosition point) { accurateAimpoint = point; }
    }
    protected AimAssist aimAssist;
    public ControlsFilter(GlobalPosition point) { aimAssist = new AimAssist(point); }
}
public class HUDBoresightState : HUDWeaponState
{
    private Vector3 gunDirectionRelative;
    private Image boresight, projectedPosition, targetPosition, line;
    private ControlsFilter controlsFilter;
    public void Configure(Vector3 direction, Image sight, Image lead, Image target, Image lineImage, GlobalPosition aim)
    {
        gunDirectionRelative = direction; boresight = sight; projectedPosition = lead;
        targetPosition = target; line = lineImage; controlsFilter = new ControlsFilter(aim);
    }
}
public class HUDTurretState : HUDWeaponState
{
    private HUDTurretCrosshair[] crosshairs;
    public void Configure(HUDTurretCrosshair crosshair) { crosshairs = new[] { crosshair }; }
}
public class HUDTurretCrosshair : MonoBehaviour
{
    public Vector3 WorldPoint;
    public int Refreshes;
    public void Refresh(Camera camera, out Vector3 crosshairPosition)
    {
        Refreshes++;
        crosshairPosition = camera.WorldToScreenPoint(WorldPoint);
        crosshairPosition.z = 0f;
        transform.position = crosshairPosition;
    }
}
public class HUDBombingState : HUDWeaponState
{
    private Vector3 ccipImpactPointSmoothed;
    private GlobalPosition averageTargetPosition;
    private Image ccipPipper, ccipLine, alignmentBar;
    private TextMeshProUGUI ccipFallTime;
    public Vector3 CachedImpact => ccipImpactPointSmoothed;
    public void Configure(Vector3 impact, GlobalPosition target, Image pipper, Image line, Image alignment, TextMeshProUGUI fallTime)
    {
        ccipImpactPointSmoothed = impact; averageTargetPosition = target;
        ccipPipper = pipper; ccipLine = line; alignmentBar = alignment;
        ccipFallTime = fallTime;
    }
}
namespace BoscaliSummer.Core.Diagnostics
{
    public static class PatchGuard
    {
        public static void Report(string name, System.Exception error) => throw new System.Exception(name, error);
    }
}
#endif
