using System;
using System.Numerics;

namespace BoscaliSummer.Modules.Cinematography.Domain
{
    internal static class ShotRigs
    {
        internal static ShotSequence Demo(string mission,string scene,string actor)
        {
            var follow=Follow("test-follow",mission,scene,actor,new Vector3(-28,8,-38),4);
            follow.options.realtimeClock=true;follow.options.stabilizeAnchor=true;follow.options.letterbox=.08f;follow.options.fadeIn=.4f;follow.options.title="CAMERA TEST · FOLLOW";
            var orbit=Orbit("test-orbit",mission,scene,Vector3.Zero,50,10,6,-45,100);orbit.anchorId=actor;
            orbit.options.realtimeClock=true;orbit.options.stabilizeAnchor=true;orbit.options.simulationRate=.25f;orbit.options.letterbox=.08f;orbit.options.title="CAMERA TEST · ORBIT / WORLD x0.25";
            var dolly=Follow("test-dolly",mission,scene,actor,new Vector3(-45,10,-55),5);
            dolly.smooth=true;dolly.keys=new[] {Key(new Vector3(-45,10,-55),Quaternion.Identity,0),
                Key(new Vector3(-30,8,-42),Quaternion.Identity,2.5f),Key(new Vector3(-18,5,-25),Quaternion.Identity,5)};
            dolly.options.realtimeClock=true;dolly.options.stabilizeAnchor=true;dolly.options.dollyZoom=true;dolly.options.referenceDistance=60;
            dolly.options.letterbox=.08f;dolly.options.fadeOut=.5f;dolly.options.title="CAMERA TEST · DOLLY ZOOM";
            return new ShotSequence {id="simple-cinematic-test",clips=new[] {follow,orbit,dolly}};
        }
        internal static ShotPlan Follow(string id,string mission,string scene,string actor,Vector3 offset,float duration) =>
            new ShotPlan { id=id,mission=mission,scene=scene,anchorId=actor,duration=duration,
                options=new ShotOptions { lookAtTarget=true }, keys=new[] { Key(offset,Quaternion.Identity,0) } };

        internal static ShotPlan Orbit(string id,string mission,string scene,Vector3 center,float radius,float height,float duration,float from,float to)
        {
            var shot = new ShotPlan { id=id,mission=mission,scene=scene,duration=duration,spline=true,
                options=new ShotOptions { lookAtTarget=true,targetX=center.X,targetY=center.Y,targetZ=center.Z }, keys=new ShotKey[17] };
            for (int i=0;i<shot.keys.Length;i++)
            {
                float angle=(from+(to-from)*i/16)*MathF.PI/180;
                Vector3 position=center+new Vector3(MathF.Sin(angle)*radius,height,MathF.Cos(angle)*radius);
                shot.keys[i]=Key(position,ShotOptions.Look(center-position),i==16 ? duration : duration*i/16);
            }
            return shot;
        }
        private static ShotKey Key(Vector3 p,Quaternion q,float time) => new ShotKey
        { x=p.X,y=p.Y,z=p.Z,qX=q.X,qY=q.Y,qZ=q.Z,qW=q.W,time=time };

        internal static Vector3 Curve(Vector3 p0,Vector3 p1,Vector3 p2,Vector3 p3,float t)
        {
            // Centripetal Catmull–Rom; repeated points receive finite knot spacing.
            float t0=0,t1=Knot(p0,p1),t2=t1+Knot(p1,p2),t3=t2+Knot(p2,p3);
            float at=t1+(t2-t1)*t;
            Vector3 a1=Blend(p0,p1,t0,t1,at),a2=Blend(p1,p2,t1,t2,at),a3=Blend(p2,p3,t2,t3,at);
            Vector3 b1=Blend(a1,a2,t0,t2,at),b2=Blend(a2,a3,t1,t3,at);
            return Blend(b1,b2,t1,t2,at);
        }
        private static float Knot(Vector3 a,Vector3 b) => Math.Max(.001f,MathF.Sqrt(Vector3.Distance(a,b)));
        private static Vector3 Blend(Vector3 a,Vector3 b,float start,float end,float at) => ((end-at)*a+(at-start)*b)/(end-start);
    }
}
