using System;
using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.Support.Domain.Space
{
    /// <summary>Whether the optical bird can image a point under the current sky.</summary>
    internal enum OpticalVerdict : byte { Ok, NightUnavailable, SkyUnknown }

    /// <summary>Why an open feed closes. A threat is never a reason: owner ruling 2026-10-03.</summary>
    internal enum FeedCloseReason : byte { None, UserExit, Idle, InvalidOwnship, FocusLost, InvalidOperator }

    /// <summary>The loudest active cockpit warning, critical first: terrain, missile, RWR, bandit.</summary>
    internal enum FeedThreat : byte { None, Rwr, Missile, Bandit, Terrain }

    /// <summary>
    /// What the local cockpit says right now, read once per poll by Support's own native probe. A ground operator has
    /// <see cref="ValidOwnship"/> false and no cockpit warnings; <see cref="ValidOperator"/> is the real local Player and faction.
    /// </summary>
    internal readonly struct CockpitThreatSnapshot
    {
        public readonly bool ValidOwnship, RwrSpike, MissileWarning, ValidOperator;
        public readonly float NearestKnownBanditMeters, TerrainUrgency;

        public CockpitThreatSnapshot(bool validOwnship, bool rwrSpike, bool missileWarning, float nearestKnownBanditMeters,
            float terrainUrgency, bool validOperator = true)
        {
            ValidOwnship = validOwnship; RwrSpike = rwrSpike; MissileWarning = missileWarning;
            NearestKnownBanditMeters = nearestKnownBanditMeters; TerrainUrgency = terrainUrgency; ValidOperator = validOperator;
        }
    }

    /// <summary>The feed's own choices and its idle/restore clock. Mission time only: a pause freezes both.</summary>
    internal sealed class FeedDraft
    {
        private float lastInput = float.NaN, closedAt = float.NaN;

        public int Zoom, Page, Selected;
        public BirdKind Source = BirdKind.Optical;

        public void Touch(float now)
        {
            if (!SpaceRules.MissionTime(now)) return;
            lastInput = now;
            closedAt = float.NaN;
        }

        public bool IsIdle(float now) =>
            SpaceRules.MissionTime(now) && SpaceRules.MissionTime(lastInput) && now - lastInput >= SpaceFeedRules.IdleSeconds;

        public void Close(float now)
        {
            if (!SpaceRules.MissionTime(now)) return;
            closedAt = now;
            lastInput = float.NaN;
        }

        public bool CanRestore(float now) =>
            SpaceRules.MissionTime(now) && SpaceRules.MissionTime(closedAt) && now >= closedAt && now - closedAt < SpaceFeedRules.DraftSeconds;

        /// <summary>Faction change or scene reset: nothing of the old view survives.</summary>
        public void Clear()
        {
            lastInput = closedAt = float.NaN;
            Zoom = Page = Selected = 0;
            Source = BirdKind.Optical;
        }
    }

    /// <summary>A TASKED card's lifecycle as the feed may show it. Stale beats everything: an expired card has no fire button.</summary>
    internal enum PostStatus : byte { Open, Launching, Stale }

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

        // ---- Safety (owner ruling: always accessible, stays open on threats) ---------------------------------

        /// <summary>No real input for this long closes the feed (mission seconds).</summary>
        public const float IdleSeconds = 8f;

        /// <summary>A closed feed's choices come back for this long (mission seconds).</summary>
        public const float DraftSeconds = 120f;

        /// <summary>A known hostile aircraft closer than this is a bandit warning (metres).</summary>
        public const float BanditWarnMeters = 30000f;

        /// <summary>
        /// Close reasons, never a threat. The operator must exist; an airborne-open window also needs its ownship; focus loss and
        /// eight idle mission seconds close. Ground entry (<paramref name="requireOwnship"/> false) needs no aircraft.
        /// </summary>
        public static FeedCloseReason CloseReason(in CockpitThreatSnapshot s, bool focused, float idleSeconds, bool requireOwnship = true)
        {
            if (!s.ValidOperator) return FeedCloseReason.InvalidOperator;
            if (requireOwnship && !s.ValidOwnship) return FeedCloseReason.InvalidOwnship;
            if (!focused) return FeedCloseReason.FocusLost;
            if (SpaceRules.Finite(idleSeconds) && idleSeconds >= IdleSeconds) return FeedCloseReason.Idle;
            return FeedCloseReason.None;
        }

        private static bool TerrainOn(in CockpitThreatSnapshot s) => s.ValidOwnship && SpaceRules.Finite(s.TerrainUrgency) && s.TerrainUrgency > 0f;
        private static bool MissileOn(in CockpitThreatSnapshot s) => s.ValidOwnship && s.MissileWarning;
        private static bool RwrOn(in CockpitThreatSnapshot s) => s.ValidOwnship && s.RwrSpike;
        private static bool BanditOn(in CockpitThreatSnapshot s) =>
            s.ValidOwnship && SpaceRules.Finite(s.NearestKnownBanditMeters) && s.NearestKnownBanditMeters >= 0f && s.NearestKnownBanditMeters < BanditWarnMeters;

        /// <summary>The top critical warning: terrain, then missile, then RWR, then bandit. A ground operator has none.</summary>
        public static FeedThreat Threat(in CockpitThreatSnapshot s)
        {
            if (TerrainOn(s)) return FeedThreat.Terrain;
            if (MissileOn(s)) return FeedThreat.Missile;
            if (RwrOn(s)) return FeedThreat.Rwr;
            if (BanditOn(s)) return FeedThreat.Bandit;
            return FeedThreat.None;
        }

        public static int ThreatCount(in CockpitThreatSnapshot s) =>
            (TerrainOn(s) ? 1 : 0) + (MissileOn(s) ? 1 : 0) + (RwrOn(s) ? 1 : 0) + (BanditOn(s) ? 1 : 0);

        /// <summary>
        /// Every active condition as its own word in priority order, so a lower warning is never hidden behind a higher one.
        /// Empty when nothing is active.
        /// </summary>
        public static string ThreatStrip(in CockpitThreatSnapshot s)
        {
            string text = "";
            if (TerrainOn(s)) text = "TERRAIN";
            if (MissileOn(s)) text = Join(text, "MISSILE WARNING");
            if (RwrOn(s)) text = Join(text, "RWR SPIKE");
            if (BanditOn(s))
                text = Join(text, "BANDIT " + Math.Max(1, (int)Math.Round(s.NearestKnownBanditMeters / 1000f)) + " KM");
            return text;
        }

        private static string Join(string a, string b) => a.Length == 0 ? b : a + " · " + b;

        // ---- Layout rules the feed panel shares with its tests -----------------------------------------------

        /// <summary>At most six contact targets show at once; a bounded page switch reaches the rest. Nothing scrolls.</summary>
        public const int ContactsPerPage = 6;
        public const int ZoomSteps = 3;

        public static int PageCount(int contacts) => contacts <= 0 ? 1 : (contacts + ContactsPerPage - 1) / ContactsPerPage;
        public static int ClampPage(int page, int contacts) => Math.Max(0, Math.Min(page, PageCount(contacts) - 1));

        /// <summary>Ground width the imager frames at a zoom step: the whole window, half, a quarter.</summary>
        public static float Footprint(int step, float windowRadius)
        {
            int z = Math.Max(0, Math.Min(step, ZoomSteps - 1));
            return 2f * windowRadius / (1 << z);
        }

        public static string ZoomWord(int step) => step <= 0 ? "WIDE" : step == 1 ? "MID" : "CLOSE";

        public static PostStatus PostStatusOf(in FeedPost post, float now)
        {
            if (!SpaceRules.MissionTime(now) || !SpaceRules.MissionTime(post.Expires) || post.Expires <= now) return PostStatus.Stale;
            return post.Launching ? PostStatus.Launching : PostStatus.Open;
        }

        /// <summary>
        /// The part of a picture a feed area shows when the picture fills it without stretching (centre crop): the sub-rectangle in
        /// picture UV (origin bottom-left, like a RawImage uvRect). A bad aspect shows everything.
        /// </summary>
        public static void CoverCrop(float areaAspect, float textureAspect, out float u0, out float v0, out float uw, out float vh)
        {
            u0 = 0f; v0 = 0f; uw = 1f; vh = 1f;
            if (!SpaceRules.Finite(areaAspect) || !SpaceRules.Finite(textureAspect) || areaAspect <= 0.01f || textureAspect <= 0.01f) return;
            if (areaAspect > textureAspect) { vh = textureAspect / areaAspect; v0 = (1f - vh) * 0.5f; }
            else if (areaAspect < textureAspect) { uw = areaAspect / textureAspect; u0 = (1f - uw) * 0.5f; }
        }

        /// <summary>Picture UV to feed-area 0..1 (origin bottom-left). False when the point lies outside the shown crop.</summary>
        public static bool CropPoint(float u, float v, float u0, float v0, float uw, float vh, out float areaX, out float areaY)
        {
            areaX = (u - u0) / uw;
            areaY = (v - v0) / vh;
            return SpaceRules.Finite(areaX) && SpaceRules.Finite(areaY) && areaX >= 0f && areaX <= 1f && areaY >= 0f && areaY <= 1f;
        }

        /// <summary>The one status line under the picture: live MARK count (a confirm at twelve answers NO CONTACT by design), uplinks, family.</summary>
        public static string StatusLine(SpaceFeedState s, bool known)
        {
            if (!known || s == null) return "NO HOST LINK · WAIT FOR THE MIRROR";
            if (!s.Active) return "NO SPACE LINK · FACTION HAS NO SATELLITES";
            string uplinks = "UPLINKS " + s.UplinksLive + "/" + s.UplinksTotal;
            if (s.Family == SpaceFamilyState.Dark) return "SPACE OFFLINE · " + uplinks + " · RESTORE A SITE";
            return "MARKS " + s.LiveMarks + "/" + SpaceWire.MaxMarks + " · " + uplinks + " · SPACE " + (s.Family == SpaceFamilyState.Degraded ? "DEGRADED" : "NORMAL");
        }

        /// <summary>The words for a MARK verdict. A replay returns the historical verdict, so it is worded as history.</summary>
        public static string MarkWords(MarkVerdict verdict, bool replayed)
        {
            string words;
            switch (verdict)
            {
                case MarkVerdict.Confirmed: words = "MARK CONFIRMED — READY TO SEND"; break;
                case MarkVerdict.Neutral: words = "MARK: NEUTRAL — NOT A TARGET"; break;
                case MarkVerdict.Friendly: words = "NEGATIVE: FRIENDLY — DO NOT STRIKE"; break;
                case MarkVerdict.Decoy: words = "MARK: DECOY — NOT A REAL TARGET"; break;
                case MarkVerdict.RateLimited: words = "NEGATIVE: TOO MANY MARKS — WAIT A MOMENT"; break;
                default: words = "NEGATIVE: NO CONTACT — IT MAY HAVE EXPIRED"; break; // NoContact and a full table read the same
            }
            return replayed ? "EARLIER MARK · " + words : words;
        }

        public static string SendWords(TaskedOutcome outcome, int detail, bool replayed)
        {
            string words = TaskedWords.Of(outcome, detail);
            return replayed ? "EARLIER SEND · " + words : words;
        }

        /// <summary>The TASKED section caption: how many are posted and how many do not fit the board.</summary>
        public static string CaptionOf(int posted, int capacity) =>
            posted <= 0 ? "TASKED CALLS · NONE" : posted <= capacity ? "TASKED CALLS · " + posted : "TASKED CALLS · " + posted + " (+" + (posted - capacity) + " MORE)";

        public static bool CanFire(PostStatus status) => status == PostStatus.Open;

        public static string PostWord(PostStatus status) =>
            status == PostStatus.Stale ? "STALE" : status == PostStatus.Launching ? "LAUNCHING" : "READY";

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
