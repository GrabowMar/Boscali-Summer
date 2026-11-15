using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;
using NOAvionics;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Presentation
{
    /// <summary>The open contract's host action under its detail: RELEASE an offer, or ABORT an accepted one behind a confirm. ACCEPT, SELECT and LOCATE are the card's row buttons.</summary>
    internal sealed class MissionTaskActions : AvPart
    {
        private readonly AvControl close;
        private readonly TMP_Text feedback;
        private readonly Action refresh;
        private SecondaryObjectiveView entry;
        private bool fresh, pending, confirm;
        private string result="";

        internal MissionTaskActions(RectTransform parent,Action refresh)
        {
            this.refresh=refresh;
            Rect=AvLay.Child(parent,"Mission actions");
            close=AvControl.Make(Rect,new AvControl.Spec("RELEASE",Cancel,AvButtonStyle.Quiet));
            feedback=AvText.Make(Rect,"Action feedback",AvTextRole.ProseSmall,"",TextAlignmentOptions.TopLeft,true);
            feedback.raycastTarget=false;
            close.Help="Release an offer, or confirm a faction-wide abort of an accepted job.";
            Restyle();
        }

        internal void Set(SecondaryObjectiveView objective,bool isFresh,bool isPending,string actionResult)
        {
            if(entry?.Id!=objective?.Id||entry?.IsActive!=objective?.IsActive)confirm=false;
            entry=objective;fresh=isFresh;pending=isPending;result=actionResult??"";
            if(!fresh||pending)confirm=false;
            bool hasBriefing=entry?.Tasking!=null;
            close.Label=entry?.IsActive==true?confirm?"CONFIRM ABORT":"ABORT":"RELEASE";
            close.Interactable=fresh&&!pending&&hasBriefing&&(entry.IsOffered||entry.IsActive);
            close.Armed=confirm;
            feedback.text=!fresh?"Host report unavailable. Job actions are disabled.":entry!=null&&!hasBriefing?"Host briefing unavailable. Refresh the job board.":pending?"WAITING FOR HOST ACKNOWLEDGMENT":confirm?AbortMessage():!string.IsNullOrWhiteSpace(result)?"HOST RESPONSE / "+JobCard.Clean(result):entry?.IsActive==true?"Acceptance is shared. HUD selection belongs to this pilot.":"Paid when the host confirms completion.";
            Changed();Restyle();
        }

        internal bool CancelConfirmation()
        {if(!confirm)return false;confirm=false;refresh?.Invoke();return true;}

        private void Cancel()
        {
            if(ModuleServices.TryGet(out ISecondaryObjectivesView view)&&view.IsFresh&&!view.IsActionPending&&view.Objectives!=null)
            {
                SecondaryObjectiveView current=null;
                foreach(var task in view.Objectives)if(task!=null&&task.Id==entry?.Id)current=task;
                if(current?.Tasking!=null)
                {
                    if(current.IsActive&&!confirm){confirm=true;refresh?.Invoke();return;}
                    if(current.IsActive||current.IsOffered)view.RequestCancel(current.Id);
                }
            }
            confirm=false;refresh?.Invoke();
        }
        private string AbortMessage()=>string.IsNullOrWhiteSpace(entry?.Tasking?.AbortConsequence)
            ?"Abort this faction's accepted job? The host applies its failure consequence. No money or XP."
            :"Abort this faction's accepted job? "+JobCard.Clean(entry.Tasking.AbortConsequence);

        public override float Measure(float width)=>40+AvText.Height(feedback,width)+8;
        public override void Place(AvSlot s)
        {
            base.Place(s);
            AvLay.Place(close.Rect,0,0,s.W*.43f,32);
            AvLay.Place(feedback.rectTransform,0,40,s.W,AvText.Height(feedback,s.W));
        }
        public override void Restyle()
        {feedback.color=AvStyleHost.FuiColor(confirm||pending?"caution":"ink-dim",confirm||pending?AvTheme.RailCaution:AvTheme.Dim);close.Restyle();}
    }
}
