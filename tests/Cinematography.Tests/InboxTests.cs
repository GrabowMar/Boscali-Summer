using BoscaliSummer.Modules.Cinematography;
using BoscaliSummer.Modules.Cinematography.Runtime;

static class InboxTests
{
    internal static void Run(Action<bool,string> check)
    {
        string root=Path.Combine(Path.GetTempPath(),"CinematicInbox-"+Guid.NewGuid().ToString("N"));int calls=0;
        var inbox=new CinematicInbox(root,args=>{calls++;return new Dictionary<string,object>{{"ok",true},{"api_version",1},{"actors",new object[] {new Dictionary<string,object>{{"id","one"}}}}};});
        try
        {
            string input=Path.Combine(root,"Inbox");Directory.CreateDirectory(input);
            File.WriteAllText(Path.Combine(input,"cmd01.json"),"{\"action\":\"capture\"}");inbox.Pump();
            check(calls==1 && File.Exists(Path.Combine(root,"Outbox","cmd01.json")),"file command executes and replies");
            File.WriteAllText(Path.Combine(input,"cmd01.json"),"{\"action\":\"capture\"}");inbox.Pump();
            check(calls==1,"duplicate ID does not execute twice");
            File.WriteAllText(Path.Combine(input,"cmd02.json"),"{");inbox.Pump();check(calls==1,"invalid inbox JSON cannot call director");
            File.WriteAllText(Path.Combine(root,"Outbox","cmd03.pending"),"in progress");
            File.WriteAllText(Path.Combine(input,"cmd03.json"),"{\"action\":\"capture\"}");inbox.Pump();check(calls==1,"ambiguous restart never replays a mutation");
            check(ShotStore.DecodeCommand("{\"action\":\"seek\",\"seconds\":2.5}",out var command,out _) && Convert.ToSingle(command["seconds"])==2.5f,"primitive command JSON codec");
            for(int i=0;i<129;i++) {File.WriteAllText(Path.Combine(input,"later"+i.ToString("000")+".json"),"{\"action\":\"status\"}");inbox.Pump();}
            int after=calls;
            File.Delete(Path.Combine(root,"Outbox","cmd01.json"));
            File.WriteAllText(Path.Combine(input,"cmd01.json"),"{\"action\":\"capture\"}");inbox.Pump();
            check(calls==after,"reply eviction must not allow a completed mutation to execute again");
            File.Delete(Path.Combine(root,"Outbox","cmd03.json"));
            File.WriteAllText(Path.Combine(input,"cmd03.json"),"{\"action\":\"capture\"}");inbox.Pump();
            check(calls==after && File.Exists(Path.Combine(root,"Outbox","cmd03.pending")),"ambiguous crash marker survives reply eviction");
        }
        finally {if(Directory.Exists(root))Directory.Delete(root,true);}
    }
}
