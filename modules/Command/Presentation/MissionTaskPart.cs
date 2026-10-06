using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Command.Presentation.MapUi;
using NOAvionics;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Presentation
{
    /// <summary>The same host briefing in the cockpit and task desk. All geometry is passive.</summary>
    internal sealed class MissionTaskPart : AvPart
    {
        private readonly bool expanded;
        private readonly TMP_Text family, title, identity, state;
        private readonly RectTransform order, gates, asset, assetArt, effect, record;
        private readonly TMP_Text number, orderHead, nextAction, condition;
        private readonly TMP_Text assetHead, assetName, contact, effectHead, effectText, reportHead, reportText;
        private readonly TMP_Text[] gateTitles = new TMP_Text[3], gateStates = new TMP_Text[3], gateNumbers = new TMP_Text[3];
        private readonly AvVector gateVectors, assetVector;
        private readonly TMP_Text rules;
        private readonly AvControl disclose;
        private SecondaryObjectiveView entry;
        private bool fresh, showRules;
        private ObjectivePhase[] phases = Array.Empty<ObjectivePhase>();
        private float plotHeight = 142f, placedWidth;
        internal MissionTaskPlot Plot { get; }

        private static Color Ink => AvInk.Ink;
        private static Color Dim => AvInk.Dim;
        private static Color Key => AvInk.Key;
        private static Color Amber => AvInk.State(AvState.Caution);

        internal MissionTaskPart(RectTransform parent, bool expanded = false)
        {
            this.expanded = expanded;
            Rect = AvLay.Child(parent, "Tactical mission briefing");
            family = Label(Rect,"Task family",AvTextRole.Micro);
            title = Label(Rect,"Task title",AvTextRole.Headline,true);
            identity = Label(Rect,"Lead pilot",AvTextRole.Micro,true);
            state = Label(Rect,"Host lifecycle",AvTextRole.Micro);
            order = Card(Rect,"Current order",true);
            number = Label(order,"Phase index",AvTextRole.Headline);
            orderHead = Label(order,"Order heading",AvTextRole.Micro);
            nextAction = Label(order,"Next action",AvTextRole.Title,true);
            condition = Label(order,"Completion condition",AvTextRole.ProseSmall,true);
            Plot = new MissionTaskPlot(Rect) { Parent = this };
            gates = Card(Rect,"Execution gates");
            gateVectors = AvVector.Create(gates,"Phase rail",512);
            for(int i=0;i<3;i++)
            {
                gateNumbers[i] = Label(gates,"Gate number",AvTextRole.Micro,false,TextAlignmentOptions.Center);
                gateTitles[i] = Label(gates,"Gate title",AvTextRole.Head,true,TextAlignmentOptions.Top);
                gateStates[i] = Label(gates,"Gate state",AvTextRole.Micro,false,TextAlignmentOptions.Top);
            }
            asset = Card(Rect,"Supported asset");
            assetHead = Label(asset,"Asset heading",AvTextRole.Micro);
            assetName = Label(asset,"Target identity",AvTextRole.Head,true);
            contact = Label(asset,"Contact quality",AvTextRole.Micro,true);
            assetArt=AvLay.Child(asset,"Identification silhouette");
            assetVector=AvVector.Create(assetArt,"Asset vectors",1200);
            effect=Card(Rect,"Mission effect");
            effectHead=Label(effect,"Effect heading",AvTextRole.Head);
            effectText=Label(effect,"Verified consequence",AvTextRole.Prose,true);
            record=Card(Rect,"Task record");
            reportHead=Label(record,"Host report",AvTextRole.Micro);
            reportText=Label(record,"Task facts",AvTextRole.ProseSmall,true);
            disclose=AvControl.Make(Rect,new AvControl.Spec("PHASE CONDITIONS",()=>
            {
                showRules=!showRules;
                disclose.Latched=showRules;
                rules.gameObject.SetActive(showRules&&phases.Length>0);
                Changed();
            }));
            disclose.Help="Read every host-reported completion condition.";
            rules=Label(Rect,"All phase conditions",AvTextRole.ProseSmall,true);
            Restyle();
        }

        internal void Set(SecondaryObjectiveView objective, bool isFresh)
        {
            entry=objective; fresh=isFresh;
            ObjectiveTasking task=entry?.Tasking;
            phases=task?.Phases??Array.Empty<ObjectivePhase>();
            family.text=entry==null?"FACTION TASKING":Family(task?.Family??ObjectiveFamily.Unknown)+" / FILE "+entry.Id.ToString("000");
            title.text=entry==null?"NO TASK SELECTED":Clean(entry.Title);
            identity.text=entry==null?"Select a task to inspect its briefing.":"LEAD / "+(string.IsNullOrWhiteSpace(entry.AcceptedBy)?entry.IsOffered?"UNASSIGNED":"NOT REPORTED":Clean(entry.AcceptedBy));
            state.text=!fresh?"HOST UNAVAILABLE":entry==null?"NO TASK":entry.IsOffered?"OFFER":entry.IsActive?"ACCEPTED":entry.IsComplete?"COMPLETE":task?.Lifecycle==ObjectiveLifecycle.Cancelled?"ABORTED":"CLOSED";
            int current=task?.CurrentPhase??-1;
            number.text=current>=0?(current+1).ToString("00"):entry?.IsComplete==true?"✓":"—";
            orderHead.text=!fresh?"CURRENT GUIDANCE UNAVAILABLE":entry?.IsOffered==true?"PROPOSED TASK":entry?.IsComplete==true?"HOST COMPLETION REPORT":"CURRENT ORDER";
            nextAction.text=!fresh?"Wait for a fresh host report.":entry==null?"Choose a task from the board.":!string.IsNullOrWhiteSpace(task?.NextAction)?Clean(task.NextAction):entry.IsOffered?"Review the host briefing, then accept.":entry.IsComplete?"Task complete.":Clean(entry.Status);
            condition.text=!fresh?"Previous briefing retained. Positions, timers and task actions are unavailable.":entry==null?"Active tasking and new offers share the same faction board.":current>=0?Clean(phases[current].Condition):Clean(entry.Description);
            if(fresh&&!string.IsNullOrWhiteSpace(task?.Blocker)) condition.text+="\n"+Clean(task.Blocker);
            assetHead.text="TASK ASSET / "+Asset(task?.Asset??ObjectiveAsset.Unknown);
            assetName.text=entry==null?"IDENTITY NOT REPORTED":string.IsNullOrWhiteSpace(entry.Target)?"IDENTITY NOT REPORTED":Clean(entry.Target);
            contact.text=!fresh?"POSITION UNAVAILABLE":Contact(task,entry);
            effectHead.text="MISSION EFFECT";
            effectText.text=entry==null?"Select a task to review its purpose.":string.IsNullOrWhiteSpace(task?.Effect)?Clean(entry.Description):Clean(task.Effect);
            reportHead.text="FACTION / HOST TASKING";
            reportText.text=entry==null?"No task selected.":(!fresh?"DEADLINE / UNAVAILABLE":"DEADLINE / "+MfdSecondaryObjectives.ChipLabel(entry))+"\nTASK AWARD / $"+AvNum.Thousands(Math.Max(0,entry.Money))+" + "+AvNum.Thousands(Math.Max(0,entry.Xp))+" XP\n"+(entry.IsActive?"Acceptance is shared by the faction.":entry.IsOffered?"Rewards require host-confirmed completion.":Clean(entry.Status));
            rules.text="";
            for(int i=0;i<phases.Length;i++) rules.text+=(i==0?"":"\n\n")+(i+1)+" / "+Clean(phases[i].Title)+"\n"+Clean(phases[i].Condition);
            disclose.gameObject.SetActive(phases.Length>0);
            rules.gameObject.SetActive(showRules&&phases.Length>0);
            for(int i=0;i<3;i++)
            {
                bool visible=i<phases.Length;
                gateNumbers[i].gameObject.SetActive(visible); gateTitles[i].gameObject.SetActive(visible); gateStates[i].gameObject.SetActive(visible);
                if(!visible)continue;
                gateNumbers[i].text=(i+1).ToString(); gateTitles[i].text=Clean(phases[i].Title);
                gateStates[i].text=!fresh?"STALE":phases[i].Status==ObjectivePhaseStatus.Done?"DONE":phases[i].Status==ObjectivePhaseStatus.Current?"CURRENT":"PENDING";
                if(fresh&&phases[i].Status==ObjectivePhaseStatus.Current&&phases[i].Progress>0)
                    gateStates[i].text+=" / "+Mathf.FloorToInt(Mathf.Clamp01(phases[i].Progress)*100)+"%";
            }
            gates.gameObject.SetActive(phases.Length>0);
            Plot.Set(entry,fresh);
            Restyle(); Changed();
        }

        internal void SetViewportHeight(float height)
        {
            float next=expanded?250f:height>=570f?240f:142f;
            if(Mathf.Abs(next-plotHeight)<.1f)return;
            plotHeight=next; Changed();
        }

        public override float Measure(float width)
        {
            float left=expanded?width*.61f:width;
            float main=IdentityHeight(left)+OrderHeight(left)+GateHeight(left)+plotHeight+36f;
            if(expanded) main=Mathf.Max(main,AssetHeight(width-left-16f)+EffectHeight(width-left-16f)+RecordHeight(width-left-16f)+24f);
            else main+=RecordHeight(width)+12f;
            if(phases.Length>0) main+=36f+(showRules?AvText.Height(rules,width)+12f:0f);
            return main;
        }

        public override void Place(AvSlot s)
        {
            base.Place(s);
            placedWidth=s.W;
            float left=expanded?s.W*.61f:s.W,right=s.W-left-16f;
            float y=0f;
            AvLay.Place(family,0,y,left-130f,20); AvLay.Place(state,left-126f,y,126f,20); state.alignment=TextAlignmentOptions.TopRight;
            float th=AvText.Height(title,left); AvLay.Place(title,0,24,left,th);
            float ih=AvText.Height(identity,left); AvLay.Place(identity,0,28+th,left,ih);
            y=IdentityHeight(left);
            // Cockpit order and gates precede the plot; the desk retains B's plot dominance.
            if(!expanded) { PlaceOrder(left,y); y+=OrderHeight(left)+12; PlaceGates(left,y); y+=GateHeight(left)+12; }
            Plot.Height=plotHeight; Plot.Place(new AvSlot(0,y,left,plotHeight)); y+=plotHeight+12;
            if(expanded){ PlaceOrder(left,y);y+=OrderHeight(left)+12;PlaceGates(left,y);y+=GateHeight(left)+12; }
            if(expanded)
            {
                float ry=0; PlaceAsset(left+16,ry,right);ry+=AssetHeight(right)+12;
                PlaceTextCard(effect,effectHead,effectText,left+16,ry,right,EffectHeight(right));ry+=EffectHeight(right)+12;
                PlaceTextCard(record,reportHead,reportText,left+16,ry,right,RecordHeight(right));
                y=Mathf.Max(y,ry+RecordHeight(right));
            }
            else
            {
                asset.gameObject.SetActive(false);effect.gameObject.SetActive(false);
                PlaceTextCard(record,reportHead,reportText,0,y,left,RecordHeight(left));y+=RecordHeight(left)+12;
            }
            if(phases.Length>0)
            {
                AvLay.Place(disclose.Rect,0,y,s.W,28);y+=36;
                if(showRules)AvLay.Place(rules,0,y,s.W,AvText.Height(rules,s.W));
            }
        }

        private float IdentityHeight(float w)=>32+AvText.Height(title,w)+AvText.Height(identity,w)+12;
        private float OrderHeight(float w)=>36+AvText.Height(nextAction,w-88)+AvText.Height(condition,w-88)+16;
        private float GateHeight(float w)
        {
            if(phases.Length==0)return 0;
            float h=0,cw=w/Mathf.Min(3,phases.Length);
            for(int i=0;i<Mathf.Min(3,phases.Length);i++) h=Mathf.Max(h,AvText.Height(gateTitles[i],cw-8));
            return 40+h+20;
        }
        private float AssetHeight(float w)=>108+AvText.Height(assetName,w-24)+AvText.Height(contact,w-24)+20;
        private float EffectHeight(float w)=>46+AvText.Height(effectText,w-24);
        private float RecordHeight(float w)=>46+AvText.Height(reportText,w-24);

        private void PlaceOrder(float w,float y)
        {
            float h=OrderHeight(w);AvLay.Place(order,0,y,w,h);
            AvLay.Place(number,12,12,48,44);AvLay.Place(orderHead,76,8,w-88,18);
            float nh=AvText.Height(nextAction,w-88);AvLay.Place(nextAction,76,32,w-88,nh);AvLay.Place(condition,76,38+nh,w-88,AvText.Height(condition,w-88));
        }
        private void PlaceGates(float w,float y)
        {
            if(phases.Length==0)return;
            float h=GateHeight(w);AvLay.Place(gates,0,y,w,h);var b=gateVectors.Buffer;b.Clear();int n=Mathf.Min(3,phases.Length);float cw=w/n;
            for(int i=0;i<n;i++)
            {
                Color c=!fresh?Dim:phases[i].Status==ObjectivePhaseStatus.Done?Key:phases[i].Status==ObjectivePhaseStatus.Current?Amber:Dim;
                float cx=cw*(i+.5f),cy=h-18;
                if(i<n-1) AvStrokes.DashedLine(b,cx+11,cy,cx+cw-11,cy,5,4,.8f,new Rgba(Dim.r,Dim.g,Dim.b,.5f));
                AvStrokes.Ring(b,cx,cy,10,32,phases[i].Status==ObjectivePhaseStatus.Current?1.5f:.8f,new Rgba(c.r,c.g,c.b,c.a));
                if(fresh&&phases[i].Status==ObjectivePhaseStatus.Current&&phases[i].Progress>0)
                    AvStrokes.Arc(b,cx,cy,12f,90f,90f+360f*Mathf.Clamp01(phases[i].Progress),32,1f,new Rgba(c.r,c.g,c.b,c.a));
                AvLay.Place(gateNumbers[i],cx-12,6,24,24);AvLay.Place(gateTitles[i],cw*i+4,34,cw-8,AvText.Height(gateTitles[i],cw-8));AvLay.Place(gateStates[i],cw*i+4,h-20,cw-8,18);
                gateNumbers[i].color=gateTitles[i].color=gateStates[i].color=c;
            }
            gateVectors.Commit();
        }
        private void PlaceAsset(float x,float y,float w)
        {
            asset.gameObject.SetActive(true);AvLay.Place(asset,x,y,w,AssetHeight(w));AvLay.Place(assetHead,12,8,w-24,18);
            float nameH=AvText.Height(assetName,w-24);AvLay.Place(assetName,12,34,w-24,nameH);
            // AvVector uses bottom-left coordinates; place its parent without changing that pivot.
            AvLay.Place(assetArt,12,42+nameH,w-24,76);
            AvLay.Place(contact,12,122+nameH,w-24,AvText.Height(contact,w-24));
            PaintAsset();
        }
        private void PaintAsset()
        {
            float width=assetVector.rectTransform.rect.width;
            if(width<=0)return;
            MissionTaskGraphics.DrawAsset(assetVector,entry?.Tasking?.Asset??ObjectiveAsset.Unknown,entry?.Tasking?.Family??ObjectiveFamily.Unknown,Mathf.Min(width,210),76,fresh?Key:Dim);
        }
        private static void PlaceTextCard(RectTransform card,TMP_Text head,TMP_Text text,float x,float y,float w,float h)
        {card.gameObject.SetActive(true);AvLay.Place(card,x,y,w,h);AvLay.Place(head,12,8,w-24,22);AvLay.Place(text,12,36,w-24,AvText.Height(text,w-24));}

        public override void Restyle()
        {
            if(Rect==null)return;
            family.color=assetHead.color=effectHead.color=reportHead.color=Key;
            title.color=nextAction.color=assetName.color=Ink;
            identity.color=condition.color=contact.color=effectText.color=reportText.color=rules.color=Dim;
            state.color=number.color=orderHead.color=Amber;
            if(placedWidth>0)
            {
                PlaceGates(expanded?placedWidth*.61f:placedWidth,-gates.anchoredPosition.y);
                if(expanded)PaintAsset();
            }
            Plot?.Restyle();
            foreach(var f in Rect.GetComponentsInChildren<AvFrame>(true)) if(f.gameObject.name=="Group frame") f.Paint(AvStyleHost.FuiColor(f.transform.parent==order?"surface-raised":"surface",AvTheme.Surface),f.transform.parent==order?Amber.WithAlpha(.65f):AvStyleHost.FuiColor("hairline",AvTheme.Hairline));
        }

        private static TMP_Text Label(RectTransform p,string name,AvTextRole role,bool wrap=false,TextAlignmentOptions alignment=TextAlignmentOptions.TopLeft)
        {var t=AvText.Make(p,name,role,"",alignment,wrap);t.raycastTarget=false;if(!wrap)AvText.Fit(t,false);return t;}
        private static RectTransform Card(RectTransform parent,string name,bool active=false)
        {var b=AvLay.Child(parent,name);var f=AvFrame.Add(b,"Group frame",AvChamfer.Diagonal(8));AvLay.Fill(f.rectTransform);f.Bracket=6;f.raycastTarget=false;return b;}
        internal static string Clean(string value)=>MfdSecondaryObjectives.Humanize(MfdSecondaryObjectives.PlainObjective(value??""));
        internal static string Family(ObjectiveFamily value)=>value==ObjectiveFamily.AirCover?"AIR COVER":value==ObjectiveFamily.Unknown?"SIDE MISSION":value.ToString().ToUpperInvariant();
        private static string Asset(ObjectiveAsset value)=>value==ObjectiveAsset.GroundVehicle?"GROUND VEHICLE":value==ObjectiveAsset.Unknown?"TYPE UNKNOWN":value.ToString().ToUpperInvariant();
        private static string Contact(ObjectiveTasking task,SecondaryObjectiveView view)
        {
            if(view==null||!view.HasMarker)return task?.Contact==ObjectiveContact.Lost?"CONTACT LOST / REACQUIRE":"POSITION NOT REPORTED";
            string word=task?.Contact==ObjectiveContact.LastKnown?"LAST KNOWN POSITION":task?.Contact==ObjectiveContact.Fixed?"FIXED LOCATION":"KNOWN POSITION";
            return word+(task!=null&&task.ContactAgeSeconds>=0&&!float.IsInfinity(task.ContactAgeSeconds)?" / AT REPORT "+Mathf.CeilToInt(task.ContactAgeSeconds)+"s":" / AGE UNKNOWN");
        }
    }
}
