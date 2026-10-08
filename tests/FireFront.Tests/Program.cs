using System;
using System.Collections.Generic;
using BoscaliSummer.Fire;

static class Program
{
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static float Distance(FireFrontCell.Point a, FireFrontCell.Point b)
        => MathF.Sqrt((a.X-b.X)*(a.X-b.X)+(a.Z-b.Z)*(a.Z-b.Z));
    static FireFrontCell.Point World(FireFrontCell c, int i) => new(c.Center.X+c.Vertices[i].X, c.Center.Z+c.Vertices[i].Z);
    static void Main()
    {
        for (int x = -16; x <= 16; x++)
        for (int z = -16; z <= 16; z++)
        {
            var cell = FireFrontCell.FromKey(FireFrontCell.KeyOf(x,z));
            Check(cell.Count >= 3 && cell.Count <= FireFrontCell.MaximumVertices, "Unbounded/empty polygon");
            Check(FireFrontCell.Locate(cell.Center.X,cell.Center.Z)==cell.Key, "Seed identity mismatch");
            float area = 0f;
            for (int edge = 0; edge < cell.Count; edge++)
            {
                var a=cell.Vertices[edge]; var b=cell.Vertices[(edge+1)%cell.Count];
                Check(a.X*b.Z-a.Z*b.X>=-0.001f,"Seed outside convex cell");
                area += a.X*b.Z-a.Z*b.X;
                var other=FireFrontCell.FromKey(cell.Neighbours[edge]); bool shared=false;
                for (int oe=0; oe<other.Count; oe++)
                    if (other.Neighbours[oe]==cell.Key &&
                        Distance(World(cell,edge),World(other,(oe+1)%other.Count))<0.003f &&
                        Distance(World(cell,(edge+1)%cell.Count),World(other,oe))<0.003f) shared=true;
                Check(shared,"Adjacent cells do not share identical reversed endpoints");
                Check(Distance(cell.Center,other.Center)<FireFrontCell.Spacing*2f,"Spread makes a long jump");
                float middleX=cell.Center.X+(a.X+b.X)*0.5f;
                float middleZ=cell.Center.Z+(a.Z+b.Z)*0.5f;
                long inside=FireFrontCell.Locate(middleX+(cell.Center.X-middleX)*0.01f,middleZ+(cell.Center.Z-middleZ)*0.01f);
                long outside=FireFrontCell.Locate(middleX+(other.Center.X-middleX)*0.01f,middleZ+(other.Center.Z-middleZ)*0.01f);
                Check(inside==cell.Key && outside==other.Key,"Cell boundary has a gap or overlap");
            }
            Check(area>300f && area<2600f,"Degenerate footprint area");
        }
        var origin=FireFrontCell.FromKey(FireFrontCell.KeyOf(0,0));
        int downwind=0; float best=float.MinValue;
        for(int edge=0;edge<origin.Count;edge++)
        {
            float score=origin.EdgeScore(edge,12f,0f,0);
            if(score>best) { best=score; downwind=edge; }
        }
        var destination=FireFrontCell.Seed((int)(origin.Neighbours[downwind]>>32),(int)origin.Neighbours[downwind]);
        Check(destination.X>origin.Center.X,"Strong wind did not favour downwind spread");
        // Query radius spans more than one index bucket at the minimum index size.
        var index=new ForestIndex(); index.BuildFromPoints(new[]{new Vector2(17.5f,0)},8f);
        Check(index.Contains(0f,0f),"Small spatial buckets miss fuel within 18 metres");
        Check(!index.ContainsWithin(0f,0f,8f),"Narrow firebreak test bridges a cleared strip");
        index.BuildFromPoints(new[]{new Vector2(6f,0)},32f);
        Check(index.ContainsWithin(0f,0f,8f),"Edge fuel sample rejects nearby trees");
        long bytes=GC.GetAllocatedBytesForCurrentThread(); float sum=0f;
        for(int n=0;n<10000;n++) for(int edge=0;edge<origin.Count;edge++) sum+=origin.EdgeScore(edge,6f,2f,n);
        Check(GC.GetAllocatedBytesForCurrentThread()==bytes,"Spread ranking allocates in the hot path");
        Check(float.IsFinite(sum),"Nonfinite spread ranking");
        Console.WriteLine($"PASS: {checks} assertions; shared Voronoi edges, seed identity, no gaps/overlap, bounded geometry, wind ranking, fuel/firebreak queries and allocation-free ranking.");
    }
}
