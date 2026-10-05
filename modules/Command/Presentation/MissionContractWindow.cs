using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Modules.Command.Presentation.MapUi;
using NOAvionics;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Command.Presentation
{
    /// <summary>A local tactical briefing over the faction's host-owned task board.</summary>
    internal sealed class MissionContractWindow : MonoBehaviour
    {
        private const float Width=1180f,Height=700f,FooterHeight=42f;
        private const int SortOrder=30002;
        private readonly List<SecondaryObjectiveView> roster=new List<SecondaryObjectiveView>(16);
        private AvWindow window;
        private AvList list;
        private AvSection rosterSection;
        private AvSegmented filterControl;
        private AvStepper families;
        private MissionTaskPart tasking;
        private MissionTaskActions actions;
        private DeskColumns columns;
        private int filter,selectedId=-1,familyFilter,renderedId=int.MinValue;
        private bool boardFresh,keyboardTouched,keyboardWas,pauseWas,ownsInput,closing;
        private int activeLimit,activeCount;
        private ISecondaryObjectivesView board;
        private string lastBoardText="";
        private float nextRefresh;
        private static int closedFrame=-10;
        private static MissionContractWindow openWindow;
        internal static bool IsOpen {get;private set;}
        internal static bool BlocksMap=>IsOpen||Time.frameCount<=closedFrame+1;
        internal static void CloseOpen()=>openWindow?.Close();

        internal static MissionContractWindow Create()
        {
            var go=new GameObject("BoscaliMissionContractWindow",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));
            var view=go.AddComponent<MissionContractWindow>();
            var canvas=go.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=SortOrder;
            var scaler=go.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution=new Vector2(1280,720);scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
            view.Build();return view;
        }
        internal void Show()
        {
            if(openWindow!=null&&openWindow!=this)openWindow.Close();
            openWindow=this;closing=false;renderedId=int.MinValue;window.Show();
            if(!ownsInput)
            {
                pauseWas=GameplayUI.AllowPauseKeybind;GameplayUI.AllowPauseKeybind=false;
                keyboardTouched=Rewired.ReInput.isReady&&Rewired.ReInput.controllers?.Keyboard!=null;
                if(keyboardTouched){keyboardWas=Rewired.ReInput.controllers.Keyboard.enabled;Rewired.ReInput.controllers.Keyboard.enabled=false;}
                ownsInput=true;
            }
            IsOpen=true;Refresh();window.Body.Relayout();
        }
        internal void Close()
        {
            if(closing)return;closing=true;
            window?.Hide();ReleaseInput();Destroy(gameObject);
        }
        private void ReleaseInput()
        {
            if(ownsInput)
            {
                GameplayUI.AllowPauseKeybind=pauseWas;
                if(keyboardTouched&&Rewired.ReInput.isReady&&Rewired.ReInput.controllers?.Keyboard!=null)Rewired.ReInput.controllers.Keyboard.enabled=keyboardWas;
                ownsInput=keyboardTouched=false;
            }
            if(openWindow!=this)return;
            openWindow=null;IsOpen=false;closedFrame=Time.frameCount;
        }
        private void OnDisable()=>Close();
        private void OnDestroy()=>ReleaseInput();
        private void Update()
        {
            if(openWindow!=this||!IsOpen)return;
            if(Input.GetKeyDown(KeyCode.Escape)){if(!actions.CancelConfirmation())Close();return;}
            if(Time.unscaledTime<nextRefresh)return;nextRefresh=Time.unscaledTime+.5f;Refresh();
        }
        private void Build()
        {
            window=AvWindow.Build(transform,"mission-desk","MIS / FACTION TASKING / TACTICAL BRIEF",Width,Height,SortOrder);
            window.Closed+=Close;window.CloseControl.Help="Close the task desk (Esc).";
            columns=window.Body.Add(new DeskColumns(window.Body.Content));
            rosterSection=new AvSection(columns.Rect,AvIcon.ListDetails,"TASK BOARD","HOST REPORT");
            filterControl=AvSegmented.Strip(columns.Rect,new[]{"ALL","OFFERS","ACTIVE","RESULTS"},()=>filter,i=>{filter=i;actions.CancelConfirmation();Refresh();});
            families=new AvStepper(columns.Rect,"FAMILY",()=>familyFilter==0?"ALL":MissionTaskPart.Family((ObjectiveFamily)familyFilter),()=>FilterFamily(-1),()=>FilterFamily(1));
            list=new AvList(columns.Rect,window.Ticker,6,BindRow);list.RowClicked=SelectRow;
            tasking=new MissionTaskPart(columns.Rect,true);window.Ticker.Register(tasking);tasking.Parent=columns;
            columns.Set(rosterSection,filterControl,families,list,tasking);
            actions=new MissionTaskActions(window.Root,Refresh,ViewMap);window.Ticker.Register(actions);
            window.Footer.Place(new AvSlot(0,Height-FooterHeight,Width,FooterHeight));
            LayoutActions();
        }
        private void FilterFamily(int delta)
        {familyFilter=(familyFilter+delta+9)%9;actions.CancelConfirmation();Refresh();}
        private void SelectRow(int index)
        {
            if(index<0||index>=roster.Count)return;
            selectedId=roster[index].Id;actions.CancelConfirmation();Render(lastBoardText);
        }
        private void BindRow(int index,AvRow row)
        {
            if(index<0||index>=roster.Count){row.Set("—","","");return;}
            var e=roster[index];string phase=e.IsActive?e.Tasking?.NextAction:e.IsOffered?"OFFER":e.IsComplete?"COMPLETE":"CLOSED";
            row.Set(MissionTaskPart.Clean(e.Title),MissionTaskPart.Family(e.Tasking?.Family??ObjectiveFamily.Unknown)+"\n"+MissionTaskPart.Clean(phase),"",e.Id==selectedId?AvState.Info:e.IsOffered?AvState.Caution:e.IsActive?AvState.Ready:AvState.Inert);
            row.Help="Inspect file "+e.Id+". Selecting a briefing does not accept it or change the pilot HUD.";
        }
        private void Refresh()
        {
            roster.Clear();activeCount=activeLimit=0;boardFresh=false;board=null;
            string status="MISSION DIRECTOR UNAVAILABLE";
            if(ModuleServices.TryGet(out board))
            {
                board.Refresh();boardFresh=board.IsFresh;activeLimit=board.ActiveLimit;
                status=string.IsNullOrWhiteSpace(board.Status)?"WAITING FOR HOST REPORT":board.Status;
                if(board.Objectives!=null)foreach(var e in board.Objectives)
                {
                    if(e==null)continue;if(e.IsActive)activeCount++;
                    if(filter==1&&!e.IsOffered||filter==2&&!e.IsActive||filter==3&&(e.IsActive||e.IsOffered))continue;
                    if(familyFilter>0&&e.Tasking?.Family!=(ObjectiveFamily)familyFilter)continue;
                    roster.Add(e);
                }
            }
            roster.Sort(CompareContracts);lastBoardText=status;Render(status);
        }
        /// <summary>Pure rendering seam also used by the actual production Unity atlas.</summary>
        private void Render(string boardText=null)
        {
            if(!roster.Exists(e=>e.Id==selectedId))
                selectedId=roster.Exists(e=>e.Id==board?.SelectedForHud)?board.SelectedForHud:roster.Count>0?roster[0].Id:-1;
            rosterSection.SetCaption(roster.Count+" IN VIEW / "+(boardFresh?"CURRENT":"HOST UNAVAILABLE"));
            filterControl.Refresh();families.Refresh();list.SetCount(roster.Count);
            var current=roster.Find(e=>e.Id==selectedId);
            tasking.Set(current,boardFresh);tasking.SetViewportHeight(550);
            actions.Set(current,boardFresh,activeLimit<=0||activeCount<activeLimit,board?.SelectedForHud??-1,board?.IsActionPending??false,board?.ActionResult??"");
            window.Footer.Set(boardFresh?"HOST REPORT / "+(board?.SnapshotAgeSeconds>=0?Mathf.CeilToInt(board.SnapshotAgeSeconds)+"s":"AGE UNKNOWN")+"  ·  "+(boardText??lastBoardText):"Host unavailable. Last briefing may be stale; current guidance and task actions are disabled.",boardFresh?AvState.Info:AvState.Caution);
            LayoutActions();window.Body.Relayout();
            if(renderedId!=selectedId)
            {
                // A new file starts at its heading; periodic reports preserve the operator's scroll.
                var scroll=window.Body.Content.parent.parent.GetComponent<ScrollRect>();
                scroll.StopMovement();scroll.verticalNormalizedPosition=1f;
                renderedId=selectedId;
            }
        }
        private void LayoutActions()
        {
            float actionH=Mathf.Max(64,actions.Measure(Width-28));
            float bodyH=Height-30-FooterHeight-actionH-12;
            var body=(RectTransform)window.Body.Content.parent.parent;var scroll=body.GetComponent<ScrollRect>();
            AvLay.Place(body,0,30,Width,bodyH);AvLay.Place(scroll.viewport,0,0,Width,bodyH);
            window.Body.ViewportHeight=bodyH;
            if(scroll.verticalScrollbar!=null){var bar=(RectTransform)scroll.verticalScrollbar.transform;bar.sizeDelta=new Vector2(bar.sizeDelta.x,bodyH-4);}
            actions.Place(new AvSlot(14,Height-FooterHeight-actionH-6,Width-28,actionH));
        }
        private void ViewMap()
        {
            Close();var map=SceneSingleton<DynamicMap>.i;if(map!=null&&!DynamicMap.mapMaximized)map.Maximize();
        }
        private static int CompareContracts(SecondaryObjectiveView a,SecondaryObjectiveView b)
        {
            int x=a.IsActive?0:a.IsOffered?1:2,y=b.IsActive?0:b.IsOffered?1:2;
            return x!=y?x.CompareTo(y):x==2?b.Id.CompareTo(a.Id):a.Id.CompareTo(b.Id);
        }
        private sealed class DeskColumns : AvPart
        {
            private AvSection heading;private AvSegmented filter;private AvStepper families;private AvList list;private MissionTaskPart task;
            public DeskColumns(RectTransform parent){Rect=AvLay.Child(parent,"Task board and briefing");}
            public void Set(AvSection h,AvSegmented f,AvStepper fam,AvList l,MissionTaskPart t)
            {heading=h;filter=f;families=fam;list=l;task=t;h.Parent=f.Parent=fam.Parent=l.Parent=t.Parent=this;}
            public override float Measure(float width)=>task==null?0:Mathf.Max(heading.Measure(276)+filter.Measure(276)+families.Measure(276)+list.Measure(276)+36,task.Measure(width-292));
            public override void Place(AvSlot s)
            {
                base.Place(s);float y=0;
                AvPart[] left={heading,filter,families,list};
                foreach(var p in left){float h=p.Measure(276);p.Place(new AvSlot(0,y,276,h));y+=h+8;}
                task.Place(new AvSlot(292,0,s.W-292,task.Measure(s.W-292)));
            }
            public override void Restyle(){heading?.Restyle();filter?.Restyle();families?.Restyle();list?.Restyle();task?.Restyle();}
        }
    }
}
