using BoscaliSummer.Framework.Contracts;

namespace BoscaliSummer.Features.DynamicOperations.Domain
{
    /// <summary>
    /// One accepted contract as the HUDs draw it: the board's own words plus the geometry the
    /// marker needs, copied out of the snapshot so nothing live is retained across frames.
    /// </summary>
    internal readonly struct ContractCard
    {
        public readonly int Id;
        public readonly string Title;
        public readonly string Family;
        public readonly string Status;
        public readonly float X;
        public readonly float Z;
        public readonly float Radius;
        public readonly float Seconds;
        public readonly float Progress;

        /// <summary>The host could mark a position for it; a lost contact cannot be drawn.</summary>
        public readonly bool HasMarker;

        private ContractCard(SecondaryObjectiveView view)
        {
            Id = view.Id;
            Title = string.IsNullOrEmpty(view.Title) ? "SECONDARY OBJECTIVE" : view.Title;
            Family = OperationTitles.FamilyFor(view.Title);
            Status = view.Status ?? string.Empty;
            HasMarker = view.HasMarker && OperationMarkerCopy.Finite(view.X) && OperationMarkerCopy.Finite(view.Z);
            X = HasMarker ? view.X : 0f;
            Z = HasMarker ? view.Z : 0f;
            Radius = OperationMarkerCopy.Finite(view.Radius) && view.Radius > 0f ? view.Radius : 0f;
            Seconds = OperationMarkerCopy.Finite(view.SecondsRemaining) ? view.SecondsRemaining : 0f;
            Progress = OperationMarkerCopy.Finite(view.Progress) ? view.Progress : 0f;
        }

        public static bool TryRead(SecondaryObjectiveView view, out ContractCard card)
        {
            card = default;
            if (view == null || !view.IsActive) return false;
            card = new ContractCard(view);
            return true;
        }

        public MarkerTone Tone => OperationMarkerCopy.Tone(Status, Seconds);

        public bool Returning => OperationMarkerCopy.Returning(Status);

        public bool Inside(float distance) =>
            HasMarker && Radius > 0f && OperationMarkerCopy.Finite(distance) && distance <= Radius;

        public string TitleLine => OperationMarkerCopy.Title(Id, Title);

        /// <summary>Family, what the contract asks for, and the clock; distance from the viewer.</summary>
        public string Detail(float distance) => OperationMarkerCopy.Detail(
            Family, distance, Radius, Seconds, Progress,
            OperationMarkerCopy.Field(Inside(distance), Returning));

        public override string ToString() => Id + " " + Title;
    }

    /// <summary>
    /// Screen and map geometry for a contract marker. Pure: the test project links this file.
    /// </summary>
    internal static class ContractMarkerMath
    {
        public const int RingDots = 24;

        public static float Distance(float ax, float az, float bx, float bz)
        {
            float dx = ax - bx, dz = az - bz;
            return (float)System.Math.Sqrt(dx * dx + dz * dz);
        }

        public static float DotX(float radius, int index)
        {
            double angle = 2.0 * System.Math.PI * index / RingDots;
            return (float)(System.Math.Cos(angle) * radius);
        }

        public static float DotY(float radius, int index)
        {
            double angle = 2.0 * System.Math.PI * index / RingDots;
            return (float)(System.Math.Sin(angle) * radius);
        }

        /// <summary>
        /// How many canvas units one metre covers at this distance, for a vertical field of
        /// view centred on the camera. The same relation the vanilla area ring uses, so a
        /// contract area drawn by the mod matches a vanilla area drawn beside it.
        /// </summary>
        public static float PixelsPerMetre(float viewportHeight, float fieldOfViewDegrees, float distance)
        {
            if (!OperationMarkerCopy.Finite(viewportHeight) || viewportHeight <= 0f) return 0f;
            if (!OperationMarkerCopy.Finite(distance) || distance <= 0f) return 0f;
            if (!OperationMarkerCopy.Finite(fieldOfViewDegrees) || fieldOfViewDegrees <= 0f) return 0f;
            double half = fieldOfViewDegrees * 0.5 * System.Math.PI / 180.0;
            double tan = System.Math.Tan(half);
            if (tan <= 0.0001) return 0f;
            return (float)(viewportHeight * 0.5 / (distance * tan));
        }

        /// <summary>
        /// Keep a point inside the box, reporting whether it had to move. Used for a marker
        /// whose target is off screen: it sits on the frame edge instead of vanishing.
        /// </summary>
        public static bool TryClampToBox(float x, float y, float halfWidth, float halfHeight,
            out float clampedX, out float clampedY)
        {
            clampedX = Clamp(x, -halfWidth, halfWidth);
            clampedY = Clamp(y, -halfHeight, halfHeight);
            return clampedX != x || clampedY != y;
        }

        /// <summary>Where a direction from the centre leaves the box; the off-screen marker spot.</summary>
        public static bool TryEdgePoint(float dirX, float dirY, float halfWidth, float halfHeight,
            out float x, out float y)
        {
            x = 0f;
            y = 0f;
            if (!OperationMarkerCopy.Finite(dirX) || !OperationMarkerCopy.Finite(dirY)) return false;
            if (dirX == 0f && dirY == 0f) return false;
            if (halfWidth <= 0f || halfHeight <= 0f) return false;

            float scaleX = dirX == 0f ? float.MaxValue : halfWidth / System.Math.Abs(dirX);
            float scaleY = dirY == 0f ? float.MaxValue : halfHeight / System.Math.Abs(dirY);
            float scale = System.Math.Min(scaleX, scaleY);
            if (!OperationMarkerCopy.Finite(scale)) return false;
            x = dirX * scale;
            y = dirY * scale;
            return true;
        }

        private static float Clamp(float value, float min, float max) =>
            value < min ? min : value > max ? max : value;
    }
}
