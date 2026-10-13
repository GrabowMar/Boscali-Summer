#if !PUBLIC_RELEASE
using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.QoL.Domain;
using BoscaliSummer.Modules.QoL.Visuals;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.Modules.QoL.Runtime
{
    /// <summary>Explicit client-local hooks for nomodkit night-vision acceptance scenarios.</summary>
    public static class NightVisionAutomation
    {
        /// <summary>Args, all optional: on (bool, goggles), look ("Vanilla" / "WhitePhosphor" / "Fused"), gain (0 AUTO .. 3 HIGH).</summary>
        public static Dictionary<string, object> Set(Dictionary<string, object> args)
        {
            NightVisionLook look = NightVisionLook.Live;
            if (look == null || look.Settings == null) return Failure("NightVisionAutomation", "Set", "no night-vision look in this scene");
            if (Has(args, "look"))
            {
                if (!Enum.TryParse(Text(args, "look"), true, out NvgLook wanted)) return Failure("NightVisionAutomation", "Set", "look must be Vanilla, WhitePhosphor or Fused");
                look.Settings.NightVisionLook.Value = wanted;
            }
            if (Has(args, "gain")) NightVisionLink.GainStep = (int)Number(args, "gain", 0f) & 3;
            if (Has(args, "on") && !GameAccess.SetNightVisionSelected(Bool(args, "on"))) return Failure("NightVisionAutomation", "Set", "the game's night vision is not available");
            return look.Readout();
        }

        public static Dictionary<string, object> Readout(Dictionary<string, object> args)
        {
            NightVisionLook look = NightVisionLook.Live;
            return look != null ? look.Readout() : Failure("NightVisionAutomation", "Readout", "no night-vision look in this scene");
        }
    }
}
#endif
