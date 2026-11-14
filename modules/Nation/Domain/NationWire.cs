using System;
using System.IO;
using System.Text;
using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.Nation.Domain
{
    /// <summary>
    /// The faction snapshot as bytes: what a client needs to draw the nation, nothing it could act on. Timers travel as seconds
    /// left (clocks differ between peers); the client ages them locally. Layout version <see cref="Version"/>; bump it with any change.
    /// </summary>
    internal static class NationWire
    {
        public const byte Version = 1;
        public const int MaxBytes = 2048;
        private const int MaxText = 32;

        public static byte[] Pack(NationView v)
        {
            using (var stream = new MemoryStream(512))
            using (var w = new BinaryWriter(stream, Encoding.UTF8))
            {
                w.Write(Version);
                w.Write((byte)v.Slots);
                w.Write(v.Output); w.Write(v.Stability); w.Write(v.Funds); w.Write(v.FactoryCost);
                w.Write((byte)v.Economy); w.Write((byte)v.Priority);
                w.Write(v.EconomyLock); w.Write(v.PriorityLock);
                w.Write(v.Done);
                w.Write((sbyte)v.Running); w.Write((sbyte)v.Queued);
                w.Write(v.RunningLeft); w.Write(v.RunningTotal);
                Text(w, v.PickHolder); w.Write(v.PickLock);
                w.Write(v.Building); w.Write(v.BuildLeft); w.Write(v.BuildTotal);
                w.Write((byte)v.Escalation);
                int plants = Math.Min(v.Plants.Count, NationRules.MaxPlants);
                w.Write((byte)plants);
                for (int i = 0; i < plants; i++)
                {
                    PlantView p = v.Plants[i];
                    w.Write(p.Id); w.Write((byte)p.Kind); w.Write((sbyte)p.Row); w.Write(p.Pinned);
                    w.Write(p.NextIn); w.Write(p.Interval); w.Write(p.Health01); w.Write(p.X); w.Write(p.Z);
                }
                int rows = Math.Min(v.Stock.Count, NationRules.MaxStockRows);
                w.Write((byte)rows);
                for (int i = 0; i < rows; i++)
                {
                    StockView s = v.Stock[i];
                    Text(w, s.Name); Text(w, s.Code); w.Write(s.Aircraft); w.Write((short)s.Count); w.Write((short)s.Target);
                }
                w.Flush();
                return stream.ToArray();
            }
        }

        /// <summary>False (and <paramref name="v"/> left cleared) on a wrong version, a short or oversized buffer or any out-of-range value.</summary>
        public static bool TryUnpack(byte[] data, int count, NationView v)
        {
            v.Plants.Clear();
            v.Stock.Clear();
            if (data == null || count < 2 || count > MaxBytes || count > data.Length) return false;
            try
            {
                using (var r = new BinaryReader(new MemoryStream(data, 0, count, false), Encoding.UTF8))
                {
                    if (r.ReadByte() != Version) return false;
                    v.Slots = r.ReadByte();
                    v.Output = r.ReadSingle(); v.Stability = r.ReadSingle(); v.Funds = r.ReadSingle(); v.FactoryCost = r.ReadSingle();
                    v.Economy = (EconomyPolicy)r.ReadByte(); v.Priority = (PriorityPolicy)r.ReadByte();
                    v.EconomyLock = r.ReadSingle(); v.PriorityLock = r.ReadSingle();
                    v.Done = r.ReadUInt16();
                    v.Running = r.ReadSByte(); v.Queued = r.ReadSByte();
                    v.RunningLeft = r.ReadSingle(); v.RunningTotal = r.ReadSingle();
                    v.PickHolder = Text(r); v.PickLock = r.ReadSingle();
                    v.Building = r.ReadBoolean(); v.BuildLeft = r.ReadSingle(); v.BuildTotal = r.ReadSingle();
                    v.Escalation = r.ReadByte();
                    int plants = r.ReadByte();
                    if (plants > NationRules.MaxPlants) return Fail(v);
                    for (int i = 0; i < plants; i++)
                        v.Plants.Add(new PlantView
                        {
                            Id = r.ReadInt32(), Kind = (LineKind)r.ReadByte(), Row = r.ReadSByte(), Pinned = r.ReadBoolean(),
                            NextIn = r.ReadSingle(), Interval = r.ReadSingle(), Health01 = r.ReadSingle(), X = r.ReadSingle(), Z = r.ReadSingle(),
                        });
                    int rows = r.ReadByte();
                    if (rows > NationRules.MaxStockRows) return Fail(v);
                    for (int i = 0; i < rows; i++)
                        v.Stock.Add(new StockView { Name = Text(r), Code = Text(r), Aircraft = r.ReadBoolean(), Count = r.ReadInt16(), Target = r.ReadInt16() });
                    bool sane = v.Economy <= EconomyPolicy.War && v.Priority <= PriorityPolicy.Ground && v.Slots <= NationRules.MaxPlants &&
                        v.Running < FocusTree.Count && v.Queued < FocusTree.Count && Finite(v.Output) && Finite(v.Stability) && Finite(v.Funds) &&
                        Finite(v.RunningLeft) && Finite(v.BuildLeft);
                    return sane || Fail(v);
                }
            }
            catch (Exception e) when (e is EndOfStreamException || e is IOException || e is ArgumentException)
            {
                return Fail(v);
            }
        }

        private static bool Fail(NationView v) { v.Plants.Clear(); v.Stock.Clear(); return false; }

        private static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);

        private static void Text(BinaryWriter w, string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text ?? "");
            int n = Math.Min(bytes.Length, MaxText);
            w.Write((byte)n);
            w.Write(bytes, 0, n);
        }

        private static string Text(BinaryReader r)
        {
            int n = r.ReadByte();
            if (n > MaxText) throw new ArgumentException("text too long");
            byte[] bytes = r.ReadBytes(n);
            if (bytes.Length != n) throw new EndOfStreamException();
            return Encoding.UTF8.GetString(bytes);
        }
    }
}
