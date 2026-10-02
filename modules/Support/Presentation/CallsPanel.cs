using System;
using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Modules.Support.Domain.Calls;
using BoscaliSummer.Modules.Support.Runtime;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Support.Presentation
{
    /// <summary>
    /// The OPS bezel page: one CALLS console. Favourites on top, then LIGHT / HEAVY / STRATEGIC tiles, each showing its
    /// price, one reason and one state word. The panel owns no policy: every figure comes from <see cref="SupportManager"/>
    /// and every press goes through <see cref="CallsController"/>.
    /// </summary>
    internal sealed class CallsPanel : MonoBehaviour, ISceneService
    {
        private const float Width = AvTokens.PanelWidth;
        private const float PanelHeight = AvTokens.PanelHeight;
        private const float RefreshInterval = 0.15f;

        private readonly List<CallTile> tiles = new List<CallTile>(16);
        private readonly Dictionary<SupportActionId, CallLine> rows = new Dictionary<SupportActionId, CallLine>();
        private AvControl[] favourites = new AvControl[0];

        private SupportManager manager;
        private CallsController calls;
        private ManualLogSource logger;

        private MFDScreen screen;
        private GameObject screenRoot;
        private AvConsole shell;
        private AvMetric[] metrics;
        private BriefCard banner;

        private float nextAttempt;
        private float nextRefresh;
        private bool failed;
        private float lineHeight = CallLine.Height;

        public void Configure(SupportManager supportManager, CallsController callsController)
        {
            manager = supportManager;
            calls = callsController;
            logger = ((ISupportHost)supportManager).Logger;
        }

        public void ResetForScene()
        {
            MfdBezel.Release(MfdSlots.Ops);
            if (screenRoot != null) UnityEngine.Object.Destroy(screenRoot);
            screenRoot = null;
            screen = null;
            shell = null;
            metrics = null;
            banner = null;
            rows.Clear();
            favourites = new AvControl[0];
            nextAttempt = 0f;
            nextRefresh = 0f;
            failed = false;
        }

        private void OnDestroy() => ResetForScene();

        private void Update()
        {
            if (failed || manager == null || calls == null) return;
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
            if (!visible || Time.unscaledTime < nextRefresh) return;

            nextRefresh = Time.unscaledTime + RefreshInterval;
            try { Refresh(); }
            catch (Exception e)
            {
                failed = true; // a refresh fault must not throw every frame
                logger?.LogError("OPS CALLS refresh failed: " + e);
            }
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

                logger?.LogInfo("OPS CALLS MFD installed on " + (left ? "left" : "right") + " bezel slot " + (slot + 1) + ".");
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

            BuildConsole(rootRect, height);

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

        /// <summary>Builds the whole page into <paramref name="root"/> without an MFD (offline render harness).</summary>
        internal void BuildForHarness(RectTransform root, float height) => BuildConsole(root, height);

        private void BuildConsole(RectTransform rootRect, float height)
        {
            rows.Clear();
            // 24 px at the 596 px page; a taller (896 px) page spreads the ten rows instead of leaving an empty band.
            lineHeight = Mathf.Clamp(CallLine.Height + (height - PanelHeight) * 0.085f, CallLine.Height, 52f);
            shell = AvConsole.Build(rootRect, "OPS", "CALLS", 1, Width, height);
            metrics = shell.Metrics("CREDIT", "NEXT");
            BuildPage(shell.Page(0));
            shell.Finish();
        }

        private static Image FindHighlight(Button button)
        {
            if (button == null) return null;
            Image[] images = button.GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
                if (images[i].gameObject != button.gameObject) return images[i];
            return button.GetComponent<Image>();
        }

        // ---- Page ------------------------------------------------------------------------

        private void BuildPage(AvFlow page)
        {
            banner = page.Add(new BriefCard(page.Content));
            AvControl abort = banner.AddControl(new AvControl.Spec("ABORT", () => calls?.Disarm(), AvButtonStyle.Danger));
            abort.Help = "Disarm the armed CALL. Nothing is spent.";

            page.Section(AvIcon.Bolt, "FAVOURITES", "KEYS 1–4 · PRESS TWICE TO FIRE");
            var specs = new AvControl.Spec[4];
            for (int i = 0; i < specs.Length; i++)
            {
                int slot = i;
                specs[i] = new AvControl.Spec((i + 1) + " ·", () => PressFavourite(slot), AvButtonStyle.Primary);
            }
            favourites = page.Buttons(specs).Controls;
            foreach (AvControl c in favourites) c.SingleLine();

            // 10 fixed one-line rows, LIGHT -> STRATEGIC; the tier word sits on each row instead of a section header.
            foreach (CallRow row in CallSheet.Rows)
            {
                SupportActionId id = row.Id;
                CallLine line = page.Add(new CallLine(page.Content, lineHeight));
                line.AddControl(new AvControl.Spec("CALL", () => calls?.Press(id), AvButtonStyle.Primary), "Arm this CALL; press again to fire.");
                line.AddControl(new AvControl.Spec("★", () => calls?.Pin(id), AvButtonStyle.Quiet), "Pin to a favourite slot.");
                if (id == SupportActionId.JtacMark)
                    line.AddControl(new AvControl.Spec("UNLASE", () => calls?.Unlase(), AvButtonStyle.Quiet),
                        "Clear the lase at the current POD or map aim. Free.");
                rows[id] = line;
            }
        }

        private void PressFavourite(int slot)
        {
            if (calls == null || slot < 0 || slot >= calls.Favourites.Length) return;
            SupportActionId? id = calls.Favourites[slot];
            if (id.HasValue) calls.Press(id.Value);
        }

        // ---- Refresh and paint ------------------------------------------------------------

        private void Refresh()
        {
            tiles.Clear();
            int frozen = manager.LocalFrozenSeconds;
            foreach (CallRow row in CallSheet.Rows)
            {
                bool unlocked = manager.Unlocked(row.Id, out string unlock);
                // A pending CALL cannot be tied to one tile from here: the banner carries PENDING, tiles stay as they are.
                CallTile t = CallsView.Tile(row, manager.Quote(row.Id), unlocked, unlock, manager.LocalCredit,
                    manager.LocalCooldownRemaining, calls.Armed == row.Id, false, !manager.Online);
                if (frozen > 0 && t.State != CallState.Offline)
                {
                    int minutes = Mathf.Max(1, Mathf.CeilToInt(frozen / 60f));
                    t = new CallTile(t.Id, t.Label, t.TierWord, t.CostText, t.Reason, CallState.Cooldown,
                        "FROZEN " + minutes + " MIN", false);
                }
                tiles.Add(t);
            }
            Paint(tiles, (int)manager.LocalCredit + " CR", manager.NextUnlockText(), calls.LastWords, calls.Pending);
        }

        internal void Paint(IReadOnlyList<CallTile> view, string balanceText, string nextUnlock, string words, bool pending = false)
        {
            if (shell == null) return;
            words = words ?? "";
            if (metrics != null && metrics.Length > 1)
            {
                metrics[0].Set(balanceText ?? "", "", 0f, AvState.Ready);
                bool all = string.IsNullOrEmpty(nextUnlock);
                metrics[1].Set(all ? "ALL UNLOCKED" : nextUnlock, "", 0f, all ? AvState.Ready : AvState.Info);
            }

            bool armed = false;
            foreach (CallTile t in view)
            {
                if (!rows.TryGetValue(t.Id, out CallLine line)) continue;
                line.Set(t, StateOf(t.State));
                line.Dim = !t.Enabled;
                line.Armed = t.State == CallState.Armed;
                armed |= line.Armed;
            }
            for (int i = 0; i < favourites.Length; i++) PaintFavourite(i, view);

            banner.Set(armed ? "▲ CALL ARMED" : pending ? "CALL PENDING" : "HOTLINE",
                words.Length == 0 ? "HOTLINE OPEN · PRESS A CALL TO ARM" : words,
                armed ? AvState.Caution : pending ? AvState.Info
                    : words.StartsWith("NEGATIVE", StringComparison.Ordinal) ? AvState.Danger : AvState.Ready);
            banner.ShowControl(armed);
        }

        private void PaintFavourite(int slot, IReadOnlyList<CallTile> view)
        {
            if (slot >= favourites.Length) return;
            AvControl button = favourites[slot];
            SupportActionId? id = calls != null && slot < calls.Favourites.Length ? calls.Favourites[slot] : null;
            if (id.HasValue)
            {
                for (int i = 0; i < view.Count; i++)
                {
                    CallTile t = view[i];
                    if (t.Id != id.Value) continue;
                    button.Label = (slot + 1) + " · " + t.Label;
                    string tip = t.Label + " · " + t.StateWord + " · " + t.CostText;
                    if (button.Help != tip) button.Help = tip;
                    button.Interactable = t.Enabled;
                    button.Armed = t.State == CallState.Armed;
                    return;
                }
            }
            button.Label = (slot + 1) + " · EMPTY";
            if (button.Help == null || !button.Help.StartsWith("Empty")) button.Help = "Empty slot: press ★ on a call to pin it here.";
            button.Interactable = false;
            button.Armed = false;
        }

        private static AvState StateOf(CallState s) =>
            s == CallState.Ready ? AvState.Ready : s == CallState.Armed ? AvState.Caution
            : s == CallState.Offline || s == CallState.LowCredit ? AvState.Danger : AvState.Inert;
    }
}
