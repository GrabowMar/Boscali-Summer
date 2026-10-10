using System.Collections.Generic;
using BoscaliSummer.Modules.Progression.Runtime;

namespace BoscaliSummer.Tests.Features.Progression
{
    /// <summary>The crest board other players' insignia land in: bounded, never evicting, and fed only by wire bytes that
    /// passed the validator. Pure, so both run without the game.</summary>
    internal static class CrestBoardTests
    {
        private static readonly EmblemDesign Gold = new EmblemDesign(1, 2, 3, 1, 4, 5, 6, 7);
        private static readonly EmblemDesign Blue = EmblemDesign.Default;

        public static void Run()
        {
            SetReplaceRemove();
            OffWithdrawsAndNoOwnerIsRefused();
            CapIsRefusedNotEvicted();
            CopyToEnumeratesWithoutKeepingStale();
            EveryLayerIsBoundedByItsCatalogue();
            PaintMustBeDefined();
            ValidBytesBuildTheSameDesign();
        }

        private static void SetReplaceRemove()
        {
            var board = new CrestBoard();
            TestAssert.That(board.Count == 0 && !board.TryGet(7, out _), "a new board is not empty");
            TestAssert.That(board.Set(7, Gold, EmblemPaint.Colour) && board.Count == 1, "a crest was not stored");
            TestAssert.That(board.TryGet(7, out CrestBoard.Entry stored) && stored.Design == Gold && stored.Paint == EmblemPaint.Colour,
                "the stored crest is not the one set");
            TestAssert.That(board.Set(7, Blue, EmblemPaint.LowVis) && board.Count == 1, "a replaced crest took a second slot");
            board.TryGet(7, out stored);
            TestAssert.That(stored.Design == Blue && stored.Paint == EmblemPaint.LowVis, "a replaced crest kept its old look");
            TestAssert.That(!board.TryGet(8, out _), "a stranger has a crest");
            TestAssert.That(board.Remove(7) && !board.Remove(7) && board.Count == 0 && !board.TryGet(7, out _),
                "a removed crest is still there, or removing twice reported success");
            board.Set(1, Gold, EmblemPaint.Colour); board.Set(2, Blue, EmblemPaint.Colour);
            board.Clear();
            TestAssert.That(board.Count == 0 && !board.TryGet(1, out _) && !board.TryGet(2, out _), "Clear left crests behind");
        }

        private static void OffWithdrawsAndNoOwnerIsRefused()
        {
            var board = new CrestBoard();
            board.Set(5, Gold, EmblemPaint.Colour);
            TestAssert.That(board.Set(5, Blue, EmblemPaint.Off) && board.Count == 0 && !board.TryGet(5, out _),
                "paint Off did not withdraw the crest");
            TestAssert.That(board.Set(5, Blue, EmblemPaint.Off) && board.Count == 0, "withdrawing a crest that is not there changed the board");
            TestAssert.That(!board.Set(CrestBoard.NoOwner, Gold, EmblemPaint.Colour) && board.Count == 0,
                "the no-owner id was given a crest");
        }

        private static void CapIsRefusedNotEvicted()
        {
            var board = new CrestBoard();
            for (ulong owner = 1; owner <= CrestBoard.Capacity; owner++)
                TestAssert.That(board.Set(owner, new EmblemDesign((int)owner, 0, 0, 0, 0, 0, 0, 0), EmblemPaint.Colour),
                    "owner " + owner + " was refused below the cap");
            TestAssert.That(board.Count == CrestBoard.Capacity && CrestBoard.Capacity == 32, "the board is not capped at 32");

            TestAssert.That(!board.Set(CrestBoard.Capacity + 1, Gold, EmblemPaint.Colour), "a 33rd owner was accepted");
            TestAssert.That(board.Count == CrestBoard.Capacity && !board.TryGet(CrestBoard.Capacity + 1, out _), "the refused owner got in");
            for (ulong owner = 1; owner <= CrestBoard.Capacity; owner++)
                TestAssert.That(board.TryGet(owner, out CrestBoard.Entry kept) && kept.Design.Shape == (int)owner % EmblemDesign.ShapeCount,
                    "owner " + owner + " was evicted to make room");

            TestAssert.That(board.Set(1, Gold, EmblemPaint.LowVis) && board.Count == CrestBoard.Capacity, "a full board refused a replacement");
            board.TryGet(1, out CrestBoard.Entry replaced);
            TestAssert.That(replaced.Design == Gold && replaced.Paint == EmblemPaint.LowVis, "the replacement at the cap was lost");

            board.Remove(2);
            TestAssert.That(board.Set(CrestBoard.Capacity + 1, Gold, EmblemPaint.Colour) && board.Count == CrestBoard.Capacity,
                "a freed slot was not reusable");
            board.Set(3, Blue, EmblemPaint.Off);
            TestAssert.That(board.Count == CrestBoard.Capacity - 1, "a withdrawn crest did not free its slot");
        }

