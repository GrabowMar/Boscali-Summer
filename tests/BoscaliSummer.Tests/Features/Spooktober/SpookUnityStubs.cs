#if UNITY_EDITOR
using BoscaliSummer.Modules.Spooktober.Domain;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
public class MFDScreen : MonoBehaviour { public TextMeshProUGUI label; }
namespace NuclearOption.UIStyleSystem
{
    public static class ThemeManager { public static Theme Active => throw new System.InvalidOperationException(); }
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
namespace BoscaliSummer.Core.Game
{
    internal static class GameAccess { public static bool IsServer() => false; }
}
namespace BoscaliSummer.Modules.Spooktober.Runtime
{
    // Offline presentation fixtures: no real config writes or boss spawning.
    internal static class SpookStore
    {
        public static SpookProgress Current = new SpookProgress();
        public static int Saves;
        public static void Save() => Saves++;
    }
    internal static class SpookBosses
    {
        public static SpookBoss? Active => null;
        public static string Spawn(SpookBoss boss) => "HOST ONLY";
        public static string Dismiss() => "HOST ONLY";
    }
}
#endif
