using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Modules;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Modules.Command.Presentation.MapUi;
using NOAvionics;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Presentation
{
    /// <summary>Both briefing surfaces dispatch the same validated host intents.</summary>
    internal sealed class MissionTaskActions : AvPart
    {
        private readonly AvControl primary,map,close;
        private readonly TMP_Text feedback;
        private readonly Action refresh, exposeMap;
        private SecondaryObjectiveView entry;
        private bool fresh, pending, confirm;
        private string result="";

        internal MissionTaskActions(RectTransform parent,Action refresh,Action exposeMap)
        {
            this.refresh=refresh;this.exposeMap=exposeMap;
            Rect=AvLay.Child(parent,"Mission actions");
            primary=AvControl.Make(Rect,new AvControl.Spec("SELECT TASK",Primary,AvButtonStyle.Primary));
            map=AvControl.Make(Rect,new AvControl.Spec("VIEW MAP",Map));
            close=AvControl.Make(Rect,new AvControl.Spec("DISMISS",Cancel,AvButtonStyle.Quiet));
            feedback=AvText.Make(Rect,"Action feedback",AvTextRole.ProseSmall,"",TextAlignmentOptions.TopLeft,true);
            feedback.raycastTarget=false;
            primary.Help="Accept through the host, or change this pilot's HUD selection.";
            map.Help="Expose the native map for a known task position. No flight order is issued.";
            close.Help="Dismiss an offer, or confirm a faction-wide abort of an accepted task.";
            Restyle();
        }

        internal void Set(SecondaryObjectiveView objective,bool isFresh,bool hasCapacity,int selectedId,bool isPending,string actionResult)
        {
            if(entry?.Id!=objective?.Id||entry?.IsActive!=objective?.IsActive)confirm=false;
            entry=objective;fresh=isFresh;pending=isPending;result=actionResult??"";
            if(!fresh||pending)confirm=false;
            primary.Label=confirm?"KEEP TASK":entry?.IsOffered==true?hasCapacity?"ACCEPT TASK":"ACTIVE LIMIT":entry?.IsActive==true?selectedId==entry.Id?"HUD / SELECTED":"SELECT FOR HUD":"TASK CLOSED";
            bool hasBriefing=entry?.Tasking!=null;
            primary.Interactable=fresh&&!pending&&hasBriefing&&(confirm||entry.IsActive||MfdSecondaryObjectives.CanAccept(entry,hasCapacity));
            primary.Latched=!confirm&&entry?.IsActive==true&&selectedId==entry.Id;
            map.Interactable=fresh&&!pending&&hasBriefing&&entry.HasMarker;
            close.Label=entry?.IsActive==true?confirm?"CONFIRM ABORT":"ABORT":"DISMISS";
            close.Interactable=fresh&&!pending&&hasBriefing&&(entry.IsOffered||entry.IsActive);
            close.Armed=confirm;
            feedback.text=!fresh?"Host report unavailable. Task actions are disabled.":entry!=null&&!hasBriefing?"Host briefing unavailable. Refresh the task board.":pending?"WAITING FOR HOST ACKNOWLEDGMENT":confirm?AbortMessage():!string.IsNullOrWhiteSpace(result)?"HOST RESPONSE / "+MissionTaskPart.Clean(result):entry?.IsActive==true?"Acceptance is shared. HUD selection belongs to this pilot.":entry==null?"Choose a task to review its briefing.":"Accepting a task does not complete it.";
            Changed();Restyle();
        }

        internal bool CancelConfirmation()
        {if(!confirm)return false;confirm=false;refresh?.Invoke();return true;}

        private bool ReadCurrent(out ISecondaryObjectivesView view,out SecondaryObjectiveView current,out bool capacity)
        {
            current=null;capacity=false;
            if(!ModuleServices.TryGet(out view))return false;
            if(!view.IsFresh||view.IsActionPending||view.Objectives==null)return false;
            int active=0;
            foreach(var task in view.Objectives)
            {if(task==null)continue;if(task.IsActive)active++;if(task.Id==entry?.Id)current=task;}
            capacity=view.ActiveLimit<=0||active<view.ActiveLimit;
            return current?.Tasking!=null;
        }

        private void Primary()
        {
            if(confirm){confirm=false;refresh?.Invoke();return;}
            if(ReadCurrent(out var view,out var current,out bool capacity))
            {
                if(current.IsOffered&&MfdSecondaryObjectives.CanAccept(current,capacity))view.RequestAccept(current.Id);
                else if(current.IsActive)view.SelectForHud(view.SelectedForHud==current.Id?-1:current.Id);
            }
            refresh?.Invoke();
        }
        private void Cancel()
        {
            if(ReadCurrent(out var view,out var current,out _))
            {
                if(current.IsActive&&!confirm){confirm=true;refresh?.Invoke();return;}
                if(current.IsActive||current.IsOffered)view.RequestCancel(current.Id);
            }
            confirm=false;refresh?.Invoke();
        }
        private void Map()
        {
            if(ReadCurrent(out _,out var current,out _)&&current.HasMarker)exposeMap?.Invoke();
            refresh?.Invoke();
        }
        private string AbortMessage()=>string.IsNullOrWhiteSpace(entry?.Tasking?.AbortConsequence)
            ?"Abort this faction's accepted task? The host applies its failure consequence. No money or XP."
            :"Abort this faction's accepted task? "+MissionTaskPart.Clean(entry.Tasking.AbortConsequence);

        public override float Measure(float width)=>40+AvText.Height(feedback,width)+8;
        public override void Place(AvSlot s)
        {
            base.Place(s);float a=s.W*.43f,b=s.W*.30f;
            AvLay.Place(primary.Rect,0,0,a-4,32);AvLay.Place(map.Rect,a+4,0,b-8,32);AvLay.Place(close.Rect,a+b+4,0,s.W-a-b-4,32);
            AvLay.Place(feedback.rectTransform,0,40,s.W,AvText.Height(feedback,s.W));
        }
        public override void Restyle()
        {feedback.color=AvStyleHost.FuiColor(confirm||pending?"caution":"ink-dim",confirm||pending?AvTheme.RailCaution:AvTheme.Dim);primary.Restyle();map.Restyle();close.Restyle();}
    }
}
