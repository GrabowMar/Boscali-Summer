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
    /// <summary>Active throws, exactly like the game's theme before a scene exists: MenuPalette falls back to the base game's green, amber and red.</summary>
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
