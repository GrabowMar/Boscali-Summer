using BoscaliSummer.Features.DynamicOperations.Domain;
using BoscaliSummer.Framework.Contracts;

namespace BoscaliSummer.Tests.Features.DynamicOperations
{
    /// <summary>
    /// The HUD's pure half: card reading, panel selection and the projection geometry the
    /// cockpit markers are placed with. Locale-proof.
    /// </summary>
    internal static class ContractMarkerTests
    {
        public static void Run()
        {
            VicinityBandIsGenerousAndSafe();
            ApproachAndBarStayInRange();
            CardsReadOnlyWhatTheHostPublished();
            SelectionOrdersInsideNearestThenLost();
            BoxGeometryKeepsMarkersOnScreen();
            ProjectionScalesLikeTheVanillaRing();
        }

        private static void VicinityBandIsGenerousAndSafe()
        {
            TestAssert.That(OperationMarkerCopy.VicinityBand(1500f) == 1500f + 20000f,
                "a small area still gets the constant floor");
            TestAssert.That(OperationMarkerCopy.VicinityBand(0f) == OperationMarkerCopy.VicinityFloorMetres,
                "a point contract gets the floor");
            TestAssert.That(OperationMarkerCopy.VicinityBand(10000f) == 10000f + 40000f,
                "a huge area scales with its radius");
            TestAssert.That(OperationMarkerCopy.VicinityBand(float.NaN) == OperationMarkerCopy.VicinityFloorMetres,
                "an unknown radius cannot poison the band");
        }

        private static void ApproachAndBarStayInRange()
        {
            const float radius = 1500f;
            float band = OperationMarkerCopy.VicinityBand(radius);
            TestAssert.That(OperationMarkerCopy.Approach(radius, radius) == 1f, "the area edge is full");
            TestAssert.That(OperationMarkerCopy.Approach(band, radius) == 0f, "the band edge is empty");
            TestAssert.That(OperationMarkerCopy.Approach(radius + (band - radius) * 0.5f, radius) == 0.5f,
                "the band is linear");
            TestAssert.That(OperationMarkerCopy.Approach(float.NaN, radius) == 0f, "an unknown distance is empty");
            TestAssert.That(OperationMarkerCopy.Bar(400f, radius, true, 1.4f) == 1f, "a hold bar clamps high");
            TestAssert.That(OperationMarkerCopy.Bar(400f, radius, true, -0.2f) == 0f, "a hold bar clamps low");
            TestAssert.That(OperationMarkerCopy.Bar(radius + 10000f, radius, false, 0.9f) == 0.5f,
                "outside the area the bar is the approach, not the hold");
        }

        private static void CardsReadOnlyWhatTheHostPublished()
        {
            TestAssert.That(!ContractCard.TryRead(null, out _), "a missing view is not a card");
            TestAssert.That(!ContractCard.TryRead(View(active: false), out _), "an offered contract is not drawn");

            TestAssert.That(ContractCard.TryRead(View(active: true), out ContractCard card), "an active contract reads");
            TestAssert.That(card.Family == "RECON", "the family comes from the wire title: " + card.Family);
            TestAssert.That(card.HasMarker, "a marked contract keeps its position");
            TestAssert.That(card.TitleLine == "#7 RECONNAISSANCE PASS", "the title line carries the board number");
            TestAssert.That(card.Inside(900f) && !card.Inside(1600f), "inside is the published radius, not a guess");
            TestAssert.That(card.Detail(900f).StartsWith("RECON · HOLD "), "inside reads as a hold: " + card.Detail(900f));
            TestAssert.That(card.Detail(5000f).Contains("TO AREA"), "outside reads as a distance");

            TestAssert.That(ContractCard.TryRead(View(active: true, x: float.NaN), out ContractCard lost), "a card still reads");
            TestAssert.That(!lost.HasMarker, "a contract without a position is a lost contact, not a zero");

            TestAssert.That(ContractCard.TryRead(View(active: true, status: "ACTIVE / RETURN TO BASE"), out ContractCard returning),
                "a returning contract reads");
            TestAssert.That(returning.Returning && returning.Tone == MarkerTone.Ready, "a return phase is ready");
            TestAssert.That(returning.Detail(900f).Contains("LAND TO DELIVER"), "a return phase says what to do");
        }