        private static void CopyToEnumeratesWithoutKeepingStale()
        {
            var board = new CrestBoard();
            board.Set(10, Gold, EmblemPaint.Colour); board.Set(11, Blue, EmblemPaint.LowVis); board.Set(12, Gold, EmblemPaint.LowVis);
            var into = new List<KeyValuePair<ulong, CrestBoard.Entry>>(CrestBoard.Capacity)
            {
                new KeyValuePair<ulong, CrestBoard.Entry>(99, new CrestBoard.Entry(Blue, EmblemPaint.Colour))
            };
            board.CopyTo(into);
            TestAssert.That(into.Count == 3, "CopyTo did not replace the list contents: " + into.Count);
            var seen = new HashSet<ulong>();
            foreach (KeyValuePair<ulong, CrestBoard.Entry> pair in into)
            {
                seen.Add(pair.Key);
                board.TryGet(pair.Key, out CrestBoard.Entry original);
                TestAssert.That(pair.Value.Design == original.Design && pair.Value.Paint == original.Paint, "a copied entry differs for " + pair.Key);
            }
            TestAssert.That(seen.SetEquals(new ulong[] { 10, 11, 12 }), "CopyTo missed or invented an owner");
            board.Remove(11);
            board.CopyTo(into);
            TestAssert.That(into.Count == 2, "a removed owner is still copied");
            board.Clear();
            board.CopyTo(into);
            TestAssert.That(into.Count == 0, "an empty board copied entries");
        }

        // ---- The wire validator ----------------------------------------------------------------

        private static bool Decode(byte[] layers, byte paint, out EmblemDesign design, out EmblemPaint mode) =>
            CrestBoard.TryDecode(layers[0], layers[1], layers[2], layers[3], layers[4], layers[5], layers[6], layers[7],
                paint, out design, out mode);

        private static void EveryLayerIsBoundedByItsCatalogue()
        {
            for (int layer = 0; layer < EmblemDesign.LayerCount; layer++)
            {
                int count = EmblemDesign.Count((EmblemLayer)layer);
                var bytes = new byte[EmblemDesign.LayerCount];
                bytes[layer] = (byte)(count - 1);
                TestAssert.That(Decode(bytes, 1, out EmblemDesign top, out _) && top.Value((EmblemLayer)layer) == count - 1,
                    (EmblemLayer)layer + " refused its last catalogue entry");
                foreach (int bad in new[] { count, count + 1, 128, 255 })
                {
                    bytes[layer] = (byte)bad;
                    TestAssert.That(!Decode(bytes, 1, out EmblemDesign refused, out EmblemPaint mode) && refused.Equals(default(EmblemDesign)) && mode == EmblemPaint.Off,
                        (EmblemLayer)layer + " accepted " + bad + " (catalogue " + count + ")");
                }
            }
        }

        private static void PaintMustBeDefined()
        {
            var zero = new byte[EmblemDesign.LayerCount];
            foreach (EmblemPaint defined in new[] { EmblemPaint.Off, EmblemPaint.Colour, EmblemPaint.LowVis })
                TestAssert.That(Decode(zero, (byte)defined, out _, out EmblemPaint mode) && mode == defined, "paint " + defined + " was refused");
            foreach (byte bad in new byte[] { 3, 4, 127, 255 })
                TestAssert.That(!Decode(zero, bad, out _, out _), "paint byte " + bad + " was accepted");
            // One bad byte spoils the whole message even when every layer is fine.
            TestAssert.That(!CrestBoard.TryDecode(0, 0, 0, 0, 0, 0, 0, 0, 9, out EmblemDesign none, out EmblemPaint off) &&
                none.Equals(default(EmblemDesign)) && off == EmblemPaint.Off, "a bad paint left a half-read crest");
        }

        private static void ValidBytesBuildTheSameDesign()
        {
            // A byte the constructor would wrap must be refused, never wrapped into somebody else's crest.
            TestAssert.That(!CrestBoard.TryDecode((byte)EmblemDesign.ShapeCount, 0, 0, 0, 0, 0, 0, 0, 1, out _, out _),
                "a shape byte one past the catalogue was wrapped instead of refused");
            TestAssert.That(CrestBoard.TryDecode((byte)Gold.Shape, (byte)Gold.Division, (byte)Gold.Charge, (byte)Gold.Accent, (byte)Gold.Field,
                    (byte)Gold.Second, (byte)Gold.Ink, (byte)Gold.Number, 2, out EmblemDesign built, out EmblemPaint mode),
                "a design read back off its own bytes was refused");
            TestAssert.That(built == Gold && mode == EmblemPaint.LowVis, "the decoded crest differs from the one sent");
            var maxed = new byte[EmblemDesign.LayerCount];
            for (int layer = 0; layer < maxed.Length; layer++) maxed[layer] = (byte)(EmblemDesign.Count((EmblemLayer)layer) - 1);
            TestAssert.That(Decode(maxed, 1, out EmblemDesign last, out _), "the last entry of every layer together was refused");
            for (int layer = 0; layer < maxed.Length; layer++)
                TestAssert.That(last.Value((EmblemLayer)layer) == maxed[layer], (EmblemLayer)layer + " changed in decoding");
        }
    }
}
