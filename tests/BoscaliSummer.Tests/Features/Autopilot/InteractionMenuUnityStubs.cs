#if UNITY_EDITOR
using UnityEngine;

// Game adapters only, scoped to this fixture's own temp project (never shared with the HUD
// fixture's stubs). The interaction menu's presentation (AceRadialMenuUi, Domain/*) is
// production source; these are just enough to let that file compile and construct off the
// game's own types, which are never exercised here (Update() never runs outside Play mode).
public class Aircraft : MonoBehaviour { }
public static class GameManager
{
    public static Aircraft aircraft;
    public static bool GetLocalAircraft(out Aircraft a) { a = aircraft; return a != null; }
}
public static class GameplayUI { public static bool GameIsPaused; }
public class DynamicMap { public static bool mapMaximized; }
[System.Flags]
public enum CursorFlags { None = 0, GameMenu = 1, Dialogue = 2, Chat = 4, SelectionMenu = 8 }
public static class CursorManager { public static CursorFlags GetFlags() => CursorFlags.None; }
public static class RadialMenuMain { public static bool IsInUse() => false; }
public class MFDScreen : MonoBehaviour { public TMPro.TMP_Text label; }

namespace BoscaliSummer
{
    public sealed class StubLogger
    {
        public void LogInfo(string message) { }
        public void LogWarning(string message) { }
        public void LogError(string message) { }
    }
    public static class Plugin
    {
        public static readonly StubLogger Logger = new StubLogger();
    }
}

namespace BoscaliSummer.Framework.Lifecycle
{
    internal interface ISceneService { void ResetForScene(); }
}

namespace BoscaliSummer.Features.Autopilot.Configuration
{
    internal sealed class KeyHolder { public KeyCode Value = KeyCode.C; }
    internal sealed class AutopilotSettings { public KeyHolder AceRadialKey = new KeyHolder(); }
}

namespace BoscaliSummer.Features.Autopilot.Runtime
{
    using BoscaliSummer.Features.Autopilot.Domain;
    internal static class AceRadialCatalog { public static AceRadialAction Build() => new AceRadialAction("root", string.Empty); }
    internal static class AceRadialInputGuard
    {
        public static void Acquire() { }
        public static void Release() { }
    }
    internal static class CockpitStateProbe { public static void Reset() { } }
}

namespace NuclearOption.UIStyleSystem
{
    using System;
    using System.Collections.Generic;
    public static class ThemeManager
    {
        public static Theme Active => throw new InvalidOperationException();
        public static event Action ThemeGroupChanged;
    }
    public class Theme { public Palette ColorTheme; public TacticalScreenTheme TacScreenTheme; public TacticalScreenTheme HudTheme; }
    public class TacticalScreenTheme { public List<TacticalTextStyle> TextStyles; }
    public class TacticalTextStyle { public TacticalStyle Style; }
    public class TacticalStyle { public TMPro.TMP_FontAsset Font; public Color Color; }
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
#endif
