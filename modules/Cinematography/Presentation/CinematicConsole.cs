using BoscaliSummer.Modules.Cinematography.Runtime;
using NOAvionics;
using System;
using System.Collections.Generic;
using Rewired;
using UnityEngine;
using UnityEngine.EventSystems;

namespace BoscaliSummer.Modules.Cinematography.Presentation
{
    internal sealed class CinematicConsole : MonoBehaviour
    {
        private const float Width = 920, Height = 720;
        private CinematicDirector director;
        private AvWindow window;
        private AvReadout time;
        private AvRow shot, keys, take;
        private AvControl smooth;
        private AvFlow body;
        private readonly List<AvPart>[] pages={ new List<AvPart>(),new List<AvPart>(),new List<AvPart>() };
        private AvControl[] tabs,markers,transport;
        private readonly List<AvControl> editing=new List<AvControl>();
        private AvRow markerDetail,timing,effects,editSummary;
        private AvRow[] clipRows;
        private int currentPage,markerStart,selectedMarker,clipStart,selectedClip;
        private AvHit[] hits;
        private AvHit hovered;
        private Mouse mouse;
        private Keyboard keyboard;
        private bool ownsInput, mouseWas, keyboardWas, pauseWas, releasePending;
        private float nextRefresh;
        internal bool IsOpen => window != null && window.Visible;

        internal static CinematicConsole Create(CinematicDirector owner)
        {
            var go = new GameObject("BoscaliSummer.CinematicConsole");
            go.transform.SetParent(owner.transform, false);
            var view = go.AddComponent<CinematicConsole>(); view.director = owner;
            view.Build(); return view;
        }

