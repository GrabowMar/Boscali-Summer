using BoscaliSummer.Core.Contracts;
using NOAvionics;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Presentation
{
    /// <summary>Passive contact and aircraft glyphs for the MIS task plot.</summary>
    internal static class MissionTaskGraphics
    {
        internal static Color Ink => AvInk.Ink;
        internal static Color Dim => AvInk.Dim;
        internal static Color Key => AvInk.Key;
        internal static Color Amber => AvInk.State(AvState.Caution);
        internal static Color Ground => AvInk.Ground;

        internal static void DrawContact(AvVector v, float x, float y, float size,
            ObjectiveAsset asset, ObjectiveFamily family, bool selected, bool lastKnown,
            bool hostile = false, bool neutral = false)
        {
            float s = size / 28f;
            Color side = lastKnown ? Dim : hostile ? Amber : neutral ? Ink : Key;
            PolygonAt(v, Ground.WithAlpha(.97f), s, x, y, -13, -7, 13, -7, 13, 10, -13, 10);
            if (hostile) AvStrokes.Diamond(v.Buffer, x, y + 1.5f*s, 12*s, 1.25f*s, side.ToRgba());
            else if (neutral) AvStrokes.Ring(v.Buffer, x, y+1.5f*s, 11.8f*s, 44, 1.1f*s, side.ToRgba());
            else PathAt(v, side, 1.25f, s, x, y, true, -11, -5, 11, -5, 11, 8, -11, 8);
            if (lastKnown)
                AvStrokes.DashedLine(v.Buffer, x-8*s, y+1.5f*s, x+8*s, y+1.5f*s, 2*s, 2*s, 1*s, Dim.ToRgba());
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
            else AvStrokes.Cross(v.Buffer, x, y+1.5f*s, 4*s, 1*s, Ink.ToRgba());
            if (asset == ObjectiveAsset.GroundVehicle)
                foreach (float tick in new[] { -5f, 0f, 5f })
                    AvStrokes.Line(v.Buffer, x+tick*s, y-9*s, x+(tick+1)*s, y-9*s, 1.6f*s, side.ToRgba());
            if (selected) AvStrokes.Bracket(v.Buffer, x-14*s, y-11*s, 28*s, 23*s, 4*s, 1.2f*s, Amber.ToRgba());
        }

        internal static void DrawAircraft(AvVector v, float x, float y, float size, float angleDegrees, Color color)
        {
            float a = angleDegrees * Mathf.Deg2Rad, dx = Mathf.Cos(a), dy = Mathf.Sin(a), s = size / 20f;
            float[] shape = { 11, 0, -1, 2, -5, 10, -7, 10, -5, 1, -10, 3, -10, -3, -5, -1, -7, -10, -5, -10, -1, -2 };
            Rgba c = color.ToRgba();
            for (int i = 0; i < shape.Length / 2; i++)
            {
                int j = (i+1) % (shape.Length/2);
                float ax = shape[i*2]*s, ay = shape[i*2+1]*s, bx = shape[j*2]*s, by = shape[j*2+1]*s;
                v.Buffer.Add(x, y, x+ax*dx-ay*dy, y+ax*dy+ay*dx, x+bx*dx-by*dy, y+bx*dy+by*dx, x, y, c, c, c, c);
            }
        }

        private static void PolygonAt(AvVector v, Color color, float s, float x, float y, params float[] xy)
        {
            Rgba c = color.ToRgba();
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
                    width*s, color.ToRgba(), .18f*s);
            }
        }

        // These bounded primitives draw passive identification art in the shared palette.
        internal static void Disc(AvVector v,float x,float y,float radius,Color color)
        {
            var c=color.ToRgba();
            for(int i=0;i<32;i++) { float a=i*Mathf.PI/16,b=(i+1)*Mathf.PI/16; v.Buffer.Add(x,y,x+radius*Mathf.Cos(a),y+radius*Mathf.Sin(a),x+radius*Mathf.Cos(b),y+radius*Mathf.Sin(b),x,y,c,c,c,c); }
        }

    }
}
