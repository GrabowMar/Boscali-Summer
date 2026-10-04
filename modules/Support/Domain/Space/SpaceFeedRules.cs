using System;
using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.Support.Domain.Space
{
    /// <summary>Whether the optical bird can image a point under the current sky.</summary>
    internal enum OpticalVerdict : byte { Ok, NightUnavailable, SkyUnknown }

    /// <summary>
    /// Pure SPACE feed policy: how weather and night change the optical and radar footprints, when the optical bird
    /// refuses, and the words. Nothing here reads the game; the host feeds it a <see cref="WeatherViewSample"/>.
    /// </summary>
    internal static class SpaceFeedRules
    {
        /// <summary>Cloud and rain take at most this share of the clear-day optical footprint (full cover halves it).</summary>
        public const float CloudRadiusLoss = 0.5f;

        /// <summary>Optical footprint at night as a share of the clear-day footprint. A tunable starting value, not a spec constant.</summary>
        public const float NightRadiusFactor = 0.35f;

        /// <summary>
        /// The game has no thermal/IR rendering path (verified 2026-10-05: no FLIR/thermal types in Assembly-CSharp, no
        /// thermal shader names in its data; IR exists only for seekers and flares, and NightVision is a gain and tint).
        /// So the optical bird has no night picture and refuses with words. A green tint is not thermal.
        /// </summary>
        public const bool NightOpticalAvailable = false;

        /// <summary>A footprint smaller than this is not a window worth opening.</summary>
        public const float MinimumOpticalRadius = 100f;

        /// <summary>SAR sigma of a host-approved ground contact: a point target, so it carries sidelobes.</summary>
        public const float SarApprovedSigma = 30f;

        /// <summary>
        /// A mirror row says moving or not but carries no velocity: a moving contact is smeared by this nominal radial speed (m/s).
        /// Small on purpose: at orbital range a real vehicle speed would displace it out of the scene.
        /// </summary>
        public const float SarMoverRadial = 2.5f;

        public static float SarContactRadial(bool moving) => moving ? SarMoverRadial : 0f;

        private static float Cover(in WeatherViewSample w)
        {
            // Rain implies cloud even where the cover sample is thin. A bad number never widens the footprint or softens it.
            float cover = float.IsNaN(w.Cover) || float.IsInfinity(w.Cover) ? 0f : Math.Max(0f, Math.Min(1f, w.Cover));
            float rain = float.IsNaN(w.Rain01) || float.IsInfinity(w.Rain01) ? 0f : Math.Max(0f, Math.Min(1f, w.Rain01));
            return Math.Max(cover, rain);
        }

        /// <summary>Footprint share against clear day. RADAR ignores weather and light.</summary>
        public static float RadiusFactor(in WeatherViewSample weather, bool sar)
        {
            if (sar) return 1f;
            float factor = 1f - CloudRadiusLoss * Cover(weather);
            return weather.Night ? factor * NightRadiusFactor : factor;
        }

        public static float OpticalRadius(float baseRadius, in WeatherViewSample weather)
        {
            if (float.IsNaN(baseRadius) || float.IsInfinity(baseRadius) || baseRadius <= 0f) return 0f;
            return Math.Max(MinimumOpticalRadius, baseRadius * RadiusFactor(weather, false));
        }

        /// <summary>0 sharp .. 1 fully overcast. Drives the image haze; it never changes which contacts are revealed.</summary>
        public static float Softness(in WeatherViewSample weather) => Cover(weather);

        /// <summary>No sky state means no clear-sky fiction; night means no picture (see <see cref="NightOpticalAvailable"/>).</summary>
        public static OpticalVerdict Optical(bool haveSample, in WeatherViewSample weather)
        {
            if (!haveSample) return OpticalVerdict.SkyUnknown;
            return weather.Night && !NightOpticalAvailable ? OpticalVerdict.NightUnavailable : OpticalVerdict.Ok;
        }

        public static string OpticalRefusal(OpticalVerdict verdict)
        {
            switch (verdict)
            {
                case OpticalVerdict.NightUnavailable: return "NEGATIVE: OPTICAL NIGHT UNAVAILABLE — USE RADAR";
                case OpticalVerdict.SkyUnknown: return "NEGATIVE: SKY STATE UNKNOWN — USE RADAR";
                default: return "";
            }
        }

        /// <summary>Bracket label for a host-revealed contact: the noisy probable class and its percentage, when one is carried.</summary>
        public static string ContactLabel(ProbableClass probable, int percent)
        {
            string word;
            switch (probable)
            {
                case ProbableClass.Hostile: word = "HOSTILE"; break;
                case ProbableClass.Neutral: word = "NEUTRAL"; break;
                case ProbableClass.Friendly: word = "FRIENDLY"; break;
                default: word = "UNKNOWN"; break;
            }
            return percent > 0 && percent <= 100 ? word + " " + percent + "%" : word;
        }
    }
}
