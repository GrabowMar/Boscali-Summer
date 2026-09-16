using System.Collections.Generic;
using BoscaliSummer.Features.Support.Domain;
using BoscaliSummer.Features.Support.Domain.Orbital;
using BoscaliSummer.Features.Support.Runtime;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Support.Presentation
{
    /// <summary>
    /// SPACE › PLATFORM — fly the station. A banner says where it is and how long until that
    /// changes; annunciators light the eight things that can go wrong; gauges show energy,
    /// fuel, rods and mass; a live schematic shows which modules are up; ability cards say in
    /// words what each ability needs next, and the voice loop keeps the story.
    /// </summary>
    internal sealed partial class SupportPanel
    {
        private const float BannerHeight = 104f;
        private const float TileHeight = 34f;
        private const float ResourceHeight = 58f;
        private const float MiniCellHeight = 24f;
        private const float CardHeight = 72f;
        private const float CardGap = 6f;

        private static readonly string[] TileKeys =
            { "POWER", "THERMAL", "FUEL", "LINK", "CREW", "DEBRIS", "ORBIT", "MODULES" };

        private enum CardKind : byte
        {
            Uplink,
            Action,
            Rephase,
            Raise,
            Lower
        }

        private sealed class AbilityCard
        {
            public CardKind Kind;
            public PlatformAbility Ability;
            public SupportActionDefinition Action;
            public Image Rail;
            public TMP_Text Status;
            public TMP_Text Cost;
            public AvButton Button;
            public Image Recharge;
            public Tone LastTone = (Tone)255;
            public string LastStatus;
        }

        private Image bannerRail;
        private TMP_Text bannerBand, bannerWord, bannerClockKey, bannerClock, bannerNote;
        private Image bannerPass;
        private AvButton bannerPlanner;
        private readonly Tile[] tiles = new Tile[8];
        private TMP_Text resourceNote, rodsLabel, massLabel;
        private Gauge energyGauge, fuelGauge;
        private readonly Image[] rodPips = new Image[6];
        private GridView miniGrid;
        private readonly List<AbilityCard> cards = new List<AbilityCard>(8);
        private TMP_Text cardsNote;
        private TMP_Text[] platformLoop;

        private void ResetPlatformPage()
        {
            bannerRail = bannerPass = null;
            bannerBand = bannerWord = bannerClockKey = bannerClock = bannerNote = null;
            bannerPlanner = null;
            for (int i = 0; i < tiles.Length; i++) tiles[i] = null;
            for (int i = 0; i < rodPips.Length; i++) rodPips[i] = null;
            resourceNote = rodsLabel = massLabel = null;
            energyGauge = fuelGauge = null;
            miniGrid = null;
            cards.Clear();
            cardsNote = null;
            platformLoop = null;
        }

        private void BuildPlatformPage(RectTransform root, Rect body)
        {
            var orbital = new List<SupportActionDefinition>(4);
            foreach (SupportActionDefinition action in support.Actions)
                if (SupportManager.OrbitalAbility(action.Id).HasValue) orbital.Add(action);
            int cardCount = 1 + orbital.Count + 3;
            int cardRows = (cardCount + 1) / 2;

            float gridHeight = OrbitalPlatform.Rows * MiniCellHeight + (OrbitalPlatform.Rows - 1) * 6f;
            float height = BannerHeight + SectionGap +
                           HeaderHeight + TileHeight * 2f + 4f + SectionGap +
                           HeaderHeight + ResourceHeight + SectionGap +
                           HeaderHeight + gridHeight + SectionGap +
                           HeaderHeight + cardRows * (CardHeight + CardGap) + SectionGap +
                           HeaderHeight + LoopLines * LoopPitch + SectionGap;
            RectTransform parent = BeginSub(root, body, height, out float x, out float y, out float width);

            BuildBanner(parent, x, y, width);
            y -= BannerHeight + SectionGap;

            Header(parent, x, ref y, width, "ANNUNCIATORS", "WORDS FIRST · COLOUR REPEATS");
            float tileWidth = (width - 12f) / 4f;
            for (int i = 0; i < tiles.Length; i++)
            {
                float tx = x + (i % 4) * (tileWidth + 4f);
                float ty = y - (i / 4) * (TileHeight + 4f);
                tiles[i] = BuildTile(parent, new Rect(tx, ty, tileWidth, TileHeight), TileKeys[i]);
            }
            y -= TileHeight * 2f + 4f + SectionGap;

            resourceNote = Header(parent, x, ref y, width, "RESOURCES", "");
            float half = (width - 12f) * 0.5f;
            energyGauge = BuildGauge(parent, x, y, half, "ENERGY", AvTheme.RailReady);
            fuelGauge = BuildGauge(parent, x + half + 12f, y, half, "FUEL", MobilityColour);
            float rodsY = y - 42f;
            rodsLabel = SingleLine(AvStyled.Label(parent, new Rect(x, rodsY, 80f, 14f), "RODS", "kv-key"));
            for (int i = 0; i < rodPips.Length; i++)
                rodPips[i] = AvKit.Panel(parent, new Rect(x + 84f + i * 14f, rodsY - 3f, 10f, 10f), Color.clear);
            massLabel = AvStyled.Label(parent, new Rect(x + half + 12f, rodsY, half, 14f), "", "kv-value",
                align: TextAlignmentOptions.MidlineRight);
            y -= ResourceHeight + SectionGap;

            Header(parent, x, ref y, width, "STATION", "LIVE SCHEMATIC · 5×3 TRUSS");
            miniGrid = BuildGrid(parent, x, y, width, MiniCellHeight, false, null);
            y -= gridHeight + SectionGap;

            cardsNote = Header(parent, x, ref y, width, "ABILITIES", "");
            float cardWidth = (width - CardGap) * 0.5f;
            int index = 0;
            AddCard(parent, x, y, cardWidth, index++, CardKind.Uplink, PlatformAbility.Uplink, null);
            foreach (SupportActionDefinition action in orbital)
                AddCard(parent, x, y, cardWidth, index++, CardKind.Action, SupportManager.OrbitalAbility(action.Id).Value, action);
            AddCard(parent, x, y, cardWidth, index++, CardKind.Rephase, PlatformAbility.Rephase, null);
            AddCard(parent, x, y, cardWidth, index++, CardKind.Raise, PlatformAbility.OrbitShift, null);
            AddCard(parent, x, y, cardWidth, index, CardKind.Lower, PlatformAbility.OrbitShift, null);
            y -= cardRows * (CardHeight + CardGap) + SectionGap;

            platformLoop = BuildLoop(parent, x, ref y, width);
        }

        private void BuildBanner(RectTransform parent, float x, float y, float width)
        {
            var area = new Rect(x, y, width, BannerHeight);
            bannerRail = AvKit.TacticalCard(parent, area, AvTheme.RailInert).Rail;
            AvKit.Label(parent, OrbitalPlatform.Callsign, new Rect(x + 12f, y - 8f, 180f, 20f), AvTheme.TextPrimary,
                AvTokens.FontTitle, FontStyles.Bold).characterSpacing = 12f;
            bannerBand = AvKit.Label(parent, "", new Rect(x + width - 232f, y - 10f, 220f, 16f), AvTheme.Dim,
                AvTokens.FontSmall, FontStyles.Bold, TextAlignmentOptions.Right);
            bannerWord = AvKit.Label(parent, "", new Rect(x + 12f, y - 32f, width * 0.56f, 30f), AvTheme.Dim, 22f,
                FontStyles.Bold);
            bannerWord.enableAutoSizing = true;
            bannerWord.fontSizeMin = AvTokens.FontLead;
            bannerWord.fontSizeMax = 22f;
            bannerClockKey = AvKit.Label(parent, "", new Rect(x + width - 172f, y - 32f, 160f, 12f), AvTheme.Dim,
                AvTokens.FontMicro, FontStyles.Bold, TextAlignmentOptions.Right);
            bannerClock = AvKit.Label(parent, "", new Rect(x + width - 172f, y - 44f, 160f, 28f), AvTheme.RailCaution, 26f,
                FontStyles.Bold, TextAlignmentOptions.Right);
            bannerPlanner = AvStyled.Button(parent, new Rect(x + width - 150f, y - 38f, 138f, 26f), "OPEN PLANNER", "btn",
                () => SelectSpaceSub(SubPlanner), AvButtonStyle.Primary)
                .WithTooltip("Design the station and launch its core in MISSION PLANNER.");
            bannerPass = AvKit.ProgressBar(parent, new Rect(x + 12f, y - 78f, width - 24f, 6f), 0f, AvTheme.RailReady);
            bannerNote = SingleLine(AvStyled.Label(parent, new Rect(x + 12f, y - 88f, width - 24f, 14f), "", "row-sub"));
        }

        private void AddCard(RectTransform parent, float x, float y, float cardWidth, int index, CardKind kind,
                             PlatformAbility ability, SupportActionDefinition action)
        {
            float cx = x + (index % 2) * (cardWidth + CardGap);
            float cy = y - (index / 2) * (CardHeight + CardGap);
            var area = new Rect(cx, cy, cardWidth, CardHeight);
            Image rail = AvKit.TacticalCard(parent, area, AvTheme.RailInert).Rail;
            AbilityInfo info = PlatformAbilities.Info(ability);
            Color colour = kind == CardKind.Action && (ability == PlatformAbility.RodStrike || ability == PlatformAbility.EmpBurst)
                ? CategoryColour(ModuleCategory.Weapon)
                : kind == CardKind.Uplink || kind == CardKind.Action ? CategoryColour(ModuleCategory.Sensor)
                : MobilityColour;
            string code = kind == CardKind.Raise ? "UP" : kind == CardKind.Lower ? "DN" : info.Code;
            string name = kind == CardKind.Raise ? "RAISE ORBIT" : kind == CardKind.Lower ? "LOWER ORBIT"
                : action != null ? action.Name : info.Name;
            AvKit.Chip(parent, code, new Rect(cx + 10f, cy - 8f, 32f, 16f), colour, colour);
            SingleLine(AvStyled.Label(parent, new Rect(cx + 48f, cy - 7f, cardWidth - 56f, 18f), name, "row-name"));

            var card = new AbilityCard
            {
                Kind = kind,
                Ability = ability,
                Action = action,
                Rail = rail,
                Status = SingleLine(AvStyled.Label(parent, new Rect(cx + 10f, cy - 29f, cardWidth - 20f, 14f), "", "row-sub")),
                Cost = SingleLine(AvKit.Label(parent, "", new Rect(cx + 10f, cy - 47f, cardWidth - 92f, 14f), AvTheme.Dim,
                    AvTokens.FontMicro)),
                Recharge = AvKit.ProgressBar(parent, new Rect(cx + 10f, cy - 63f, cardWidth - 20f, 4f), 0f, AvTheme.RailCaution)
            };
            float buttonWidth = kind == CardKind.Uplink ? 38f : 72f;
            card.Button = AvStyled.Button(parent, new Rect(cx + cardWidth - buttonWidth - 10f, cy - 42f, buttonWidth, 20f),
                kind == CardKind.Uplink ? "OPEN" : kind == CardKind.Action ? "ARM" : "BURN", "btn",
                () => OnCard(card), AvButtonStyle.Primary);
            if (kind == CardKind.Uplink)
            {
                AvStyled.Button(parent, new Rect(cx + cardWidth - 92f, cy - 42f, 40f, 20f), "MAP", "btn", () =>
                {
                    support.ArmLocalPick("UPLINK AIM", point => OpenUplink(point));
                    nextRefresh = 0f;
                }, AvButtonStyle.Quiet).WithTooltip("Right-click the map where the sensor should look, then the feed opens there.");
                card.Cost.rectTransform.sizeDelta = new Vector2(cardWidth - 112f, 14f);
            }
            cards.Add(card);
        }

        private void OnCard(AbilityCard card)
        {
            OrbitalPlatform platform = support.LocalPlatform;
            switch (card.Kind)
            {
                case CardKind.Uplink:
                    OpenUplink(null);
                    break;
                case CardKind.Action:
                    support.Arm(card.Action.Id);
                    break;
                case CardKind.Rephase:
                    Log("FLIGHT · PHASING BURN REQUESTED");
                    support.RequestRephase();
                    break;
                default:
                    if (platform == null) return;
                    int target = platform.Regime + (card.Kind == CardKind.Raise ? 1 : -1);
                    if (!OrbitRegimes.Valid(target)) return;
                    Log("FLIGHT · " + (card.Kind == CardKind.Raise ? "RAISE" : "LOWER") + " TO " +
                        OrbitRegimes.Get(target).Code + " REQUESTED");
                    support.RequestOrbitShift((byte)target);
                    break;
            }
            nextRefresh = 0f;
        }

        // ---- Refresh -------------------------------------------------------------------------------

        private void RefreshPlatformPage(bool bypass, OrbitalPlatform platform, double now, in OrbitClock clock)
        {
            if (bannerWord == null) return;
            bool station = platform != null && platform.Exists;
            PlatformStats stats = station ? platform.Stats(now) : default;
            OrbitState state = station ? platform.State(now, clock) : default;
            RefreshBanner(platform, station, state, now, clock);
            RefreshTiles(platform, station, stats, state, now);
            RefreshResources(platform, station, stats, now, clock);
            PaintGrid(miniGrid, platform, now, -1, false);
            RefreshCards(bypass, platform, station, state, now, clock);
            WriteLoop(platformLoop, loop);
        }

        private void RefreshBanner(OrbitalPlatform platform, bool station, in OrbitState state, double now, in OrbitClock clock)
        {
            bannerPlanner.gameObject.SetActive(!station);
            bannerClock.gameObject.SetActive(station);
            bannerClockKey.gameObject.SetActive(station);
            if (!station)
            {
                bannerRail.color = AvTheme.RailInert;
                bannerBand.text = "NO ORBIT";
                bannerWord.text = "NO STATION";
                bannerWord.color = AvTheme.Dim;
                bannerPass.fillAmount = 0f;
                bannerNote.text = "Design it in MISSION PLANNER: launch a core, then dock modules one at a time.";
                return;
            }

            OrbitRegime orbit = platform.Orbit;
            bannerBand.text = orbit.Name + " · " + TheaterGrid.Km(orbit.Altitude) + " KM · " +
                              orbit.InclinationDeg.ToString("0.0", Invariant) + "°";
            PlatformHold hold = platform.HoldAt(now);
            if (hold != PlatformHold.None)
            {
                double total = hold == PlatformHold.Insertion
                    ? (support.Settings != null ? support.Settings.PlatformInsertionSeconds.Value : 45f)
                    : hold == PlatformHold.Rephase ? OrbitalPlatform.RephaseLeadSeconds
                    : OrbitalPlatform.TransferSeconds;
                double left = platform.CycleStart - now;
                bannerRail.color = AvTheme.RailInfo;
                bannerWord.text = PlatformWords.Hold(hold);
                bannerWord.color = AvTheme.RailInfo;
                bannerClockKey.text = "ON STATION IN";
                bannerClock.text = PlatformWords.Clock(left);
                bannerClock.color = AvTheme.RailInfo;
                bannerPass.fillAmount = total > 0.0 ? 1f - Mathf.Clamp01((float)(left / total)) : 0f;
                bannerPass.color = AvTheme.RailInfo;
                bannerNote.text = hold == PlatformHold.Insertion
                    ? "Core climbing to " + orbit.Name + ". Dock modules now; abilities wait for insertion."
                    : hold == PlatformHold.SafeMode ? "Out of propellant at LOW: climbing to MID. Abilities offline."
                    : "Burn in progress. Abilities are offline until the station is back on its pass cycle.";
                return;
            }

            if (state.InPass)
            {
                bannerRail.color = AvTheme.RailReady;
                bannerWord.text = "OVERHEAD";
                bannerWord.color = AvTheme.RailReady;
                bannerClockKey.text = "LOSS OF SIGNAL IN";
                bannerClock.text = PlatformWords.Clock(state.TimeToPassEnd);
                bannerClock.color = AvTheme.RailReady;
                bannerPass.fillAmount = (float)(state.TimeInPass / System.Math.Max(1.0, state.Window));
                bannerPass.color = AvTheme.RailReady;
                bannerNote.text = "PASS " + (state.Pass.Index + 1) + (state.Pass.Ascending ? " ASC" : " DSC") + " · HDG " +
                                  (System.Math.Round(state.Pass.Heading / OrbitMath.Deg + 360.0) % 360.0).ToString("000", Invariant) +
                                  "° · " + PlatformWords.Clock(state.TimeInPass) + " OF " + PlatformWords.Clock(state.Window) +
                                  " · GET " + TheaterGrid.Elapsed(platform.Elapsed(now));
            }
            else
            {
                double gap = TheaterTrack.CycleSeconds(orbit, clock) - state.Window;
                bannerRail.color = AvTheme.RailCaution;
                bannerWord.text = "BELOW HORIZON";
                bannerWord.color = AvTheme.RailCaution;
                bannerClockKey.text = "ACQUISITION IN";
                bannerClock.text = PlatformWords.Clock(state.TimeToPass);
                bannerClock.color = AvTheme.RailCaution;
                bannerPass.fillAmount = gap > 0.0 ? 1f - Mathf.Clamp01((float)(state.TimeToPass / gap)) : 0f;
                bannerPass.color = AvTheme.RailCaution;
                bannerNote.text = "NEXT PASS " + PlatformWords.Clock(state.Window) + " LONG · FAR-SIDE ARC SIMULATED · PERIOD " +
                                  PlatformWords.Clock(OrbitMath.Period(orbit.Altitude)) + " · GET " +
                                  TheaterGrid.Elapsed(platform.Elapsed(now));
            }
        }

        private void RefreshTiles(OrbitalPlatform platform, bool station, in PlatformStats stats, in OrbitState state, double now)
        {
            if (!station)
            {
                for (int i = 0; i < tiles.Length; i++) PaintTile(tiles[i], "—", Tone.Locked);
                return;
            }

            float charge = stats.StorageKj > 0f ? platform.Energy / stats.StorageKj : 0f;
            PaintTile(tiles[0],
                platform.Brownout ? "BROWNOUT" : charge < 0.25f ? "LOW " + Mathf.RoundToInt(charge * 100f) + "%"
                : stats.NetEclipseKw < 0f && state.InPass ? "ON CELLS" : "NOMINAL",
                platform.Brownout ? Tone.Danger : charge < 0.25f ? Tone.Armed : Tone.Ready);

            string hot = platform.RunsHot(ModuleKind.Emp, now) && platform.Fitted(ModuleKind.Emp) ? "EMP"
                : platform.RunsHot(ModuleKind.Reactor, now) && platform.Fitted(ModuleKind.Reactor) ? "RTG" : null;
            PaintTile(tiles[1], hot != null ? "HOT · " + hot : "NOMINAL", hot != null ? Tone.Armed : Tone.Ready);

            if (stats.FuelCapacity <= 0f) PaintTile(tiles[2], "NO TANKS", Tone.Locked);
            else
            {
                float fuel = platform.Fuel / stats.FuelCapacity;
                string drag = platform.Orbit.DragFuelPerSecond > 0f ? " · DRAG" : "";
                PaintTile(tiles[2], platform.Fuel <= 0.5f ? "DRY" : Mathf.RoundToInt(fuel * 100f) + "%" + drag,
                    platform.Fuel <= 0.5f ? Tone.Danger : fuel < 0.25f ? Tone.Armed : Tone.Ready);
            }

            PlatformHold hold = platform.HoldAt(now);
            PaintTile(tiles[3], hold != PlatformHold.None ? "HOLD" : state.InPass ? "AOS" : "LOS",
                hold != PlatformHold.None ? Tone.Pending : state.InPass ? Tone.Ready : Tone.Locked);
            PaintTile(tiles[4], stats.Crewed ? "3 ABOARD" : "UNCREWED", stats.Crewed ? Tone.Ready : Tone.Locked);

            int down = -1;
            for (int i = 0; i < OrbitalPlatform.CellCount && down < 0; i++)
                if (platform.Cell(i) != ModuleKind.None && !platform.IsOnline(i, now)) down = i;
            bool deflected = platform.Notice == PlatformNotice.DebrisDeflected;
            PaintTile(tiles[5],
                down >= 0 ? "HIT · " + PlatformModules.Info(platform.Cell(down)).Code : deflected ? "DEFLECTED" : "CLEAR",
                down >= 0 ? Tone.Danger : deflected ? Tone.Pending : Tone.Ready);

            PaintTile(tiles[6], platform.Orbit.Code + (hold == PlatformHold.None ? "" : " · BURN"),
                hold == PlatformHold.None ? Tone.Ready : Tone.Pending);
            PaintTile(tiles[7], stats.Online + "/" + stats.Modules + " ONLINE",
                stats.Online < stats.Modules ? Tone.Danger : Tone.Ready);
        }

        private void RefreshResources(OrbitalPlatform platform, bool station, in PlatformStats stats, double now,
                                      in OrbitClock clock)
        {
            if (!station)
            {
                resourceNote.text = "";
                energyGauge.Reading.text = fuelGauge.Reading.text = "—";
                energyGauge.Fill.fillAmount = fuelGauge.Fill.fillAmount = 0f;
                energyGauge.Note.text = fuelGauge.Note.text = "";
                rodsLabel.text = "RODS —";
                massLabel.text = "";
                for (int i = 0; i < rodPips.Length; i++) rodPips[i].color = Color.clear;
                return;
            }

            resourceNote.text = stats.Modules + "/" + OrbitalPlatform.CellCount + " CELLS";
            energyGauge.Reading.text = PlatformWords.Whole(platform.Energy) + " / " + PlatformWords.Whole(stats.StorageKj) + " KJ";
            energyGauge.Fill.fillAmount = stats.StorageKj > 0f ? platform.Energy / stats.StorageKj : 0f;
            energyGauge.Fill.color = platform.Brownout ? AvTheme.RailDanger : AvTheme.RailReady;
            energyGauge.Note.text = "SUN " + PlatformWords.Kilowatts(stats.NetSunKw) + " · DARK " +
                                    PlatformWords.Kilowatts(stats.NetEclipseKw);

            if (stats.FuelCapacity > 0f)
            {
                fuelGauge.Reading.text = PlatformWords.Whole(platform.Fuel) + " / " + PlatformWords.Whole(stats.FuelCapacity);
                fuelGauge.Fill.fillAmount = platform.Fuel / stats.FuelCapacity;
                fuelGauge.Note.text = "REPHASE 25 · BAND 35" +
                                      (platform.Orbit.DragFuelPerSecond > 0f ? " · DRAG " +
                                       (platform.Orbit.DragFuelPerSecond * 60f).ToString("0.0", Invariant) + "/MIN" : "");
            }
            else
            {
                fuelGauge.Reading.text = "—";
                fuelGauge.Fill.fillAmount = 0f;
                fuelGauge.Note.text = "NO PROPULSION FITTED";
            }

            rodsLabel.text = stats.RodCapacity > 0 ? "RODS " + platform.Rods + "/" + stats.RodCapacity : "RODS —";
            for (int i = 0; i < rodPips.Length; i++)
            {
                rodPips[i].color = i >= stats.RodCapacity ? Color.clear
                    : i < platform.Rods ? CategoryColour(ModuleCategory.Weapon)
                    : AvTheme.SurfaceInert;
            }
            massLabel.text = "MASS " + PlatformWords.Tonnes(stats.Mass) + " / " + PlatformWords.Tonnes(OrbitalPlatform.MassLimit);
        }

        private void RefreshCards(bool bypass, OrbitalPlatform platform, bool station, in OrbitState state, double now,
                                  in OrbitClock clock)
        {
            cardsNote.text = !station ? "NO STATION"
                : platform.HoldAt(now) != PlatformHold.None ? "HOLDING · BURN IN PROGRESS"
                : state.InPass ? "OVERHEAD · STRIKE WINDOW OPEN" : "AWAY · PLAN, RECHARGE, REPHASE";
            float allocation = support.LocalAllocation;
            float cooldown = support.LocalCooldownRemaining;

            for (int i = 0; i < cards.Count; i++)
            {
                AbilityCard card = cards[i];
                AbilityInfo info = PlatformAbilities.Info(card.Ability);
                Tone tone;
                string status;
                bool enabled;
                float recharge = 0f;

                switch (card.Kind)
                {
                    case CardKind.Uplink:
                    {
                        PlatformDenial denial = support.PlatformCheck(PlatformAbility.Uplink);
                        bool fitted = station && platform.Fitted(ModuleKind.Imager);
                        enabled = fitted;
                        tone = denial == PlatformDenial.None ? Tone.Ready : fitted ? Tone.Pending : Tone.Locked;
                        status = denial == PlatformDenial.None ? "READY · FEED LIVE"
                            : PlatformWords.Denial(denial, platform, card.Ability, now, clock);
                        card.Cost.text = "IMAGER LOAD 2.0 KW · VIEW ONLY";
                        break;
                    }
                    case CardKind.Action:
                    {
                        SupportActionDefinition action = card.Action;
                        float cost = support.Cost(action);
                        bool armed = support.ArmedAction.HasValue && support.ArmedAction.Value == action.Id;
                        PlatformDenial denial = support.PlatformCheck(card.Ability);
                        enabled = false;
                        if (!action.Enabled || cost <= 0f) { tone = Tone.Locked; status = "UNAVAILABLE ON THIS SERVER"; }
                        else if (armed) { tone = Tone.Armed; status = "ARMED · RIGHT-CLICK MAP OR UPLINK"; enabled = true; }
                        else if (support.RequestPending) { tone = Tone.Pending; status = "REQUEST PENDING"; }
                        else if (!support.IsAuthorised(action)) { tone = Tone.Locked; status = "LOCKED · UNLOCK IN SQD"; }
                        else if (denial != PlatformDenial.None)
                        {
                            tone = denial == PlatformDenial.NotOverhead || denial == PlatformDenial.Recharging ||
                                   denial == PlatformDenial.Holding
                                ? Tone.Pending
                                : denial == PlatformDenial.NotFitted || denial == PlatformDenial.NoPlatform ? Tone.Locked
                                : Tone.Danger;
                            status = PlatformWords.Denial(denial, platform, card.Ability, now, clock);
                        }
                        else if (cooldown > 0.5f) { tone = Tone.Armed; status = "NET COOLING · T-" + Mathf.CeilToInt(cooldown) + "S"; }
                        else if (!bypass && allocation + 0.001f < cost) { tone = Tone.Danger; status = "INSUFFICIENT ALLOCATION"; }
                        else { tone = Tone.Ready; status = "READY · ARM, THEN AIM"; enabled = true; }

                        card.Button.SetLatched(armed);
                        card.Button.SetText(armed ? "ABORT" : "ARM");
                        card.Cost.text = (cost > 0f ? Figure(cost) + " ALLOC · " : "") + PlatformWords.Whole(info.EnergyKj) +
                                         " KJ · " + Mathf.RoundToInt(station ? platform.RechargeSeconds(card.Ability, now)
                                             : info.RechargeSeconds) + " S";
                        if (station)
                        {
                            float total = platform.RechargeSeconds(card.Ability, now);
                            recharge = total > 0f ? (float)(platform.RechargeRemaining(card.Ability, now) / total) : 0f;
                        }
                        break;
                    }
                    case CardKind.Rephase:
                    {
                        PlatformDenial denial = station ? platform.Check(PlatformAbility.Rephase, now, clock) : PlatformDenial.NoPlatform;
                        enabled = denial == PlatformDenial.None && !support.CommandPending;
                        tone = enabled ? Tone.Ready : denial == PlatformDenial.Overhead || denial == PlatformDenial.Recharging ||
                                                    denial == PlatformDenial.Holding ? Tone.Pending : Tone.Locked;
                        status = support.CommandPending ? "COMMAND PENDING"
                            : denial == PlatformDenial.None ? "READY · NEXT PASS IN 10 S"
                            : PlatformWords.Denial(denial, platform, card.Ability, now, clock);
                        card.Cost.text = "25 FUEL · SKIPS THE FAR-SIDE WAIT";
                        if (station)
                        {
                            float total = platform.RechargeSeconds(PlatformAbility.Rephase, now);
                            recharge = total > 0f ? (float)(platform.RechargeRemaining(PlatformAbility.Rephase, now) / total) : 0f;
                        }
                        break;
                    }
                    default:
                    {
                        int target = (station ? platform.Regime : OrbitRegimes.Mid) + (card.Kind == CardKind.Raise ? 1 : -1);
                        bool valid = OrbitRegimes.Valid(target);
                        PlatformDenial denial = !station ? PlatformDenial.NoPlatform
                            : !valid ? PlatformDenial.SameOrbit
                            : platform.CheckShift((byte)target, now, clock);
                        enabled = denial == PlatformDenial.None && !support.CommandPending;
                        tone = enabled ? Tone.Ready : denial == PlatformDenial.Holding ? Tone.Pending
                            : denial == PlatformDenial.NoFuel || denial == PlatformDenial.Brownout ? Tone.Danger : Tone.Locked;
                        status = support.CommandPending ? "COMMAND PENDING"
                            : !valid && station ? (card.Kind == CardKind.Raise ? "HIGHEST BAND" : "LOWEST BAND")
                            : denial == PlatformDenial.None ? "READY · TO " + OrbitRegimes.Get(target).Name
                            : PlatformWords.Denial(denial, platform, card.Ability, now, clock);
                        card.Cost.text = valid
                            ? "35 FUEL · 30 S TRANSFER · " + OrbitRegimes.Get(target).Summary
                            : "35 FUEL PER BAND · 30 S TRANSFER";
                        break;
                    }
                }

                card.Button.SetEnabled(enabled);
                card.Recharge.fillAmount = Mathf.Clamp01(recharge);
                if (card.LastTone == tone && card.LastStatus == status) continue;
                card.LastTone = tone;
                card.LastStatus = status;
                card.Rail.color = RailColor(tone);
                card.Status.text = status;
                card.Status.color = ToneColour(tone);
                card.Button.WithTooltip(CardTooltip(card, info) + " " + status + ".");
            }
        }

        private static string CardTooltip(AbilityCard card, in AbilityInfo info)
        {
            switch (card.Kind)
            {
                case CardKind.Uplink:
                    return "Open the full-screen uplink: drag or WASD to slew, wheel to zoom, 1-4 to task at the crosshair.";
                case CardKind.Action:
                    return card.Action.Name + " — " + card.Action.Description +
                           " Arm, then right-click the map or fire from the uplink crosshair.";
                case CardKind.Rephase:
                    return "Phasing burn while the station is away: the next pass begins about 10 s later.";
                case CardKind.Raise:
                    return "Raise one band: longer passes and wider scans, blurrier optics and looser rods.";
                default:
                    return "Lower one band: sharper optics and tighter rods, shorter passes. LOW burns drag fuel.";
            }
        }
    }
}
