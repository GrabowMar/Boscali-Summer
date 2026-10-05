using BoscaliSummer.Core.Contracts;
using NOAvionics;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Presentation
{
    /// <summary>Passive, bounded asset identification art. Silhouettes identify a class, never a specific vehicle model.</summary>
    internal static class MissionTaskGraphics
    {
        internal static Color Ink => AvStyleHost.FuiColor("ink", AvTheme.TextPrimary);
        internal static Color Dim => AvStyleHost.FuiColor("ink-dim", AvTheme.Dim);
        internal static Color Key => AvStyleHost.FuiColor("key", AvTheme.RailInfo);
        internal static Color Amber => AvStyleHost.FuiColor("caution", AvTheme.RailCaution);
        internal static Color Ground => AvStyleHost.FuiColor("ground", AvTheme.Ground);
        internal static Rgba RgbaOf(Color c) => new Rgba(c.r, c.g, c.b, c.a);

        internal static void DrawAsset(AvVector v, ObjectiveAsset asset, ObjectiveFamily family,
            float width, float height, Color color)
        {
            if (v == null) return;
            v.Buffer.Clear();
            float s = Mathf.Min(width / 160f, height / 76f);
            switch (asset)
            {
                case ObjectiveAsset.GroundVehicle:
                    if (family == ObjectiveFamily.Logistics) DrawTruck(v, width, height, color);
                    else DrawGroundVehicle(v, s, color);
                    break;
                case ObjectiveAsset.Aircraft:
                    DrawAircraft(v, width * .5f, height * .5f, Mathf.Min(width * .54f, height * .78f), 0f, color);
                    StrokePath(v, color.WithAlpha(.25f), .7f, s, false, 20, 8, 140, 8);
                    break;
                case ObjectiveAsset.Base:
                    Polygon(v, color.WithAlpha(.08f), s, 18, 14, 142, 14, 142, 63, 18, 63);
                    StrokePath(v, color.WithAlpha(.6f), 1f, s, true, 18, 14, 142, 14, 142, 63, 18, 63);
                    Polygon(v, color.WithAlpha(.24f), s, 68, 18, 88, 18, 88, 60, 68, 60);
                    AvStrokes.DashedLine(v.Buffer, 78*s, 19*s, 78*s, 59*s, 5*s, 4*s, .9f*s, RgbaOf(Ink));
                    StrokePath(v, color, 1.1f, s, true, 26, 21, 54, 21, 54, 36, 26, 36);
                    StrokePath(v, color, 1f, s, true, 102, 42, 130, 42, 130, 56, 102, 56);
                    StrokePath(v, color.WithAlpha(.55f), .9f, s, false, 54, 28, 68, 28);
                    break;
                case ObjectiveAsset.Personnel:
                    Disc(v, 80*s, 55*s, 8*s, color.WithAlpha(.16f));
                    AvStrokes.Ring(v.Buffer, 80*s, 55*s, 8*s, 32, 1.1f*s, RgbaOf(color));
                    Polygon(v, color.WithAlpha(.16f), s, 64, 16, 64, 38, 72, 43, 88, 43, 96, 38, 96, 16);
                    StrokePath(v, color, 1.1f, s, false, 64, 16, 64, 38, 72, 43, 88, 43, 96, 38, 96, 16);
                    StrokePath(v, color.WithAlpha(.55f), .8f, s, false, 72, 16, 72, 35, 88, 35, 88, 16);
                    break;
                case ObjectiveAsset.Site:
                    Polygon(v, color.WithAlpha(.12f), s, 28, 16, 28, 39, 47, 53, 79, 53, 98, 39, 98, 16);
                    StrokePath(v, color, 1.1f, s, true, 28, 16, 28, 39, 47, 53, 79, 53, 98, 39, 98, 16);
                    StrokePath(v, color.WithAlpha(.6f), .8f, s, false, 35, 17, 35, 36, 62, 36, 62, 17);
                    StrokePath(v, color, .9f, s, false, 120, 16, 120, 54, 108, 63, 132, 63, 120, 54);
                    AvStrokes.Arc(v.Buffer, 120*s, 55*s, 17*s, 20f, 160f, 24, .8f*s, RgbaOf(color.WithAlpha(.45f)));
                    break;
                default:
                    AvStrokes.Bracket(v.Buffer, 52*s, 13*s, 56*s, 50*s, 12*s, 1.1f*s, RgbaOf(color));
                    AvStrokes.Cross(v.Buffer, 80*s, 38*s, 8*s, .8f*s, RgbaOf(color.WithAlpha(.55f)));
                    break;
            }
            v.Commit();
        }

        private static void DrawGroundVehicle(AvVector v, float s, Color color)
        {
            // A generic fighting vehicle class; no model-specific weapon or faction claim.
            Polygon(v, Color.Lerp(Ground, color, .15f), s, 17, 24, 27, 42, 89, 42, 112, 33, 140, 29, 144, 20, 17, 20);
            StrokePath(v, color, 1.1f, s, true, 17, 24, 27, 42, 89, 42, 112, 33, 140, 29, 144, 20, 17, 20);
            Polygon(v, Color.Lerp(Ground, color, .25f), s, 44, 42, 48, 51, 76, 54, 89, 45, 89, 42);
            StrokePath(v, color, .9f, s, true, 44, 42, 48, 51, 76, 54, 89, 45, 89, 42);
            StrokePath(v, color.WithAlpha(.55f), .7f, s, false, 35, 38, 78, 38, 103, 29, 132, 26);
            foreach (float x in new[] { 34f, 60f, 108f, 132f })
            {
                Disc(v, x*s, 18*s, 8*s, Ground);
                AvStrokes.Ring(v.Buffer, x*s, 18*s, 7*s, 28, .9f*s, RgbaOf(color));
                Disc(v, x*s, 18*s, 2.4f*s, Color.Lerp(Ground, color, .4f));
            }
        }

        internal static void DrawContact(AvVector v, float x, float y, float size,
            ObjectiveAsset asset, ObjectiveFamily family, bool selected, bool lastKnown,
            bool hostile = false, bool neutral = false)
        {
            float s = size / 28f;
            Color side = lastKnown ? Dim : hostile ? Amber : neutral ? Ink : Key;
            PolygonAt(v, Ground.WithAlpha(.97f), s, x, y, -13, -7, 13, -7, 13, 10, -13, 10);
            if (hostile) AvStrokes.Diamond(v.Buffer, x, y + 1.5f*s, 12*s, 1.25f*s, RgbaOf(side));
            else if (neutral) AvStrokes.Ring(v.Buffer, x, y+1.5f*s, 11.8f*s, 44, 1.1f*s, RgbaOf(side));
            else PathAt(v, side, 1.25f, s, x, y, true, -11, -5, 11, -5, 11, 8, -11, 8);
            if (lastKnown)
                AvStrokes.DashedLine(v.Buffer, x-8*s, y+1.5f*s, x+8*s, y+1.5f*s, 2*s, 2*s, 1*s, RgbaOf(Dim));
            else if (family == ObjectiveFamily.Logistics)
            {
                PolygonAt(v, Ink.WithAlpha(.14f), s, x, y, 0, 6.5f, 5, 4, 5, -1.5f, 0, -4, -5, -1.5f, -5, 4);
                PolygonAt(v, Ink.WithAlpha(.22f), s, x, y, -5, 4, 0, 6.5f, 5, 4, 0, 1.5f);
                PathAt(v, Ink, 1f, s, x, y, true, 0, 6.5f, 5, 4, 5, -1.5f, 0, -4, -5, -1.5f, -5, 4);
                PathAt(v, Ink, .9f, s, x, y, false, -5, 4, 0, 1.5f, 5, 4);
                PathAt(v, Ink, .9f, s, x, y, false, 0, 1.5f, 0, -4);
            }
            else if (asset == ObjectiveAsset.Aircraft) DrawAircraft(v, x, y+1.5f*s, 10*s, 0f, Ink);
            else if (asset == ObjectiveAsset.Personnel)
            {
                Disc(v, x, y+5*s, 1.8f*s, Ink);
                PathAt(v, Ink, .9f, s, x, y, false, -3, -2, -3, 1, 0, 2, 3, 1, 3, -2);
            }
            else if (asset == ObjectiveAsset.Base)
                PathAt(v, Ink, 1f, s, x, y, false, -6, -2, 0, 5, 6, -2, -6, -2);
            else AvStrokes.Cross(v.Buffer, x, y+1.5f*s, 4*s, 1*s, RgbaOf(Ink));
            if (asset == ObjectiveAsset.GroundVehicle)
                foreach (float tick in new[] { -5f, 0f, 5f })
                    AvStrokes.Line(v.Buffer, x+tick*s, y-9*s, x+(tick+1)*s, y-9*s, 1.6f*s, RgbaOf(side));
            if (selected) AvStrokes.Bracket(v.Buffer, x-14*s, y-11*s, 28*s, 23*s, 4*s, 1.2f*s, RgbaOf(Amber));
        }

        internal static void DrawAircraft(AvVector v, float x, float y, float size, float angleDegrees, Color color)
        {
            float a = angleDegrees * Mathf.Deg2Rad, dx = Mathf.Cos(a), dy = Mathf.Sin(a), s = size / 20f;
            float[] shape = { 11, 0, -1, 2, -5, 10, -7, 10, -5, 1, -10, 3, -10, -3, -5, -1, -7, -10, -5, -10, -1, -2 };
            Rgba c = RgbaOf(color);
            for (int i = 0; i < shape.Length / 2; i++)
            {
                int j = (i+1) % (shape.Length/2);
                float ax = shape[i*2]*s, ay = shape[i*2+1]*s, bx = shape[j*2]*s, by = shape[j*2+1]*s;
                v.Buffer.Add(x, y, x+ax*dx-ay*dy, y+ax*dy+ay*dx, x+bx*dx-by*dy, y+bx*dy+by*dx, x, y, c, c, c, c);
            }
        }

        private static void PolygonAt(AvVector v, Color color, float s, float x, float y, params float[] xy)
        {
            Rgba c = RgbaOf(color);
            for (int i = 1; i < xy.Length/2-1; i++)
                v.Buffer.Add(x+xy[0]*s, y+xy[1]*s, x+xy[i*2]*s, y+xy[i*2+1]*s,
                    x+xy[i*2+2]*s, y+xy[i*2+3]*s, x+xy[0]*s, y+xy[1]*s, c, c, c, c);
        }

        private static void PathAt(AvVector v, Color color, float width, float s, float x, float y, bool closed, params float[] xy)
        {
            int n = xy.Length/2;
            for (int i = 0; i < (closed ? n : n-1); i++)
            {
                int j = (i+1)%n;
                AvStrokes.Line(v.Buffer, x+xy[i*2]*s, y+xy[i*2+1]*s, x+xy[j*2]*s, y+xy[j*2+1]*s,
                    width*s, RgbaOf(color), .18f*s);
            }
        }

        internal static void DrawTruck(AvVector v, float width, float height, Color color)
        {
            if (v == null || width <= 0f || height <= 0f) return;
            float s = Mathf.Min(width / 160f, height / 76f);
            var edge=Color.Lerp(Dim,color,.55f); var light=Color.Lerp(Ink,color,.22f);
            var body=Color.Lerp(Ground,color,.17f); var shade=Color.Lerp(Ground,color,.08f);
            // The art is a generic transport silhouette, not an invented game model.
            // A tandem rear bogie, chassis and cab-over profile carry the identification.
            StrokePath(v,color.WithAlpha(.16f),.65f,s,false,7,7,153,7);
            foreach(float xx in new[]{7f,39f,71f,103f,135f,153f}) StrokePath(v,color.WithAlpha(.22f),.65f,s,false,xx,7,xx,10);
            Polygon(v,shade,s,12,22,12,28,146,28,146,22);
            StrokePath(v,edge,.9f,s,false,12,22,146,22);
            // Canvas-covered cargo bed: broad tone, shoulder light and restrained seams.
            Polygon(v,body,s,12,32,12,58,17,63,97,63,103,58,103,32);
            Polygon(v,Color.Lerp(Ground,color,.25f),s,12,58,17,63,97,63,103,58);
            Polygon(v,shade,s,12,32,103,32,103,39,12,39);
            StrokePath(v,edge,1.05f,s,true,12,32,12,58,17,63,97,63,103,58,103,32);
            StrokePath(v,light.WithAlpha(.6f),.7f,s,false,17,62,97,62);
            StrokePath(v,edge.WithAlpha(.55f),.65f,s,false,12,39,103,39);
            foreach(float xx in new[]{31f,55f,79f})
            {
                StrokePath(v,edge.WithAlpha(.48f),.65f,s,false,xx,59,xx-1,44,xx,39);
                Polygon(v,edge.WithAlpha(.7f),s,xx-1,31,xx+1,31,xx+1,34,xx-1,34);
            }
            // Cab shell, angled armor and dark inset glazing.
            Polygon(v,body,s,108,29,108,55,112,59,129,59,140,49,143,34,149,31,149,24,108,24);
            Polygon(v,Color.Lerp(Ground,color,.29f),s,108,55,112,59,129,59,140,49,134,48,124,55);
            Polygon(v,shade,s,140,49,143,34,149,31,149,24,136,24,136,47);
            StrokePath(v,edge,1.1f,s,true,108,29,108,55,112,59,129,59,140,49,143,34,149,31,149,24,108,24);
            Polygon(v,Ground,s,113,44,113,53,125,53,133,46,133,43);
            StrokePath(v,light.WithAlpha(.68f),.7f,s,true,113,44,113,53,125,53,133,46,133,43);
            StrokePath(v,edge,.7f,s,false,124,53,124,44);
            StrokePath(v,edge.WithAlpha(.7f),.65f,s,false,111,41,133,40,133,31,111,31,111,41);
            StrokePath(v,light,.8f,s,false,124,37,128,37);
            StrokePath(v,edge,.7f,s,false,136,48,140,47,140,43);
            StrokePath(v,light.WithAlpha(.65f),.8f,s,false,139,36,143,36);
            foreach(float yy in new[]{29f,32f}) StrokePath(v,edge.WithAlpha(.65f),.65f,s,false,143,yy,148,yy);
            Polygon(v,edge,s,139,21,152,21,152,24,139,24);
            // Bed rail, fuel tank, cab steps and mudguards break up the lower silhouette.
            Polygon(v,Color.Lerp(Ground,color,.22f),s,11,28,104,28,104,32,11,32);
            StrokePath(v,edge,.8f,s,false,11,31,104,31);
            Polygon(v,body,s,76,16,97,16,99,19,99,25,74,25,74,19);
            StrokePath(v,edge.WithAlpha(.75f),.7f,s,true,76,16,97,16,99,19,99,25,74,25,74,19);
            StrokePath(v,edge,.7f,s,false,112,20,120,20,120,17,112,17);
            StrokePath(v,edge,1.1f,s,false,19,23,21,29,62,29,67,23);
            StrokePath(v,edge,1.1f,s,false,118,22,120,28,138,28,143,22);
            foreach(float xx in new[]{31f,55f,130f})
            {
                Disc(v,xx*s,17*s,9*s,Ground);
                Disc(v,xx*s,17*s,7.8f*s,Color.Lerp(Ground,Dim,.2f));
                AvStrokes.Ring(v.Buffer,xx*s,17*s,7.8f*s,32,.85f*s,RgbaOf(edge));
                Disc(v,xx*s,17*s,3.1f*s,Color.Lerp(Ground,Dim,.55f));
                AvStrokes.Ring(v.Buffer,xx*s,17*s,3.1f*s,20,.7f*s,RgbaOf(light.WithAlpha(.8f)));
                Disc(v,xx*s,17*s,.95f*s,Ground);
            }
            v.Commit();
        }

        // These bounded primitives draw passive identification art in the shared palette.
        internal static void Polygon(AvVector v,Color color,float scale,params float[] xy)
        {
            var c=RgbaOf(color);
            for(int i=1;i<xy.Length/2-1;i++) v.Buffer.Add(xy[0]*scale,xy[1]*scale,xy[i*2]*scale,xy[i*2+1]*scale,xy[i*2+2]*scale,xy[i*2+3]*scale,xy[0]*scale,xy[1]*scale,c,c,c,c);
        }
        internal static void StrokePath(AvVector v,Color color,float width,float scale,bool closed,params float[] xy)
        {
            int n=xy.Length/2;
            for(int i=0;i<(closed?n:n-1);i++) { int j=(i+1)%n; AvStrokes.Line(v.Buffer,xy[i*2]*scale,xy[i*2+1]*scale,xy[j*2]*scale,xy[j*2+1]*scale,width*scale,RgbaOf(color),.18f*scale); }
        }
        internal static void Disc(AvVector v,float x,float y,float radius,Color color)
        {
            var c=RgbaOf(color);
            for(int i=0;i<32;i++) { float a=i*Mathf.PI/16,b=(i+1)*Mathf.PI/16; v.Buffer.Add(x,y,x+radius*Mathf.Cos(a),y+radius*Mathf.Sin(a),x+radius*Mathf.Cos(b),y+radius*Mathf.Sin(b),x,y,c,c,c,c); }
        }

    }
}
