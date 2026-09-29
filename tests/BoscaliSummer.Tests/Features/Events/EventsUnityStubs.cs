#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using BoscaliSummer.Features.Events.Domain;
using BoscaliSummer.Framework.Contracts;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Game and framework types the EVN panel and field archive touch, stubbed so the real presentation code
// compiles offline. Nothing here has behaviour of its own except what the harness scripts through it.

public class SceneSingleton<T> { public static T i; }
public class DynamicMap : MonoBehaviour { public Canvas maximizedMapCanvas; }
public class VirtualMFD : MonoBehaviour { }
public static class GameplayUI { public static bool AllowPauseKeybind = true; }
public class MFDScreen : MonoBehaviour
{
    public string shortName; public GameObject displayPanel; public bool aircraftOnly; public bool isActive;
    public TextMeshProUGUI label; public Image highlight;
}

public class AircraftInfo { public float maxSpeed, stallSpeed, emptyWeight; }
public class AircraftDefinition
{
    public string unitName, code, description;
    public AircraftInfo aircraftInfo;
    public GameObject unitPrefab;
    public bool IsAllowed(bool flag) => true;
}
public class Encyclopedia
{
    public static Encyclopedia i;
    public List<AircraftDefinition> aircraft = new List<AircraftDefinition>();
}

namespace NuclearOption.Networking
{
    public class MissionManager { public float MissionTime; }
    public static class NetworkSceneSingleton<T> where T : class { public static T i; }
}

namespace BoscaliSummer.Framework.Lifecycle { public interface ISceneService { void ResetForScene(); } }

namespace BoscaliSummer.Framework.Fx
{
    internal static class FxRtPool
    {
        public static bool Own(RenderTexture target) => target != null;
        public static void Disown(RenderTexture target) { }
    }
}

namespace BoscaliSummer.Runtime
{
    internal static class MfdSlots { public const string Events = "EVN"; }
    public static class GameAccess { public static bool MfdAvailable => true; }
    internal static class MfdScreenHost
    {
        public static bool TryHost(string id, bool preferLeft, VirtualMFD mfd, out List<Button> buttons,
            out List<MFDScreen> screens, out int slot, out bool left)
        { buttons = null; screens = null; slot = 0; left = false; return false; }
        public static void Release(string id) { }
    }
    internal static class MfdBezel
    {
        public static MFDScreen FindTemplate(List<MFDScreen> screens) => null;
        public static MFDScreen FindTemplate(VirtualMFD mfd) => null;
        public static bool Bind(VirtualMFD mfd, List<Button> buttons, List<MFDScreen> screens, int slot, bool left, MFDScreen screen) => false;
    }
}

namespace NuclearOption.UIStyleSystem
{
    public static class ThemeManager { public static Theme Active => throw new InvalidOperationException(); }
    public class Theme { public Palette ColorTheme; public TacticalScreenTheme TacScreenTheme; }
    public class TacticalScreenTheme { public List<TacticalTextStyle> TextStyles; }
    public class TacticalTextStyle { public TacticalStyle Style; }
    public class TacticalStyle { public TMP_FontAsset Font; }
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

namespace BoscaliSummer.Features.Events.Runtime
{
    internal readonly struct EventDecisionQuote
    {
        public string Label { get; }
        public int Cost { get; }
        public string Unit { get; }
        public float EffectiveMultiplier { get; }
        public bool Available { get; }
        public string Reason { get; }
        public bool Shared { get; }

        public EventDecisionQuote(string label, int cost, string unit,
            float effectiveMultiplier, bool available, string reason, bool shared)
        {
            Label = label; Cost = cost; Unit = unit;
            EffectiveMultiplier = effectiveMultiplier; Available = available;
            Reason = reason; Shared = shared;
        }
    }

    /// <summary>The slice of the real manager the panel reads; the harness scripts every value.</summary>
    internal sealed class EventsManager : MonoBehaviour
    {
        public bool Available { get; set; } = true;
        public ActiveEventView Current { get; set; }
        public readonly List<ActiveEventView> HistoryList = new List<ActiveEventView>();
        public IReadOnlyList<ActiveEventView> History => HistoryList;
        internal TheaterBalance Balance { get; set; } = TheaterBalance.Unknown;
        internal int SupersFired { get; set; }
        internal bool LocalTargeted { get; set; }
        internal string Signal => "";
        public float SupportCostMultiplier { get; set; } = 1f;
        internal float LocalSupportCooldownMultiplier { get; set; } = 1f;
        internal float LocalBaseMultiplier { get; set; } = 1f;
        internal EventResponseKind LocalResponse { get; set; }
        internal EventResponseKind LocalFactionResponse { get; set; }
        internal readonly List<EventResponseKind> Requests = new List<EventResponseKind>();
        internal Func<EventResponseKind, EventDecisionQuote> QuoteFn;

        internal EventDecisionQuote Quote(EventResponseKind kind) => QuoteFn(kind);
        internal void RequestResponse(EventResponseKind kind) => Requests.Add(kind);

        internal static string ResponseLabel(EventResponseKind kind) =>
            kind == EventResponseKind.Contain ? "CONTAIN" :
            kind == EventResponseKind.Leverage ? "LEVERAGE" :
            kind == EventResponseKind.Treasury ? "TREASURY DIRECTIVE" :
            kind == EventResponseKind.Contract ? "CONTRACT INTELLIGENCE" :
            kind == EventResponseKind.Perk ? "PILOT CHANNEL" : "NONE";
    }
}
#endif
