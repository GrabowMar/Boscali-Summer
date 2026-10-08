// Explicit fixture: exercises state ownership/global transforms without claiming native Play-mode acceptance.
namespace UnityEngine
{
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => new(0, 0, 0);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x-b.x,a.y-b.y,a.z-b.z);
        public float sqrMagnitude => x*x+y*y+z*z;
    }
    public struct Quaternion
    {
        public float x, y, z, w;
        public Quaternion(float x,float y,float z,float w) { this.x=x;this.y=y;this.z=z;this.w=w; }
        public static Quaternion identity => new(0,0,0,1);
        public static float Dot(Quaternion a, Quaternion b) => a.x*b.x+a.y*b.y+a.z*b.z+a.w*b.w;
    }
    public class Transform
    {
        public Vector3 position;
        public Quaternion rotation = Quaternion.identity;
        public void SetParent(Transform parent,bool worldPositionStays=false) { }
        public void SetPositionAndRotation(Vector3 p, Quaternion r) { position=p;rotation=r; }
    }
    public class Camera { public float fieldOfView = 45; }
    public static class Time { public static float timeScale = 1, fixedDeltaTime=.02f,unscaledTime; }
    public class GameObject { }
    public class MonoBehaviour
    {
        public Transform transform=new();public GameObject gameObject=new();public bool isActiveAndEnabled=true;
        protected static void Destroy(GameObject go) { }
    }
    public static class Application { public static bool isFocused=true,isBatchMode; }
    public enum KeyCode { None,Escape }
    public static class Input { public static bool GetKeyDown(KeyCode key)=>false; }
    public static class Mathf { public static float Clamp(float x,float lo,float hi)=>Math.Clamp(x,lo,hi);public static float Min(float a,float b)=>Math.Min(a,b); }
    public static class Screen { public static int width=1920,height=1080; }
    public static class Debug { public static void LogWarning(string x) { } }
}
public static class Datum { public static UnityEngine.Vector3 offset; }
public struct GlobalPosition
{
    public float x,y,z;
    public GlobalPosition(float x,float y,float z) { this.x=x;this.y=y;this.z=z; }
    public UnityEngine.Vector3 ToLocalPosition() => new(x-Datum.offset.x,y-Datum.offset.y,z-Datum.offset.z);
}
public abstract class CameraBaseState
{
    public abstract void EnterState(CameraStateManager cam);
    public abstract void LeaveState(CameraStateManager cam);
    public abstract void UpdateState(CameraStateManager cam);
    public abstract void FixedUpdateState(CameraStateManager cam);
}
public sealed class CameraStateManager
{
    public CameraBaseState currentState;
    public CameraBaseState freeState = new FreeState();
    public UnityEngine.Transform transform = new();
    public UnityEngine.Camera mainCamera = new();
    public float desiredFOV = 45;
    public bool allowInputs = true;
    public UnityEngine.Vector3 cameraVelocity;
    public Unit previousFollowingUnit;
    public Unit followingUnit;
    public CameraStateManager() { currentState=freeState; }
    public void SwitchState(CameraBaseState state)
    { currentState?.LeaveState(this); currentState=state; desiredFOV=mainCamera.fieldOfView; state.EnterState(this); }
    public void GetCameraPosition(out GlobalPosition p, out UnityEngine.Quaternion r)
    { var pos=transform.position; p=new(pos.x+Datum.offset.x,pos.y+Datum.offset.y,pos.z+Datum.offset.z); r=transform.rotation; }
    public void SetFollowingUnit(Unit unit) {previousFollowingUnit=followingUnit;followingUnit=unit;SwitchState(freeState);}
    sealed class FreeState : CameraBaseState
    {
        public override void EnterState(CameraStateManager cam)
        { if (cam.previousFollowingUnit != null) cam.transform.rotation = new UnityEngine.Quaternion(0, 1, 0, 0); }
        public override void LeaveState(CameraStateManager cam) { }
        public override void UpdateState(CameraStateManager cam) { }
        public override void FixedUpdateState(CameraStateManager cam) { }
    }
}
public class Unit:UnityEngine.MonoBehaviour { public bool disabled;public string NetworkUniqueName;public UnitDefinition definition=new(); }
public sealed class UnitDefinition { public string unitName="UNIT"; }
public sealed class Aircraft:Unit { }
public static class UnitRegistry { public static List<Unit> allUnits=new(); }
public static class GameManager
{
    public static GameState gameState=GameState.SinglePlayer;public static Aircraft ownship;
    public static bool GetLocalAircraft(out Aircraft aircraft) { aircraft=ownship;return aircraft!=null; }
}
public enum GameState { SinglePlayer,Multiplayer }
public class MissionManager { public static bool IsRunning=true;public static Mission CurrentMission=new();public float MissionTime=100; }
public class Mission { public string Name="m"; }
public class SceneSingleton<T> { public static T i; }
public class NetworkSceneSingleton<T> { public static T i; }
public static class GameplayUI { public static bool AllowPauseKeybind=true; }
public static class NativeExtensions
{
    public static GlobalPosition ToGlobalPosition(this UnityEngine.Vector3 p)=>new(p.x+Datum.offset.x,p.y+Datum.offset.y,p.z+Datum.offset.z);
}
namespace UnityEngine.SceneManagement { public struct Scene { public string name; }public static class SceneManager { public static Scene GetActiveScene()=>new(){name="s"}; } }
namespace NuclearOption.MissionEditorScripts { public static class InputFieldChecker { public static bool InsideInputField; } }
namespace BoscaliSummer.Core.Game { public static class GameAccess { public static bool Server=true;public static bool IsServer()=>Server; } }
namespace BoscaliSummer { public static class Plugin { public const string PluginVersion="fixture"; } }
namespace BepInEx { public static class Paths { public static string ConfigPath=Path.Combine(Path.GetTempPath(),"CinematicApi-"+Guid.NewGuid().ToString("N")); } }
namespace BepInEx.Logging { public class ManualLogSource { public void LogWarning(string x) { } } }
namespace BepInEx.Configuration
{
    public sealed class ConfigEntry<T> { public T Value;public ConfigEntry(T value) { Value=value; } }
    public sealed class ConfigFile { public ConfigEntry<T> Bind<T>(string section,string key,T value,string help)=>new(value); }
    public struct KeyboardShortcut { public KeyboardShortcut(UnityEngine.KeyCode key) { }public bool IsDown()=>false; }
}
namespace BoscaliSummer.Modules.Cinematography.Runtime { internal sealed class CameraPathClearance { internal void Reset() { }internal bool Clear(UnityEngine.Vector3 p)=>true; } }
namespace BoscaliSummer.Modules.Cinematography.Presentation
{
    internal class CinematicConsole:UnityEngine.MonoBehaviour { internal bool IsOpen;internal static CinematicConsole Create(object d)=>new();internal void Show() {IsOpen=true;}internal void Close(){IsOpen=false;}internal void SwitchPage(int p) { } }
    internal class CinematicOverlay
    {
        internal static int ActiveCount;
        internal static CinematicOverlay Create(UnityEngine.Transform t) { ActiveCount++;return new(); }
        internal void Paint(BoscaliSummer.Modules.Cinematography.Domain.ShotPlan plan,float seconds) { }
        internal void Release() { ActiveCount--; }
    }
}
