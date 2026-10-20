using System.Numerics;
using BoscaliSummer.Modules.Cinematography;
using BoscaliSummer.Modules.Cinematography.Domain;

static class FramingTests
{
    internal static void Run(Action<bool,string> check)
    {
        check(ShotStore.DecodeOptions("{}",4,out var defaults,out _) && defaults.frameX==.5f && defaults.frameY==.5f &&
            !defaults.headingOnlyAnchor && !defaults.worldSpacePath,"old option JSON preserves centred actor-local defaults");
        var follow=ShotRigs.Follow("framing","m","s","actor",new Vector3(5,12,-60),4);
        var actor=new ShotPose(new Vector3(100,300,200),Quaternion.CreateFromYawPitchRoll(.7f,.4f,1.1f),45);
        var original=follow.SamplePresentation(0,actor);
        check(Near(original.Position,actor.Position+Vector3.Transform(follow.keys[0].Position,actor.Rotation)),
            "default anchor retains full aircraft rotation");
        check(Near(Project(original,actor.Position,16f/9),new Vector2(.5f,.5f)),"default look-at remains centred");
        check(Math.Abs(Quaternion.Dot(original.Rotation,ShotOptions.Look(actor.Position-original.Position)))>.999999f,
            "centred framing retains the original look-at orientation");

        follow.options.headingOnlyAnchor=true;
        foreach(var angles in new[] {new Vector3(.7f,0,0),new Vector3(.7f,.9f,0),new Vector3(.7f,0,MathF.PI/2),
            new Vector3(.7f,.8f,1.2f),new Vector3(.7f,MathF.PI/2,0),new Vector3(.7f,-MathF.PI/2,0)})
        {
            var rotated=new ShotPose(actor.Position,Quaternion.CreateFromYawPitchRoll(angles.X,angles.Y,angles.Z),45);
            var pose=follow.SamplePresentation(0,rotated);
            var expected=actor.Position+Vector3.Transform(follow.keys[0].Position,Quaternion.CreateFromAxisAngle(Vector3.UnitY,angles.X));
            check(Near(pose.Position,expected),"heading-only offset stays level through bank, climb and vertical flight");
            check(Math.Abs(Vector3.Transform(Vector3.UnitX,pose.Rotation).Y)<.0001f,"heading-only look-at retains a level horizon");
        }
        var verticalBank=new ShotPose(actor.Position,Quaternion.CreateFromYawPitchRoll(.4f,MathF.PI/2,1.1f),45);
        var verticalPose=follow.SamplePresentation(0,verticalBank);
        check(Finite(verticalPose) && verticalPose.Position==follow.SamplePresentation(0,verticalBank).Position,
            "vertical heading fallback is finite and deterministic even with bank");
        follow.options.stabilizeAnchor=true;
        check(!ShotPlan.Validate(follow,out _),"conflicting anchor frames rejected");
        follow.options.headingOnlyAnchor=false;
        check(Near(follow.SamplePresentation(0,actor).Position,actor.Position+follow.keys[0].Position),
            "existing world-stabilized anchor remains unchanged");

        var track=ShotRigs.Follow("tracking","m","s","actor",new Vector3(800,120,1100),4);
        track.options.worldSpacePath=true;
        track.options.targetX=3;track.options.targetY=2;track.options.targetZ=4;
        Vector3 target=actor.Position+Vector3.Transform(new Vector3(3,2,4),actor.Rotation);
        var stationary=track.SamplePresentation(0,actor);
        check(Near(stationary.Position,track.keys[0].Position),"world path keeps a fixed global vantage with an actor anchor");
        check(Near(Project(stationary,target,16f/9),new Vector2(.5f,.5f)),"world path tracks the actor-local focus point");
        var moved=new ShotPose(actor.Position+new Vector3(300,70,-200),Quaternion.CreateFromYawPitchRoll(-.8f,.2f,-1),45);
        var panned=track.SamplePresentation(0,moved);
        var movedTarget=moved.Position+Vector3.Transform(new Vector3(3,2,4),moved.Rotation);
        check(panned.Position==stationary.Position && Math.Abs(Quaternion.Dot(panned.Rotation,stationary.Rotation))<.999f,
            "world vantage stays parked while its live target moves");
        check(Near(Project(panned,movedTarget,16f/9),new Vector2(.5f,.5f)),"parked vantage pans toward the current actor pose");
        track.options.lookAtTarget=false;
        check(track.SamplePresentation(0,moved).Rotation==track.keys[0].Rotation,"world path retains authored camera rotation when tracking disabled");
        track.options.lookAtTarget=true;
        var globalShift=new Vector3(1000000,2000000,-3000000);
        track.keys[0].x+=globalShift.X;track.keys[0].y+=globalShift.Y;track.keys[0].z+=globalShift.Z;
        var shiftedActor=new ShotPose(actor.Position+globalShift,actor.Rotation,45);
        var shifted=track.SamplePresentation(0,shiftedActor);
        check(Near(shifted.Position-globalShift,stationary.Position),"fixed vantage persists global coordinates independently of an origin shift");
        check(Math.Abs(Quaternion.Dot(shifted.Rotation,stationary.Rotation))>.99999f,"large global coordinates retain the tracking direction");

        foreach(float aspect in new[] {16f/9,9f/16,1f,21f/9})
            foreach(var frame in new[] {new Vector2(.5f,.5f),new Vector2(1f/3,2f/3),new Vector2(.8f,.2f),Vector2.Zero,Vector2.One})
            {
                follow.options.stabilizeAnchor=false;follow.options.headingOnlyAnchor=true;
                follow.options.frameX=frame.X;follow.options.frameY=frame.Y;
                var composed=follow.SamplePresentation(1,actor,aspect);
                check(Near(Project(composed,actor.Position,aspect),frame),"viewport framing projects exactly at wide and portrait aspects");
                check(Math.Abs(Vector3.Transform(Vector3.UnitX,composed.Rotation).Y)<.0001f,
                    "off-centre framing preserves the level horizon across viewport aspects");
            }
        var framing=new ShotOptions {lookAtTarget=true,frameX=.25f,frameY=.7f};
        var cameraPose=new ShotPose(Vector3.Zero,Quaternion.Identity,45);
        foreach(var focus in new[] {new Vector3(0,0,100),new Vector3(60,30,80),new Vector3(-60,-30,-80),new Vector3(80,-20,-60)})
        {
            framing.targetX=focus.X;framing.targetY=focus.Y;framing.targetZ=focus.Z;
            var aimed=framing.Apply(cameraPose,0,null);
            check(Near(Project(aimed,focus,16f/9),new Vector2(.25f,.7f)) &&
                Math.Abs(Vector3.Transform(Vector3.UnitX,aimed.Rotation).Y)<.0001f,
                "framing keeps exact composition and level horizon above, below and behind the camera");
        }
        var level=framing.Apply(cameraPose,0,null);
        framing.rollDegrees=10;
        var rolled=framing.Apply(cameraPose,0,null);
        check(Math.Abs(Quaternion.Dot(rolled.Rotation,level.Rotation*Quaternion.CreateFromYawPitchRoll(0,0,10*MathF.PI/180)))>.999999f,
            "framing retains deliberate camera roll after levelling");
        framing.rollDegrees=0;framing.targetX=.001f;framing.targetY=100;framing.targetZ=.002f;
        var polar=framing.Apply(cameraPose,0,null);
        check(Finite(polar) && Near(Project(polar,new Vector3(.001f,100,.002f),16f/9),new Vector2(.25f,.7f)),
            "near-vertical targets preserve exact off-centre composition when a level solution is impossible");
        follow.options.dollyZoom=true;follow.options.referenceDistance=30;
        check(Near(Project(follow.SamplePresentation(1,actor,9f/16),actor.Position,9f/16),Vector2.One),
            "composition uses the final dolly-zoom lens");
        follow.options.dollyZoom=false;
        check(Finite(follow.SamplePresentation(1,actor,float.NaN)) && Finite(follow.SamplePresentation(1,actor,0)),
            "unavailable runtime aspect falls back to a finite default");
        foreach(float bad in new[] {float.NaN,float.PositiveInfinity,-.001f,1.001f})
        {
            follow.options.frameX=bad;follow.options.frameY=.5f;
            check(!ShotPlan.Validate(follow,out _),"invalid horizontal framing rejected");
            follow.options.frameX=.5f;follow.options.frameY=bad;
            check(!ShotPlan.Validate(follow,out _),"invalid vertical framing rejected");
        }
        follow.options.frameX=.3f;follow.options.frameY=.7f;follow.options.lookAtTarget=false;
        check(!ShotPlan.Validate(follow,out _),"off-centre framing without tracking rejected");
        follow.options.lookAtTarget=true;follow.options.worldSpacePath=true;
        check(ShotStore.DecodeShot(ShotStore.Encode(follow),out var decoded,out _) && decoded.options.headingOnlyAnchor &&
            decoded.options.worldSpacePath && decoded.options.frameX==.3f && decoded.options.frameY==.7f,
            "new framing and anchor options survive canonical JSON round trip");
        check(Near(decoded.SamplePresentation(2,actor).Position,follow.SamplePresentation(2,actor).Position) &&
            Math.Abs(Quaternion.Dot(decoded.SamplePresentation(2,actor).Rotation,follow.SamplePresentation(2,actor).Rotation))>.99999f,
            "round trip preserves sampled composition");
    }

    private static bool Near(Vector3 a,Vector3 b) => Vector3.Distance(a,b)<.001f;
    private static bool Near(Vector2 a,Vector2 b) => Vector2.Distance(a,b)<.0001f;
    private static bool Finite(ShotPose pose) => ShotPlan.Finite(pose.Position.X) && ShotPlan.Finite(pose.Position.Y) &&
        ShotPlan.Finite(pose.Position.Z) && ShotPlan.Finite(pose.Rotation.LengthSquared());
    private static Vector2 Project(ShotPose pose,Vector3 target,float aspect)
    {
        Vector3 local=Vector3.Transform(target-pose.Position,Quaternion.Inverse(pose.Rotation));
        float tangent=MathF.Tan(pose.Fov*MathF.PI/360);
        return new Vector2(.5f+local.X/(2*local.Z*tangent*aspect),.5f+local.Y/(2*local.Z*tangent));
    }
}