        private void Build()
        {
            window = AvWindow.Build(transform, "cinematography", "CINEMATOGRAPHY · PRIVATE PRODUCTION", Width, Height, 30008);
            window.Closed += ReleaseInput;
            window.CloseControl.Help = "Close the console with Escape. Escape aborts an active shot independently of this window.";
            body = window.Body; body.ViewportHeight = Height - 30 - AvGridTokens.Footer;
            time = body.Add(new AvReadout(body.Content));
            tabs=body.Buttons(new AvControl.Spec("CAMERA",()=>SwitchPage(0)),new AvControl.Spec("TIMING & FX",()=>SwitchPage(1)),
                new AvControl.Spec("EDIT LIST",()=>SwitchPage(2))).Controls;
            transport=body.Buttons(new AvControl.Spec("PLAY SHOT",director.Play),new AvControl.Spec("PAUSE",()=>director.PauseCamera(true)),
                new AvControl.Spec("RESUME",()=>director.PauseCamera(false)),new AvControl.Spec("STOP",()=>director.Stop())).Controls;
            Section(0,"SHOT & MARKERS","GLOBAL OR ACTOR-RELATIVE CAMERA");
            shot=Add(0,new AvRow(body.Content));
            Buttons(0,new AvControl.Spec("NEW / CLEAR",director.NewShot),new AvControl.Spec("NEXT SLOT",director.NextSlot),new AvControl.Spec("SAVE",director.Save),new AvControl.Spec("LOAD",director.Load));
            keys=Add(0,new AvRow(body.Content));
            var markerSpecs=new AvControl.Spec[8];
            for(int i=0;i<8;i++) {int slot=i;markerSpecs[i]=new AvControl.Spec("M"+(i+1),()=>SelectMarker(markerStart+slot));}
            markers=Buttons(0,false,markerSpecs);
            Buttons(0,false,new AvControl.Spec("PREVIOUS 8",()=>markerStart=Math.Max(0,markerStart-8)),new AvControl.Spec("NEXT 8",()=>markerStart=Math.Min(56,markerStart+8)));
            markerDetail=Add(0,new AvRow(body.Content));
            Buttons(0,new AvControl.Spec("CAPTURE POSE",director.Capture),new AvControl.Spec("REMOVE LAST",director.RemoveLast),
                new AvControl.Spec("TIME -0.25",()=>director.EditKey(selectedMarker,-.25f,0)),new AvControl.Spec("TIME +0.25",()=>director.EditKey(selectedMarker,.25f,0)));
            smooth=Buttons(0,new AvControl.Spec("PATH",director.CyclePath),new AvControl.Spec("FOV -5",()=>director.EditKey(selectedMarker,0,-5)),
                new AvControl.Spec("FOV +5",()=>director.EditKey(selectedMarker,0,5)),new AvControl.Spec("-1 SECOND",()=>director.AdjustDuration(-1)),new AvControl.Spec("+1 SECOND",()=>director.AdjustDuration(1)))[0];
            Section(1,"TIMING","CAMERA SPEED AND SIMULATION SPEED ARE SEPARATE");
            timing=Add(1,new AvRow(body.Content));
            Buttons(1,new AvControl.Spec("CAM x0.25",()=>director.SetCameraRate(.25f)),new AvControl.Spec("CAM x0.5",()=>director.SetCameraRate(.5f)),
                new AvControl.Spec("CAM x1",()=>director.SetCameraRate(1)),new AvControl.Spec("CAM x2",()=>director.SetCameraRate(2)));
            Buttons(1,new AvControl.Spec("WORLD x0.25",()=>director.SetSimulationRate(.25f)),new AvControl.Spec("WORLD x0.5",()=>director.SetSimulationRate(.5f)),
                new AvControl.Spec("WORLD x1",()=>director.SetSimulationRate(1)),new AvControl.Spec("CLOCK MODE",()=>director.ToggleOption("clock")));
            Section(1,"PRESENTATION","DETERMINISTIC MOTION · INPUT-TRANSPARENT OVERLAYS");
            effects=Add(1,new AvRow(body.Content));
            Buttons(1,new AvControl.Spec("CLEAN",()=>director.SetPreset("clean")),new AvControl.Spec("FILM",()=>director.SetPreset("film")),new AvControl.Spec("HANDHELD",()=>director.SetPreset("handheld")));
            Buttons(1,new AvControl.Spec("LETTERBOX",()=>director.ToggleOption("bars")),new AvControl.Spec("FADE",()=>director.ToggleOption("fade")),
                new AvControl.Spec("TITLE",()=>director.ToggleOption("title")),new AvControl.Spec("DOLLY ZOOM",()=>director.ToggleOption("dolly")));
            Buttons(1,new AvControl.Spec("ROLL -5",()=>director.ToggleOption("rollLeft")),new AvControl.Spec("ROLL +5",()=>director.ToggleOption("rollRight")),
                new AvControl.Spec("WORLD ANCHOR",()=>director.ToggleOption("anchor")),new AvControl.Spec("SCRIPT INBOX",()=>director.ToggleOption("scripts")));
            Section(2,"EDIT LIST","CLIPS PLAY IN ORDER OVER THE LIVE WORLD");
            editSummary=Add(2,new AvRow(body.Content));
            Buttons(2,new AvControl.Spec("ADD CURRENT SHOT",director.AppendClip),new AvControl.Spec("SAVE EDIT",director.SaveEdit),new AvControl.Spec("LOAD EDIT",director.LoadEdit),new AvControl.Spec("CLEAR EDIT",director.ClearEdit));
            clipRows=new AvRow[4];for(int i=0;i<4;i++) {int row=i;clipRows[i]=Add(2,new AvRow(body.Content,()=>selectedClip=clipStart+row));}
            Buttons(2,false,new AvControl.Spec("PREVIOUS 4",()=>clipStart=Math.Max(0,clipStart-4)),new AvControl.Spec("NEXT 4",()=>clipStart=Math.Min(28,clipStart+4)));
            Buttons(2,new AvControl.Spec("MOVE UP",()=>director.MoveClip(selectedClip,-1)),new AvControl.Spec("MOVE DOWN",()=>director.MoveClip(selectedClip,1)),new AvControl.Spec("REMOVE CLIP",()=>director.RemoveClip(selectedClip)));
            Buttons(2,false,new AvControl.Spec("PLAY EDIT",()=>director.PlayEdit()),new AvControl.Spec("BOOKMARK",director.Bookmark),new AvControl.Spec("CLOSE",Close));
            take=body.Add(new AvRow(body.Content));
            hits = window.Root.GetComponentsInChildren<AvHit>(true);
            SwitchPage(0);Refresh();
        }
        private T Add<T>(int page,T part) where T:AvPart {body.Add(part);pages[page].Add(part);return part;}
        private void Section(int page,string title,string hint) {pages[page].Add(body.Section(AvIcon.PlayerPlay,title,hint));}
        private AvControl[] Buttons(int page,params AvControl.Spec[] specs)=>Buttons(page,true,specs);
        private AvControl[] Buttons(int page,bool edits,params AvControl.Spec[] specs)
        {var buttons=body.Buttons(specs);pages[page].Add(buttons);if(edits) editing.AddRange(buttons.Controls);return buttons.Controls;}
        internal void SwitchPage(int page)
        {
            currentPage=Math.Clamp(page,0,2);
            for(int p=0;p<pages.Length;p++) {foreach(var part in pages[p]) part.Rect.gameObject.SetActive(p==currentPage);tabs[p].Latched=p==currentPage;}
            body.RequestRelayout();
        }
        private void SelectMarker(int index)
        { if(index<0 || index>=director.Plan.keys.Length) return;selectedMarker=index;if(director.Playing) {director.PauseCamera(true);director.Seek(director.Plan.keys[index].time);} }

