namespace BoscaliSummer.Modules.Autopilot.Domain
{
    internal enum MenuMode : byte { Hold, Latched }

    /// <summary>Who drove the menu last; decides the BACK glyph (RMB / BKSP / B) and the hat legend.</summary>
    internal enum MenuDevice : byte { Mouse, Key, Pad }

    /// <summary>Which input is held down on a danger row; releasing it abandons the 0.8 s hold.</summary>
    internal enum PressSource : byte { None, Lmb, Enter, Space, Pad, Digit }

    /// <summary>
    /// One frame of input, already reduced to menu pixels (origin top-left, y down) by the input source. Edge fields are true for the
    /// frame the input went down; the *Held fields are the level. The controller never touches Unity.
    /// </summary>
    internal struct MenuInput
    {
        /// <summary>Unscaled seconds.</summary>
        public float Time;
        /// <summary>The open key (or the pad's Y button) is down.</summary>
        public bool CHeld;
        /// <summary>Host gate for opening: not paused, no map, no native wheel, a local aircraft exists.</summary>
        public bool CanOpen;
        /// <summary>HOLD flick: mouse travel since the last frame.</summary>
        public float DeltaX, DeltaY;
        /// <summary>LATCHED cursor: absolute pointer.</summary>
        public float PointerX, PointerY;
        /// <summary>The pointer moved or was clicked since the last frame.</summary>
        public bool PointerMoved;
        /// <summary>0..9 pressed this frame, -1 none.</summary>
        public int Digit;
        /// <summary>Hat / arrow steps (the source applies the 350 ms / 100 ms repeat for a pad d-pad).</summary>
        public bool Left, Right, Up, Down;
        /// <summary>The discrete inputs of this frame (steps, press, back, digit) came from a pad or HOTAS rather than the keyboard.</summary>
        public bool FromPad;
        public bool Press;
        public PressSource PressSource;
        public bool Back;
        /// <summary>Wheel steps: +1 scrolls down, -1 up.</summary>
        public int Wheel;
        public bool Rmb, Lmb, Esc;
        public bool LmbHeld, EnterHeld, SpaceHeld, PadAHeld;
        /// <summary>The digit key currently held, -1 none.</summary>
        public int DigitHeld;
        /// <summary>Absolute stick (Radial Menu Horizontal/Vertical), -1..1, y down. Active beyond 0.25.</summary>
        public float StickX, StickY;

        public static MenuInput Idle(float time) => new MenuInput { Time = time, Digit = -1, DigitHeld = -1 };
    }

    internal enum MenuCommandKind : byte { Execute, Close, Back, Pulse }

    /// <summary>What the host must do after a frame. Everything the player sees (focus, hold bar, flash, toast) is controller state.</summary>
    internal readonly struct MenuCommand
    {
        public MenuCommand(MenuCommandKind kind, MenuRow row = null) { Kind = kind; Row = row; }
        public MenuCommandKind Kind { get; }
        /// <summary>Execute: the row whose <see cref="MenuRow.Run"/> to call, already confirmed available.</summary>
        public MenuRow Row { get; }
    }
}
