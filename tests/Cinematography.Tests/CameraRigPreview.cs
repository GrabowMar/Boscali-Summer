#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using BoscaliSummer.Modules.Cinematography.Domain;
using UnityEngine;
using Object=UnityEngine.Object;
using NVector3=System.Numerics.Vector3;
using NQuaternion=System.Numerics.Quaternion;

internal static class CameraRigPreview
{
    internal static void Run()
    {
        var root=new GameObject("Offline cinematic rig fixture");
        var materials=new List<Material>();
        try
        {
            Material Paint(Color color) { var m=new Material(Shader.Find("Unlit/Color")){color=color};materials.Add(m);return m; }
            var lightGround=Paint(new Color(.19f,.24f,.23f));var darkGround=Paint(new Color(.15f,.19f,.18f));
            var hull=Paint(new Color(.66f,.72f,.67f));var wing=Paint(new Color(.35f,.42f,.36f));
            var glass=Paint(new Color(.06f,.16f,.20f));var warning=Paint(new Color(.85f,.45f,.16f));
            for(int x=-5;x<=5;x++) for(int z=-3;z<=7;z++)
                Cube(root.transform,new Vector3(x*40,-1,z*40),new Vector3(40,1,40),(x+z)%2==0?lightGround:darkGround);
            var aircraft=new GameObject("Stylized aircraft heading +Z");aircraft.transform.SetParent(root.transform,false);
            Cube(aircraft.transform,Vector3.zero,new Vector3(1.7f,1.4f,14),hull);
            Cube(aircraft.transform,new Vector3(0,0,-1),new Vector3(18,.35f,3),wing);
            Cube(aircraft.transform,new Vector3(0,.5f,-5),new Vector3(7,.3f,2),wing);
            Cube(aircraft.transform,new Vector3(0,2,-5),new Vector3(.3f,3,2),hull);
            Cube(aircraft.transform,new Vector3(0,.9f,3),new Vector3(1.2f,.6f,3),glass);
            Cube(aircraft.transform,new Vector3(0,0,7),new Vector3(.8f,.8f,1.5f),warning);
            var camera=new GameObject("Rig verification camera").AddComponent<Camera>();camera.transform.SetParent(root.transform);
            camera.nearClipPlane=.1f;camera.farClipPlane=2000;camera.clearFlags=CameraClearFlags.SolidColor;
            camera.backgroundColor=new Color(.34f,.43f,.49f);
            var actorLocal=new Vector3(0,40,150);var global=actorLocal.ToGlobalPosition();
            var actor=new ShotPose(new NVector3(global.x,global.y,global.z),NQuaternion.CreateFromYawPitchRoll(.4f,.25f,1.1f),45);
            var chase=ShotRigs.Follow("preview-chase","preview","editor","actor",new NVector3(8,8,-50),4);
            chase.options.headingOnlyAnchor=true;
            Render(camera,aircraft.transform,chase,actor,1280,720,"camera-heading-chase");
            var level=chase.SamplePresentation(0,actor);
            var expected=actor.Position+NVector3.Transform(chase.keys[0].Position,NQuaternion.CreateFromAxisAngle(NVector3.UnitY,.4f));
            Require(NVector3.Distance(level.Position,expected)<.001f,"heading chase retained actor bank or pitch");
            var track=ShotRigs.Follow("preview-track","preview","editor","actor",actor.Position+new NVector3(-65,-15,-90),4);
            track.options.worldSpacePath=true;track.options.frameX=1f/3;track.options.frameY=2f/3;
            Render(camera,aircraft.transform,track,actor,1280,720,"camera-track-thirds-landscape");
            var moved=new ShotPose(actor.Position+new NVector3(20,4,15),NQuaternion.CreateFromYawPitchRoll(-.2f,.1f,.65f),45);
            Require(track.SamplePresentation(2,moved).Position==track.SamplePresentation(0,actor).Position,"world vantage moved with actor");
            Render(camera,aircraft.transform,track,moved,720,1280,"camera-track-thirds-portrait");
            File.WriteAllText("renders/camera-rigs-check.txt","PASS: production domain rigs projected through real Unity Camera at landscape/portrait aspects; heading, fixed world vantage, moving target and native global/local conversion checked. Offline stylized fixture only.");
        }
        finally { Object.DestroyImmediate(root);foreach(var material in materials)Object.DestroyImmediate(material); }
    }

    private static void Cube(Transform parent,Vector3 at,Vector3 scale,Material material)
    {
        var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);cube.transform.SetParent(parent,false);
        cube.transform.localPosition=at;cube.transform.localScale=scale;cube.GetComponent<Renderer>().sharedMaterial=material;
        Object.DestroyImmediate(cube.GetComponent<Collider>());
    }
    private static void Render(Camera camera,Transform aircraft,ShotPlan plan,ShotPose actor,int width,int height,string name)
    {
        Require(ShotPlan.Validate(plan,out var error),"invalid preview shot: "+error);
        var pose=plan.SamplePresentation(0,actor,(float)width/height);
        camera.transform.SetPositionAndRotation(Local(pose.Position),Rotation(pose.Rotation));camera.fieldOfView=pose.Fov;
        aircraft.SetPositionAndRotation(Local(actor.Position),Rotation(actor.Rotation));
        var target=new RenderTexture(width,height,24);var previous=RenderTexture.active;Texture2D png=null;
        try
        {
            camera.targetTexture=target;camera.aspect=(float)width/height;
            var viewport=camera.WorldToViewportPoint(aircraft.position);
            Require(viewport.z>0 && Math.Abs(viewport.x-plan.options.frameX)<.0001f && Math.Abs(viewport.y-plan.options.frameY)<.0001f,
                "Unity viewport framing mismatch: "+name+" "+viewport);
            camera.Render();RenderTexture.active=target;png=new Texture2D(width,height,TextureFormat.RGB24,false);
            png.ReadPixels(new Rect(0,0,width,height),0,0);png.Apply();Directory.CreateDirectory("renders");
            File.WriteAllBytes("renders/"+name+".png",png.EncodeToPNG());
        }
        finally { camera.targetTexture=null;RenderTexture.active=previous;if(png!=null)Object.DestroyImmediate(png);Object.DestroyImmediate(target); }
    }
    private static Vector3 Local(NVector3 p)=>new GlobalPosition(p.X,p.Y,p.Z).ToLocalPosition();
    private static Quaternion Rotation(NQuaternion q)=>new Quaternion(q.X,q.Y,q.Z,q.W);
    private static void Require(bool condition,string error) { if(!condition)throw new Exception(error); }
}
#endif
