using System;
using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Features.Support.Domain;
using BoscaliSummer.Features.Support.Domain.Cyber;
using BoscaliSummer.Features.Support.Domain.Orbital;
using BoscaliSummer.Features.Support.Domain.SpecOps;
using BoscaliSummer.Features.Support.Presentation.Viz;
using BoscaliSummer.Features.Support.Runtime;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Lifecycle;
using BoscaliSummer.Runtime;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Support.Presentation
{
    /// <summary>
    /// "OPS" — the multi-domain operations MFD: SPACE, CYBER and SPEC OPS, on kit v2's
    /// <see cref="AvConsole"/> chrome (spec §9.2, §10 OPS slice).
    ///
    /// <para>The panel owns no policy. Every figure it shows comes from
    /// <see cref="SupportManager"/> (host snapshot or the same pricing the host charges) and
    /// every control is a request the host validates. A row that cannot act says why, in
    /// words on the row and in the footer, never by colour alone.</para>
    ///
    /// <para>This file is the shell: bezel install, header/chip/metric wiring, refresh cadence
    /// and the shared support-action row builder. Each domain page lives in its own partial.</para>
    /// </summary>
    internal sealed partial class SupportPanel : MonoBehaviour, ISceneService
    {
        private const float Width = AvTokens.PanelWidth;
        private const float PanelHeight = AvTokens.PanelHeight;
        private const float RefreshInterval = 0.15f;

        private const int ChipCount = 3;
        private const int DomainCount = 3;
        private const float SectionGap = 16f;

        private const int TabSpace = (int)OpsDomain.Space;
        private const int TabCyber = (int)OpsDomain.Cyber;
        private const int TabSpecOps = (int)OpsDomain.SpecialOperations;

        private SupportManager support;
        private IProgressionView progression;
        private ManualLogSource logger;

        private MFDScreen screen;
        private GameObject screenRoot;
        private AvConsole shell;

        private AvChip chipNet, chipLink, chipMap;
        private AvMetric allocationMetric, orbitMetric, stationMetric, reserveMetric;

        /// <summary>Support-action rows on every page, refreshed by the page that owns them.</summary>
        private readonly List<ActionRow> actionRows = new List<ActionRow>(12);

        private float nextAttempt;
        private float nextRefresh;
        private bool failed;
        private bool viewOpen;

        public void Configure(
            SupportManager manager,
            IProgressionView progressionView,
            ManualLogSource log,
            IBaseDefenseAlarmService _ = null)
        {
            support = manager;
            progression = progressionView;
            logger = log;
        }

        public void ResetForScene()
        {
            MfdBezel.Release(MfdSlots.Ops);
            if (screenRoot != null) UnityEngine.Object.Destroy(screenRoot);

            screenRoot = null;
            screen = null;
            shell = null;
            chipNet = chipLink = chipMap = null;
            allocationMetric = orbitMetric = stationMetric = reserveMetric = null;
            actionRows.Clear();
            ResetOpsWindow();
            ResetSpacePage();
            ResetCyberPage();
            ResetSpecOpsPage();

            nextAttempt = 0f;
            nextRefresh = 0f;
            failed = false;
            opsSnapshotSeen = false;
            SetViewOpen(false);
        }

        private void OnDestroy() => ResetForScene();

        private void Update()
        {
            if (failed || support == null) return;
            if (Application.isBatchMode) { failed = true; return; }
            if (!GameAccess.MfdAvailable) { failed = true; return; }

            if (screen == null)
            {
                if (Time.unscaledTime < nextAttempt) return;
                nextAttempt = Time.unscaledTime + 1f;
                TryInstall();
                return;
            }

            bool visible = screen.isActive &&
                SceneSingleton<DynamicMap>.i?.maximizedMapCanvas?.isActiveAndEnabled == true;
            SetViewOpen(visible);
            // The launch count, the voice loop and SAR hand-off keep running with the screen closed.
            TickSpaceBackground();
            TickCyberBackground();
            TickSpecOpsBackground();
            if (!visible || Time.unscaledTime < nextRefresh) return;

            nextRefresh = Time.unscaledTime + RefreshInterval;
            try
            {
                support.PollOps();
                Refresh();
            }
            catch (Exception e)
            {
                // A refresh fault must not throw every frame; stop drawing and say so once.
                failed = true;
                logger?.LogError("OPS MFD refresh failed: " + e);
            }
        }

        private void SetViewOpen(bool open)
        {
            if (viewOpen == open) return;
            viewOpen = open;
            progression?.SetViewOpen(open);
        }

        // ---- Installation ----------------------------------------------------------------

        private void TryInstall()
        {
            try
            {
                VirtualMFD mfd = SceneSingleton<DynamicMap>.i?.maximizedMapCanvas?.GetComponentInChildren<VirtualMFD>(true)
                    ?? UnityEngine.Object.FindObjectOfType<VirtualMFD>();
                if (mfd == null) return;

                if (!MfdBezel.TryClaim(MfdSlots.Ops, preferLeft: true, mfd,
                    out List<Button> buttons, out List<MFDScreen> screens, out int slot, out bool left))
                {
                    failed = true;
                    logger?.LogWarning("OPS MFD unavailable: no free bezel slot.");
                    return;
                }

                MFDScreen template = MfdBezel.FindTemplate(screens) ?? MfdBezel.FindTemplate(mfd);
                if (template == null)
                {
                    MfdBezel.Release(MfdSlots.Ops);
                    return;
                }

                screen = Build(template, buttons[slot]);
                if (screen == null)
                {
                    MfdBezel.Release(MfdSlots.Ops);
                    failed = true;
                    return;
                }

                if (!MfdBezel.Bind(mfd, buttons, screens, slot, left, screen))
                {
                    MfdBezel.Release(MfdSlots.Ops);
                    if (screenRoot != null) UnityEngine.Object.Destroy(screenRoot);
                    screenRoot = null;
                    screen = null;
                    failed = true;
                    logger?.LogWarning("OPS MFD unavailable: claimed bezel changed before binding.");
                    return;
                }

                logger?.LogInfo("OPS MFD installed on " + (left ? "left" : "right") +
                                " bezel slot " + (slot + 1) + ".");
            }
            catch (Exception e)
            {
                MfdBezel.Release(MfdSlots.Ops);
                failed = true;
                logger?.LogError("OPS MFD install failed: " + e);
            }
        }

        private MFDScreen Build(MFDScreen template, Button bezel)
        {
            var root = new GameObject("BoscaliOperations.Screen", typeof(RectTransform));
            screenRoot = root;
            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.SetParent(template.transform.parent, false);

            RectTransform templateRect = (RectTransform)template.transform;
            rootRect.anchorMin = templateRect.anchorMin;
            rootRect.anchorMax = templateRect.anchorMax;
            rootRect.pivot = templateRect.pivot;
            rootRect.localScale = templateRect.localScale;

            float height = AvLay.ResolveHeight(templateRect.parent as RectTransform, PanelHeight, AvTokens.PanelHeightMax);
            rootRect.sizeDelta = new Vector2(Width, height);
            AvLay.ClampIntoCanvas(rootRect);

            shell = AvConsole.Build(rootRect, "OPS", "OPERATIONS", DomainCount, Width, height);
            shell.PageChanged += _ => nextRefresh = 0f;

            AvChip[] chips = shell.Chips(ChipCount);
            chipNet = chips[0];
            chipLink = chips[1];
            chipMap = chips[2];

            AvMetric[] metrics = shell.Metrics("ALLOCATION", "ORBIT", "CYBER", "SPEC OPS");
            allocationMetric = metrics[0];
            orbitMetric = metrics[1];
            stationMetric = metrics[2];
            reserveMetric = metrics[3];

            shell.Tabs((AvIcon.Satellite, "SPACE"), (AvIcon.ShieldLock, "CYBER"), (AvIcon.UsersGroup, "SPEC OPS"));

            BuildSpacePage(shell.Page(TabSpace));
            BuildCyberPage(shell.Page(TabCyber));
            BuildSpecOpsPage(shell.Page(TabSpecOps));
            shell.Finish();

            MFDScreen result = root.AddComponent<MFDScreen>();
            result.shortName = MfdSlots.Ops;
            result.displayPanel = shell.Root.gameObject;
            result.aircraftOnly = false;
            result.label = bezel != null ? bezel.GetComponentInChildren<TextMeshProUGUI>(true) : null;
            result.highlight = FindHighlight(bezel);
            if (result.label == null || result.highlight == null)
            {
                UnityEngine.Object.Destroy(root);
                screenRoot = null;
                shell = null;
                return null;
            }

            return result;
        }

        private static Image FindHighlight(Button button)
        {
            if (button == null) return null;
            Image[] images = button.GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
            {
                if (images[i].gameObject != button.gameObject) return images[i];
            }
            return button.GetComponent<Image>();
        }

        // ---- Tone mapping ------------------------------------------------------------------

        /// <summary>The one place an <see cref="AbilityTone"/> becomes an <see cref="AvState"/>
        /// (R1: status is always a word or glyph too, never colour alone).</summary>
        private static AvState ToState(AbilityTone tone)
        {
            switch (tone)
            {
                case AbilityTone.Ready: return AvState.Ready;
                case AbilityTone.Armed: return AvState.Caution;
                case AbilityTone.Pending: return AvState.Info;
                case AbilityTone.Danger: return AvState.Danger;
                default: return AvState.Inert;
            }
        }

        // ---- Support-action rows (shared by SPACE, CYBER and SPEC OPS) --------------------

        private sealed class ActionRow
        {
            public SupportActionDefinition Definition;
            public ActionTile View;
            public AvControl Trailing;
            public int Tab;
            public string Verb;
        }

        /// <summary>Which domain page hosts an action. Nothing is left without a page.</summary>
        private static int HomeTab(SupportActionDefinition action)
        {
            if (action.IsCyber) return TabCyber;
            if (SupportManager.OrbitalAbility(action.Id).HasValue) return TabSpace;
            if (action.Id == SupportActionId.FlareMissile) return TabCyber;
            return TabSpecOps;
        }

        private int CountActions(int tab)
        {
            int count = 0;
            foreach (SupportActionDefinition action in support.Actions)
                if (HomeTab(action) == tab) count++;
            return count;
        }

        /// <summary>The tile icon for an ability (a lock is drawn instead while it is locked).</summary>
        private static AvIcon AbilityIcon(SupportActionDefinition action)
        {
            switch (action.Id)
            {
                case SupportActionId.Recon: return AvIcon.Radar2;
                case SupportActionId.MtiSweep: return AvIcon.Activity;
                case SupportActionId.ElintSweep: return AvIcon.Antenna;
                case SupportActionId.Artillery: return AvIcon.ArrowDown;
                case SupportActionId.Emp: return AvIcon.Bolt;
                case SupportActionId.FlareMissile: return AvIcon.Flame;
                case SupportActionId.Fortify: return AvIcon.Shield;
                case SupportActionId.HackPing: return AvIcon.WaveSine;
                case SupportActionId.HackScan: return AvIcon.Radar2;
                case SupportActionId.HackTrack: return AvIcon.Target;
                case SupportActionId.HackBlackout: return AvIcon.Cloud;
                case SupportActionId.HackGhost: return AvIcon.Eye;
                case SupportActionId.HackSpoof: return AvIcon.Focus2;
                case SupportActionId.HackHijack: return AvIcon.Link;
                case SupportActionId.HackOverload: return AvIcon.Bolt;
                case SupportActionId.CapReveal: return AvIcon.Eye;
                case SupportActionId.CapJammer: return AvIcon.WaveSine;
                case SupportActionId.CapSabotage: return AvIcon.Skull;
                case SupportActionId.SpecSpot: return AvIcon.Focus2;
                case SupportActionId.SpecSuppress: return AvIcon.WaveSine;
                case SupportActionId.SpecSkywatch: return AvIcon.Plane;
                case SupportActionId.SpecEavesdrop: return AvIcon.Antenna;
                case SupportActionId.SpecHunt: return AvIcon.Target;
                default: return AvIcon.Bolt;
            }
        }

        /// <summary>The one paint for an ability tile on every ACTIONS page: name, the words
        /// <see cref="AbilityStatus"/> decided, the cost in mono, and the control's verb / enabled / armed state.</summary>
        private static void PaintAbilityTile(ActionTile tile, AvControl button, SupportActionDefinition action,
            in AbilityFacts facts, string sub, string verb, string nameOverride = null)
        {
            bool cost = !action.IsCyber && facts.CostText != "—";
            tile.Set(nameOverride ?? action.Name, sub, facts.CostText, cost ? "ALLOC" : "", ToState(facts.Tone),
                facts.Tone == AbilityTone.Locked ? AvIcon.Lock : AbilityIcon(action));
            tile.Armed = facts.Armed;
            tile.Dim = !facts.Enabled && !facts.Armed;
            button.Interactable = facts.Enabled;
            button.Latched = facts.Armed;
            button.Label = facts.Armed ? "ABORT" : verb;
        }

        /// <summary>Build every action tile this tab hosts into <paramref name="flow"/>, post-gated
        /// SPEC OPS abilities first.</summary>
        private void BuildActionRows(AvFlow flow, int tab, string verb)
        {
            for (int pass = 0; pass < 2; pass++)
            foreach (SupportActionDefinition action in support.Actions)
            {
                if (HomeTab(action) != tab || action.IsField != (pass == 0)) continue;
                SupportActionId id = action.Id;
                ActionTile view = flow.Add(new ActionTile(flow.Content, AbilityIcon(action)));
                view.Set(action.Name, "", "", "", AvState.Inert, AbilityIcon(action));
                AvControl trailing = view.AddTrailing(new AvControl.Spec(verb, () =>
                {
                    if (support.ArmedAction.HasValue && support.ArmedAction.Value == id) support.Disarm();
                    else support.Arm(id);
                    nextRefresh = 0f;
                }));
                actionRows.Add(new ActionRow { Definition = action, View = view, Trailing = trailing, Tab = tab, Verb = verb });
            }
        }

        private void RefreshActionRows(int tab, bool bypass)
        {
            for (int i = 0; i < actionRows.Count; i++)
            {
                ActionRow row = actionRows[i];
                if (row.Tab != tab) continue;

                SupportActionDefinition action = row.Definition;
                AbilityFacts facts = AbilityStatus.For(support, action, bypass);
                string sub = facts.Readiness;
                if (action.Id == SupportActionId.Fortify)
                {
                    SpecOpsDetachment detachment = support.LocalDetachment;
                    if (detachment != null)
                    {
                        int shells = detachment.GroundReadiness;
                        sub += " · " + shells + (shells == 1 ? " POSITION" : " POSITIONS") +
                               " · BEST " + FieldWords.Rank(detachment.BestRank);
                    }
                }
                PaintAbilityTile(row.View, row.Trailing, action, facts, sub, row.Verb);
                bool cyber = action.IsCyber;
                float cost = cyber ? 0f : support.Cost(action);
                float intel = AbilityStatus.Intel(action);
                SetTileHelp(row.View, row.Trailing, action.Name + " — " +
                    (cyber ? AvNum.Thousands(Mathf.Round(intel)) + " INTEL. " : cost > 0f ? AvNum.Thousands(Mathf.Round(cost)) + " ALLOC. " : "") +
                    action.Description + " " + facts.Readiness + ".");
            }
        }

        // ---- Refresh ---------------------------------------------------------------------

        private bool opsSnapshotSeen;

        private void Refresh()
        {
            if (shell == null || allocationMetric == null) return;
            bool bypass = support.BypassRequirements;

            RefreshChips(bypass);
            RefreshMetrics(bypass);

            int page = shell.CurrentPage;
            switch (page)
            {
                case TabSpace: RefreshSpace(bypass); break;
                case TabCyber: RefreshCyber(bypass); break;
                case TabSpecOps: RefreshSpecOps(bypass); break;
            }
            RefreshActionRows(page, bypass);

            bool armed = support.ArmedAction.HasValue || support.LocalPickArmed;
            string alert = opsSnapshotSeen && !support.OpsStateFresh
                ? "HOST SNAPSHOT STALE · FIGURES MAY BE OUT OF DATE"
                : null;
            string status = alert ?? (armed ? support.Status : null) ?? support.Status ??
                             OpsDomains.Mission((OpsDomain)Mathf.Max(0, page));
            shell.Footer.Set(status, alert != null ? AvState.Danger : armed ? AvState.Caution : AvState.Inert);
        }

        private void RefreshChips(bool bypass)
        {
            bool pending = support.RequestPending || support.CommandPending;
            bool armed = support.ArmedAction.HasValue || support.LocalPickArmed;
            float cooldown = support.LocalCooldownRemaining;
            bool fresh = support.OpsStateFresh;
            opsSnapshotSeen |= fresh;

            chipNet.Set(pending ? "PENDING" : cooldown > 0.5f ? "NET COOL" : bypass ? "BYPASS" : "NET READY",
                        pending || cooldown > 0.5f ? AvState.Caution : bypass ? AvState.Caution : AvState.Ready);
            chipLink.Set(fresh ? "LINKED" : opsSnapshotSeen ? "LINK STALE" : "SYNCING",
                        fresh ? AvState.Ready : opsSnapshotSeen ? AvState.Danger : AvState.Info);
            chipMap.Set(armed ? "MAP ARMED" : WingLink.WingMapGestureArmed ? "WING MAP" : "MAP IDLE",
                        armed ? AvState.Caution : WingLink.WingMapGestureArmed ? AvState.Info : AvState.Inert);
        }

        private void RefreshMetrics(bool bypass)
        {
            float allocation = support.LocalAllocation;
            float cooldown = support.LocalCooldownRemaining;
            float cooldownTotal = support.LocalCooldownTotal;

            if (support.RequestPending || support.CommandPending)
                allocationMetric.Set(AvNum.Compact(allocation), "AWAITING HOST", 1f, AvState.Info);
            else if (cooldown > 0.5f && cooldownTotal > 0f)
                allocationMetric.Set(AvNum.Compact(allocation), "NET T-" + Mathf.CeilToInt(cooldown) + "s",
                                     1f - cooldown / cooldownTotal, AvState.Caution);
            else
                allocationMetric.Set(AvNum.Compact(allocation), bypass ? "BYPASS" : "NET READY", 1f,
                                     bypass ? AvState.Caution : AvState.Ready);

            OrbitalPlatform platform = support.LocalPlatform;
            double now = support.OrbitNow;
            if (platform == null || !platform.Exists)
            {
                orbitMetric.Set("0/" + OrbitalPlatform.CellCount, "NO STATION", 0f, AvState.Inert);
            }
            else
            {
                PlatformStats stats = platform.Stats(now);
                OrbitState state = platform.State(now);
                bool holding = platform.HoldAt(now) != PlatformHold.None;
                orbitMetric.Set(stats.Modules + "/" + OrbitalPlatform.CellCount,
                                holding ? PlatformWords.Hold(platform.HoldAt(now)) : "ON STATION",
                                stats.Mass / OrbitalPlatform.MassLimit,
                                platform.Brownout ? AvState.Danger
                                : holding ? AvState.Info
                                : state.InPass ? AvState.Ready : AvState.Caution);
            }

            CyberNetwork cyber = support.LocalCyber;
            if (cyber == null || !cyber.HasCommand)
            {
                stationMetric.Set("0/0", "NO AIRBASE", 0f, AvState.Inert);
            }
            else
            {
                int infocon = cyber.Infocon;
                CyberStats stats = cyber.Stats();
                float computing = cyber.ComputingCapacity() > 0f ? cyber.Computing / cyber.ComputingCapacity() : 0f;
                stationMetric.Set(stats.Hacked + "/" + stats.Nodes, CyberWords.Infocon(infocon), computing,
                                  infocon >= 5 ? AvState.Ready : infocon >= 3 ? AvState.Caution : AvState.Danger);
            }

            SpecOpsDetachment detachment = support.LocalDetachment;
            if (detachment == null || !detachment.Enabled || !support.SpecOpsEnabled)
            {
                reserveMetric.Set("—", detachment == null ? "NO DATA" : "OFF", 0f, AvState.Inert);
            }
            else
            {
                int ready = detachment.Count(TeamState.Ready);
                int posts = detachment.Posts();
                int field = detachment.Count(TeamState.EnRoute) + detachment.Count(TeamState.Deciding) +
                    detachment.Count(TeamState.OnTask) + detachment.Count(TeamState.Holding);
                reserveMetric.Set(ready + "/" + detachment.Formed,
                                  posts > 0 ? posts + (posts == 1 ? " POST" : " POSTS")
                                  : field > 0 ? field + " OUT" : "STANDBY",
                                  ready / (float)SpecOpsDetachment.TeamCount,
                                  posts > 0 ? AvState.Ready : field > 0 ? AvState.Info : AvState.Inert);
            }
        }
    }
}
