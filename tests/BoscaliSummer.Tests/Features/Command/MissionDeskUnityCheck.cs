#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Modules.Command.Presentation;
using NOAvionics;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using static UnityCheckHarness;

// Actual production desk and MIS, production fonts and chart art; synthetic host views.
// Geometry, passive art, readable states and local controls only. No live dispatch claim.
public static class MissionDeskUnityCheck
{
    private const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const string Contracts = "BoscaliSummer.Core.Contracts.";
    private const string Presentation = "BoscaliSummer.Modules.Command.Presentation.";
    private static Assembly assembly;
    private static Sprite chart;
    private static int captures, assertions, texts, controls;
    private static readonly List<string> Notes = new List<string>();
    private enum State { Active, Offered, Completed, Lost, Stale, Unknown, Long, Empty }

    public static void Run()
    {
        try
        {
            if (!EnsureTmpEssentials(Run)) return;
            SetExecutablePath("MissionDeskCheck.exe"); AvBundle.Load(Debug.Log);
            Check(AvBundle.Available && AvIcons.Available, "Production fonts and icons must load.");
            AvStyleHost.Configure(Directory.GetCurrentDirectory(), Debug.Log, Debug.LogWarning);
            AvFxDriver.Configure(AvFxTier.Off, true); new GameObject("Events", typeof(EventSystem));
            assembly = typeof(AvConsole).Assembly; LoadChart();
            AvStyleHost.SetTheme(AvThemeId.Portal); CheckActionDispatch();
            foreach (AvThemeId theme in new[] { AvThemeId.Portal, AvThemeId.Steel })
            {
                AvStyleHost.SetTheme(theme);
                foreach (State state in new[] { State.Active, State.Offered, State.Completed, State.Lost, State.Stale, State.Unknown, State.Long, State.Empty })
                {
                    Desk(state, theme);
                    foreach (float height in new[] { 896f, 596f }) Mis(state, theme, height);
                }
            }
            File.WriteAllText("result.txt", "PASS: " + captures + " production side-mission PNGs; real desk and MIS at 896/596 in Portal/Steel. " + texts +
                " text checks, " + controls + " control checks, " + assertions + " assertions. Active/offered/completed/lost/stale/unknown/long/empty fixtures.\n" +
                "No overflow; shared 11px floor; Navigation.None; passive bounded vectors; scrolling reachability; title-bar close releases guard.\n" +
                "Synthetic action provider checks: accept correct current ID without a pre-action refresh, local HUD select/deselect, confirmed abort, stale/pending dispatch refusal and pending feedback.\n" +
                "Production built DLL and native map artwork, synthetic snapshots and static chart extent/ownship inputs. No live input, gameplay, multiplayer or deployment claim.\n" + string.Join("\n", Notes));
            EditorApplication.Exit(0);
        }
        catch (Exception error) { File.WriteAllText("result.txt", "FAIL: " + error); Debug.LogException(error); EditorApplication.Exit(1); }
    }

