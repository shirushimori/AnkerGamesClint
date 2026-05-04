using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace AnkerGamesClient.Services;

/// <summary>
/// Polls XInput (built into Windows) for any connected gamepad/controller.
/// Fires events that the UI layer subscribes to for navigation.
/// Works with Xbox controllers, generic XInput controllers, and most
/// modern gamepads (PS4/PS5 via DS4Windows, Switch Pro via BetterJoy, etc.).
/// No NuGet packages required — uses direct P/Invoke to XInput1_4.dll.
/// </summary>
public sealed class ControllerService : IDisposable
{
    // ── XInput P/Invoke ──────────────────────────────────────────────────────

    [DllImport("XInput1_4.dll", EntryPoint = "XInputGetState")]
    private static extern int XInputGetState(int dwUserIndex, out XInputState pState);

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputState
    {
        public uint dwPacketNumber;
        public XInputGamepad Gamepad;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputGamepad
    {
        public ushort wButtons;
        public byte  bLeftTrigger;
        public byte  bRightTrigger;
        public short sThumbLX;
        public short sThumbLY;
        public short sThumbRX;
        public short sThumbRY;
    }

    // Button bitmasks
    private const ushort DPAD_UP        = 0x0001;
    private const ushort DPAD_DOWN      = 0x0002;
    private const ushort DPAD_LEFT      = 0x0004;
    private const ushort DPAD_RIGHT     = 0x0008;
    private const ushort START          = 0x0010;
    private const ushort BACK           = 0x0020;
    private const ushort LEFT_THUMB     = 0x0040;
    private const ushort RIGHT_THUMB    = 0x0080;
    private const ushort LEFT_SHOULDER  = 0x0100;  // LB
    private const ushort RIGHT_SHOULDER = 0x0200;  // RB
    private const ushort BTN_A          = 0x1000;
    private const ushort BTN_B          = 0x2000;
    private const ushort BTN_X          = 0x4000;
    private const ushort BTN_Y          = 0x8000;

    private const int    STICK_DEADZONE = 10000;
    private const byte   TRIGGER_THRESHOLD = 100;
    private const int    ERROR_DEVICE_NOT_CONNECTED = 1167;

    // ── Events ───────────────────────────────────────────────────────────────

    public event Action? NavLeft;
    public event Action? NavRight;
    public event Action? NavUp;
    public event Action? NavDown;
    public event Action? NavConfirm;      // A button
    public event Action? NavBack;         // B button
    public event Action? TabNext;         // RB
    public event Action? TabPrev;         // LB
    public event Action? OpenSearch;      // Start
    public event Action? ScrollUp;        // Left trigger held
    public event Action? ScrollDown;      // Right trigger held
    public event Action? ControllerConnected;
    public event Action? ControllerDisconnected;

    // ── State ────────────────────────────────────────────────────────────────

    private readonly DispatcherTimer _timer;
    private ushort   _prevButtons;
    private bool     _stickLeftFired;
    private bool     _stickRightFired;
    private bool     _stickUpFired;
    private bool     _stickDownFired;

    // Repeat-fire for held directions (ms between repeats after initial delay)
    private DateTime _lastRepeat = DateTime.MinValue;
    private const int REPEAT_INITIAL_MS = 400;
    private const int REPEAT_INTERVAL_MS = 150;
    private ushort   _heldDirection;

    public bool IsConnected { get; private set; }

    public ControllerService()
    {
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16) // ~60 Hz poll
        };
        _timer.Tick += Poll;
    }

    public void Start() => _timer.Start();
    public void Stop()  => _timer.Stop();

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= Poll;
    }

    // ── Poll loop ────────────────────────────────────────────────────────────

    private void Poll(object? sender, EventArgs e)
    {
        // Try all 4 controller slots; use the first connected one
        for (int i = 0; i < 4; i++)
        {
            var result = XInputGetState(i, out var state);

            if (result == ERROR_DEVICE_NOT_CONNECTED)
            {
                if (i == 3 && IsConnected)
                {
                    IsConnected = false;
                    ControllerDisconnected?.Invoke();
                }
                continue;
            }

            // Connected
            if (!IsConnected)
            {
                IsConnected = true;
                ControllerConnected?.Invoke();
            }

            ProcessState(state.Gamepad);
            break; // only handle first connected controller
        }
    }

    private void ProcessState(XInputGamepad pad)
    {
        var buttons = pad.wButtons;
        var now = DateTime.UtcNow;

        // ── Button press events (fire once on press) ─────────────────────────
        var pressed = (ushort)(buttons & ~_prevButtons);

        if ((pressed & BTN_A)          != 0) NavConfirm?.Invoke();
        if ((pressed & BTN_B)          != 0) NavBack?.Invoke();
        if ((pressed & RIGHT_SHOULDER) != 0) TabNext?.Invoke();
        if ((pressed & LEFT_SHOULDER)  != 0) TabPrev?.Invoke();
        if ((pressed & START)          != 0) OpenSearch?.Invoke();

        // ── D-pad navigation with repeat ─────────────────────────────────────
        ushort dirPressed = 0;
        if ((buttons & DPAD_LEFT)  != 0) dirPressed = DPAD_LEFT;
        if ((buttons & DPAD_RIGHT) != 0) dirPressed = DPAD_RIGHT;
        if ((buttons & DPAD_UP)    != 0) dirPressed = DPAD_UP;
        if ((buttons & DPAD_DOWN)  != 0) dirPressed = DPAD_DOWN;

        if (dirPressed != 0)
        {
            bool isNewPress = (pressed & dirPressed) != 0;
            bool repeatReady = dirPressed == _heldDirection &&
                               (now - _lastRepeat).TotalMilliseconds >
                               (isNewPress ? REPEAT_INITIAL_MS : REPEAT_INTERVAL_MS);

            if (isNewPress || repeatReady)
            {
                FireDirection(dirPressed);
                _heldDirection = dirPressed;
                _lastRepeat = now;
            }
        }
        else
        {
            _heldDirection = 0;
        }

        // ── Left stick navigation ─────────────────────────────────────────────
        bool stickLeft  = pad.sThumbLX < -STICK_DEADZONE;
        bool stickRight = pad.sThumbLX >  STICK_DEADZONE;
        bool stickUp    = pad.sThumbLY >  STICK_DEADZONE;
        bool stickDown  = pad.sThumbLY < -STICK_DEADZONE;

        if (stickLeft  && !_stickLeftFired)  { NavLeft?.Invoke();  _stickLeftFired  = true; }
        if (stickRight && !_stickRightFired) { NavRight?.Invoke(); _stickRightFired = true; }
        if (stickUp    && !_stickUpFired)    { NavUp?.Invoke();    _stickUpFired    = true; }
        if (stickDown  && !_stickDownFired)  { NavDown?.Invoke();  _stickDownFired  = true; }

        if (!stickLeft)  _stickLeftFired  = false;
        if (!stickRight) _stickRightFired = false;
        if (!stickUp)    _stickUpFired    = false;
        if (!stickDown)  _stickDownFired  = false;

        // ── Triggers — scroll ─────────────────────────────────────────────────
        if (pad.bLeftTrigger  > TRIGGER_THRESHOLD) ScrollUp?.Invoke();
        if (pad.bRightTrigger > TRIGGER_THRESHOLD) ScrollDown?.Invoke();

        _prevButtons = buttons;
    }

    private void FireDirection(ushort dir)
    {
        if (dir == DPAD_LEFT  || dir == DPAD_RIGHT)
        {
            if (dir == DPAD_LEFT)  NavLeft?.Invoke();
            else                   NavRight?.Invoke();
        }
        else
        {
            if (dir == DPAD_UP)   NavUp?.Invoke();
            else                  NavDown?.Invoke();
        }
    }
}