        internal void Show()
        {
            if (IsOpen || releasePending || CursorManager.GetFlag(CursorFlags.SelectionMenu)) return;
            ownsInput = true;
            CursorManager.SetFlag(CursorFlags.SelectionMenu, true);
            pauseWas = GameplayUI.AllowPauseKeybind; GameplayUI.AllowPauseKeybind = false;
            if (ReInput.isReady && ReInput.controllers != null)
            {
                mouse = ReInput.controllers.Mouse; keyboard = ReInput.controllers.Keyboard;
                if (mouse != null) { mouseWas = mouse.enabled; mouse.enabled = false; }
                if (keyboard != null) { keyboardWas = keyboard.enabled; keyboard.enabled = false; }
            }
            window.Show(); nextRefresh = 0; Refresh();
            if(director.Playing) director.PauseCamera(true);
        }

        internal void Close() { if (IsOpen) window.Hide(); }
        private void ReleaseInput()
        {
            SetHovered(null);
            if (!ownsInput) return;
            ownsInput = false;
            if (CursorManager.GetFlag(CursorFlags.SelectionMenu)) CursorManager.SetFlag(CursorFlags.SelectionMenu, false);
            if (!GameplayUI.AllowPauseKeybind) GameplayUI.AllowPauseKeybind = pauseWas;
            if (keyboard != null && !keyboard.enabled) keyboard.enabled = keyboardWas;
            keyboard = null;
            releasePending = mouse != null;
            RestoreMouseIfReleased();
        }

        private void RestoreMouseIfReleased(bool force = false)
        {
            if (!releasePending || !force && (Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButton(2))) return;
            if (mouse != null && !mouse.enabled) mouse.enabled = mouseWas;
            mouse = null; releasePending = false;
        }

        private void Update()
        {
            RestoreMouseIfReleased();
            if (!IsOpen) return;
            if (!Application.isFocused || !director.CanUseConsole(out _) || Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }
            // Native uGUI reads the disabled Rewired mouse; forward pointer events to shared kit controls.
            if (mouse != null)
            {
                AvHit under = null;
                for (int i = 0; i < hits.Length; i++)
                    if (hits[i] != null && hits[i].isActiveAndEnabled &&
                        RectTransformUtility.RectangleContainsScreenPoint((RectTransform)hits[i].transform, Input.mousePosition, null))
                    { under = hits[i]; break; }
                SetHovered(under);
                if (under != null && Input.GetMouseButtonDown(0))
                    ((IPointerClickHandler)under).OnPointerClick(Pointer());
            }
            if (Time.unscaledTime >= nextRefresh) { nextRefresh = Time.unscaledTime + .2f; Refresh(); }
        }

        private static PointerEventData Pointer() => new PointerEventData(EventSystem.current)
        { button = PointerEventData.InputButton.Left, position = Input.mousePosition };
        private void SetHovered(AvHit next)
        {
            if (hovered == next) return;
            if (hovered != null) ((IPointerExitHandler)hovered).OnPointerExit(Pointer());
            hovered = next;
            if (hovered != null) ((IPointerEnterHandler)hovered).OnPointerEnter(Pointer());
        }

