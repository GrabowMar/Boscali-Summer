using System;
using System.IO;
using System.Text;
using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.Nation.Domain
{
    /// <summary>
    /// The faction snapshot as bytes: what a client needs to draw the nation and its politics, nothing it could act on. Timers travel as
    /// seconds left (clocks differ between peers); the client ages them locally. Layout version <see cref="Version"/>; bump it with any change.
    /// </summary>
    internal static class NationWire
    {
        public const byte Version = 2;
        public const int MaxBytes = 4096;
        private const int MaxText = 48;

        public static byte[] Pack(NationView v)
        {
            using (var stream = new MemoryStream(1024))
            using (var w = new BinaryWriter(stream, Encoding.UTF8))
            {
                w.Write(Version);
                w.Write((byte)v.Slots);
                w.Write(v.Output); w.Write(v.Stability); w.Write(v.Funds); w.Write(v.FactoryCost);
                w.Write((byte)v.Economy); w.Write((byte)v.Priority);
                w.Write(v.EconomyLock); w.Write(v.PriorityLock);
                w.Write(v.Building); w.Write(v.BuildLeft); w.Write(v.BuildTotal);
                w.Write((byte)v.Escalation);
                int plants = Math.Min(v.Plants.Count, NationRules.MaxPlants);
                w.Write((byte)plants);
                for (int i = 0; i < plants; i++)
                {
                    PlantView p = v.Plants[i];
                    w.Write(p.Id); w.Write((byte)p.Kind); w.Write((sbyte)p.Row); w.Write(p.Pinned); w.Write(p.Raided);
                    w.Write(p.NextIn); w.Write(p.Interval); w.Write(p.Health01); w.Write(p.X); w.Write(p.Z);
                }
                int rows = Math.Min(v.Stock.Count, NationRules.MaxStockRows);
                w.Write((byte)rows);
                for (int i = 0; i < rows; i++)
                {
                    StockView s = v.Stock[i];
                    Text(w, s.Name); Text(w, s.Code); w.Write(s.Aircraft); w.Write((short)s.Count); w.Write((short)s.Target);
                }

                // Politics.
                w.Write(v.WarSupport);
                int moves = Math.Min(v.Moves.Count, PoliticsRules.MaxMoves);
                w.Write((byte)moves);
                for (int i = 0; i < moves; i++) { w.Write(v.Moves[i].Delta); Text(w, v.Moves[i].Text); w.Write(v.Moves[i].Age); }
                w.Write((byte)v.Doctrine); w.Write((byte)v.Rank); w.Write(v.DoctrineAge); Text(w, v.DoctrineBy);
                w.Write(v.PostureFloor); w.Write(v.PostureCeiling); w.Write((sbyte)v.JetsDelta);
                for (int i = 0; i < Decisions.Count; i++) { w.Write(Seconds(v.Cooldown[i])); w.Write(Seconds(v.InForce[i])); }
                w.Write((sbyte)v.Decided); w.Write(v.DecidedSerial); Text(w, v.DecidedBy); w.Write(v.DecidedAge);
                Text(w, v.EnemyName); Text(w, v.EnemyIntel); w.Write((byte)v.EnemyDoctrine); w.Write((byte)v.EnemyRank); w.Write(v.EnemyWarSupport);
                w.Write((sbyte)v.EnemyDecided); w.Write(v.EnemyDecidedSerial); w.Write(v.EnemyDecidedAge); w.Write(v.EnemyDecidedLeft);
                w.Write(v.CabinetSerial); w.Write((sbyte)v.CabinetId); w.Write((byte)v.CabinetStaff); w.Write((sbyte)v.CabinetChosen);
                Text(w, v.CabinetPlace); Text(w, v.CabinetChosenBy); w.Write(v.CabinetLeft);
                w.Flush();
                return stream.ToArray();
            }
        }

        /// <summary>False (and <paramref name="v"/>'s lists left cleared) on a wrong version, a short or oversized buffer or any out-of-range value.</summary>
        public static bool TryUnpack(byte[] data, int count, NationView v)
        {
            v.Plants.Clear();
            v.Stock.Clear();
            v.Moves.Clear();
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
                    v.Building = r.ReadBoolean(); v.BuildLeft = r.ReadSingle(); v.BuildTotal = r.ReadSingle();
                    v.Escalation = r.ReadByte();
                    int plants = r.ReadByte();
                    if (plants > NationRules.MaxPlants) return Fail(v);
                    for (int i = 0; i < plants; i++)
                        v.Plants.Add(new PlantView
                        {
                            Id = r.ReadInt32(), Kind = (LineKind)r.ReadByte(), Row = r.ReadSByte(), Pinned = r.ReadBoolean(), Raided = r.ReadBoolean(),
                            NextIn = r.ReadSingle(), Interval = r.ReadSingle(), Health01 = r.ReadSingle(), X = r.ReadSingle(), Z = r.ReadSingle(),
                        });
                    int rows = r.ReadByte();
                    if (rows > NationRules.MaxStockRows) return Fail(v);
                    for (int i = 0; i < rows; i++)
                        v.Stock.Add(new StockView { Name = Text(r), Code = Text(r), Aircraft = r.ReadBoolean(), Count = r.ReadInt16(), Target = r.ReadInt16() });

                    v.WarSupport = r.ReadSingle();
                    int moves = r.ReadByte();
                    if (moves > PoliticsRules.MaxMoves) return Fail(v);
                    for (int i = 0; i < moves; i++) v.Moves.Add(new SupportMove { Delta = r.ReadSingle(), Text = Text(r), Age = r.ReadSingle() });
                    v.Doctrine = (Doctrine)r.ReadByte(); v.Rank = r.ReadByte(); v.DoctrineAge = r.ReadSingle(); v.DoctrineBy = Text(r);
                    v.PostureFloor = r.ReadByte(); v.PostureCeiling = r.ReadByte(); v.JetsDelta = r.ReadSByte();
                    for (int i = 0; i < Decisions.Count; i++) { v.Cooldown[i] = r.ReadUInt16(); v.InForce[i] = r.ReadUInt16(); }
                    v.Decided = r.ReadSByte(); v.DecidedSerial = r.ReadInt32(); v.DecidedBy = Text(r); v.DecidedAge = r.ReadSingle();
                    v.EnemyName = Text(r); v.EnemyIntel = Text(r); v.EnemyDoctrine = (Doctrine)r.ReadByte(); v.EnemyRank = r.ReadByte(); v.EnemyWarSupport = r.ReadSingle();
                    v.EnemyDecided = r.ReadSByte(); v.EnemyDecidedSerial = r.ReadInt32(); v.EnemyDecidedAge = r.ReadSingle(); v.EnemyDecidedLeft = r.ReadSingle();
                    v.CabinetSerial = r.ReadInt32(); v.CabinetId = r.ReadSByte(); v.CabinetStaff = r.ReadByte(); v.CabinetChosen = r.ReadSByte();
                    v.CabinetPlace = Text(r); v.CabinetChosenBy = Text(r); v.CabinetLeft = r.ReadSingle();

                    bool sane = v.Economy <= EconomyPolicy.War && v.Priority <= PriorityPolicy.Ground && v.Slots <= NationRules.MaxPlants &&
                        v.Doctrine <= Doctrine.Shadow && v.EnemyDoctrine <= Doctrine.Shadow && v.Rank <= 3 && v.EnemyRank <= 3 && v.PostureCeiling <= 2 &&
                        v.PostureFloor <= v.PostureCeiling && v.Decided < Decisions.Count && v.EnemyDecided < Decisions.Count &&
                        v.CabinetId < CabinetCards.Count && v.CabinetChosen < 3 && Finite(v.Output) && Finite(v.Stability) && Finite(v.Funds) &&
                        Finite(v.BuildLeft) && Finite(v.WarSupport) && Finite(v.EnemyWarSupport) && Finite(v.CabinetLeft);
                    return sane || Fail(v);
                }
            }
            catch (Exception e) when (e is EndOfStreamException || e is IOException || e is ArgumentException)
            {
                return Fail(v);
            }
        }

        private static bool Fail(NationView v) { v.Plants.Clear(); v.Stock.Clear(); v.Moves.Clear(); return false; }

        private static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);

        /// <summary>A clock as whole seconds left, 0..65535.</summary>
        private static ushort Seconds(float s) => s <= 0f || float.IsNaN(s) ? (ushort)0 : s >= 65535f ? ushort.MaxValue : (ushort)Math.Ceiling(s);

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
