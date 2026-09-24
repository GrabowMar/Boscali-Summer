using System;
using System.Collections.Generic;
using BoscaliSummer.Features.Command.Domain;
using BoscaliSummer.Framework.Contracts;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Command.Presentation
{
    /// <summary>The director's read-only plotting room for confirmed theater plans.</summary>
    internal sealed class StrPlanningWindow : MonoBehaviour
    {
        private const float Width = 1824f;
        private const float Height = 968f;
        private const float LeftWidth = 346f;
        private const int SortOrder = 30001;

        private ITheaterOperationsView operations;
        private ITheaterPriorityView priority;
        private ITheaterLogisticsView logistics;
        private IHighCommandView highCommand;
        private IActiveEventsView events;
        private ITheaterStrikePicture strikePicture;
        private Canvas canvas;
        private GameObject surface;
        private RectTransform planningFrame;
        private Vector2 fittedCanvasSize;
        private AvButton[] planButtons;
        private AvButton defenseButton;
        private TMP_Text[] planNames, planStates, stages;
        private TMP_Text defenseName, defenseState, title, subtitle, target, phase, next,
            waves, budget, spent, clock, committed, returned, delivery, stance, chest, reserve, staff;
        private TMP_Text dispatch, eventLine, eventEffect, forceNote, tacticEffort, tacticFire,
            tacticAxis, mapNote, commandSignal, mapPreviewLabel;
        private readonly TMP_Text[] forceNames = new TMP_Text[4], forceUnits = new TMP_Text[4],
            forceCosts = new TMP_Text[4], forceStates = new TMP_Text[4];
        private GameObject[] detailPages;
        private AvButton[] detailButtons;
        private Image[] planRails;
        private Image defenseRail, stageFill, staffRail;
        private Image postureFill, plotFix;
        private PlotGrid plotGrid;
        private GameObject plotAwaiting;
        private TMP_Text plotTitle, plotStatus, plotCoordinate, plotEffort, plotNote, plotAwaitingTitle,
            plotAwaitingDetail;
        private int selected;
        private RectTransform mapPreview;
        private Image mapPreviewImage;
        private float mapPreviewUntil;
        private float mapPreviewX, mapPreviewZ;
        private float nextRefresh;
        private bool keyboardTouched, keyboardWas, pauseWas;
        private static int closedFrame = -10;

        internal static bool IsOpen { get; private set; }
        internal static bool BlocksMap => IsOpen || Time.frameCount <= closedFrame + 1;

        internal static StrPlanningWindow Create(ITheaterOperationsView operations, ITheaterPriorityView priority,
            ITheaterLogisticsView logistics = null, IHighCommandView highCommand = null,
            IActiveEventsView events = null, ITheaterStrikePicture strikePicture = null)
        {
            var go = new GameObject("BoscaliStrategyWindow", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            var view = go.AddComponent<StrPlanningWindow>();
            view.operations = operations;
            view.priority = priority;
            view.logistics = logistics;
            view.highCommand = highCommand;
            view.events = events;
            view.strikePicture = strikePicture;
            view.canvas = go.GetComponent<Canvas>();
            view.canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            view.canvas.sortingOrder = SortOrder;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            view.Build();
            view.canvas.enabled = false;
            view.surface.SetActive(false);
            return view;
        }

        internal void Show(int selection)
        {
            selected = selection;
            FitRoom();
            canvas.enabled = true;
            surface.SetActive(true);
            if (!IsOpen)
            {
                pauseWas = GameplayUI.AllowPauseKeybind;
                GameplayUI.AllowPauseKeybind = false;
                keyboardTouched = Rewired.ReInput.isReady && Rewired.ReInput.controllers != null &&
                                  Rewired.ReInput.controllers.Keyboard != null;
                if (keyboardTouched)
                {
                    keyboardWas = Rewired.ReInput.controllers.Keyboard.enabled;
                    Rewired.ReInput.controllers.Keyboard.enabled = false;
                }
            }
            IsOpen = true;
            AvButton.ClearTooltip();
            Refresh();
        }

        internal void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            closedFrame = Time.frameCount;
            canvas.enabled = false;
            surface.SetActive(false);
            GameplayUI.AllowPauseKeybind = pauseWas;
            if (keyboardTouched && Rewired.ReInput.isReady && Rewired.ReInput.controllers != null &&
                Rewired.ReInput.controllers.Keyboard != null)
                Rewired.ReInput.controllers.Keyboard.enabled = keyboardWas;
            keyboardTouched = false;
            AvButton.ClearTooltip();
        }

        private void OnDestroy()
        {
            Close();
            if (mapPreview != null) Destroy(mapPreview.gameObject);
        }

        private void Update()
        {
            UpdateMapPreview();
            if (!IsOpen) return;
            FitRoom();
            if (Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + .25f;
            operations?.Refresh();
            priority?.Refresh();
            logistics?.Refresh();
            highCommand?.Refresh();
            Refresh();
        }

        private void FitRoom()
        {
            if (planningFrame == null) return;
            Rect canvasArea = ((RectTransform)transform).rect;
            float canvasW = canvasArea.width > 0f ? canvasArea.width : 1920f;
            float canvasH = canvasArea.height > 0f ? canvasArea.height : 1080f;
            if (Mathf.Abs(fittedCanvasSize.x - canvasW) < .5f &&
                Mathf.Abs(fittedCanvasSize.y - canvasH) < .5f) return;
            fittedCanvasSize = new Vector2(canvasW, canvasH);

            Rect fit = AvRoomFrame.WindowRect(canvasW, canvasH);
            float scale = Mathf.Min(1f, Mathf.Min(fit.width / Width, fit.height / Height));
            float shownW = Width * scale, shownH = Height * scale;
            float x = fit.x + (fit.width - shownW) * .5f;
            float y = Mathf.Max(fit.y, AvRoomFrame.NotchHeight * scale + 8f);
            if (y + shownH > canvasH - 8f)
                y = Mathf.Max(AvRoomFrame.NotchHeight * scale, canvasH - shownH - 8f);
            planningFrame.localScale = new Vector3(scale, scale, 1f);
            planningFrame.anchoredPosition = new Vector2(x + shownW * .5f,
                -(y + shownH * .5f));
        }

        private void Build()
        {
            var root = (RectTransform)transform;
            surface = new GameObject("StrategySurface", typeof(RectTransform));
            var full = (RectTransform)surface.transform;
            full.SetParent(root, false);
            AvKit.Stretch(full);
            Image shade = AvRoomFrame.CreateBackdrop(full, .72f);
            shade.gameObject.AddComponent<Button>().onClick.AddListener(Close);

            CanvasGroup frameGroup;
            var frame = AvRoomFrame.CreateFrame(full, "PlanningFrame", out frameGroup);
            planningFrame = frame;
            frameGroup.alpha = 1f;
            frame.sizeDelta = new Vector2(Width, Height);
            FitRoom();
            AvKit.Panel(frame, new Rect(0f, 0f, Width, Height), AvTheme.Ground, AvSprites.Panel)
                .raycastTarget = true;
            AvRoomFrame.CreateEdge(frame, new Rect(0f, 0f, 1f, Height), AvTheme.Frame);
            AvRoomFrame.CreateEdge(frame, new Rect(Width - 1f, 0f, 1f, Height), AvTheme.Frame);
            AvRoomFrame.CreateEdge(frame, new Rect(0f, -Height + 1f, Width, 1f), AvTheme.Frame);
            AvRoomFrame.CreateEdge(frame, new Rect(0f, 0f, Width, 1f), AvTheme.Frame);
            AvKit.Rule(frame, new Rect(28f, -82f, Width - 56f, 2f), AvTheme.RailInfo);

            // The room uses the same notch chrome and geometry as OPS, while Command
            // keeps its own close/input behavior.
            var notchObject = new GameObject("StrategyNotch", typeof(RectTransform));
            var notchRect = (RectTransform)notchObject.transform;
            notchRect.SetParent(frame, false);
            AvKit.Place(notchRect, new Rect(24f, 32f, 164f, AvRoomFrame.NotchHeight));
            AvRoomFrame.NotchChrome strategyNotch = AvRoomFrame.CreateNotchChrome(
                notchRect, "STRATEGY", "");
            AvRoomFrame.LayoutNotch(strategyNotch, 164f);
            strategyNotch.Fill.color = AvTheme.Surface;
            strategyNotch.ActiveBar.color = AvTheme.RailReady;
            strategyNotch.Label.color = AvTheme.TextPrimary;

            AvButton closeHit = AvKit.HitButton(frame,
                new Rect(Width - 156f, 32f, 132f, AvRoomFrame.NotchHeight), Close);
            closeHit.WithTooltip("Close the briefing (Esc).");
            AvRoomFrame.NotchChrome closeNotch = AvRoomFrame.CreateNotchChrome(
                (RectTransform)closeHit.transform, "× CLOSE", "ESC");
            AvRoomFrame.LayoutNotch(closeNotch, 132f);

            Label(frame, "THEATER / OPERATIONS ROOM", 28f, -20f, 850f, 30f, 23f, AvTheme.TextPrimary, true);
            Label(frame, "DIRECTOR'S WORKING BOARD  /  LIVE REPORT", 30f, -53f, 850f, 18f, 12f, AvTheme.Dim);
            AvKit.Rule(frame, new Rect(1060f, -22f, 3f, 38f), AvTheme.RailInfo);
            Label(frame, "OBSERVE / READ ONLY", 1076f, -23f, 364f, 19f, 13f,
                AvTheme.RailInfo, true);
            Label(frame, "ORDERS ARE SET ON STR", 1076f, -44f, 364f, 16f, 11f, AvTheme.Dim);

            AvKit.Panel(frame, new Rect(24f, -100f, LeftWidth, 808f), AvTheme.SurfaceInert);
            Label(frame, "OFFENSIVES", 42f, -116f, 220f, 24f, 16f, AvTheme.TextPrimary, true);
            Label(frame, "UP TO TWO STAFF PLANS", 42f, -142f, 278f, 16f, 11f, AvTheme.Dim);
            planButtons = new AvButton[2];
            planNames = new TMP_Text[2];
            planStates = new TMP_Text[2];
            planRails = new Image[2];
            for (int i = 0; i < 2; i++)
            {
                int slot = i;
                float y = -171f - i * 86f;
                var row = AvKit.Panel(frame, new Rect(42f, y, 310f, 72f), AvTheme.Surface);
                AvKit.Outline(frame, new Rect(42f, y, 310f, 72f), AvTheme.Hairline);
                planRails[i] = AvKit.Rule(frame, new Rect(42f, y, 3f, 72f), AvTheme.RailInert);
                planNames[i] = Label(frame, "—", 55f, y - 10f, 282f, 22f, 15f, AvTheme.TextPrimary, true);
                planStates[i] = Label(frame, "", 55f, y - 38f, 282f, 18f, 12f, AvTheme.Dim);
                planButtons[i] = AvKit.HitButton(frame, new Rect(42f, y, 310f, 72f), () => Select(slot));
                planButtons[i].WithTooltip("Preview this offensive's phase, target, funds and waves.");
                row.raycastTarget = false;
            }

            Label(frame, "DEFENSIVE POSTURE", 42f, -356f, 278f, 22f, 16f, AvTheme.TextPrimary, true);
            Label(frame, "THE DIRECTOR HOLDS ONE GUARD POINT", 42f, -382f, 290f, 16f, 11f, AvTheme.Dim);
            AvKit.Panel(frame, new Rect(42f, -411f, 310f, 82f), AvTheme.Surface);
            AvKit.Outline(frame, new Rect(42f, -411f, 310f, 82f), AvTheme.Hairline);
            defenseRail = AvKit.Rule(frame, new Rect(42f, -411f, 3f, 82f), AvTheme.RailCaution);
            defenseName = Label(frame, "—", 55f, -422f, 282f, 22f, 15f, AvTheme.TextPrimary, true);
            defenseState = Label(frame, "", 55f, -450f, 282f, 20f, 12f, AvTheme.Dim);
            defenseButton = AvKit.HitButton(frame, new Rect(42f, -411f, 310f, 82f), () => Select(-1));
            defenseButton.WithTooltip("Preview the director's current defensive posture.");

            Label(frame, "STANDING ORDERS", 42f, -520f, 278f, 22f, 15f, AvTheme.TextPrimary, true);
            stance = Label(frame, "STANCE —", 42f, -548f, 290f, 17f, 12f, AvTheme.Dim);
            chest = Label(frame, "PLAN CAP —", 42f, -574f, 290f, 17f, 12f, AvTheme.Dim);
            reserve = Label(frame, "POOL RESERVE —", 42f, -600f, 290f, 17f, 12f, AvTheme.Dim);
            Label(frame, "STAFF SIGNAL", 42f, -650f, 290f, 20f, 15f, AvTheme.TextPrimary, true);
            commandSignal = Label(frame, "—", 42f, -680f, 290f, 18f, 12f, AvTheme.Dim);
            Label(frame, "Orders lean the director's next review.", 42f, -705f, 300f, 18f, 12f, AvTheme.Dim);
            Label(frame, "DIRECTOR POSTURE", 42f, -752f, 290f, 17f, 12f, AvTheme.Dim, true);
            AvKit.Panel(frame, new Rect(42f, -782f, 290f, 8f), AvTheme.Ground);
            postureFill = AvKit.Panel(frame, new Rect(42f, -782f, 290f, 8f), AvTheme.RailInfo);
            postureFill.sprite = AvSprites.White;
            postureFill.type = Image.Type.Filled;
            postureFill.fillMethod = Image.FillMethod.Horizontal;
            postureFill.fillAmount = 0f;
            AvKit.Rule(frame, new Rect(187f, -778f, 1f, 16f), AvTheme.Frame);
            Label(frame, "DEFEND", 42f, -803f, 120f, 16f, 11f, AvTheme.Dim);
            Label(frame, "ATTACK", 248f, -803f, 84f, 16f, 11f, AvTheme.Dim);

            const float rightX = 392f;
            const float rightW = 822f;
            AvKit.Panel(frame, new Rect(rightX, -100f, rightW, 808f), AvTheme.SurfaceInert);
            title = Label(frame, "SELECT A PLAN", rightX + 22f, -119f, rightW - 44f, 31f, 22f, AvTheme.TextPrimary, true);
            subtitle = Label(frame, "", rightX + 22f, -159f, rightW - 44f, 20f, 12f, AvTheme.Dim);
            AvKit.Rule(frame, new Rect(rightX + 22f, -191f, rightW - 44f, 1f), AvTheme.Hairline);
            target = Label(frame, "TARGET —", rightX + 22f, -209f, rightW - 44f, 28f, 17f, AvTheme.TextPrimary);
            phase = Label(frame, "", rightX + 22f, -250f, rightW - 44f, 19f, 13f, AvTheme.Accent);
            stages = new TMP_Text[5];
            string[] steps = { "MUSTER", "PLAN", "TARGET", "H-HOUR", "PUSH" };
            for (int i = 0; i < steps.Length; i++)
                stages[i] = Label(frame, steps[i], rightX + 22f + i * 155f, -288f, 148f, 20f,
                    13f, AvTheme.Disabled, true);
            AvKit.Panel(frame, new Rect(rightX + 22f, -319f, 776f, 4f), AvTheme.Surface);
            stageFill = AvKit.Panel(frame, new Rect(rightX + 22f, -319f, 776f, 4f), AvTheme.Accent);
            stageFill.sprite = AvSprites.White;
            stageFill.type = Image.Type.Filled;
            stageFill.fillMethod = Image.FillMethod.Horizontal;
            stageFill.fillAmount = 0f;

            waves = Metric(frame, "WAVES", rightX + 22f, -355f);
            budget = Metric(frame, "ESCROW", rightX + 220f, -355f);
            spent = Metric(frame, "SPENT", rightX + 418f, -355f);
            clock = Metric(frame, "CLOCK", rightX + 616f, -355f);
            detailPages = new GameObject[3];
            detailButtons = new AvButton[3];
            string[] tabs = { "BRIEF", "FORCES", "TACTICS" };
            for (int i = 0; i < tabs.Length; i++)
            {
                int tab = i;
                detailButtons[i] = AvKit.Button(frame, tabs[i],
                    new Rect(rightX + 22f + i * 132f, -456f, 122f, 34f), () => SelectPage(tab), 13f);
                var page = new GameObject(tabs[i] + "Page", typeof(RectTransform));
                page.transform.SetParent(frame, false);
                AvKit.Stretch((RectTransform)page.transform);
                detailPages[i] = page;
            }
            BuildPlot(frame);

            var brief = (RectTransform)detailPages[0].transform;
            Label(brief, "NEXT / ASSESSMENT", rightX + 22f, -509f, 300f, 19f, 13f, AvTheme.Dim, true);
            next = Label(brief, "", rightX + 22f, -538f, rightW - 44f, 53f, 15f, AvTheme.TextPrimary);
            next.enableWordWrapping = true;
            AvKit.Rule(brief, new Rect(rightX + 22f, -602f, rightW - 44f, 1f), AvTheme.Hairline);
            dispatch = Label(brief, "", rightX + 22f, -614f, rightW - 44f, 25f, 13f, AvTheme.TextPrimary);
            eventLine = Label(brief, "", rightX + 22f, -644f, rightW - 44f, 24f, 13f, AvTheme.TextPrimary);
            eventEffect = Label(brief, "", rightX + 22f, -670f, rightW - 44f, 37f, 12f, AvTheme.Dim);
            eventEffect.enableWordWrapping = true;
            AvKit.Rule(brief, new Rect(rightX + 22f, -780f, rightW - 44f, 1f), AvTheme.Hairline);
            Label(brief, "COMMITTED", rightX + 22f, -798f, 116f, 18f, 12f, AvTheme.Dim, true);
            committed = Label(brief, "—", rightX + 143f, -798f, 170f, 18f, 13f, AvTheme.TextPrimary);
            Label(brief, "RETURNED", rightX + 350f, -798f, 110f, 18f, 12f, AvTheme.Dim, true);
            returned = Label(brief, "—", rightX + 470f, -798f, 260f, 18f, 13f, AvTheme.TextPrimary);

            var forces = (RectTransform)detailPages[1].transform;
            forceNote = Label(forces, "", rightX + 22f, -507f, rightW - 44f, 38f, 13f, AvTheme.Dim);
            Label(forces, "MISSION GROUP", rightX + 22f, -553f, 150f, 18f, 11f, AvTheme.Dim, true);
            Label(forces, "VEHICLES / COMPOSITION", rightX + 185f, -553f, 380f, 18f, 11f, AvTheme.Dim, true);
            Label(forces, "COST", rightX + 579f, -553f, 68f, 18f, 11f, AvTheme.Dim, true);
            Label(forces, "GATE", rightX + 653f, -553f, 122f, 18f, 11f, AvTheme.Dim, true);
            for (int i = 0; i < forceNames.Length; i++)
            {
                float y = -582f - 39f * i;
                AvKit.Panel(forces, new Rect(rightX + 22f, y, 776f, 35f), AvTheme.Surface);
                forceNames[i] = Label(forces, "—", rightX + 32f, y - 7f, 145f, 19f, 12f, AvTheme.TextPrimary, true);
                forceUnits[i] = Label(forces, "", rightX + 185f, y - 7f, 384f, 19f, 12f, AvTheme.Dim);
                forceCosts[i] = Label(forces, "", rightX + 579f, y - 7f, 68f, 19f, 12f, AvTheme.TextPrimary);
                forceStates[i] = Label(forces, "", rightX + 653f, y - 7f, 123f, 19f, 11f, AvTheme.Dim, true);
                foreach (TMP_Text cell in new[] { forceNames[i], forceUnits[i], forceCosts[i], forceStates[i] })
                {
                    cell.enableWordWrapping = false;
                    cell.overflowMode = TextOverflowModes.Ellipsis;
                }
            }
            delivery = Label(forces, "", rightX + 22f, -822f, rightW - 44f, 20f, 11f, AvTheme.Dim);

            var tactics = (RectTransform)detailPages[2].transform;
            Label(tactics, "TACTICAL PLAYBOOK  /  VERIFIED EFFECTS", rightX + 22f, -507f,
                rightW - 44f, 20f, 12f, AvTheme.Dim, true);
            tacticEffort = TacticRow(tactics, rightX, -540f, "01  MAIN EFFORT");
            tacticFire = TacticRow(tactics, rightX, -603f, "02  FIRE PREPARATION");
            tacticAxis = TacticRow(tactics, rightX, -666f, "03  STAFF MANEUVER");
            mapNote = Label(tactics, "", rightX + 22f, -822f, rightW - 44f, 22f, 11f, AvTheme.Dim);

            AvKit.Rule(frame, new Rect(28f, -925f, Width - 56f, 1f), AvTheme.Hairline);
            staffRail = AvKit.Rule(frame, new Rect(30f, -936f, 3f, 22f), AvTheme.RailInert);
            staff = Label(frame, "STAFF LOG —", 42f, -936f, Width - 72f, 22f, 12f, AvTheme.Dim);
            SelectPage(0);
        }

        private static TMP_Text Label(RectTransform parent, string value, float x, float y, float w,
            float h, float size, Color color, bool bold = false) =>
            AvKit.Label(parent, value, new Rect(x, y, w, h), color, size,
                bold ? FontStyles.Bold : FontStyles.Normal, TextAlignmentOptions.MidlineLeft);

        private static TMP_Text Metric(RectTransform parent, string key, float x, float y)
        {
            AvKit.Panel(parent, new Rect(x, y, 184f, 83f), AvTheme.Surface);
            Label(parent, key, x + 13f, y - 10f, 158f, 18f, 12f, AvTheme.Dim, true);
            return Label(parent, "—", x + 13f, y - 35f, 158f, 34f, 22f, AvTheme.TextPrimary, true);
        }

        private static TMP_Text TacticRow(RectTransform parent, float x, float y, string heading)
        {
            AvKit.Panel(parent, new Rect(x + 22f, y, 776f, 55f), AvTheme.Surface);
            AvKit.Rule(parent, new Rect(x + 22f, y, 3f, 55f), AvTheme.RailInfo);
            Label(parent, heading, x + 35f, y - 5f, 735f, 17f, 12f, AvTheme.TextPrimary, true);
            return Label(parent, "—", x + 35f, y - 27f, 735f, 19f, 12f, AvTheme.Dim);
        }

        private void BuildPlot(RectTransform frame)
        {
            const float x = 1236f;
            const float width = 564f;
            AvKit.Panel(frame, new Rect(x, -100f, width, 808f), AvTheme.SurfaceInert);
            AvKit.Rule(frame, new Rect(x, -100f, 3f, 808f), AvTheme.RailInfo);
            Label(frame, "OBJECTIVE PLOT", x + 24f, -118f, 280f, 23f, 17f,
                AvTheme.TextPrimary, true);
            Label(frame, "LOCAL FIX / READ ONLY", x + 306f, -119f, 234f, 20f, 12f,
                AvTheme.Dim);
            plotTitle = Label(frame, "SELECT A PLAN", x + 24f, -150f, width - 48f, 19f,
                12f, AvTheme.RailInfo, true);

            Rect field = new Rect(x + 24f, -180f, width - 48f, 420f);
            AvKit.Panel(frame, field, AvTheme.Ground);
            AvKit.Outline(frame, field, AvTheme.Hairline);
            var plotObject = new GameObject("SchematicPlot", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(PlotGrid));
            var grid = (RectTransform)plotObject.transform;
            grid.SetParent(frame, false);
            AvKit.Place(grid, field);
            plotGrid = plotObject.GetComponent<PlotGrid>();
            plotGrid.raycastTarget = false;

            // The coordinate grid only appears for a unique locally known objective. An
            // unresolved selection gets a designed holding card instead of a false plot.
            plotAwaiting = new GameObject("AwaitingMapFix", typeof(RectTransform));
            var awaiting = (RectTransform)plotAwaiting.transform;
            awaiting.SetParent(frame, false);
            AvKit.Place(awaiting, field);
            AvKit.Rule(awaiting, new Rect(18f, -18f, 48f, 2f), AvTheme.RailCaution);
            Label(awaiting, "PLOT / HELD", 76f, -10f, 200f, 20f, 13f,
                AvTheme.RailCaution, true);
            Label(awaiting, "UNVERIFIED", field.width - 147f, -10f, 128f, 20f, 12f,
                AvTheme.Dim, true);
            AvKit.Rule(awaiting, new Rect(18f, -49f, field.width - 36f, 1f), AvTheme.Hairline);
            AvKit.Rule(awaiting, new Rect(field.width * .5f - 32f, -135f, 64f, 1f),
                AvTheme.RailInfo);
            AvKit.Rule(awaiting, new Rect(field.width * .5f, -104f, 1f, 63f),
                AvTheme.RailInfo);
            plotAwaitingTitle = Label(awaiting, "NO VERIFIED FIX", 32f, -205f,
                field.width - 64f, 34f, 22f, AvTheme.TextPrimary, true);
            plotAwaitingTitle.alignment = TextAlignmentOptions.Center;
            plotAwaitingDetail = Label(awaiting, "", 58f, -256f,
                field.width - 116f, 66f, 13f, AvTheme.Dim);
            plotAwaitingDetail.alignment = TextAlignmentOptions.Center;
            plotAwaitingDetail.enableWordWrapping = true;
            AvKit.Rule(awaiting, new Rect(18f, -355f, field.width - 36f, 1f), AvTheme.Hairline);
            Label(awaiting, "LOCAL INTEL REQUIRED  /  NO ROUTE ASSUMED", 28f, -372f,
                field.width - 56f, 18f, 11f, AvTheme.Dim);

            // One location marker means a uniquely resolved objective, not a drawn route.
            plotFix = AvKit.Panel(frame, new Rect(x + width * .5f - 8f, -381f, 16f, 16f),
                AvTheme.RailCaution, AvSprites.White);
            plotFix.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            plotFix.raycastTarget = false;
            plotFix.enabled = false;
            Label(grid, "+Z", field.width * .5f - 12f, -8f, 32f, 16f, 11f, AvTheme.Dim);
            Label(grid, "+X", field.width - 32f, -213f, 32f, 16f, 11f, AvTheme.Dim);
            Label(grid, "SCHEMATIC · NO ROUTE PROJECTION", 14f, -388f,
                field.width - 28f, 18f, 11f, AvTheme.Dim);

            plotStatus = Label(frame, "NO VERIFIED FIX", x + 24f, -626f,
                width - 48f, 26f, 18f, AvTheme.RailCaution, true);
            plotCoordinate = Label(frame, "POSITION UNAVAILABLE", x + 24f, -661f,
                width - 48f, 22f, 13f, AvTheme.Dim);
            AvKit.Rule(frame, new Rect(x + 24f, -701f, width - 48f, 1f), AvTheme.Hairline);
            plotEffort = Label(frame, "MAIN EFFORT / UNASSIGNED", x + 24f, -718f,
                width - 48f, 22f, 13f, AvTheme.TextPrimary);
            plotNote = Label(frame, "Select a plan or guard point to inspect its map fix.",
                x + 24f, -750f, width - 48f, 54f, 13f, AvTheme.Dim);
            plotNote.enableWordWrapping = true;
            AvKit.Button(frame, "VIEW THEATER MAP", new Rect(x + 24f, -830f, width - 48f, 46f),
                    ViewMap, 15f, AvButtonStyle.Primary)
                .WithTooltip("Close the desk. A unique locally known objective receives a temporary map pin.");
            Label(frame, "A MAP PIN LASTS 20s  /  NO ORDER IS ISSUED", x + 24f, -886f,
                width - 48f, 16f, 11f, AvTheme.Dim);
        }

        private void SelectPage(int page)
        {
            for (int i = 0; i < detailPages.Length; i++)
            {
                detailPages[i].SetActive(i == page);
                detailButtons[i].SetLatched(i == page);
            }
            RefreshDetails();
        }

        private void ViewMap()
        {
            TheaterPriorityOption match = SelectedObjective();
            if (match != null)
            {
                var map = SceneSingleton<DynamicMap>.i;
                if (map != null && map.iconLayer != null)
                {
                    if (mapPreview == null)
                    {
                        var marker = new GameObject("BoscaliPlanPreviewMarker", typeof(RectTransform), typeof(Image));
                        mapPreview = (RectTransform)marker.transform;
                        mapPreview.sizeDelta = new Vector2(17f, 17f);
                        mapPreview.localRotation = Quaternion.Euler(0f, 0f, 45f);
                        mapPreviewImage = marker.GetComponent<Image>();
                        mapPreviewImage.raycastTarget = false;
                        mapPreviewLabel = Label(mapPreview, "PLAN", 18f, 0f, 70f, 20f,
                            11f, AvTheme.Accent, true);
                        mapPreviewLabel.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -45f);
                    }
                    mapPreview.SetParent(map.iconLayer.transform, false);
                    mapPreviewX = match.X;
                    mapPreviewZ = match.Z;
                    mapPreviewImage.color = AvTheme.Accent;
                    mapPreviewLabel.text = selected == -1 ? "GUARD" : "PLAN";
                    mapPreviewUntil = Time.unscaledTime + 20f;
                    UpdateMapPreview();
                }
            }
            Close();
        }

        private TheaterPriorityOption SelectedObjective()
        {
            string label = null;
            if (selected == -1) label = operations?.Direction?.DefenseLabel;
            else
            {
                IReadOnlyList<TheaterOperationView> plans = operations?.Operations;
                if (plans != null && selected >= 0 && selected < plans.Count)
                    label = plans[selected]?.TargetLabel;
            }
            // The plan wire has a display label, not a map point. Resolve only a unique,
            // locally visible active objective; never guess a hidden or duplicate position.
            TheaterPriorityOption match = null;
            IReadOnlyList<TheaterPriorityOption> options = priority != null && priority.Available
                ? priority.Options : null;
            if (!string.IsNullOrEmpty(label) && options != null)
                for (int i = 0; i < options.Count; i++)
                {
                    TheaterPriorityOption option = options[i];
                    if (option == null || !string.Equals(option.Label, label, StringComparison.Ordinal) ||
                        float.IsNaN(option.X) || float.IsNaN(option.Z) ||
                        float.IsInfinity(option.X) || float.IsInfinity(option.Z)) continue;
                    if (match != null) { match = null; break; }
                    match = option;
                }
            return match;
        }

        private void UpdateMapPreview()
        {
            if (mapPreview == null) return;
            var map = SceneSingleton<DynamicMap>.i;
            if (Time.unscaledTime >= mapPreviewUntil || map == null || map.mapImage == null ||
                map.iconLayer == null)
            {
                Destroy(mapPreview.gameObject);
                mapPreview = null;
                return;
            }
            float scale = map.mapImage.transform.localScale.x;
            if (scale > 0f && !float.IsNaN(scale) && !float.IsInfinity(scale))
                mapPreview.localScale = Vector3.one / scale;
            Vector3 world = new GlobalPosition(mapPreviewX, 0f, mapPreviewZ).AsVector3();
            mapPreview.localPosition = new Vector3(world.x, world.z, 0f);
        }

        private void Select(int index)
        {
            selected = index;
            Refresh();
        }

        internal void Refresh()
        {
            bool available = operations != null && operations.Available;
            IReadOnlyList<TheaterOperationView> plans = available ? operations.Operations : null;
            int count = plans != null ? Mathf.Min(plans.Count, 2) : 0;
            for (int i = 0; i < planButtons.Length; i++)
            {
                TheaterOperationView plan = i < count ? plans[i] : null;
                planNames[i].text = plan != null ? "OPERATION " + plan.Name : "NO ACTIVE PUSH";
                planStates[i].text = plan != null
                    ? TheaterReadout.OffensivePhaseWord(plan.Phase, plan.Outcome) + "  ·  " +
                      plan.WavesLaunched + " / " + plan.WavesPlanned + " WAVES"
                    : i == 0 ? "THE STAFF IS REVIEWING THE THEATER" : "SLOT AVAILABLE";
                planRails[i].color = plan == null ? AvTheme.RailInert :
                    AvStyleHost.Resolve(AvStyleHost.Style("rail " +
                    TheaterReadout.OffensiveRail(plan.Phase, plan.Outcome)).Background, AvTheme.RailInert);
                planButtons[i].SetEnabled(plan != null);
                planNames[i].color = selected == i && plan != null ? AvTheme.Accent : AvTheme.TextPrimary;
            }

            TheaterDirectionView direction = available ? operations.Direction : null;
            bool defending = direction != null && !string.IsNullOrEmpty(direction.DefenseLabel);
            defenseName.text = defending ? direction.DefenseLabel.ToUpperInvariant() : "NO ACTIVE DEFENSE";
            defenseState.text = defending
                ? direction.EffortIsDefense ? "MAIN EFFORT  ·  DEFENSE" : "GUARDING  ·  SUPPORT IN RESERVE"
                : "THE STAFF IS WATCHING HELD GROUND";
            defenseRail.color = defending ? AvTheme.RailCaution : AvTheme.RailInert;
            defenseButton.SetEnabled(defending);
            defenseName.color = selected == -1 && defending ? AvTheme.Accent : AvTheme.TextPrimary;

            TheaterInfluenceView influence = available ? operations.Influence : null;
            stance.text = influence == null ? "STANCE —" : "STANCE  " +
                Mathf.RoundToInt(influence.Stance * 100f) + "% ATTACK" +
                (influence.HoldOffense ? "  ·  HELD" : "");
            chest.text = influence == null ? "PLAN CAP —" : "PLAN CAP  " +
                UnitConverter.ValueReading(influence.MaxEscrowPerPlan);
            reserve.text = influence == null ? "POOL RESERVE —" : "POOL RESERVE  " +
                UnitConverter.ValueReading(influence.ReserveFloor);
            if (postureFill.enabled != (influence != null)) postureFill.enabled = influence != null;
            postureFill.fillAmount = influence == null ? 0f : Mathf.Clamp01(influence.Stance);
            postureFill.color = influence != null && influence.HoldOffense
                ? AvTheme.RailCaution : AvTheme.RailInfo;
            staff.text = available && operations.StaffLog != null && operations.StaffLog.Count > 0
                ? "STAFF LOG   ·   " + operations.StaffLog[0]
                : "STAFF LOG   ·   NO RECENT REPORT";
            bool contact = staff.text.Contains("FRONT UNDER FIRE") || staff.text.Contains("H-HOUR");
            bool movement = staff.text.Contains("WAVE ") || staff.text.Contains("FORTIFIED APPROACH");
            staff.color = contact ? AvTheme.Warning : movement ? AvTheme.TextPrimary : AvTheme.Dim;
            staffRail.color = contact ? AvTheme.RailDanger : movement ? AvTheme.RailInfo : AvTheme.RailInert;

            if (selected == -1 && defending) { ShowDefense(direction); return; }
            if (selected < 0 || selected >= count || plans[selected] == null)
            {
                if (count > 0) { selected = 0; Refresh(); return; }
                ShowEmpty(available);
                return;
            }
            ShowPlan(plans[selected]);
        }

        private void ShowPlan(TheaterOperationView plan)
        {
            title.text = "OPERATION " + plan.Name;
            subtitle.text = plan.Phase == TheaterOperationPhase.Assault
                ? plan.WavesLaunched > 0
                    ? "LIVE ASSAULT  /  WAVES ON THE ROAD  /  HOST REPORT"
                    : "LIVE ASSAULT  /  FIRST WAVE AWAITING SUPPLY  /  HOST REPORT"
                : "OFFENSIVE PLAN  /  DIRECTOR CONTROLLED  /  LIVE HOST REPORT";
            target.text = string.IsNullOrEmpty(plan.TargetLabel)
                ? "TARGET   AWAITING STAFF SELECTION" : "TARGET   " + plan.TargetLabel.ToUpperInvariant();
            phase.text = TheaterReadout.OffensivePhaseWord(plan.Phase, plan.Outcome) +
                (plan.IsHeld ? "  ·  MAIN EFFORT HELD" : "");
            Color mood = plan.Phase == TheaterOperationPhase.Concluded
                ? plan.Outcome == TheaterOperationOutcome.ObjectiveSecured ? AvTheme.RailReady
                    : plan.Outcome == TheaterOperationOutcome.ObjectiveLost ||
                      plan.Outcome == TheaterOperationOutcome.Stalled ? AvTheme.RailDanger
                    : AvTheme.Dim
                : plan.Phase == TheaterOperationPhase.Assault ||
                  plan.Phase == TheaterOperationPhase.Launching ||
                  plan.Phase == TheaterOperationPhase.Holding ? AvTheme.RailCaution
                : AvTheme.RailInfo;
            phase.color = mood;
            stageFill.color = mood;
            int reached = TheaterReadout.OffensiveStage(plan.Phase);
            SetStages(reached, plan.Phase == TheaterOperationPhase.Concluded);
            waves.text = plan.WavesLaunched + " / " + plan.WavesPlanned;
            budget.text = UnitConverter.ValueReading(plan.Budget);
            spent.text = UnitConverter.ValueReading(plan.Spent);
            committed.text = UnitConverter.ValueReading(plan.Committed);
            returned.text = plan.Phase == TheaterOperationPhase.Concluded
                ? UnitConverter.ValueReading(plan.Returned) : "—";
            delivery.text = "DELIVERY  /  VANILLA SUPPLY QUEUE";
            clock.text = plan.Phase == TheaterOperationPhase.Launching
                ? "H-" + Mathf.CeilToInt(Mathf.Max(0f, plan.Countdown)) + "s"
                : plan.Phase >= TheaterOperationPhase.Assault ? TheaterReadout.Clock(plan.ElapsedSeconds) : "—";
            next.text = PlanPreview(plan);
            RefreshDetails();
            RefreshPlot();
        }

        private void ShowDefense(TheaterDirectionView direction)
        {
            title.text = "DEFENSE / " + direction.DefenseLabel.ToUpperInvariant();
            subtitle.text = "DIRECTOR GUARD POINT  /  LIVE HOST REPORT";
            target.text = "GUARDING   " + direction.DefenseLabel.ToUpperInvariant();
            phase.text = direction.EffortIsDefense ? "MAIN EFFORT IS DEFENSE" : "DEFENSE IN SUPPORT";
            phase.color = AvTheme.RailCaution;
            SetStages(0, true);
            waves.text = "—";
            budget.text = "—";
            spent.text = "—";
            clock.text = "—";
            committed.text = returned.text = "—";
            delivery.text = "DEFENSE  /  HOST-FUNDED SHIELD CONVOY";
            next.text = direction.EffortIsDefense
                ? "The director is holding the main effort here. Defensive funding and the next posture review are host decisions."
                : "The director is guarding this point while another objective holds the main effort. Change stance or hold from STR to influence its next review.";
            RefreshDetails();
            RefreshPlot();
        }

        private void ShowEmpty(bool available)
        {
            title.text = available ? "NO OFFENSIVE UNDER WAY" : "THEATER DIRECTOR UNAVAILABLE";
            subtitle.text = available ? "STAFF REVIEWING THE THEATER" : "NO VERIFIED PLAN STATE";
            target.text = "TARGET   —";
            phase.text = available ? "AWAITING STAFF DECISION" : "UNAVAILABLE";
            phase.color = available ? AvTheme.RailInfo : AvTheme.RailInert;
            SetStages(0, true);
            waves.text = budget.text = spent.text = clock.text = "—";
            committed.text = returned.text = "—";
            delivery.text = "DELIVERY  /  NO ACTIVE PLAN";
            next.text = available
                ? "Stance, hold, plan cap and axis leans on STR influence what the director may open next."
                : "The theater operations service has not provided a plan. The briefing will update when it becomes available.";
            RefreshDetails();
            RefreshPlot();
        }

        private void RefreshPlot()
        {
            TheaterPriorityOption objective = SelectedObjective();
            bool fixedPoint = objective != null;
            plotGrid.gameObject.SetActive(fixedPoint);
            plotAwaiting.SetActive(!fixedPoint);
            if (plotFix.enabled != fixedPoint) plotFix.enabled = fixedPoint;
            if (!fixedPoint)
            {
                bool hasSelection = selected == -1
                    ? !string.IsNullOrEmpty(operations?.Direction?.DefenseLabel)
                    : operations?.Operations != null && selected >= 0 &&
                      selected < operations.Operations.Count && operations.Operations[selected] != null;
                plotAwaitingTitle.text = hasSelection ? "FIX UNRESOLVED" : "NO PLAN TO PLOT";
                plotAwaitingDetail.text = hasSelection
                    ? "The selected target has no unique, locally known map position. Its label is not a coordinate."
                    : "The director has not supplied an active objective. The theater map remains available.";
            }
            plotTitle.text = selected == -1 ? "DEFENSIVE GUARD POINT"
                : fixedPoint ? "SELECTED OFFENSIVE / LOCAL FIX"
                : "OFFENSIVE / MAP INTELLIGENCE";
            plotStatus.text = fixedPoint ? objective.Label.ToUpperInvariant() : "NO VERIFIED MAP FIX";
            plotStatus.color = fixedPoint ? AvTheme.TextPrimary : AvTheme.RailCaution;
            plotCoordinate.text = fixedPoint
                ? "MAP X " + objective.X.ToString("+0;-0;0") + "  /  Z " +
                  objective.Z.ToString("+0;-0;0")
                : "POSITION UNAVAILABLE";
            plotCoordinate.color = fixedPoint ? AvTheme.RailInfo : AvTheme.Dim;
            plotEffort.text = priority != null && priority.Available && priority.HasPriority &&
                              !string.IsNullOrEmpty(priority.PriorityLabel)
                ? "MAIN EFFORT / " + priority.PriorityLabel.ToUpperInvariant()
                : "MAIN EFFORT / UNASSIGNED";
            plotNote.text = fixedPoint
                ? "VIEW MAP marks this locally known objective for 20 seconds. No order is issued."
                : "VIEW MAP opens the theater without a pin. This selection has no unique local objective fix.";
        }

        private void RefreshDetails()
        {
            if (detailPages == null || detailPages.Length == 0 || tacticEffort == null) return;
            IReadOnlyList<TheaterOperationView> plans = operations != null && operations.Available
                ? operations.Operations : null;
            TheaterOperationView plan = selected >= 0 && plans != null && selected < plans.Count
                ? plans[selected] : null;
            bool defense = selected == -1 && operations?.Direction != null &&
                           !string.IsNullOrEmpty(operations.Direction.DefenseLabel);

            CommanderView lead = null;
            IReadOnlyList<CommanderView> commanders = highCommand != null && highCommand.Available
                ? highCommand.Commanders : null;
            if (commanders != null)
                for (int i = 0; i < commanders.Count; i++)
                {
                    CommanderView commander = commanders[i];
                    if (commander == null || !commander.IsFriendly || commander.IsKia) continue;
                    if (lead == null || commander.Tier < lead.Tier) lead = commander;
                }
            dispatch.text = lead != null
                ? "COMMAND DESK  /  " + lead.Name + "  ·  " + lead.Role
                : "COMMAND DESK  /  STAFF POST UNAVAILABLE";
            dispatch.color = lead != null && (lead.Disrupted || lead.InTransit)
                ? AvTheme.Warning : AvTheme.TextPrimary;
            commandSignal.text = highCommand != null && highCommand.Available
                ? "FRIENDLY COHESION  " + Mathf.RoundToInt(highCommand.FriendlyCohesion * 100f) + "%"
                : "CHAIN OF COMMAND UNAVAILABLE";

            ActiveEventView current = events != null && events.Available ? events.Current : null;
            eventLine.text = current != null
                ? "THEATER EVENT  /  " + current.Title.ToUpperInvariant() + "  ·  " + current.Tier
                : "THEATER EVENT  /  NO ACTIVE INCIDENT";
            eventEffect.text = current != null
                ? current.Target + "  ·  " + current.EffectSummary + "  ·  " + current.FlavorText
                : "The operations room is working from the standing theater picture.";
            eventLine.color = current != null ? AvTheme.Warning : AvTheme.Dim;

            IReadOnlyList<ReinforcementOption> groups = logistics != null && logistics.Available
                ? logistics.Reinforcements : null;
            bool observed = groups != null && groups.Count > 0;
            forceNote.text = !observed
                ? "MISSION FORCE CATALOG  /  NO GROUPS OBSERVED"
                : !logistics.CanCommand ? "MISSION GROUPS  /  COST AND READINESS ARE HOST-ONLY"
                : defense ? "DEFENSE  /  THE DIRECTOR CAN FUND THE CHEAPEST READY SHIELD GROUP"
                : plan != null ? "OFFENSE  /  ESCROW FUNDS WAVES; POOL FUNDS MANUAL CALLS"
                : "MISSION FORCE CATALOG  /  NO WAVE IS COMMITTED";
            int candidate = -1;
            float candidateCost = defense ? float.MaxValue : -1f;
            if (observed && logistics.CanCommand &&
                (defense || plan != null && plan.Phase != TheaterOperationPhase.Concluded))
                for (int i = 0; i < groups.Count; i++)
                {
                    ReinforcementOption group = groups[i];
                    if (group == null || float.IsNaN(group.Cost) ||
                        float.IsInfinity(group.Cost) || group.Cost < 0f ||
                        group.CooldownSeconds > 0f || defense && !group.Ready) continue;
                    if (!defense && (group.Cost > plan.Budget || group.Cost <= candidateCost)) continue;
                    if (defense && (group.Cost >= candidateCost ||
                                    group.Cost > (logistics.FactionFunds -
                                                  (operations.Influence?.ReserveFloor ?? 0f)))) continue;
                    candidate = i;
                    candidateCost = group.Cost;
                }
            for (int row = 0; row < forceNames.Length; row++)
            {
                int index = row == 0 && candidate >= 0 ? candidate
                    : row <= candidate && candidate >= 0 ? row - 1 : row;
                ReinforcementOption group = observed && index < groups.Count ? groups[index] : null;
                forceNames[row].text = group != null ? group.Label : "—";
                forceUnits[row].text = group != null ? group.Detail : "";
                forceCosts[row].text = group == null || !logistics.CanCommand ? ""
                    : float.IsNaN(group.Cost) ? "—" : UnitConverter.ValueReading(group.Cost);
                forceStates[row].text = group == null ? ""
                    : index == candidate ? "CANDIDATE"
                    : !logistics.CanCommand ? "HOST ONLY"
                    : group.CooldownSeconds > 0f ? "COOLDOWN"
                    : defense ? group.Ready ? "READY" : "UNFUNDED"
                    : plan != null && group.Cost > plan.Budget ? "OVER ESCROW" : "READY";
                forceStates[row].color = index == candidate ? AvTheme.Accent : AvTheme.Dim;
            }
            delivery.text = observed && logistics.CanCommand
                ? "CANDIDATE IS ADVISORY  ·  ACTUAL WAVE ENTERS THE VANILLA SUPPLY QUEUE AT RELEASE"
                : "GROUP COMPOSITION IS LOCAL MISSION DATA  ·  HOST EXECUTION IS REPLICATED IN THE PLAN";

            string effort = priority != null && priority.Available && priority.HasPriority
                ? priority.PriorityLabel : null;
            tacticEffort.text = defense && operations.Direction.EffortIsDefense
                ? "GUARD  /  " + operations.Direction.DefenseLabel.ToUpperInvariant() +
                  "  ·  the director holds the main effort on this line"
                : !string.IsNullOrEmpty(effort)
                ? "LIVE  /  " + effort.ToUpperInvariant() + "  ·  unassigned ground and mission AI air follow the effort"
                : "WAITING  /  the director has not committed a main effort";
            TheaterPriorityOption objective = SelectedObjective();
            string strikeLabel = null;
            float strikeEta = 0f;
            bool fireInArea = objective != null && strikePicture != null &&
                strikePicture.TryGetNear(objective.X, objective.Z, 2000f,
                    out strikeLabel, out strikeEta);
            tacticFire.text = fireInArea
                ? "IN AREA  /  " + strikeLabel +
                  (strikeEta > 0f ? " INBOUND T-" + Mathf.CeilToInt(strikeEta) + "s"
                      : strikeLabel == "KINETIC ROD" ? " IMPACT REPORTED" : " EFFECT ACTIVE")
                : current != null
                    ? "OPS missile / flare calls remain pilot-led  ·  LOCAL COST ×" +
                      events.SupportCostMultiplier.ToString("0.##")
                    : "OPS missile / flare calls remain pilot-led; no fire mission is booked by this plan";
            int axes = operations?.Influence?.Axes?.Count ?? 0;
            bool shock = plan != null && plan.WavesPlanned >= 3 &&
                         highCommand != null && highCommand.Available &&
                         highCommand.FriendlyCohesion >= .65f;
            tacticAxis.text = defense
                ? "SHIELD CONVOY  /  the director can fund the cheapest ready group above reserve"
                : shock && plan.Phase == TheaterOperationPhase.Launching
                ? "SHOCK PUSH POSSIBLE  /  cohesive staff can release two distinct funded groups at H-hour"
                : plan != null && plan.WavesLaunched >= 2 &&
                  (plan.Phase == TheaterOperationPhase.Assault || plan.Phase == TheaterOperationPhase.Holding)
                ? plan.WavesLaunched + " WAVES ON THE ROAD  /  staff reports confirm delivery"
                : axes > 0
                    ? axes + " standing axis lean" + (axes == 1 ? "" : "s") +
                      " shape the next review; two pushes use the main effort in turn"
                    : "Set axis leans on STR to shape the next review; two pushes use the effort in turn";
            mapNote.text = objective == null
                ? "No unique active map point is known for this selection; VIEW MAP opens the theater."
                : !string.IsNullOrEmpty(effort)
                    ? "VIEW MAP pins this objective briefly and reveals the live effort diamond."
                    : "VIEW MAP pins this objective briefly; no effort marker is active yet.";
        }

        private void SetStages(int reached, bool hidden)
        {
            for (int i = 0; i < stages.Length; i++)
                stages[i].color = hidden ? AvTheme.Disabled :
                    i < reached ? AvTheme.Dim : i == reached ? AvTheme.TextPrimary : AvTheme.Disabled;
            stageFill.fillAmount = hidden ? 0f : reached / 4f;
        }

        private static string PlanPreview(TheaterOperationView plan)
        {
            switch (plan.Phase)
            {
                case TheaterOperationPhase.Mustering:
                    return "The staff is gathering the funded waves. It will plan the push before naming an objective.";
                case TheaterOperationPhase.Planning:
                    return "The staff is shaping the push. " + plan.WavesPlanned +
                           " wave slots are funded; the target remains subject to its theater review.";
                case TheaterOperationPhase.AwaitingTarget:
                    return "The plan is ready. The director must name an active objective before H-hour can begin.";
                case TheaterOperationPhase.Launching:
                    return plan.IsHeld
                        ? "Launch is held because " + plan.Holder + " owns the main effort. The countdown is frozen."
                        : "At H-hour the director names this target as the main effort and releases the first funded wave through the mission's convoy queue.";
                case TheaterOperationPhase.Assault:
                    return "Waves already delivered: " + plan.WavesLaunched + " of " + plan.WavesPlanned +
                           ". Remaining funded waves follow the staff schedule while the objective stays active.";
                case TheaterOperationPhase.Holding:
                    return "The funded waves are on the road. The director may fund another wave during the hold; otherwise the push concludes.";
                default:
                    return TheaterReadout.OffensiveOutcomeWord(plan.Outcome) + ". " +
                           UnitConverter.ValueReading(plan.Spent) + " spent; " +
                           UnitConverter.ValueReading(plan.Returned) + " returned to the faction pool.";
            }
        }

        /// <summary>A single static canvas mesh for the plotting grid and range rings.</summary>
        private sealed class PlotGrid : MaskableGraphic
        {
            protected override void OnPopulateMesh(VertexHelper mesh)
            {
                mesh.Clear();
                Rect r = rectTransform.rect;
                Color32 minor = AvTheme.RailInfo.WithAlpha(.10f);
                Color32 major = AvTheme.RailInfo.WithAlpha(.27f);
                Color32 axis = AvTheme.RailInfo.WithAlpha(.50f);
                for (int i = 1; i < 16; i++)
                {
                    float x = r.xMin + r.width * i / 16f;
                    DrawLine(mesh, new Vector2(x, r.yMin), new Vector2(x, r.yMax),
                        i % 4 == 0 ? major : minor, 1f);
                }
                for (int i = 1; i < 12; i++)
                {
                    float y = r.yMin + r.height * i / 12f;
                    DrawLine(mesh, new Vector2(r.xMin, y), new Vector2(r.xMax, y),
                        i % 3 == 0 ? major : minor, 1f);
                }
                Vector2 center = r.center;
                float reach = Mathf.Min(r.width, r.height) * .48f;
                for (int ring = 1; ring <= 3; ring++)
                {
                    float radius = reach * ring / 3f;
                    Vector2 previous = center + new Vector2(radius, 0f);
                    for (int segment = 1; segment <= 48; segment++)
                    {
                        float angle = segment * Mathf.PI * 2f / 48f;
                        Vector2 point = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                        DrawLine(mesh, previous, point, ring == 3 ? major : minor, 1f);
                        previous = point;
                    }
                }
                DrawLine(mesh, center + Vector2.left * 30f, center + Vector2.left * 12f, axis, 1.5f);
                DrawLine(mesh, center + Vector2.right * 12f, center + Vector2.right * 30f, axis, 1.5f);
                DrawLine(mesh, center + Vector2.up * 12f, center + Vector2.up * 30f, axis, 1.5f);
                DrawLine(mesh, center + Vector2.down * 30f, center + Vector2.down * 12f, axis, 1.5f);
            }

            private static void DrawLine(VertexHelper mesh, Vector2 a, Vector2 b,
                Color32 tint, float width)
            {
                Vector2 delta = b - a;
                if (delta.sqrMagnitude < .01f) return;
                Vector2 normal = new Vector2(-delta.y, delta.x).normalized * (width * .5f);
                int index = mesh.currentVertCount;
                Add(mesh, a - normal, tint);
                Add(mesh, a + normal, tint);
                Add(mesh, b + normal, tint);
                Add(mesh, b - normal, tint);
                mesh.AddTriangle(index, index + 1, index + 2);
                mesh.AddTriangle(index, index + 2, index + 3);
            }

            private static void Add(VertexHelper mesh, Vector2 point, Color32 tint)
            {
                UIVertex vertex = UIVertex.simpleVert;
                vertex.position = point;
                vertex.color = tint;
                mesh.AddVert(vertex);
            }
        }
    }
}