    private static void LoadChart()
    {
        using (Stream stream = assembly.GetManifestResourceStream("BoscaliSummer.Command.terrain2_intel.png"))
        using (var bytes = new MemoryStream())
        {
            Check(stream != null, "Embedded native terrain chart must exist."); stream.CopyTo(bytes);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false); Check(texture.LoadImage(bytes.ToArray()), "Native chart decodes.");
            texture.filterMode = FilterMode.Bilinear; texture.wrapMode = TextureWrapMode.Clamp;
            chart = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f));
        }
    }

    private sealed class ActionBoard : ISecondaryObjectivesView
    {
        internal readonly List<SecondaryObjectiveView> Cards=new List<SecondaryObjectiveView>();
        internal int Refreshes,Accepts,Cancels,Selections,AcceptedId,CancelledId;
        public IReadOnlyList<SecondaryObjectiveView> Objectives=>Cards;
        public string Status=>"SYNTHETIC HOST BOARD";
        public int ActiveLimit=>2;
        public bool IsFresh {get;set;}=true;
        public float SnapshotAgeSeconds=>0;
        public int SelectedForHud {get;private set;}=-1;
        public bool IsActionPending {get;set;}
        public int PendingObjectiveId=>IsActionPending?Cards[0].Id:0;
        public string ActionResult=>"";
        public void Refresh()=>Refreshes++;
        public void RequestAccept(int id){Accepts++;AcceptedId=id;}
        public void RequestCancel(int id){Cancels++;CancelledId=id;}
        public void SelectForHud(int id){Selections++;SelectedForHud=id;}
    }
    private static void CheckActionDispatch()
    {
        ServiceRegistry saved=ModuleServices.Active;
        var go=new GameObject("Production task action fixture",typeof(RectTransform),typeof(Canvas));
        var root=(RectTransform)go.transform;root.sizeDelta=new Vector2(470,180);go.GetComponent<Canvas>().renderMode=RenderMode.WorldSpace;
        try
        {
            var board=new ActionBoard();board.Cards.Add((SecondaryObjectiveView)Card(State.Offered,71));
            var registry=new ServiceRegistry();registry.Add<ISecondaryObjectivesView>(board);ModuleServices.Active=registry;
            MissionTaskActions actions=null;int mapRequests=0;
            Action refresh=()=>
            {
                actions.Set(board.Cards[0],board.IsFresh,true,board.SelectedForHud,board.IsActionPending,board.ActionResult);
                actions.Place(new AvSlot(14,14,442,actions.Measure(442)));
            };
            actions=new MissionTaskActions(root,refresh,()=>mapRequests++);refresh();
            Click(actions,"primary");
            Check(board.Accepts==1&&board.AcceptedId==71&&board.Refreshes==0,"Accept dispatches the current offered ID without starting a competing refresh first.");
            board.Cards[0]=(SecondaryObjectiveView)Card(State.Active,71);refresh();
            Click(actions,"primary");Check(board.SelectedForHud==71&&board.Selections==1,"HUD selection is a local view intent.");
            Click(actions,"primary");Check(board.SelectedForHud==-1&&board.Selections==2,"The selected task can be locally deselected.");
            Click(actions,"close");Check(board.Cancels==0&&((AvControl)Field(actions,"close")).Label=="CONFIRM ABORT","First abort click only arms confirmation.");
            Click(actions,"close");Check(board.Cancels==1&&board.CancelledId==71,"Confirmed abort dispatches the current accepted ID.");
            Click(actions,"map");Check(mapRequests==1,"View map exposes the native map without a mission or flight command.");
            // The provider can become stale or pending after a control was painted as enabled.
            board.IsFresh=false;Click(actions,"primary");Click(actions,"close");Click(actions,"map");
            Check(board.Selections==2&&board.Cancels==1&&mapRequests==1,"A stale provider cannot dispatch through a previously enabled control.");
            board.IsFresh=true;refresh();board.IsActionPending=true;Click(actions,"primary");Click(actions,"close");Click(actions,"map");
            Check(board.Selections==2&&board.Cancels==1&&mapRequests==1,"A pending host action prevents every competing task intent.");
            refresh();Check(((TMP_Text)Field(actions,"feedback")).text.Contains("WAITING FOR HOST"),"Pending feedback names the required host acknowledgement.");
            foreach(string name in new[]{"primary","map","close"})Check(!((AvControl)Field(actions,name)).Interactable,"Pending actions disable "+name+".");
            Settle(go);Capture(root,"mission-actions-pending-portal.png",2f);Gate(go,"task actions pending");
        }
        finally{ModuleServices.Active=saved;Object.DestroyImmediate(go);}
    }
    private static void Click(object actions,string name)=>((AvControl)Field(actions,name)).GetComponentInChildren<AvHit>(true).OnPointerClick(new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left});

    private static void Desk(State state, AvThemeId theme)
    {
        Type type = assembly.GetType(Presentation + "MissionContractWindow", true);
        bool pauseBefore = GameplayUI.AllowPauseKeybind;
        object desk = type.GetMethod("Create", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null); Call(desk, "Show");
        var view = (MonoBehaviour)desk; IList roster = (IList)Field(desk, "roster"); object card = state == State.Empty ? null : Card(state, 41);
        if (card != null) roster.Add(card); Set(desk, "selectedId", card == null ? -1 : 41); Set(desk, "boardFresh", state != State.Stale);
        SetChart(Field(desk, "tasking"), state != State.Unknown);
        Call(desk, "Render", "HOST BOARD / SYNTHETIC REPORT"); Settle(view.gameObject); AssertPlot(Field(desk, "tasking"), state); AssertActions(Field(desk,"actions"),state);
        string prefix = "mission-desk-" + Tag(theme, state);
        CaptureWindow(view, prefix + "-1920.png", 1920f, 1080f); Gate(view.gameObject, prefix); AssertState(view.gameObject, state);
        ScrollRect deskScroll=view.GetComponentInChildren<ScrollRect>(true);
        Check(Mathf.Abs(deskScroll.content.anchoredPosition.y)<1f,"Opening a task starts at its heading, with the asset silhouette in view.");
        if (state == State.Active || state == State.Long) { CaptureWindow(view, prefix + "-1280.png", 1280f, 720f); Gate(view.gameObject, prefix + "-1280"); }
        AvWindow window = (AvWindow)Field(desk, "window"); Capture(window.Root, prefix + "-detail.png", 1.5f); Bottom(view.gameObject, prefix, window.Root);
        if (state == State.Active)
        {
            SetChart(Field(desk,"tasking"),true,20000f);Call(desk,"Render","HOST BOARD / SMALL THEATER CHART");Settle(view.gameObject);AssertPlot(Field(desk,"tasking"),state);
            Capture(window.Root,prefix+"-small-chart.png",1.5f);Gate(view.gameObject,prefix+"-small-chart");
            SetChart(Field(desk,"tasking"),true);
            roster[0] = Card(State.Active, 41, true); Call(desk, "Render", "HOST BOARD / LAST-KNOWN REPORT"); Settle(view.gameObject);
            object plot = Field(desk, "tasking").GetType().GetProperty("Plot", All).GetValue(Field(desk, "tasking"));
            Check(((TMP_Text)Field(plot, "caption")).text.Contains("LAST KNOWN"), "A last-known contact is explicitly marked as uncertain.");
            Capture(window.Root, prefix + "-last-known.png", 1.5f); Gate(view.gameObject, prefix + "-last-known");
            roster[0] = card;
            object tasking = Field(desk, "tasking");
            ((AvControl)Field(tasking, "disclose")).GetComponentInChildren<AvHit>(true).OnPointerClick(new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left });
            Settle(view.gameObject);
            Check(((TMP_Text)Field(tasking, "rules")).gameObject.activeSelf, "Phase condition disclosure opens through the production control.");
            Check(HasText(view.gameObject, "Wait for confirmed ammunition transfer."), "The final gate condition is readable on disclosure.");
            Capture(window.Root, prefix + "-conditions.png", 1.5f); Gate(view.gameObject, prefix + "-conditions"); Bottom(view.gameObject, prefix + "-conditions", window.Root);
            for (int i = 0; i < 10; i++) roster.Add(Card(State.Offered, 42 + i));
            Call(desk, "Render", "HOST BOARD / ELEVEN RECORDS"); Settle(view.gameObject);
            object list = Field(desk, "list"); AvControl next = (AvControl)Field(list, "next");
            next.GetComponentInChildren<AvHit>(true).OnPointerClick(new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left });
            Check((int)list.GetType().GetProperty("Page", All).GetValue(list) == 1, "Next control reaches the second roster page.");
            var rows = (AvRow[])Field(list, "rows");
            rows[0].Rect.GetComponentInChildren<AvHit>(true).OnPointerClick(new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left });
            Settle(view.gameObject);
            Check((int)Field(desk, "selectedId") == 41 + rows.Length, "Roster selection picks the clicked host record.");
            CaptureWindow(view, prefix + "-page2.png", 1920f, 1080f); Gate(view.gameObject, prefix + "-page2");
        }
        // A hidden window cannot retain the keyboard/map guard. This failed on the pre-change DLL.
        window.CloseControl.GetComponentInChildren<AvHit>(true).OnPointerClick(new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left });
        Check(!(bool)type.GetProperty("IsOpen", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null), "Title-bar close releases MissionContractWindow.IsOpen.");
        Check(GameplayUI.AllowPauseKeybind == pauseBefore, "Closing restores the original pause-key policy.");
        Object.DestroyImmediate(view.gameObject);
    }

    private static void Mis(State state, AvThemeId theme, float height)
    {
        Type type = assembly.GetType(Presentation + "MapUi.VanillaMfdRebuild+MissionPresenter", true);
        object presenter = Activator.CreateInstance(type, All, null, new object[] { null }, null);
        var go = new GameObject("MIS " + state, typeof(RectTransform), typeof(Canvas)); var root = (RectTransform)go.transform;
        root.sizeDelta = new Vector2(AvTokens.PanelWidth, height); go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace; Call(presenter, "Build", root);
        AvConsole console = (AvConsole)type.BaseType.GetProperty("Console", All).GetValue(presenter); object model = Field(presenter, "model");
        Set(model, "Installed", true); Set(model, "Streamed", true); Set(model, "Limit", 2);
        Set(model, "ActiveContracts", state == State.Offered || state == State.Completed || state == State.Empty ? 0 : 1);
        Set(model, "Offers", state == State.Offered ? 1 : 0); Set(model, "Closed", state == State.Completed ? 1 : 0);
        Set(model, "AtStake", 1400); Set(model, "Offered", 1400); Set(model, "Paid", state == State.Completed ? 1400 : 0);
        Set(presenter, "secondaryHasCapacity", true); Set(presenter, "secondaryFresh", state != State.Stale);
        Set(presenter, "secondaryFilter", state == State.Offered ? 0 : state == State.Completed ? 2 : 1);
        if (state != State.Empty) { object card = Card(state, 41); ((IList)Field(model, "Contracts")).Add(card); Set(model, "Lead", card); }
        SetChart(Field(presenter, "tasking"), state != State.Unknown); console.SetPage(2); Call(presenter, "Render");
        console.SetTitle("TASKING / HOST BOARD"); console.Footer.Set("OFFLINE CHECK / SYNTHETIC HOST VIEW", AvState.Inert); Settle(go); AssertPlot(Field(presenter, "tasking"), state); AssertActions(Field(presenter,"taskActions"),state);
        string prefix = "mission-mis-" + Tag(theme, state) + "-" + (int)height;
        Capture(console.Root, prefix + ".png", 2f); Gate(go, prefix); AssertState(go, state); AssertPinnedActions(console,Field(presenter,"taskActions")); Bottom(go, prefix, console.Root); Object.DestroyImmediate(go);
    }

    private static string Tag(AvThemeId theme, State state) => theme.ToString().ToLowerInvariant() + "-" + state.ToString().ToLowerInvariant();
    private static void SetChart(object tasking, bool available,float span=60000f)
    {
        object plot = tasking.GetType().GetProperty("Plot", All).GetValue(tasking);
        Call(plot, "SetOfflineChart", available ? chart : null, span, span, 1900f, -4900f, 70f);
    }

    private static object Card(State state, int id, bool lastKnown = false)
    {
        bool complete = state == State.Completed, offered = state == State.Offered, known = state != State.Lost && !complete && !offered;
        object[] values = { id, state == State.Long ? "COVER THE EASTERN LOGISTICS REPLENISHMENT CORRIDOR" : "COVER THE SUPPLY RUN",
            "Cover the munitions truck while it reaches friendly units. Hold nearby for sixty seconds, then wait for a confirmed ammunition transfer.",
            state == State.Long ? "Eastern Forward Logistics / Emergency Munitions Carrier" : "HLT Munitions Truck",
            complete ? "AMMUNITION TRANSFER CONFIRMED" : offered ? "AWAITING ACCEPTANCE" : known ? "COVER TRUCK / 33 OF 60 SECONDS" : "CONTACT LOST / REACQUIRE TRUCK",
            "Faction morale +3", complete ? 1f : .55f, state == State.Unknown ? float.NaN : 702f,
            1400, 125, complete, offered, !complete && !offered, known, known ? 3200f : 0f, known ? -4500f : 0f, 1500f,
            offered ? "" : state == State.Long ? "ROOK / RECONNAISSANCE-FLIGHT-17" : "ROOK 1" };
        Type type = assembly.GetType(Contracts + "SecondaryObjectiveView", true);
        if (state == State.Unknown) return Activator.CreateInstance(type, All, null, values, null);
        var extended = new object[values.Length + 1]; Array.Copy(values, extended, values.Length); extended[values.Length] = Tasking(state, lastKnown);
        return Activator.CreateInstance(type, All, null, extended, null);
    }

    private static object Tasking(State state, bool lastKnown)
    {
        Type phase = assembly.GetType(Contracts + "ObjectivePhase", true); Array phases = Array.CreateInstance(phase, 3);
        string[] names = { "JOIN", "COVER", "TRANSFER" }, conditions = { "Reach the friendly truck.", "Maintain escort cover for 60 seconds.", "Wait for confirmed ammunition transfer." };
        for (int i = 0; i < 3; i++)
        {
            string status = state == State.Completed ? "Done" : state == State.Offered ? "Pending" : i == 0 ? "Done" : i == 1 ? "Current" : "Pending";
            phases.SetValue(Activator.CreateInstance(phase, All, null, new object[] { (byte)(i + 1), names[i], conditions[i], EnumValue("ObjectivePhaseStatus", status), i == 1 ? .55f : status == "Done" ? 1f : 0f }, null), i);
        }
        return Activator.CreateInstance(assembly.GetType(Contracts + "ObjectiveTasking", true), All, null, new object[] {
            EnumValue("ObjectiveFamily", "Logistics"), EnumValue("ObjectiveAsset", "GroundVehicle"), EnumValue("ObjectiveContact", state == State.Lost ? "Lost" : state == State.Offered || state == State.Completed ? "Unavailable" : lastKnown ? "LastKnown" : "Known"), state == State.Lost ? 26f : lastKnown ? 20f : 0f,
            state == State.Completed ? "Ammunition transfer confirmed." : state == State.Offered ? "Accept and join the supply truck." : state == State.Lost || lastKnown ? "Reacquire the supply truck." : "Cover the supply truck.",
            state == State.Lost ? "Position is unconfirmed." : "", "Keep the eastern line supplied.", phases,
            EnumValue("ObjectiveLifecycle", state == State.Offered ? "Offered" : state == State.Completed ? "Completed" : "Active"),
            "Ends the sortie without an award.", EnumValue("ObjectiveAllegiance", "Friendly") }, null);
    }
    private static object EnumValue(string type, string value) => Enum.Parse(assembly.GetType(Contracts + type, true), value);
    private static void AssertPlot(object tasking, State state)
    {
        object plot = tasking.GetType().GetProperty("Plot", All).GetValue(tasking);
        bool permitted = state == State.Active || state == State.Long;
        Check((bool)plot.GetType().GetProperty("HasPlottedContact", All).GetValue(plot) == permitted, "Only fresh accepted permitted coordinates get a task contact.");
        Check((bool)plot.GetType().GetProperty("HasBearing", All).GetValue(plot) == permitted, "Only a permitted contact gets an ownship bearing.");
        if (state == State.Stale) Check(((TMP_Text)Field(plot, "caption")).text.Contains("STALE"), "Stale host position is explicitly withheld.");
        if (state == State.Unknown)
        {
            Check(!((Image)Field(plot, "terrain")).enabled, "Missing chart source cannot produce a fabricated terrain projection.");
            Check(((AvVector)Field(plot, "vectors")).Buffer.Count == 0, "No task or ownship geometry survives a missing chart frame.");
        }
    }
    private static void AssertState(GameObject root, State state)
    {
        if (state == State.Empty || state == State.Stale) return;
        string value = state == State.Unknown ? "TIME UNKNOWN" : state == State.Lost ? "REACQUIRE" : state == State.Completed ? "COMPLETE" : "SUPPLY";
        Check(HasText(root, value), root.name + " explains host state " + state + ".");
        if (state == State.Active || state == State.Long) Check(HasText(root, "COVER") && HasText(root, "TRANSFER"), "Host phase gates are visible.");
    }
    private static void AssertActions(object actions,State state)
    {
        if(state!=State.Unknown&&state!=State.Stale)return;
        foreach(string name in new[]{"primary","map","close"})
            Check(!((AvControl)Field(actions,name)).Interactable,"Stale or untyped tasking cannot enable "+name+" task actions.");
    }
    private static void AssertPinnedActions(AvConsole console,object actions)
    {
        RectTransform rect=((MissionTaskActions)actions).Rect;
        var body=(RectTransform)console.Root.Find("Body");float top=-rect.anchoredPosition.y;
        float bodyBottom=-body.anchoredPosition.y+body.rect.height;
        float footerTop=-console.Footer.Rect.anchoredPosition.y;
        Check(top>=bodyBottom+3f&&top+rect.rect.height<=footerTop+1f,"Task actions are pinned below the scrolling body and above the footer.");
    }
    private static bool HasText(GameObject root, string value)
    {
        foreach (TMP_Text label in root.GetComponentsInChildren<TMP_Text>(true)) if (label.isActiveAndEnabled && CanvasOn(label.transform) && label.text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    private static void Settle(GameObject root)
    {
        foreach (AvTicker ticker in root.GetComponentsInChildren<AvTicker>(true)) for (int i = 0; i < 4; i++) ticker.TickNow();
        foreach (AvReveal reveal in root.GetComponentsInChildren<AvReveal>(true)) reveal.Finish();
        foreach (Image fill in root.GetComponentsInChildren<Image>(true)) if (fill.name == "ScanCover") fill.enabled = false;
        Canvas.ForceUpdateCanvases(); foreach (ScrollRect scroll in root.GetComponentsInChildren<ScrollRect>(true)) scroll.Rebuild(CanvasUpdate.PostLayout); Canvas.ForceUpdateCanvases();
    }
    private static bool CanvasOn(Transform target) { foreach (Canvas canvas in target.GetComponentsInParent<Canvas>(false)) if (!canvas.enabled) return false; return true; }
    private static void Gate(GameObject root, string where)
    {
        foreach (TMP_Text label in root.GetComponentsInChildren<TMP_Text>(true))
        {
            if (!label.isActiveAndEnabled || !CanvasOn(label.transform) || label.text.Length == 0 || label.name.StartsWith("Icon")) continue;
            label.ForceMeshUpdate(); texts++; Check(!label.isTextOverflowing, where + ": text clips: " + label.text);
            Check(label.fontSize >= AvTypeScale.Floor - .01f, where + ": below shared 11px floor: " + label.text);
            Bounds bounds = label.textBounds; Rect rect = label.rectTransform.rect;
            Check(bounds.size.x <= rect.width + 1.5f && bounds.size.y <= rect.height + 1.5f, where + ": text exceeds rect: " + label.text);
        }
        foreach (Selectable control in root.GetComponentsInChildren<Selectable>(true)) { controls++; Check(control.navigation.mode == Navigation.Mode.None, where + ": controller navigation not stripped."); }
        foreach (AvControl control in root.GetComponentsInChildren<AvControl>(true))
        {
            if (!control.Rect.gameObject.activeInHierarchy || !CanvasOn(control.Rect)) continue;
            controls++; Check(control.Rect.rect.width >= 24f && control.Rect.rect.height >= 24f, where + ": undersized shared control.");
        }
        foreach (AvVector vector in root.GetComponentsInChildren<AvVector>(true))
        {
            Check(!vector.raycastTarget, where + ": art intercepts input.");Check(vector.Buffer != null && !vector.Buffer.Overflowed, where + ": vector capacity exceeded.");
            if(vector.name!="Asset vectors")continue;
            Check(vector.rectTransform.pivot==Vector2.zero,where+": asset art retains its bottom-left vector origin.");
            Rect bounds=vector.rectTransform.rect;
            for(int i=0;i<vector.Buffer.Count*4;i++)
                Check(vector.Buffer.X[i]>=-.75f&&vector.Buffer.X[i]<=bounds.width+.75f&&vector.Buffer.Y[i]>=-.75f&&vector.Buffer.Y[i]<=bounds.height+.75f,where+": asset art stays inside its identification frame.");
        }
    }
    private static void Bottom(GameObject root, string prefix, RectTransform frame)
    {
        ScrollRect scroll = null;
        foreach (ScrollRect candidate in root.GetComponentsInChildren<ScrollRect>(true)) if (candidate.isActiveAndEnabled && CanvasOn(candidate.transform) && candidate.content != null && candidate.viewport != null) { scroll = candidate; break; }
        if (scroll == null || scroll.content.rect.height <= scroll.viewport.rect.height + 1f) return;
        float overflow = scroll.content.rect.height - scroll.viewport.rect.height; scroll.verticalNormalizedPosition = 0f; Canvas.ForceUpdateCanvases();
        Check(Mathf.Abs(scroll.content.anchoredPosition.y - overflow) <= 2f, prefix + ": last row unreachable."); Capture(frame, prefix + "-bottom.png", 1.5f); Gate(root, prefix + "-bottom"); scroll.verticalNormalizedPosition = 1f;
    }
    private static void CaptureWindow(MonoBehaviour view, string file, float width, float height)
    {
        Canvas canvas = view.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
        CanvasScaler scaler = canvas.GetComponent<CanvasScaler>(); Vector2 reference = scaler.referenceResolution; scaler.enabled = false;
        float scale = Mathf.Min(width / reference.x, height / reference.y); Vector2 size = new Vector2(width / scale, height / scale); ((RectTransform)canvas.transform).sizeDelta = size;
        Settle(view.gameObject); Capture((RectTransform)canvas.transform, file, width / size.x);
    }
    private static void Capture(RectTransform rect, string file, float scale)
    {
        Vector3 center = rect.TransformPoint(rect.rect.center);
        Camera camera = OrthoCamera("MissionCheckCamera", rect.rect.height * Mathf.Abs(rect.lossyScale.y) * .5f, AvStyleHost.FuiColor("ground", AvTheme.Ground), new Vector3(center.x, center.y, -10f));
        Canvas.ForceUpdateCanvases();
        CapturePng(camera, Mathf.RoundToInt(rect.rect.width * scale), Mathf.RoundToInt(rect.rect.height * scale), file);
        Object.DestroyImmediate(camera.gameObject); captures++;
        Check(new FileInfo(file).Length > 0, "Render failed: " + file); Notes.Add(file + " / " + new FileInfo(file).Length + " bytes");
    }
    private static object Field(object owner, string name)
    {
        for (Type type = owner.GetType(); type != null; type = type.BaseType) { FieldInfo field = type.GetField(name, All | BindingFlags.DeclaredOnly); if (field != null) return field.GetValue(owner); }
        throw new Exception(owner.GetType().Name + " has no field " + name + ".");
    }
    private static void Set(object owner, string name, object value)
    {
        for (Type type = owner.GetType(); type != null; type = type.BaseType) { FieldInfo field = type.GetField(name, All | BindingFlags.DeclaredOnly); if (field == null) continue; field.SetValue(owner, value); return; }
        throw new Exception(owner.GetType().Name + " has no field " + name + ".");
    }
    private static object Call(object owner, string name, params object[] args)
    {
        foreach (MethodInfo method in owner.GetType().GetMethods(All)) if (method.Name == name && method.GetParameters().Length == args.Length) return method.Invoke(owner, args);
        throw new Exception(owner.GetType().Name + " has no " + name + "/" + args.Length + ".");
    }
    private static void Check(bool condition, string message) { assertions++; if (!condition) throw new Exception(message); }
}
#endif
