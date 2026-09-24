using System.Collections.Generic;
using BoscaliSummer.Features.Support.Domain.Orbital;
using BoscaliSummer.Features.Support.Domain;
using BoscaliSummer.Features.Support.Presentation.Viz;
using BoscaliSummer.Features.Support.Presentation.Window;
using BoscaliSummer.Features.Support.Runtime;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Support.Presentation
{
    /// <summary>
    /// SPACE actions: host-authorized abilities under persistent coverage.
    /// Position control opens the station room for explicit sector selection and fitting.
    /// Ability readiness uses the shared presenter on every surface.
    /// </summary>
    internal sealed partial class SupportPanel
    {
        private const float SpaceGroupHeight = 22f;
        private const float SpaceRowMin = 58f;
        private const float SpaceRowMax = 84f;
        private const float SpaceSwitchWidth = 88f;

        private enum SpaceRowKind : byte
        {
            Uplink,
            Strike,
            Rephase
        }

        private sealed class SpaceRow
        {
            public SpaceRowKind Kind;
            public PlatformAbility Ability;
            public SupportActionDefinition Action;
            public RectTransform Host;
            public Image Background;
            public Image Rail;
            public TMP_Text Cost;
            public TMP_Text Readiness;
            public Image Recharge;
            public GuardedSwitch Switch;
            public string LastReadiness;
            public string LastCost;
            public bool Ready;
        }

        private readonly List<SpaceRow> spaceRows = new List<SpaceRow>(8);
        private ArmedBanner spaceBanner;
        private TMP_Text spaceOverheadTitle;
        private float spaceRowHeight, spaceOverheadTop, spaceRowX;
        private int spaceOrder = -1;

        private void ResetSpaceOpsPage()
        {
            spaceRows.Clear();
            spaceBanner = null;
            spaceOverheadTitle = null;
            spaceOrder = -1;
        }

        // ---- Build -----------------------------------------------------------------------------

        private void BuildSpaceOpsPage(RectTransform root, Rect body)
        {
            var strikes = new List<SupportActionDefinition>(4);
            foreach (SupportActionDefinition action in support.Actions)
                if (SupportManager.OrbitalAbility(action.Id).HasValue) strikes.Add(action);

            Rect content = PageFrame(root, body, OpsDomain.Space, "STATION ABILITIES", SubActions, SelectSpaceSub, StationTips, out _);
            spaceBanner = BuildArmedBanner(root, new Rect(content.x, content.y, content.width, BannerRowHeight),
                "OPEN WALL", OpenStationConsole, "Open station control to launch or fit the orbital station.");
            var list = new Rect(content.x, content.y - BannerRowHeight - 8f, content.width, content.height - BannerRowHeight - 8f);

            int rows = 1 + strikes.Count + 1;
            float fixedHeight = SpaceGroupHeight * 2f + SectionGap;
            // Rows share the panel's height (M2): taller at 896, a scroll only when 420 cannot hold them.
            spaceRowHeight = Mathf.Clamp((list.height - fixedHeight) / rows, SpaceRowMin, SpaceRowMax);
            float height = fixedHeight + rows * spaceRowHeight;
            float x, y, width;
            RectTransform parent;
            if (height > list.height + 0.5f) parent = BeginSub(root, list, height, out x, out y, out width);
            else
            {
                parent = root;
                x = list.x;
                y = list.y;
                width = list.width;
            }
            spaceRowX = x;

            spaceOverheadTitle = SingleLine(AvStyled.Label(parent, new Rect(x, y, width, 14f), "STATION CAPABILITIES",
                "section-title"));
            y -= SpaceGroupHeight;
            spaceOverheadTop = y;
            spaceRows.Add(BuildSpaceRow(parent, x, y, width, SpaceRowKind.Uplink, PlatformAbility.Uplink, null));
            y -= spaceRowHeight;
            foreach (SupportActionDefinition action in strikes)
            {
                spaceRows.Add(BuildSpaceRow(parent, x, y, width, SpaceRowKind.Strike, SupportManager.OrbitalAbility(action.Id).Value,
                    action));
                y -= spaceRowHeight;
            }

            y -= SectionGap;
            AvStyled.Label(parent, new Rect(x, y, width, 14f), "POSITION CONTROL · CHOOSE A SECTOR", "section-title");
            y -= SpaceGroupHeight;
            spaceRows.Add(BuildSpaceRow(parent, x, y, width, SpaceRowKind.Rephase, PlatformAbility.Rephase, null));
        }

        private SpaceRow BuildSpaceRow(RectTransform parent, float x, float y, float width, SpaceRowKind kind,
                                       PlatformAbility ability, SupportActionDefinition action)
        {
            AbilityInfo info = PlatformAbilities.Info(ability);
            var row = new SpaceRow { Kind = kind, Ability = ability, Action = action };
            float h = spaceRowHeight - 4f;
            row.Host = Section(parent, "SpaceRow" + spaceRows.Count, new Rect(x, y, width, h));
            RectTransform host = row.Host;
            row.Background = AvKit.Panel(host, new Rect(0f, 0f, width, h), AvTheme.SurfaceInert);
            AvKit.Rule(host, new Rect(4f, 0f, width - 4f, 1f), AvTheme.RailInfo.WithAlpha(0.55f));
            AvKit.Rule(host, new Rect(12f, -h + 1f, width - 24f, 1f), AvTheme.Hairline);
            row.Rail = AvKit.Rule(host, new Rect(0f, 0f, 3f, h), AvTheme.RailInert);

            Color colour = AvTheme.RailInfo;
            int symbol = ability == PlatformAbility.Uplink ? OpsSprites.G.Uplink
                : ability == PlatformAbility.RadarScan ? OpsSprites.G.RadarScan
                : ability == PlatformAbility.Elint ? OpsSprites.G.Elint
                : ability == PlatformAbility.RodStrike ? OpsSprites.G.Rod
                : ability == PlatformAbility.EmpBurst ? OpsSprites.G.Emp : OpsSprites.G.Space;
            Image icon = AvKit.Panel(host, new Rect(11f, -5f, 18f, 18f), colour, OpsSprites.Glyph(symbol));
            icon.raycastTarget = false;
            AvKit.Label(host, info.Code, new Rect(33f, -6f, 34f, 16f), colour, AvTokens.FontMicro, FontStyles.Bold);

            float lane = SpaceSwitchWidth + 14f + (kind == SpaceRowKind.Uplink ? 82f : 0f);
            float textWidth = width - lane;
            string name = action != null ? action.Name : info.Name;
            if (ability == PlatformAbility.EmpBurst) name += " · FRIENDLY FIRE";
            SingleLine(AvStyled.Label(host, new Rect(72f, -6f, textWidth - 72f, 18f), name, "row-name"));
            row.Readiness = SingleLine(AvStyled.Label(host, new Rect(12f, -26f, textWidth - 12f, 14f), "", "row-sub"));
            row.Cost = SingleLine(AvKit.Label(host, "", new Rect(12f, -39f, textWidth - 12f, 12f), AvTheme.Dim,
                AvTokens.FontMicro, FontStyles.Bold));
            row.Recharge = AvKit.ProgressBar(host, new Rect(12f, -h + 3f, width - 24f, 3f), 0f, AvTheme.RailCaution);

            float sh = Mathf.Min(34f, h - 16f);
            row.Switch = BuildGuardedSwitch(host, new Rect(width - SpaceSwitchWidth - 8f, -(h - sh) * 0.5f, SpaceSwitchWidth, sh),
                () => OnSpaceRow(row));
            if (kind != SpaceRowKind.Uplink) return row;
            // The feed is worth aiming before it opens, so the uplink keeps a second control.
            AvStyled.Button(host, new Rect(width - SpaceSwitchWidth - 90f, -(h - 26f) * 0.5f, 76f, 26f), "AIM MAP", "btn", () =>
            {
                support.ArmLocalPick("UPLINK AIM", point => OpenUplink(point));
                nextRefresh = 0f;
            }, AvButtonStyle.Quiet)
                .WithTooltip("Right-click the map where the sensor should look, then the feed opens there.");
            return row;
        }

        private void OnSpaceRow(SpaceRow row)
        {
            switch (row.Kind)
            {
                case SpaceRowKind.Uplink:
                    OpenUplink(null);
                    break;
                case SpaceRowKind.Rephase:
                    OpenStationConsole();
                    break;
                default:
                    if (support.ArmedAction.HasValue && support.ArmedAction.Value == row.Action.Id) support.Disarm();
                    else support.Arm(row.Action.Id);
                    break;
            }
            nextRefresh = 0f;
        }

        private static string SpaceRowTooltip(SpaceRow row, in AbilityInfo info)
        {
            switch (row.Kind)
            {
                case SpaceRowKind.Uplink:
                    return "Open the sensor feed: drag or WASD to slew, wheel to zoom, 1-5 to task at the crosshair.";
                case SpaceRowKind.Rephase:
                    return "Open station control to choose a destination sector or fit propulsion. Relocation uses " +
                           PlatformWords.Whole(info.Fuel) + " fuel.";
                default:
                    return row.Action.Name + " — " + row.Action.Description +
                           " Arm, then right-click the map or fire from the feed's crosshair.";
            }
        }

        // ---- Refresh ---------------------------------------------------------------------------

        private void RefreshSpaceOpsPage(bool bypass, OrbitalPlatform platform, double now, in OrbitClock clock)
        {
            if (spaceBanner == null) return;
            bool station = platform != null && platform.Exists;
            PlatformStats stats = station ? platform.Stats(now) : default;
            OrbitState state = station ? platform.State(now, clock) : default;
            PlatformHold hold = station ? platform.HoldAt(now) : PlatformHold.None;

            string hint, overhead;
            Color tone;
            if (!station)
            {
                hint = "NO STATION · LAUNCH A CORE FROM THE STATION WALL";
                overhead = "CAPABILITIES · NO STATION";
                tone = AvTheme.Dim;
            }
            else if (platform.Brownout)
            {
                hint = "BROWNOUT · ABILITIES REFUSED UNTIL CELLS RECHARGE";
                overhead = "CAPABILITIES · POWER LOW";
                tone = AvTheme.RailDanger;
            }
            else if (hold != PlatformHold.None)
            {
                hint = PlatformWords.Hold(hold) + " · ABILITIES OFFLINE UNTIL T-" + PlatformWords.Clock(platform.CycleStart - now);
                overhead = "ON STATION IN " + PlatformWords.Clock(platform.CycleStart - now);
                tone = AvTheme.RailInfo;
            }
            else
            {
                hint = "ON STATION · ARM, THEN RIGHT-CLICK THE MAP";
                overhead = "PERSISTENT COVERAGE · " + StationKeeping.Name(platform.PositionIndex);
                tone = AvTheme.RailReady;
            }
            PaintArmedBanner(spaceBanner, TabSpace, hint, tone);
            if (spaceOverheadTitle.text != overhead) spaceOverheadTitle.text = overhead;
            spaceOverheadTitle.color = tone == AvTheme.Dim ? AvTheme.TextPrimary : tone;

            foreach (SpaceRow row in spaceRows) PaintSpaceRow(row, bypass, platform, station, stats, now, clock);
            OrderSpaceRows();
        }

        /// <summary>Ready abilities first inside the overhead group; stable otherwise.</summary>
        private void OrderSpaceRows()
        {
            int signature = 0;
            for (int i = 0; i < spaceRows.Count; i++)
                if (spaceRows[i].Ready) signature |= 1 << i;
            if (signature == spaceOrder) return;
            spaceOrder = signature;
            int slot = 0;
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < spaceRows.Count; i++)
                {
                    SpaceRow row = spaceRows[i];
                    if (row.Kind == SpaceRowKind.Rephase || row.Ready != (pass == 0)) continue;
                    row.Host.anchoredPosition = new Vector2(spaceRowX, spaceOverheadTop - slot * spaceRowHeight);
                    slot++;
                }
        }

        private void PaintSpaceRow(SpaceRow row, bool bypass, OrbitalPlatform platform, bool station,
                                   in PlatformStats stats, double now, in OrbitClock clock)
        {
            AbilityInfo info = PlatformAbilities.Info(row.Ability);
            string cost, word, verb;
            Color tone;
            bool enabled, armed = false;
            float recharge = 0f;

            switch (row.Kind)
            {
                case SpaceRowKind.Uplink:
                {
                    PlatformDenial denial = support.PlatformCheck(PlatformAbility.Uplink);
                    bool fitted = station && platform.Fitted(ModuleKind.Imager);
                    cost = "2.0 KW LOAD · REVEALS NOTHING";
                    enabled = fitted;
                    word = denial == PlatformDenial.None ? "READY · FEED LIVE"
                        : PlatformWords.Denial(denial, platform, row.Ability, now, clock);
                    tone = denial == PlatformDenial.None ? AvTheme.RailReady : fitted ? AvTheme.RailInfo : AvTheme.RailInert;
                    verb = "OPEN";
                    break;
                }
                case SpaceRowKind.Rephase:
                {
                    cost = PlatformWords.Whole(info.Fuel) + " FUEL PER RELOCATION";
                    enabled = true;
                    word = !station ? "OPEN STATION CONTROL · LAUNCH A CORE"
                        : platform.FittedOnline(ModuleKind.Propulsion, now) ? "CHOOSE DESTINATION ON THE STATION WALL"
                        : "OPEN STATION CONTROL · FIT PROPULSION TO MOVE";
                    tone = AvTheme.RailInfo;
                    verb = "OPEN";
                    break;
                }
                default:
                {
                    // One presenter for every surface (M5): the MFD rows, this page and the feed's softkeys.
                    AbilityFacts facts = AbilityStatus.For(support, row.Action, bypass);
                    cost = (facts.CostText != "—" ? facts.CostText + " ALLOC · " : "") + PlatformWords.Whole(info.EnergyKj) + " KJ · " +
                           Mathf.RoundToInt(station ? platform.RechargeSeconds(row.Ability, now) : info.RechargeSeconds) + " S RECHARGE";
                    word = facts.Readiness;
                    tone = FactsColour(facts.Tone);
                    enabled = facts.Enabled;
                    armed = facts.Armed;
                    if (station)
                    {
                        float total = platform.RechargeSeconds(row.Ability, now);
                        recharge = total > 0f ? (float)(platform.RechargeRemaining(row.Ability, now) / total) : 0f;
                    }
                    verb = armed ? "ABORT" : "ARM";
                    break;
                }
            }

            if (row.LastCost != cost) row.Cost.text = row.LastCost = cost;
            if (row.LastReadiness != word) row.Readiness.text = row.LastReadiness = word;
            row.Ready = enabled && !armed;
            row.Readiness.color = tone == AvTheme.RailInert ? AvTheme.Dim : tone;
            row.Rail.color = tone;
            row.Background.color = armed ? AvTheme.RailCaution.WithAlpha(0.08f) : AvTheme.SurfaceInert;
            row.Recharge.fillAmount = Mathf.Clamp01(recharge);
            SetSwitch(row.Switch, verb, enabled, armed, SpaceRowTooltip(row, info) + " " + word + ".");
        }

        private static Color FactsColour(AbilityTone tone)
        {
            switch (tone)
            {
                case AbilityTone.Ready: return AvTheme.RailReady;
                case AbilityTone.Armed: return AvTheme.RailCaution;
                case AbilityTone.Pending: return AvTheme.RailInfo;
                case AbilityTone.Danger: return AvTheme.RailDanger;
                default: return AvTheme.RailInert;
            }
        }
    }
}
