#if UNITY_2022_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

// Test-only runtime closure. Preserve the native type distinction that matters:
// Aircraft.cockpit is a UnitPart; joystick arrays belong to a Cockpit component.
public class UnitPart : MonoBehaviour { }
public sealed class AeroPart : UnitPart { }
public sealed class Aircraft : MonoBehaviour
{
    public UnitPart cockpit;
    public Pilot[] pilots;
    public bool disabled;
    public readonly ControlInputs inputs = new ControlInputs();
    public ControlInputs GetInputs() => inputs;
}
public sealed class Pilot : MonoBehaviour
{
    private SkinnedMeshRenderer skinnedMeshRenderer;
    private Animator animator;
    public bool dead, ejected;
    public void SetFixtureSource(SkinnedMeshRenderer mesh, Animator animation) { skinnedMeshRenderer = mesh; animator = animation; }
}
public sealed class Cockpit : MonoBehaviour
{
    private class Joystick
    {
        private Transform transform;
        private float range;
        public Joystick(Transform value, float authoredRange) { transform = value; range = authoredRange; }
    }
    private class Throttle
    {
        private Transform transform;
        private float range;
        private bool rotation, motion;
        public Throttle(Transform value, float authoredRange, bool rotates, bool moves)
        { transform = value; range = authoredRange; rotation = rotates; motion = moves; }
    }
    private Joystick[] joysticks;
    private Throttle[] throttles;
    public void SetFixtureControls(Transform stick, Transform throttle, float stickRange = 0, float throttleRange = 0,
        bool throttleRotation = true, bool throttleMotion = false)
    {
        joysticks = stick == null ? Array.Empty<Joystick>() : new[] { new Joystick(stick,stickRange) };
        throttles = throttle == null ? Array.Empty<Throttle>() : new[] { new Throttle(throttle,throttleRange,throttleRotation,throttleMotion) };
    }
}
public sealed class ControlInputs { public float pitch, roll, yaw, throttle; }
public enum CameraMode { cockpit, external }
public sealed class CameraStateManager : MonoBehaviour
{
    public static CameraMode cameraMode;
    public UnityEngine.Object followingUnit;
    public Camera cockpitCamRender, mainCamera;
    public Transform cameraPivot;
    public object currentState, cockpitState;
}
public sealed class LevelInfo : MonoBehaviour { public float GetAmbientLight() => 1f; }

namespace HarmonyLib
{
    public static class AccessTools
    {
        private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        public static FieldInfo Field(Type type, string name) => type?.GetField(name, All);
        public static Type Inner(Type type, string name) => type?.GetNestedType(name, All);
    }
}
namespace BepInEx.Logging
{
    public sealed class ManualLogSource
    {
        public readonly List<string> Messages = new List<string>();
        public void LogWarning(object message) => Messages.Add(message?.ToString());
        public void LogInfo(object message) => Messages.Add(message?.ToString());
        public void LogDebug(object message) => Messages.Add(message?.ToString());
        public void LogError(object message) => Messages.Add(message?.ToString());
    }
}
namespace BoscaliSummer.Core.Game
{
    public static class SceneSingleton<T> where T : class { public static T i; }
    public static class NetworkSceneSingleton<T> where T : class { public static T i; }
}
namespace BoscaliSummer.Core.Services
{
    internal static class ModuleServices
    {
        internal static bool TryGet<T>(out T value) where T : class { value = null; return false; }
    }
}
namespace BoscaliSummer.Modules.Immersion.Configuration
{
    internal sealed class FixtureSetting { public bool Value = true; }
    internal sealed class ImmersionSettings
    {
        internal readonly FixtureSetting Enabled = new FixtureSetting();
        internal readonly FixtureSetting PilotBodyEnabled = new FixtureSetting();
        internal readonly FixtureSetting PilotReflectionEnabled = new FixtureSetting();
        internal readonly FixtureSetting PilotControlMotionEnabled = new FixtureSetting();
        internal readonly FixtureSetting ComfortMotionEnabled = new FixtureSetting();
    }
}
namespace BoscaliSummer.Modules.Immersion.Runtime
{
    internal sealed class FixtureHead { internal Vector3 ForceG = Vector3.up; }
    internal sealed class ImmersionManager
    {
        internal static ImmersionManager Live;
        internal static bool IsCockpitActive;
        internal Quaternion HeadOffset = Quaternion.identity;
        internal readonly FixtureHead Head = new FixtureHead();
    }
}
#endif
