using System;
using System.Numerics;

namespace BoscaliSummer.Modules.Cinematography.Domain
{
    /// <summary>The ready-made camera moves. Positions are global (sea-level) metres, or metres from the actor when the shot has one.</summary>
    internal static class ShotRigs
    {
        /// <summary>Rides with the actor at <paramref name="offset"/> (right, up, ahead) and looks at it.</summary>
        internal static ShotPlan Follow(string actor,Vector3 offset,float duration) =>
            new ShotPlan { anchorId=actor,duration=duration,
                options=new ShotOptions { lookAtTarget=true }, keys=new[] { Key(offset,Quaternion.Identity,0) } };

        /// <summary>Stands at a fixed global vantage and pans with the actor.</summary>
        internal static ShotPlan Track(string actor,ShotPose vantage,float duration) =>
            new ShotPlan { anchorId=actor,duration=duration,
                options=new ShotOptions { lookAtTarget=true,worldSpacePath=true },
                keys=new[] { new ShotKey { x=vantage.Position.X,y=vantage.Position.Y,z=vantage.Position.Z,
                    qX=vantage.Rotation.X,qY=vantage.Rotation.Y,qZ=vantage.Rotation.Z,qW=vantage.Rotation.W,fov=vantage.Fov } } };

        /// <summary>
        /// Stands at <paramref name="offset"/> in the actor's heading frame and pans as the actor passes. Where that is gets worked out when the clip
        /// starts (<see cref="Place"/>), so a flyby late in an edit still stands ahead of where the actor then is.
        /// </summary>
        internal static ShotPlan Flyby(string actor,Vector3 offset,float duration)
        {
            var shot=Follow(actor,offset,duration);
            shot.options.headingOnlyAnchor=true;shot.placeAtStart=true;shot.keys[0].fov=35;
            return shot;
        }

        /// <summary>Turns a flyby's offset into its fixed global vantage, from where the actor is now.</summary>
        internal static void Place(ShotPlan shot,ShotPose subject)
        {
            ShotPose vantage=shot.SamplePresentation(0,subject);
            ShotKey key=shot.keys[0];
            key.x=vantage.Position.X;key.y=vantage.Position.Y;key.z=vantage.Position.Z;
            key.qX=vantage.Rotation.X;key.qY=vantage.Rotation.Y;key.qZ=vantage.Rotation.Z;key.qW=vantage.Rotation.W;
            shot.options.headingOnlyAnchor=false;shot.options.worldSpacePath=true;shot.placeAtStart=false;
        }

        /// <summary>Circles <paramref name="center"/> between two compass bearings.</summary>
        internal static ShotPlan Orbit(Vector3 center,float radius,float height,float duration,float from,float to)
        {
            var shot = new ShotPlan { duration=duration,spline=true,
                options=new ShotOptions { lookAtTarget=true,targetX=center.X,targetY=center.Y,targetZ=center.Z }, keys=new ShotKey[17] };
            for (int i=0;i<shot.keys.Length;i++)
            {
                float angle=(from+(to-from)*i/16)*MathF.PI/180;
                Vector3 position=center+new Vector3(MathF.Sin(angle)*radius,height,MathF.Cos(angle)*radius);
                shot.keys[i]=Key(position,ShotOptions.Look(center-position),i==16 ? duration : duration*i/16);
            }
            return shot;
        }

        /// <summary>Travels in a line from one point to another, looking at a third.</summary>
        internal static ShotPlan Fly(Vector3 from,Vector3 to,Vector3 look,float duration) =>
            new ShotPlan { duration=duration,smooth=true,
                options=new ShotOptions { lookAtTarget=true,targetX=look.X,targetY=look.Y,targetZ=look.Z },
                keys=new[] { Key(from,ShotOptions.Look(look-from),0),Key(to,ShotOptions.Look(look-to),duration) } };

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
