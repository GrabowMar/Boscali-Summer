using System.Numerics;
using BoscaliSummer.Modules.Cinematography;
using BoscaliSummer.Modules.Cinematography.Domain;

static class EditorTests
{
    internal static void Run(Action<bool,string> check)
    {
        var shot = ShotRigs.Orbit("orbit", "m", "s", new Vector3(100,100,100), 50, 10, 8, 0, 180);
        check(ShotPlan.Validate(shot, out _), "valid orbit");
        var a = shot.SamplePresentation(0);
        check(Math.Abs(a.Position.X - 100) < .01 && Math.Abs(a.Position.Z - 150) < .01, "orbit start");
        check(Vector3.Dot(Vector3.Transform(Vector3.UnitZ, a.Rotation), Vector3.Normalize(new Vector3(100,100,100)-a.Position)) > .99, "look-at rig");
        shot.options.shakeMeters = .5f; shot.options.shakeDegrees = 2;
        check(shot.SamplePresentation(2).Position == shot.SamplePresentation(2).Position, "scrub must reproduce shake");
        check(Vector3.Distance(shot.SamplePresentation(2).Position, shot.Sample(2).Position) <= .87f, "shake bound");
        shot.options.simulationRate = float.NaN; check(!ShotPlan.Validate(shot, out _), "NaN effect settings");
        shot.options.simulationRate = 1; shot.options.playbackRate = 0; check(!ShotPlan.Validate(shot, out _), "zero camera speed");
        shot.options.playbackRate = 1; shot.options.title = new string('x', 129); check(!ShotPlan.Validate(shot, out _), "title cap");
        shot.options.title = "";
        var clone = ShotStore.DecodeShot(ShotStore.Encode(shot), out var decoded, out _);
        check(clone && decoded.keys.Length == 17, "canonical script shot round trip");
        check(ShotStore.DecodeShot("{\"id\":\"partial\",\"mission\":\"m\",\"scene\":\"s\",\"keys\":[{}]}", out decoded, out _) && decoded.keys[0].qW == 1,
            "omitted JSON fields use documented neutral defaults");
        check(!ShotStore.DecodeShot("{", out _, out _), "malformed API JSON");
        var sequence = new ShotSequence { id = "edit01", clips = new[] { shot, shot } };
        check(ShotSequence.Validate(sequence, out _) && sequence.LengthSeconds == 16, "edit duration");
        sequence.clips = new ShotPlan[33]; check(!ShotSequence.Validate(sequence, out _), "edit clip ceiling");
        var anchor = new ShotPose(new Vector3(200,300,400), Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)Math.PI/2), 45);
        var follow = ShotRigs.Follow("follow", "m", "s", "actor1", new Vector3(0,10,-20), 4);
        var pose = follow.SamplePresentation(0, anchor);
        check(Vector3.Distance(pose.Position, new Vector3(180,310,400)) < .01, "actor local coordinates");
        var take = new TakeLog("take01", shot, "b");
        check(take.RecordEdit("seek", 6, "{}") && take.RecordEdit("seek", 1, "{}"), "backward camera edit records");
        check(!take.RecordEdit("bad", float.NaN, "{}"), "bad edit clock");
        take.Finish("Completed", "", 8);
        check(take.edits.Length == 2 && !take.RecordEdit("after", 1, "{}"), "closed edit log");
    }
}
