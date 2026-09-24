namespace BoscaliSummer.Features.Support.Domain.Layout
{
    /// <summary>An axis-aligned rectangle. The origin is the top-left and Y grows downward.</summary>
    internal readonly struct Box
    {
        public readonly float X;
        public readonly float Y;
        public readonly float Width;
        public readonly float Height;

        public Box(float x, float y, float width, float height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public float Right => X + Width;
        public float Bottom => Y + Height;

        public bool Contains(float x, float y) => x >= X && y >= Y && x < Right && y < Bottom;

        public bool Intersects(Box other) =>
            X < other.Right && Right > other.X && Y < other.Bottom && Bottom > other.Y;

        public Box Inflate(float dx, float dy) => new Box(X - dx, Y - dy, Width + dx * 2f, Height + dy * 2f);
    }
}
