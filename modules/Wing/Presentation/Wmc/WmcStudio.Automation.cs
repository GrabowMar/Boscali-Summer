#if !PUBLIC_RELEASE
using System;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    // SQUADRON › STUDIO for scenarios (Automation.Wmc sq_* arguments): pick a pilot, start a draft, type through the page's own commit
    // path, step the look, save, and report the page as numbers (the harness checks result_above / result_below only).
    internal sealed partial class WmcStudio
    {
        /// <summary>Open a pilot as a row press would, without the unsaved-edits ask: a callsign, or "live0" (this mission's first unsaved
        /// pilot). False when nobody matches.</summary>
        public bool Pick(string who)
        {
            if (string.IsNullOrEmpty(who)) return false;
            Scan(last);
            Entry e = null;
            if (string.Equals(who, "live0", StringComparison.OrdinalIgnoreCase))
            {
                foreach (Entry x in entries)
                    if (x.Saved == null && x.Live != null)
                    {
                        e = x;
                        break;
                    }
            }
            else e = Find(who);
            if (e == null) return false;
            WmcNameField.BlurAny();
            Select(e);
            return true;
        }
    }
}
#endif