        private void Refresh()
        {
            var plan = director.Plan; var last = director.Take;
            time.Set(director.Playing?director.Elapsed.ToString("0.00"):"0.00","/ "+plan.duration.ToString("0.0")+" SEC",
                director.Status+" · CAMERA SCRUB ONLY · WORLD DOES NOT REWIND");
            shot.Set(plan.id.ToUpperInvariant(),plan.anchorId==null?"Global sea-level poses. Save/load is scoped to this mission and scene.":"FOLLOW ACTOR: "+plan.anchorId,plan.spline?"CURVED":plan.smooth?"SMOOTH":"LINEAR");
            keys.Set("MARKERS", "Select a marker during playback to pause and scrub the camera. Middle-marker times and FOV are editable while stopped.", plan.keys.Length + " / 64");
            smooth.Label=plan.spline?"PATH: CURVED":plan.smooth?"PATH: SMOOTH":"PATH: LINEAR";
            for(int i=0;i<8;i++) {int index=markerStart+i;bool valid=index<plan.keys.Length;markers[i].Interactable=valid;markers[i].Label=valid?(index+1).ToString("00")+" · "+plan.keys[index].time.ToString("0.0")+"S":"—";markers[i].Latched=valid&&index==selectedMarker;}
            if(selectedMarker>=plan.keys.Length) selectedMarker=Math.Max(0,plan.keys.Length-1);
            markerDetail.Set("SELECTED "+(selectedMarker+1).ToString("00"),plan.keys.Length==0?"Capture the first pose to begin.":"TIME "+plan.keys[selectedMarker].time.ToString("0.00")+" S · FOV "+plan.keys[selectedMarker].fov.ToString("0.0")+"°",plan.keys.Length==0?"EMPTY":"MARKER");
            var o=plan.options;
            timing.Set("CAMERA x"+(o?.playbackRate ?? 1).ToString("0.00"),"WORLD requested x"+(o?.simulationRate ?? 1).ToString("0.00")+" · actual x"+Time.timeScale.ToString("0.00")+" · world slowmo needs single-player opt-in.",o?.realtimeClock==true?"REALTIME":"MISSION");
            effects.Set("SHAKE / ROLL / LENS","SHAKE "+(o?.shakeMeters ?? 0).ToString("0.00")+" M · ROLL "+(o?.rollDegrees ?? 0).ToString("0.0")+"° · letterbox/fade/title are presentation only.",o?.dollyZoom==true?"DOLLY":"NATIVE");
            editSummary.Set(director.Edit.id.ToUpperInvariant(),director.Edit.clips.Length+" / 32 CLIPS · "+director.Edit.LengthSeconds.ToString("0.0")+" CAMERA CLOCK SECONDS",director.Playing?"ACTIVE":"READY");
            for(int i=0;i<4;i++) {int index=clipStart+i;bool valid=index<director.Edit.clips.Length;clipRows[i].Rect.gameObject.SetActive(valid&&currentPage==2);if(valid) {var clip=director.Edit.clips[index];clipRows[i].Set((index+1).ToString("00")+" · "+clip.id,clip.duration.ToString("0.0")+" S · camera x"+(clip.options?.playbackRate ?? 1).ToString("0.00"),index==selectedClip?"SELECTED":"CLIP");}}
            foreach(var control in editing) control.Interactable=!director.Playing;
            transport[0].Interactable=!director.Playing&&plan.keys.Length>0;transport[1].Interactable=director.Playing&&!director.CameraPaused;
            transport[2].Interactable=director.Playing&&director.CameraPaused;transport[3].Interactable=director.Playing;
            take.Set(last == null ? "NO TAKE YET" : last.status.ToUpperInvariant(),
                "RECORDER UNKNOWN · API: BoscaliSummer.Cinematics.CinematicAutomation.Step · manual markers and camera edits.",
                last == null ? "—" : last.BookmarkCount + " MARKS");
            window.Footer.Set(director.Message, AvState.Info);
        }

        private void OnDisable() { Close(); RestoreMouseIfReleased(true); }
        private void OnDestroy()
        {
            Close(); ReleaseInput(); RestoreMouseIfReleased(true);
            if (window != null && window.Root != null) Destroy(window.Root.gameObject);
        }
    }
}
