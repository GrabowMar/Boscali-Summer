using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Ui;
using BoscaliSummer.Modules.Support.Domain.Fronts;
using BoscaliSummer.Modules.Support.Presentation.C2;
using NOAvionics;
using Rewired;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Support.Presentation.Fronts
{
    /// <summary>
    /// The shared root of the three front windows (R3): a near-full-screen popup over a dimmed backdrop with the classification
    /// strip, the ORBIT / NETWORK / SHADOW switch in the title bar, one room on the left (the hero) and the shared
    /// <see cref="FrontRail"/> on the right. Input follows the SPACE feed: the window owns the pointer only (the game's cursor
    /// flag frees the cursor, the Rewired mouse is off so a click cannot fire a weapon, the window hit-tests its own
    /// controls); keyboard and joysticks stay live; Esc or the X closes. Every press is a
    /// request to <see cref="IFrontActions"/>; nothing here decides or sends.
    /// </summary>
    internal sealed class OpsFrontWindow : MonoBehaviour
    {
        public const float WindowWidth = 1880f, WindowHeight = 1040f; // AvWindow caps at 1040: the shared footer is taller than the 30 this window used to force
        private const int SortOrder = 30006;
        private const float TitleH = 30f, StripH = 18f, Pad = 10f, FooterH = AvGridTokens.Footer, Gap = 8f, HeroW = 1240f; // the room (maps) takes every pixel the 572 px rail leaves

        private IFrontActions actions;
        private AvWindow window;
        private GameObject backdrop;
        private readonly FrontSkin skin = new FrontSkin();
        private FrontRail rail;
        private readonly IFrontRoom[] rooms = new IFrontRoom[FrontRules.FrontCount];
        private readonly RectTransform[] roomRoots = new RectTransform[FrontRules.FrontCount];
        // The management column right of the map: each room's panels are tabs, the front itself (status, posture, programmes) is the last one.
        private readonly RectTransform[] columnRoots = new RectTransform[FrontRules.FrontCount];
        private readonly AvTabBar[] bars = new AvTabBar[FrontRules.FrontCount];
        private readonly int[] tabOf = new int[FrontRules.FrontCount];
        private RectTransform railRoot;
        private int shownFront = -1;
        private readonly AvControl[] tabs = new AvControl[FrontRules.FrontCount];
        private TMP_Text titleText, headRight, stripLeft, stripMid, stripRight;
        private RawImage emblem;
        private Image stripBack;
        private readonly FeedInputLease lease = new FeedInputLease();
        private readonly List<AvHit> hits = new List<AvHit>(160);
        private AvHit hovered;
        private bool ownsCursorFlag, mouseDisabledByUs, pauseTouched, pauseWas, closing;
        private float nextHits;
        private FrontRoomView lastView;

        public bool IsOpen => window != null && window.Visible;
        public event Action Closed;

        /// <summary>The runtime window (no window in batch mode, like the SPACE feed).</summary>
        internal static OpsFrontWindow Create(Transform parent, IFrontActions actions) =>
            Application.isBatchMode ? null : Make(parent, actions);

        /// <summary>The offline harness builds the very same window (batch mode included).</summary>
        internal static OpsFrontWindow Make(Transform parent, IFrontActions actions)
        {
            var go = new GameObject("BoscaliOpsFront", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<OpsFrontWindow>();
            view.actions = actions;
            view.Build(go.transform);
            return view;
        }

        internal RectTransform Root => window.Root;
        internal AvWindow Window => window;

        private void Build(Transform uiRoot)
        {
            if (uiRoot.GetComponentInParent<Canvas>() != null) backdrop = Backdrop(uiRoot);
            window = AvWindow.Build(uiRoot, "OpsFront", "OPS · SPACE FRONT", WindowWidth, WindowHeight, SortOrder);
            window.Closed += OnWindowClosed;
            window.CloseControl.Help = "Close the window (Esc). Keyboard and joystick stay live; the mouse drives the window.";
            window.Footer.Set("Keyboard and joystick stay live; the mouse drives this window. It closes with Esc.", AvState.Inert);
            titleText = window.Root.Find("Title").GetComponent<TMP_Text>();
            RectTransform r = window.Root;
            // The compartment's command emblem left of the title, in its banner colour (Mark).
            emblem = FrontArt.Fill(r, FrontArt.Emblem(Front.Space), AvGridTokens.Pad, 4f, 22f, 22f, Color.white);
            if (emblem != null) AvLay.Place(titleText.rectTransform, AvGridTokens.Pad + 28f, 0f, WindowWidth - 2f * AvGridTokens.Pad - 64f, TitleH);

            // The body is opaque: the CAP panel behind the popup must not ghost through the gaps between the plates.
            FrontKit.Solid(r, "Body", 0f, TitleH, WindowWidth, WindowHeight - TitleH - FooterH, AvInk.Ground.WithAlpha(0.98f)).transform.SetAsFirstSibling();

            // Classification strip.
            float sy = TitleH + 2f;
            stripBack = FrontKit.Solid(r, "StripBack", Pad, sy, WindowWidth - 2f * Pad, StripH, Color.clear);
            stripLeft = FrontKit.Mono(r, "StripLeft", Pad + 8f, sy, 420f, StripH, 11f, TextAlignmentOptions.MidlineLeft, true, 1.5f);
            stripMid = FrontKit.Mono(r, "StripMid", WindowWidth * 0.5f - 400f, sy, 800f, StripH, 11.5f, TextAlignmentOptions.Midline, true, 4f);
            stripRight = FrontKit.Mono(r, "StripRight", WindowWidth - Pad - 428f, sy, 420f, StripH, 11f, TextAlignmentOptions.MidlineRight, true, 1.5f);

            // Front switch in the title bar: the domain and its room, so the two names stay connected.
            string[] names = { "SPACE · ORBIT", "CYBER · NETWORK", "SOF · SHADOW" };
            string[] help = { "The SPACE front's room: satellites, ground tracks and the live camera.",
                "The CYBER front's room: your data centers and EW trucks, the enemy node graph and the nodes you hop.", "The SOF front's room: teams, camps and the infiltration map." };
            float tw = 150f, tabX = (WindowWidth - (3f * tw + 12f)) * 0.5f, tabEnd = tabX + 3f * tw + 12f;
            // The front's callsign header sits right of the switch: net callsign, readiness rung and superiority band.
            headRight = FrontKit.Mono(r, "HeadRight", tabEnd + 16f, 0f, WindowWidth - 44f - tabEnd - 16f, TitleH, 11f, TextAlignmentOptions.MidlineRight, true, 1f);
            for (int i = 0; i < 3; i++)
            {
                int slot = i;
                AvControl c = AvControl.Make(r, new AvControl.Spec(names[i], () => { actions.Touch(); actions.SelectFront((Front)slot); }, AvButtonStyle.Default));
                AvLay.Place(c.Rect, tabX + i * (tw + 6f), 3f, tw, 24f);
                c.SingleLine();
                c.Help = help[i];
                tabs[i] = c;
            }

            // Map on the left, one management column on the right (spec 6): a tab bar, then the chosen panel under it.
            float my = TitleH + 2f + StripH + Gap, mh = WindowHeight - my - FooterH - Gap;
            float cx = Pad + HeroW + Gap, cw = WindowWidth - Pad - cx, ty = my + AvGridTokens.Tab + 6f, ch = mh - (ty - my);
            RectTransform hero = AvLay.Child(r, "Hero");
            AvLay.Place(hero, Pad, my, HeroW, mh);
            rooms[(int)Front.Space] = new OrbitRoom(actions, skin);
            rooms[(int)Front.Cyber] = new NetworkRoom(actions, skin);
            rooms[(int)Front.Sof] = new ShadowRoom(actions, skin);
            railRoot = AvLay.Child(r, "Rail");
            AvLay.Place(railRoot, cx, ty, cw, ch);
            rail = new FrontRail(railRoot, skin, actions, 0f, 0f, cw, ch);
            for (int i = 0; i < rooms.Length; i++)
            {
                roomRoots[i] = AvLay.Child(hero, "Room " + (Front)i);
                AvLay.Place(roomRoots[i], 0f, 0f, HeroW, mh);
                columnRoots[i] = AvLay.Child(r, "Column " + (Front)i);
                AvLay.Place(columnRoots[i], cx, ty, cw, ch);
                rooms[i].Build(roomRoots[i], HeroW, mh, columnRoots[i], cw, ch);
                IReadOnlyList<ColumnTab> panels = rooms[i].Column;
                // Every panel sits on its room's painted ground: ORBIT's orbit-chart glass, NETWORK's glass over circuit, SHADOW's olive map-table canvas.
                string ground = i == (int)Front.Space ? "ops_bg_orbit" : i == (int)Front.Cyber ? "ops_bg_network" : "ops_bg_shadow";
                for (int k = 0; k < panels.Count; k++) FrontArt.Ground(panels[k].Panel, ground, GroundTint);
                var items = new (AvIcon, string)[panels.Count + 1];
                for (int k = 0; k < panels.Count; k++) items[k] = (panels[k].Icon, panels[k].Label);
                items[panels.Count] = (AvIcon.Stack2, "FRONT");
                int room = i;
                bars[i] = new AvTabBar(r, items, k => { actions.Touch(); ShowTab(room, k); if (k != 0) LeaveFirstTab(room); });
                bars[i].SetAccent(i == (int)Front.Space ? RoomInk.Ice : i == (int)Front.Cyber ? RoomInk.Phosphor : RoomInk.Gold);
                bars[i].Place(new AvSlot(cx, my, cw, AvGridTokens.Tab));
                skin.Add(bars[i].Restyle);
            }
            skin.Add(Restyle);
        }

        private GameObject Backdrop(Transform uiRoot)
        {
            var go = new GameObject("BoscaliOpsFrontBackdrop", typeof(RectTransform));
            go.transform.SetParent(uiRoot.GetComponentInParent<Canvas>().rootCanvas.transform, false); // full screen, not the zero-size host
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            var canvas = go.AddComponent<Canvas>();
            canvas.overrideSorting = true; canvas.sortingOrder = SortOrder - 1;
            go.AddComponent<GraphicRaycaster>();
            Image dim = go.AddComponent<Image>();
            dim.color = AvInk.Ground.WithAlpha(0.7f);
            dim.raycastTarget = true; // a click outside the window never reaches the map behind
            go.SetActive(false);
            return go;
        }

        private static readonly Color GroundTint = Color.white.WithAlpha(0.85f);

        private void Restyle()
        {
            headRight.color = AvInk.Dim;
            Mark(lastView != null ? lastView.Front : Front.Space);
        }

        /// <summary>Each compartment flies its own banner colour: ORBIT top secret orange, NETWORK TS/SCI yellow, SHADOW secret red.</summary>
        private void Mark(Front f)
        {
            stripBack.color = f == Front.Space ? RoomInk.Rgb(0xFF8C1A) : f == Front.Cyber ? RoomInk.Rgb(0xFFD21F) : RoomInk.Rgb(0xC8102E);
            if (emblem != null) { emblem.texture = FrontArt.Get(FrontArt.Emblem(f)); emblem.color = stripBack.color; }
            Color ink = f == Front.Sof ? Color.white : RoomInk.Rgb(0x0B0B0B);
            stripLeft.color = ink; stripMid.color = ink; stripRight.color = ink;
        }

        private static string Caveat(Front f) => f == Front.Space ? "TOP SECRET // SI-TK" : f == Front.Cyber ? "TOP SECRET // SCI" : "SECRET // NOFORN";

        // ---- Paint / open / close ------------------------------------------------------------------------------------

        public void Paint(FrontRoomView v)
        {
            if (v == null) return;
            lastView = v;
            string name = FrontRules.Name(v.Front);
            AvText.Set(titleText, "OPS · " + name + " FRONT");
            string cls = string.IsNullOrEmpty(v.Classification) ? Caveat(v.Front) + " // " + (v.Faction ?? "").ToUpperInvariant() + " EYES ONLY // " + Command(v.Front) : v.Classification;
            AvText.Set(stripMid, cls);
            Mark(v.Front);
            AvText.Set(stripLeft, C2Kit.FitTo(stripLeft, "OPERATOR " + (v.Callsign ?? "") + " · COMPARTMENT " + Room(v.Front), stripLeft.rectTransform.rect.width));
            AvText.Set(stripRight, C2Kit.FitTo(stripRight, "DTG " + (v.Dtg ?? "") + " · " + Feed(v.FeedAge), stripRight.rectTransform.rect.width));
            AvText.Set(headRight, C2Kit.FitTo(headRight, "NET " + FrontWords.Callsign(v.Front) + " · " + FrontRail.Headline(v), headRight.rectTransform.rect.width));
            for (int i = 0; i < tabs.Length; i++) tabs[i].Latched = i == (int)v.Front;
            int front = (int)v.Front;
            for (int i = 0; i < roomRoots.Length; i++)
            {
                roomRoots[i].gameObject.SetActive(i == front);
                columnRoots[i].gameObject.SetActive(i == front);
                bars[i].Rect.gameObject.SetActive(i == front);
            }
            if (shownFront != front) { shownFront = front; EndPicks(); ShowTab(front, tabOf[front]); } // a map pick never waits behind another room
            rail.Paint(v);
            rooms[front]?.Paint(v);
            int want = rooms[front] != null ? rooms[front].TakeTab() : -1;
            if (want >= 0 && want != tabOf[front]) ShowTab(front, want);
        }

        /// <summary>Shows column tab <paramref name="tab"/> of a room: one of its panels, or the FRONT panel (status, posture, programmes) after them.</summary>
        private void ShowTab(int room, int tab)
        {
            IReadOnlyList<ColumnTab> panels = rooms[room].Column;
            tabOf[room] = Mathf.Clamp(tab, 0, panels.Count);
            for (int k = 0; k < panels.Count; k++) panels[k].Panel.gameObject.SetActive(k == tabOf[room]);
            railRoot.gameObject.SetActive(tabOf[room] == panels.Count);
            bars[room].Select(tabOf[room], false);
            skin.Apply(); // a box first shown now lays its header out now (TMP measures nothing while it is inactive)
        }

        private static string Command(Front f) => f == Front.Space ? "ORBITAL COMMAND" : f == Front.Cyber ? "CYBER COMMAND" : "SPECIAL OPERATIONS COMMAND";

        private static string Room(Front f) => f == Front.Space ? "ORBIT" : f == Front.Cyber ? "NETWORK" : "SHADOW";

        /// <summary>How long ago the host's last front snapshot arrived. Snapshots are change-only, so this is an age, never a staleness verdict.</summary>
        private static string Feed(float age) => age < 0f ? "NO HOST FEED YET" : age < 1f ? "HOST UPDATE NOW" : "HOST UPDATE " + FrontKit.Clock(age) + " AGO";

        /// <summary>Opens the window and takes the pointer. False when it could not.</summary>
        internal bool Open(FrontRoomView view)
        {
            if (window == null) return false;
            if (IsOpen) return true;
            closing = false;
            try
            {
                ShowFrame();
                TakeInput();
                Paint(view);
                nextHits = 0f;
                return true;
            }
            catch (Exception e)
            {
                Plugin.Logger?.LogError("OPS front window open failed: " + e);
                Abort();
                return false;
            }
        }

        /// <summary>Shows the window without touching input (the offline harness).</summary>
        internal void ShowFrame()
        {
            if (backdrop != null) backdrop.SetActive(true);
            window.Show();
            skin.Apply();
        }

        internal void Close()
        {
            if (!IsOpen || closing) return;
            closing = true;
            window.Hide(); // raises Closed -> OnWindowClosed hands the input back
        }

        private OrbitRoom Orbit => rooms[(int)Front.Space] as OrbitRoom;

        /// <summary>The SPACE room shows the DOWNLINK picture (its column tab, or enlarged over the map): only then does the feed hold its lease and render.</summary>
        internal bool DownlinkShown => IsOpen && shownFront == (int)Front.Space && Orbit != null && (tabOf[shownFront] == OrbitRoom.DownlinkTab || Orbit.Enlarged);

        /// <summary>A hand click on a room's later tab: an armed map pick of its first tab ends (it would wait off screen) and the map drops its rings now.</summary>
        private void LeaveFirstTab(int room)
        {
            rooms[room].EndPicks();
            if (room == (int)Front.Space && Orbit != null && tabOf[room] == OrbitRoom.DownlinkTab) Orbit.DownlinkOpened();
            if (lastView != null && (int)lastView.Front == room) rooms[room].Paint(lastView);
        }

        private void EndPicks() { foreach (IFrontRoom room in rooms) room?.EndPicks(); }

        private void OnWindowClosed()
        {
            EndPicks(); // a reopened window starts with no pick armed
            Orbit?.ReleaseDownlink(); // the resolve's small targets go back to the budget
            if (backdrop != null) backdrop.SetActive(false);
            WindowPointer.Hover(ref hovered, null);
            GiveBackInput();
            closing = false;
            Closed?.Invoke();
        }

        private void Abort()
        {
            try { if (window != null && window.Visible) window.Hide(); }
            catch (Exception) { /* the guard below must run whatever the window did */ }
            GiveBackInput();
            if (lease.ForceRelease()) RestoreMouse();
        }

        private void OnDisable()
        {
            if (lease.ForceRelease()) RestoreMouse();
            ReleaseFlags();
        }

        private void OnDestroy()
        {
            if (window != null && window.Visible) { try { window.Hide(); } catch (Exception) { } }
            if (backdrop != null) Destroy(backdrop);
            if (lease.ForceRelease()) RestoreMouse();
            ReleaseFlags();
        }

        private void Update()
        {
            // A mouse still held at close is handed back the moment its buttons are up.
            if (lease.Tick(ButtonsDown())) RestoreMouse();
            if (!IsOpen) return;
            try
            {
                if (FrontPointer.Escape)
                {
                    IFrontRoom active = lastView != null ? rooms[(int)lastView.Front] : null;
                    if (active != null && active.CancelMode()) { active.Paint(lastView); return; } // a relocation proposal ends first
                    Close();
                    return;
                }
                Vector2 mouse = FrontPointer.Position;
                // No idle close: a pilot flying on the stick (Rewired, not Input) touches nothing for minutes; only Esc, X, death or a faction change closes.

                // With the Rewired mouse off (by us, or already) the event system hears nothing: hit-test our own controls.
                KeepInput();
                Mouse device = TryMouse();
                if (mouseDisabledByUs || (device != null && !device.enabled)) Pointer(mouse);
                IFrontRoom room = lastView != null ? rooms[(int)lastView.Front] : null;
                if (room != null && room.Tick(mouse)) room.Paint(lastView);
            }
            catch (Exception e)
            {
                Plugin.Logger?.LogError("OPS front window fault, closing: " + e);
                Abort();
            }
        }

        // ---- Pointer (the Rewired mouse is off, so the window hit-tests its own controls) ------------------------------

        private void Pointer(Vector2 mouse)
        {
            if (Time.unscaledTime >= nextHits)
            {
                nextHits = Time.unscaledTime + 0.1f;
                hits.Clear();
                window.Root.GetComponentsInChildren(false, hits);
            }
            AvHit under = null;
            for (int i = hits.Count - 1; i >= 0; i--) // later children draw on top
            {
                AvHit h = hits[i];
                if (h != null && h.isActiveAndEnabled && h.Interactable &&
                    RectTransformUtility.RectangleContainsScreenPoint((RectTransform)h.transform, mouse, null)) { under = h; break; }
            }
            WindowPointer.Hover(ref hovered, under);
            if (under != null && FrontPointer.Down(0)) ((IPointerClickHandler)under).OnPointerClick(WindowPointer.Data());
        }

        // ---- Input ownership -------------------------------------------------------------------------------------------

        private static bool ButtonsDown() => Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButton(2);

        private static Mouse TryMouse() => ReInput.isReady && ReInput.controllers != null ? ReInput.controllers.Mouse : null;

        private void TakeInput()
        {
            ownsCursorFlag = !CursorManager.GetFlag(CursorFlags.SelectionMenu);
            if (ownsCursorFlag) CursorManager.SetFlag(CursorFlags.SelectionMenu, true);
            if (!pauseTouched)
            {
                pauseTouched = true;
                pauseWas = GameplayUI.AllowPauseKeybind;
                GameplayUI.AllowPauseKeybind = false; // Esc closes the window instead of opening the pause menu
            }
            Mouse mouse = TryMouse();
            if (lease.Acquire(mouse != null, mouse != null && mouse.enabled))
            {
                mouse.enabled = false;
                mouseDisabledByUs = true;
            }
        }

        /// <summary>
        /// Every frame while open: takes back what another overlay handed back under the window (the C-menu, the Met Office or the cinematic console
        /// held the mouse when the window opened and switched it on again as they closed, leaving the window deaf: no hit test, event system unheard).
        /// </summary>
        private void KeepInput()
        {
            if (!CursorManager.GetFlag(CursorFlags.SelectionMenu))
            {
                CursorManager.SetFlag(CursorFlags.SelectionMenu, true);
                ownsCursorFlag = true;
            }
            Mouse mouse = TryMouse();
            if (mouse == null || !mouse.enabled) return;
            if (lease.Acquire(true, true) || mouseDisabledByUs)
            {
                mouse.enabled = false;
                mouseDisabledByUs = true;
            }
        }

        private void GiveBackInput()
        {
            ReleaseFlags();
            if (lease.Release(ButtonsDown())) RestoreMouse();
        }

        private void ReleaseFlags()
        {
            if (ownsCursorFlag) CursorManager.SetFlag(CursorFlags.SelectionMenu, false);
            ownsCursorFlag = false;
            if (pauseTouched) GameplayUI.AllowPauseKeybind = pauseWas;
            pauseTouched = false;
        }

        private void RestoreMouse()
        {
            Mouse mouse = TryMouse();
            if (mouse != null) mouse.enabled = lease.RestoreMouseTo;
            mouseDisabledByUs = false;
        }
    }
}
