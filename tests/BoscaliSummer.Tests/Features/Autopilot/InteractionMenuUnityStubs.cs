#if UNITY_EDITOR
using UnityEngine;

// Game adapters only, scoped to this fixture's own temp project (never shared with the HUD fixture's stubs). The v7 menu's presentation
// (MenuPainter, MenuTextPool, MenuPalette) and every Domain/Menu file are production source; the game host (AceRadialMenuUi), the input source and
// the game adapters (MenuGame) are not part of this check. These are just enough for the kit and the palette to compile and construct.
public class MFDScreen : MonoBehaviour { public TMPro.TMP_Text label; }

namespace NuclearOption.UIStyleSystem
{
    using System;
    using System.Collections.Generic;
    /// <summary>
    /// The live game's HUD theme as the cockpit shows it: near-pure green ink (the native HUD text samples about 0, 216, 16), amber and red. The editor's
    /// own ThemeManager throws before a scene exists, which would send <c>AvTheme</c> to its minty fallbacks and make the renders judge a ring the player never sees.
    /// </summary>
    public static class ThemeManager
    {
        private static readonly Theme Theme = new Theme
        {
            ColorTheme = new Palette { AllClear = new Color(0f, 0.9f, 0.08f), Warning = new Color(1f, 0.85f, 0f), Alert = new Color(1f, 0.2f, 0.15f) },
        };

        public static Theme Active => Theme;
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
    // The kit's keyboard guard (AvInput) compiles against these; the check never switches a keyboard.
    public sealed class Keyboard { public bool enabled; }
    public sealed class Controllers { public Keyboard Keyboard => null; }
    public static class ReInput
    {
        public static bool isReady => false;
        public static Controllers controllers => null;
    }
}
#endif
