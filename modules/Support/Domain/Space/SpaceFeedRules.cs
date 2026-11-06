using System;
using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.Support.Domain.Space
{
    /// <summary>Whether the optical bird can image a point under the current sky.</summary>
    internal enum OpticalVerdict : byte { Ok, NightUnavailable, SkyUnknown }

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

    /// <summary>What the DOWNLINK shows: the OPTICAL bird's EO / IR (white-hot) / LOW LIGHT, the RADAR bird's SAR strip, or the KINETIC bird's STRIKE view.</summary>
    internal enum DownlinkMode : byte { Eo, Ir, LowLight, Sar, Strike }

    /// <summary>The feed's own choices and its idle/restore clock. Mission time only: a pause freezes both.</summary>
    internal sealed class FeedDraft
    {
        private float closedAt = float.NaN;

        public int Selected;
        /// <summary>Continuous zoom 1..16 and the look's offset from the window centre (metres east, north).</summary>
        public float Zoom = 1f, PanX, PanZ;
        public DownlinkMode Mode = DownlinkMode.Eo;
        public BirdKind Source => SpaceFeedRules.SourceOf(Mode);

        public void Touch(float now)
        {
            if (!SpaceRules.MissionTime(now)) return;
            closedAt = float.NaN;
        }

        public void Close(float now)
        {
            if (!SpaceRules.MissionTime(now)) return;
            closedAt = now;
        }

        public bool CanRestore(float now) =>
            SpaceRules.MissionTime(now) && SpaceRules.MissionTime(closedAt) && now >= closedAt && now - closedAt < SpaceFeedRules.DraftSeconds;

        /// <summary>Faction change or scene reset: nothing of the old view survives.</summary>
        public void Clear()
        {
            closedAt = float.NaN;
            Selected = 0;
            Zoom = 1f; PanX = PanZ = 0f;
            Mode = DownlinkMode.Eo;
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
            float cover = !float.IsFinite(w.Cover) ? 0f : Math.Clamp(w.Cover, 0f, 1f);
            float rain = !float.IsFinite(w.Rain01) ? 0f : Math.Clamp(w.Rain01, 0f, 1f);
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
            if (!float.IsFinite(baseRadius) || baseRadius <= 0f) return 0f;
            return Math.Max(MinimumOpticalRadius, baseRadius * RadiusFactor(weather, false));
        }

        /// <summary>0 sharp .. 1 fully overcast. Drives the image haze; it never changes which contacts are revealed.</summary>
        public static float Softness(in WeatherViewSample weather) => Cover(weather);

        /// <summary>
        /// No sky state means no clear-sky fiction; night means no picture. The game has no thermal/IR rendering path
        /// (verified 2026-10-05: no FLIR/thermal types in Assembly-CSharp, no thermal shader names in its data; IR exists only
        /// for seekers and flares, and NightVision is a gain and tint), so the optical bird refuses with words.
        /// </summary>
        public static OpticalVerdict Optical(bool haveSample, in WeatherViewSample weather)
        {
            if (!haveSample) return OpticalVerdict.SkyUnknown;
            return weather.Night ? OpticalVerdict.NightUnavailable : OpticalVerdict.Ok;
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

        /// <summary>A closed feed's choices come back for this long (mission seconds).</summary>
        public const float DraftSeconds = 120f;

        /// <summary>A known hostile aircraft closer than this is a bandit warning (metres).</summary>
        public const float BanditWarnMeters = 30000f;

        private static bool TerrainOn(in CockpitThreatSnapshot s) => s.ValidOwnship && float.IsFinite(s.TerrainUrgency) && s.TerrainUrgency > 0f;
        private static bool MissileOn(in CockpitThreatSnapshot s) => s.ValidOwnship && s.MissileWarning;
        private static bool RwrOn(in CockpitThreatSnapshot s) => s.ValidOwnship && s.RwrSpike;
        private static bool BanditOn(in CockpitThreatSnapshot s) =>
            s.ValidOwnship && float.IsFinite(s.NearestKnownBanditMeters) && s.NearestKnownBanditMeters >= 0f && s.NearestKnownBanditMeters < BanditWarnMeters;

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

        /// <summary>Paging for a surface that shows <paramref name="perPage"/> track-file rows (the compact page shows fewer than the station).</summary>
        public static int PageCount(int contacts, int perPage) => contacts <= 0 || perPage <= 0 ? 1 : (contacts + perPage - 1) / perPage;
        public static int ClampPage(int page, int contacts, int perPage) => Math.Max(0, Math.Min(page, PageCount(contacts, perPage) - 1));

        // ---- DOWNLINK: zoom, pan, link ------------------------------------------------------------------------------

        public const float MinZoom = 1f, MaxZoom = 16f;
        /// <summary>The PKT n / 256 counter of a resolving picture.</summary>
        public const int ResolvePackets = 256;

        public static float ClampZoom(float zoom) => float.IsFinite(zoom) ? Math.Clamp(zoom, MinZoom, MaxZoom) : MinZoom;

        /// <summary>Ground width the imager frames: the whole window (2R) at 1X, 2R / zoom closer in, zoom clamped 1..16.</summary>
        public static float Footprint(float zoom, float windowRadius) => 2f * windowRadius / ClampZoom(zoom);

        /// <summary>The old ZOOM button's steps: 1, 2, 4, 8, 16, then back to 1.</summary>
        public static float NextZoomStep(float zoom)
        {
            float z = ClampZoom(zoom);
            for (float s = 2f; s <= MaxZoom; s *= 2f) if (s > z + 1e-3f) return s;
            return MinZoom;
        }

        public static string ZoomWord(float zoom) => ClampZoom(zoom).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "X";

        /// <summary>How far the look may sit off the window centre: the look box (2R / zoom wide) stays inside the R window, so nothing at 1X.</summary>
        public static float PanReach(float zoom, float windowRadius) => Math.Max(0f, windowRadius - Footprint(zoom, windowRadius) * 0.5f);

        /// <summary>A pan offset pulled back along its own line to at most <paramref name="maxRadius"/>; a bad offset or no reach centres it.</summary>
        public static (float X, float Z) ClampPan(float px, float pz, float maxRadius)
        {
            if (!float.IsFinite(px) || !float.IsFinite(pz) || !float.IsFinite(maxRadius) || maxRadius <= 0f) return (0f, 0f);
            float length = (float)Math.Sqrt((double)px * px + (double)pz * pz);
            if (length <= maxRadius) return (px, pz);
            float k = maxRadius / length;
            return (px * k, pz * k);
        }

        /// <summary>Link quality 1..5 from the live pads (two 5, one 3, none 1; never more than are built), one less while the bird is jammed.</summary>
        public static int LinkQuality(int padsLive, int padsTotal, bool jammed)
        {
            int live = Math.Max(0, Math.Min(padsLive, padsTotal));
            int link = live >= 2 ? 5 : live == 1 ? 3 : 1;
            return Math.Max(1, jammed ? link - 1 : link);
        }

        private static int ClampLink(int link) => Math.Clamp(link, 1, 5);

        /// <summary>Seconds a fresh picture takes to resolve: 0.6 at link 5, a quarter second more per lost step.</summary>
        public static float ResolveSeconds(int link) => 0.6f + 0.25f * (5 - ClampLink(link));

        /// <summary>The block size (post pixels) a resolved picture keeps: 1 at link 4-5, 2 at 2-3, 4 at 1.</summary>
        public static int ResolveBands(int link) => 1 << ((5 - ClampLink(link)) / 2);

        /// <summary>What a bird looks at when nothing is picked: 0 its nadir, 1 the nearest airbase in its footprint, 2 the nearest land, 3 the nadir over open water.</summary>
        public const int LookNadir = 0, LookAirbase = 1, LookLand = 2, LookWater = 3;

        /// <summary>
        /// The default look of a bird nobody has pointed (world metres): the parked slots often sit over the theatre's inner sea, which images as a flat
        /// sheet. The airbase nearest the nadir inside the footprint wins; else the nadir when it is land; else the nearest land on rings 1 km apart
        /// (16 bearings from north), pushed 1.5 km further inland when that is still land inside the footprint; else the nadir over open water.
        /// </summary>
        public static (float X, float Z, int Kind) DefaultLook(float nadirX, float nadirZ, float radius, float[] airX, float[] airZ, int airN, Func<float, float, bool> land)
        {
            float best = float.MaxValue, bx = nadirX, bz = nadirZ;
            for (int i = 0; airX != null && airZ != null && i < Math.Min(airN, Math.Min(airX.Length, airZ.Length)); i++)
            {
                float dx = airX[i] - nadirX, dz = airZ[i] - nadirZ, d = dx * dx + dz * dz;
                if (d <= radius * radius && d < best) { best = d; bx = airX[i]; bz = airZ[i]; }
            }
            if (best < float.MaxValue) return (bx, bz, LookAirbase);
            if (land == null || !float.IsFinite(radius) || radius <= 0f) return (nadirX, nadirZ, LookNadir);
            if (land(nadirX, nadirZ)) return (nadirX, nadirZ, LookNadir);
            const float Step = 1000f, Inland = 1500f;
            for (float r = Step; r <= radius; r += Step)
                for (int k = 0; k < 16; k++)
                {
                    double a = k * Math.PI / 8.0;
                    float sx = (float)Math.Sin(a), sz = (float)Math.Cos(a), x = nadirX + sx * r, z = nadirZ + sz * r;
                    if (!land(x, z)) continue;
                    float ix = nadirX + sx * (r + Inland), iz = nadirZ + sz * (r + Inland);
                    return r + Inland <= radius && land(ix, iz) ? (ix, iz, LookLand) : (x, z, LookLand);
                }
            return (nadirX, nadirZ, LookWater);
        }

        /// <summary>The client's acquire sequence: a wide frame of the footprint, a slew into the look, then the resolve (seconds).</summary>
        public const float AcquireWide = 0.3f, AcquireSlew = 0.4f;

        /// <summary>
        /// Where the acquire sequence stands <paramref name="t"/> seconds after it began (every step <paramref name="slow"/> times longer): 0 WIDE, 1 SLEW,
        /// 2 RESOLVE, 3 done; <paramref name="k"/> is the progress through that step (1 when done). A bad clock reads done.
        /// </summary>
        public static int AcquirePhase(float t, int link, float slow, out float k)
        {
            k = 1f;
            float s = float.IsFinite(slow) && slow > 0f ? slow : 1f, wide = AcquireWide * s, slew = AcquireSlew * s, resolve = ResolveSeconds(link) * s;
            if (!float.IsFinite(t) || t < 0f || t >= wide + slew + resolve) return 3;
            if (t < wide) { k = t / wide; return 0; }
            if (t < wide + slew) { k = (t - wide) / slew; return 1; }
            k = (t - wide - slew) / resolve;
            return 2;
        }

        /// <summary>A bird switch, a new lock or a new pick (the resolve restarted on its own) replays the whole sequence; another mode of the same bird or a release only resolves again.</summary>
        public static bool AcquireFull(DownlinkMode was, DownlinkMode now, int wasSelected, int nowSelected, bool restarted) =>
            SourceOf(was) != SourceOf(now) || (nowSelected != wasSelected && nowSelected != 0) || (restarted && was == now && wasSelected == nowSelected);

        public static string ModeWord(DownlinkMode mode) =>
            mode == DownlinkMode.Ir ? "IR" : mode == DownlinkMode.LowLight ? "LOW LIGHT" : mode == DownlinkMode.Sar ? "SAR" : mode == DownlinkMode.Strike ? "STRIKE" : "EO";

        public static BirdKind SourceOf(DownlinkMode mode) =>
            mode == DownlinkMode.Sar ? BirdKind.Radar : mode == DownlinkMode.Strike ? BirdKind.Kinetic : BirdKind.Optical;

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
            if (!float.IsFinite(areaAspect) || !float.IsFinite(textureAspect) || areaAspect <= 0.01f || textureAspect <= 0.01f) return;
            if (areaAspect > textureAspect) { vh = textureAspect / areaAspect; v0 = (1f - vh) * 0.5f; }
            else if (areaAspect < textureAspect) { uw = areaAspect / textureAspect; u0 = (1f - uw) * 0.5f; }
        }

        /// <summary>Picture UV to feed-area 0..1 (origin bottom-left). False when the point lies outside the shown crop.</summary>
        public static bool CropPoint(float u, float v, float u0, float v0, float uw, float vh, out float areaX, out float areaY)
        {
            areaX = (u - u0) / uw;
            areaY = (v - v0) / vh;
            return float.IsFinite(areaX) && float.IsFinite(areaY) && areaX >= 0f && areaX <= 1f && areaY >= 0f && areaY <= 1f;
        }

        /// <summary>The one status line under the picture: live MARK count (a confirm at twelve answers NO CONTACT by design), live pads out of the cap, family.</summary>
        public static string StatusLine(SpaceFeedState s, bool known)
        {
            if (!known || s == null) return "NO HOST LINK · WAIT FOR THE MIRROR";
            if (!s.Active) return "NO SPACE LINK · FACTION HAS NO SATELLITES";
            string uplinks = "PADS " + s.UplinksLive + "/" + Fronts.FrontRules.PadCap;
            if (s.Family == SpaceFamilyState.Dark) return "SPACE OFFLINE · " + uplinks + " · BUILD A PAD";
            return "MARKS " + s.LiveMarks + "/" + SpaceWire.MaxMarks + " · " + uplinks + " · SPACE " + (s.Family == SpaceFamilyState.Degraded ? "DEGRADED" : "NORMAL");
        }

        /// <summary>Seconds until the RADAR bird can scan again, from the host's mission-time deadline mapped onto this clock (0 = ready).</summary>
        public static int RadarReadySeconds(int deadline, float now)
        {
            if (deadline <= 0 || deadline == SpaceWire.RadarUnavailable || !SpaceRules.MissionTime(now)) return 0;
            return Math.Max(0, (int)Math.Ceiling(deadline - (double)now));
        }

        /// <summary>
        /// The one line that replaces the contact targets when the faction has none revealed: the legal next step (a RADAR SCAN, ready
        /// or when) or that SPACE is offline. It never suggests a target; it says how to get one.
        /// </summary>
        public static string NoContactsLine(SpaceFamilyState family, int uplinksLive, int radarSeconds, bool radarUnavailable = false)
        {
            if (family == SpaceFamilyState.Dark || uplinksLive <= 0) return "NO CONTACTS — SPACE OFFLINE · RESTORE A SITE";
            if (radarUnavailable) return "NO CONTACTS — RADAR UNAVAILABLE · TRY SAT CAMERA";
            if (radarSeconds <= 0) return "NO CONTACTS — RADAR SCAN READY";
            return "NO CONTACTS — RADAR READY " + (radarSeconds / 60) + ":" + (radarSeconds % 60).ToString("00");
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
                default: words = "NEGATIVE: NO CONTACT — RE-SELECT A TARGET"; break; // NoContact and a full table read the same
            }
            return replayed ? "EARLIER MARK · " + words : words;
        }

        public static string SendWords(TaskedOutcome outcome, int detail, bool replayed)
        {
            string words = TaskedWords.Of(outcome, detail);
            return replayed ? "EARLIER SEND · " + words : words;
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
