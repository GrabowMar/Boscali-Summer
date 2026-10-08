using System;
using System.Collections.Generic;
using System.IO;
using BoscaliSummer.Core.Storage;
using BoscaliSummer.Modules.Cinematography.Domain;

namespace BoscaliSummer.Modules.Cinematography.Runtime
{
    // File transport for LLMs without a runtime reflection tool. Pump runs on Unity's main thread, one request per tick.
    internal sealed class CinematicInbox
    {
        private readonly string input,output;
        private readonly Func<Dictionary<string,object>,Dictionary<string,object>> dispatch;
        private string pendingId,pendingResponse;
        private readonly HashSet<string> completed=new HashSet<string>(StringComparer.Ordinal);
        private readonly string history;
        private bool historyLoaded,pendingMutation,preserveAmbiguity;
        internal const int HistoryLimit=4096;
        internal string LastError { get; private set; }
        internal CinematicInbox(string root,Func<Dictionary<string,object>,Dictionary<string,object>> handler)
        {input=Path.Combine(root,"Inbox");output=Path.Combine(root,"Outbox");history=Path.Combine(root,"History");dispatch=handler;}
        internal void Pump()
        {
            try
            {
                Directory.CreateDirectory(input);Directory.CreateDirectory(output);
                Directory.CreateDirectory(history);
                if(!historyLoaded)
                {
                    int seen=0;
                    foreach(string file in Directory.EnumerateFiles(history,"*.done"))
                    {if(ShotPlan.SafeId(Path.GetFileNameWithoutExtension(file))) completed.Add(Path.GetFileNameWithoutExtension(file));if(++seen>=HistoryLimit)break;}
                    historyLoaded=true;
                }
                if(pendingId!=null) { Flush();return; }
                pendingMutation=false;
                preserveAmbiguity=false;
                var candidates=new List<string>(64);
                int scanned=0;
                foreach(string file in Directory.EnumerateFiles(input,"*.json"))
                {if(ShotPlan.SafeId(Path.GetFileNameWithoutExtension(file))) candidates.Add(file);if(++scanned==64)break;}
                candidates.Sort(StringComparer.Ordinal);
                if(candidates.Count==0) return;
                string request=candidates[0],id=Path.GetFileNameWithoutExtension(request);
                if(File.Exists(Path.Combine(output,id+".json"))) { File.Delete(request);LastError=null;return; }
                Dictionary<string,object> response;
                string marker=Path.Combine(output,id+".pending");
                if(completed.Contains(id) || File.Exists(Path.Combine(history,id+".done"))) response=Error("ALREADY_EXECUTED; reply expired, mutation will not be replayed");
                else if(File.Exists(marker)) { preserveAmbiguity=true;response=Error("AMBIGUOUS_PRIOR_EXECUTION; inspect status and use a new ID, never automatically retry the mutation"); }
                else if(new FileInfo(request).Length>ShotStore.FileLimit) response=Error("Request exceeds 1 MiB");
                else if(!ShotStore.DecodeCommand(File.ReadAllText(request),out var args,out string error)) response=Error(error);
                else
                {
                    string action=args.TryGetValue("action",out var a)?a as string:null;
                    bool mutation=action!="schema" && action!="status" && action!="get" && action!="pose" && action!="actors" && action!="list";
                    if(mutation && completed.Count>=HistoryLimit) response=Error("SCRIPT_HISTORY_FULL; archive the channel while inactive before starting a fresh production session");
                    else
                    {
                    if(!AtomicFile.WriteAllText(marker,"Execution may have started; do not replay after restart.",out error)) {LastError=error;return;}
                    try {response=dispatch(args);}catch(Exception e) {response=Error("Dispatch failed: "+e.GetType().Name);}
                    pendingMutation=mutation && response.TryGetValue("ok",out var ok) && ok is bool accepted && accepted;
                    }
                }
                response["request_id"]=id;
                pendingResponse=ShotStore.Encode(response);pendingId=id;Flush();
            }
            catch(Exception e) when(e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is System.Runtime.Serialization.SerializationException)
            {LastError="SCRIPT INBOX: "+e.GetType().Name;}
        }
        private void Flush()
        {
            if(pendingMutation && !completed.Contains(pendingId))
            {
                if(!AtomicFile.WriteAllText(Path.Combine(history,pendingId+".done"),"Mutation executed; never reuse this ID.",out string historyError)) {LastError=historyError;return;}
                completed.Add(pendingId);
            }
            if(!AtomicFile.WriteAllText(Path.Combine(output,pendingId+".json"),pendingResponse,out string error)) {LastError=error;return;}
            string id=pendingId;pendingId=pendingResponse=null;pendingMutation=false;
            if(!preserveAmbiguity) File.Delete(Path.Combine(output,id+".pending"));
            preserveAmbiguity=false;File.Delete(Path.Combine(input,id+".json"));LastError=null;
            var replies=new List<FileInfo>(129);
            int inspected=0;
            foreach(string file in Directory.EnumerateFiles(output,"*.json"))
            {if(ShotPlan.SafeId(Path.GetFileNameWithoutExtension(file))) replies.Add(new FileInfo(file));if(++inspected==129)break;}
            if(replies.Count>128) {replies.Sort((a,b)=>a.LastWriteTimeUtc.CompareTo(b.LastWriteTimeUtc));File.Delete(replies[0].FullName);}
        }
        private static Dictionary<string,object> Error(string message)=>new Dictionary<string,object>{["ok"]=false,["api_version"]=1,["error"]=message};
    }
}
