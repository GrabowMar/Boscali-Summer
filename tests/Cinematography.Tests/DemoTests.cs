using System.Numerics;
using BoscaliSummer.Modules.Cinematography.Domain;

static class DemoTests
{
    internal static void Run(Action<bool,string> check)
    {
        var edit=ShotRigs.Demo("m","s","actor1");
        check(ShotSequence.Validate(edit,out _) && edit.clips.Length==3,"simple test edit is valid");
        check(edit.clips[1].options.simulationRate==.25f && edit.clips[1].options.realtimeClock,"orbit has explicit independent world slowmo");
        check(edit.clips[2].options.dollyZoom && edit.clips[2].keys.Length==3,"test covers tracked dolly lens");
        var anchor=new ShotPose(new Vector3(100,1000,100),Quaternion.Identity,45);
        var pose=edit.clips[0].SamplePresentation(0,anchor);
        check(Vector3.Dot(Vector3.Transform(Vector3.UnitZ,pose.Rotation),Vector3.Normalize(anchor.Position-pose.Position))>.99,"follow keeps subject framed");
        edit.clips[0].options.stabilizeAnchor=true;
        var rolled=new ShotPose(anchor.Position,Quaternion.CreateFromAxisAngle(Vector3.UnitZ,1.5f),45);
        check(Vector3.Distance(edit.clips[0].SamplePresentation(0,rolled).Position,pose.Position)<.01,"world-aligned anchor ignores aircraft roll/pitch");
    }
}