        private static void SelectionOrdersInsideNearestThenLost()
        {
            var cards = new[]
            {
                Card(1, 15000f, 500f, true),
                Card(2, 5000f, 500f, true),
                Card(3, 300f, 500f, true),
                Card(4, 0f, 0f, false),
            };
            var selected = new ContractCard[4];
            var distances = new float[4];
            int count = ContractSelection.Select(cards, cards.Length, 0f, 0f, selected, distances, 4);
            TestAssert.That(count == 4, "every card is eligible here, including the lost one");
            TestAssert.That(selected[0].Id == 3, "the area you are inside comes first: " + selected[0].Id);
            TestAssert.That(selected[1].Id == 2 && selected[2].Id == 1, "then the nearest marked contract");
            TestAssert.That(selected[3].Id == 4, "a lost contact is listed last, never dropped");

            int capped = ContractSelection.Select(cards, cards.Length, 0f, 0f, selected, distances, 2);
            TestAssert.That(capped == 2 && selected[0].Id == 3 && selected[1].Id == 2, "the row ceiling holds");

            var far = new[] { Card(9, 90000f, 500f, true) };
            var one = new ContractCard[1];
            var oneDistance = new float[1];
            TestAssert.That(ContractSelection.Select(far, 1, 0f, 0f, one, oneDistance, 1) == 0,
                "a contract far outside its own vicinity is not listed");
            TestAssert.That(ContractSelection.Visible(Card(4, 0f, 0f, false), float.NaN),
                "a lost contact is always visible");
        }

        private static void BoxGeometryKeepsMarkersOnScreen()
        {
            TestAssert.That(!ContractMarkerMath.TryClampToBox(10f, -20f, 100f, 50f, out float ix, out float iy) &&
                ix == 10f && iy == -20f, "a point inside the box is untouched");
            TestAssert.That(ContractMarkerMath.TryClampToBox(400f, 0f, 100f, 50f, out float cx, out float cy) &&
                cx == 100f && cy == 0f, "a point off the right edge lands on it");
            TestAssert.That(ContractMarkerMath.TryClampToBox(-400f, -400f, 100f, 50f, out float dx, out float dy) &&
                dx == -100f && dy == -50f, "a corner point lands on the corner");

            TestAssert.That(ContractMarkerMath.TryEdgePoint(1f, 0f, 100f, 50f, out float ex, out float ey) &&
                ex == 100f && ey == 0f, "a bearing leaves the box on the matching edge");
            TestAssert.That(ContractMarkerMath.TryEdgePoint(0f, -1f, 100f, 50f, out float fx, out float fy) &&
                fx == 0f && fy == -50f, "straight down leaves the bottom edge");
            TestAssert.That(!ContractMarkerMath.TryEdgePoint(0f, 0f, 100f, 50f, out _, out _),
                "a target exactly behind the camera cannot be placed");
            TestAssert.That(!ContractMarkerMath.TryEdgePoint(float.NaN, 0f, 100f, 50f, out _, out _),
                "a degenerate bearing cannot be placed");
        }

        private static void ProjectionScalesLikeTheVanillaRing()
        {
            float metres = ContractMarkerMath.PixelsPerMetre(1080f, 60f, 1000f);
            TestAssert.That(System.Math.Abs(metres - 0.9353f) < 0.001f,
                "the angular scale matches the vanilla ring formula: " + metres);
            TestAssert.That(ContractMarkerMath.PixelsPerMetre(1080f, 60f, 2000f) < metres,
                "twice the distance is half the scale");
            TestAssert.That(ContractMarkerMath.PixelsPerMetre(1080f, 60f, 0f) == 0f, "a zero range is not a scale");
            TestAssert.That(ContractMarkerMath.PixelsPerMetre(0f, 60f, 1000f) == 0f, "an empty viewport is not a scale");
            TestAssert.That(ContractMarkerMath.PixelsPerMetre(1080f, 0f, 1000f) == 0f, "a zero field of view is not a scale");

            float radius = ContractMarkerMath.DotX(100f, 0);
            float zero = ContractMarkerMath.DotY(100f, 0);
            TestAssert.That(radius == 100f && zero == 0f, "the first ring dot sits on the circle");
            TestAssert.That(System.Math.Abs(ContractMarkerMath.DotY(100f, ContractMarkerMath.RingDots / 4) - 100f) < 0.001f,
                "a quarter ring is a quarter turn");
            TestAssert.That(System.Math.Abs(ContractMarkerMath.Distance(0f, 0f, 3f, 4f) - 5f) < 0.001f,
                "distance is planar");
        }

        private static SecondaryObjectiveView View(
            bool active, float x = 100f, float z = -50f, string status = "ACTIVE")
        {
            return new SecondaryObjectiveView(7, "RECONNAISSANCE PASS", "description", "target", status, "reward",
                0.42f, 161f, 4500, 60, false, isOffered: !active, isActive: active, hasMarker: true,
                x: x, z: z, radius: 1500f);
        }

        private static ContractCard Card(int id, float x, float radius, bool marker)
        {
            var view = new SecondaryObjectiveView(id, "RECONNAISSANCE PASS", "description", "target", "ACTIVE",
                "reward", 0.5f, 300f, 100, 10, false, isOffered: false, isActive: true, hasMarker: marker,
                x: x, z: 0f, radius: radius);
            TestAssert.That(ContractCard.TryRead(view, out ContractCard card), "test card must read");
            return card;
        }
    }
}
