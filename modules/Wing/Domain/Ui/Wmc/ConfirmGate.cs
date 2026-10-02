using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Runtime;
using BoscaliSummer.Modules.Wing.Presentation;
using BoscaliSummer.Modules.Wing.Patches;
using BoscaliSummer.Modules.Wing.Networking;
using BoscaliSummer.Modules.Wing.Configuration;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Core.Util;
using BoscaliSummer.Core.Storage;
namespace BoscaliSummer.Modules.Wing.Domain
{
    /// <summary>A two-press confirmation tied to what it asked about (review P3 I4): the second press confirms only for the
    /// same target within <see cref="Window"/> seconds; anything else asks again.</summary>
    internal sealed class ConfirmGate
    {
        public const float Window = 3f;
        private string armed;
        private float until = float.NegativeInfinity;

        /// <summary>True when this press confirms; false when it (re)arms the question for <paramref name="target"/>.</summary>
        public bool Press(string target, float now)
        {
            if (armed != null && armed == target && now <= until)
            {
                armed = null;
                until = float.NegativeInfinity;
                return true;
            }
            armed = target;
            until = now + Window;
            return false;
        }

        /// <summary>A question is open for <paramref name="target"/> (the label shows "?").</summary>
        public bool IsArmed(string target, float now) => armed != null && armed == target && now <= until;
    }
}
