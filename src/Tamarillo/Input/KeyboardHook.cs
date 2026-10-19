using System;
using System.Runtime.InteropServices;
using System.Threading;
using Tamarillo.Core;
using static Tamarillo.Native.NativeMethods;

namespace Tamarillo.Input
{
    public enum NavigationKey
    {
        Escape,
        Enter,
        Left,
        Right,
        Up,
        Down,
        Tab,
    }

    /// <summary>
    /// Global low-level keyboard hook that only watches for the configured chord.
    /// </summary>
    /// <remarks>
    /// Cost model: the hook lives on its own thread that sleeps in GetMessage, so it is woken only by key
    /// events and never waits on the (shared) UI thread; a busy UI can therefore never lag the system
    /// keyboard. There is no mouse hook and no timer. The callback does a table update and a compare, then
    /// forwards the event untouched; it swallows keys only while <see cref="CaptureNavigation"/> is set
    /// (the wheel is open without a drag and owns Esc, Enter and the arrows).
    /// Events are raised on the hook thread: subscribers must marshal and return immediately.
    /// </remarks>
    public sealed class KeyboardHook : IDisposable
    {
        private const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12;
        private const int VK_LSHIFT = 0xA0, VK_RSHIFT = 0xA1, VK_LCONTROL = 0xA2, VK_RCONTROL = 0xA3, VK_LMENU = 0xA4, VK_RMENU = 0xA5;
        private const int VK_LWIN = 0x5B, VK_RWIN = 0x5C;

        private readonly LowLevelKeyboardProc _proc;
        private readonly bool[] _down = new bool[256];
        private readonly ManualResetEventSlim _ready = new ManualResetEventSlim();
        private Thread _thread;
        private uint _threadId;
        private IntPtr _hook;
        private volatile Shortcut _shortcut = Shortcut.Default;
        private bool _matched;

        public KeyboardHook()
        {
            _proc = HookProc; // kept alive for the lifetime of the hook
        }

        /// <summary>Raised (hook thread) when the chord becomes fully pressed.</summary>
        public event Action ChordDown;

        /// <summary>Raised (hook thread) when the chord stops being fully pressed.</summary>
        public event Action ChordUp;

        /// <summary>Raised (hook thread) for navigation keys while <see cref="CaptureNavigation"/> is set.</summary>
        public event Action<NavigationKey> Navigation;

        public Shortcut Shortcut
        {
            get => _shortcut;
            set => _shortcut = value?.Clone() ?? Shortcut.Default;
        }

        /// <summary>While true, Esc/Enter/arrows/Tab are swallowed and raised through <see cref="Navigation"/>.</summary>
        public volatile bool CaptureNavigation;

        public bool IsRunning => _hook != IntPtr.Zero;

        /// <summary>Installs the hook. Returns false if Windows refused it.</summary>
        public bool Start()
        {
            if (_thread != null)
            {
                return IsRunning;
            }
            _ready.Reset();
            _thread = new Thread(Run) { IsBackground = true, Name = "Tamarillo.KeyboardHook", Priority = ThreadPriority.AboveNormal };
            _thread.Start();
            _ready.Wait(TimeSpan.FromSeconds(2));
            return IsRunning;
        }

        public void Stop()
        {
            Thread thread = _thread;
            if (thread == null)
            {
                return;
            }
            if (_threadId != 0)
            {
                PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            }
            thread.Join(TimeSpan.FromSeconds(2));
            _thread = null;
            _threadId = 0;
            _matched = false;
            Array.Clear(_down, 0, _down.Length);
        }

        public void Dispose()
        {
            Stop();
            _ready.Dispose();
        }

        private void Run()
        {
            _threadId = GetCurrentThreadId();
            _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
            _ready.Set();
            if (_hook == IntPtr.Zero)
            {
                return;
            }
            try
            {
                while (GetMessage(out MSG _, IntPtr.Zero, 0, 0) > 0)
                {
                }
            }
            finally
            {
                UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }
        }

        private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                try
                {
                    var info = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
                    int message = wParam.ToInt32();
                    bool isDown = message == WM_KEYDOWN || message == WM_SYSKEYDOWN;
                    bool isUp = message == WM_KEYUP || message == WM_SYSKEYUP;
                    if ((isDown || isUp) && info.vkCode < 256)
                    {
                        if (OnKey((int)info.vkCode, isDown))
                        {
                            return new IntPtr(1);
                        }
                    }
                }
                catch
                {
                    // Never let anything escape a system hook.
                }
            }
            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        /// <summary>Returns true to swallow the key.</summary>
        private bool OnKey(int vk, bool isDown)
        {
            bool repeat = isDown && _down[vk];
            _down[vk] = isDown;
            ResyncStaleKeys(vk);

            if (CaptureNavigation && TryNavigation(vk, out NavigationKey nav))
            {
                if (isDown)
                {
                    Navigation?.Invoke(nav);
                }
                return true;
            }

            if (repeat)
            {
                return false;
            }
            bool matched = Matches(_shortcut);
            if (matched != _matched)
            {
                _matched = matched;
                if (matched)
                {
                    ChordDown?.Invoke();
                }
                else
                {
                    ChordUp?.Invoke();
                }
            }
            return false;
        }

        /// <summary>
        /// Key-ups are lost when the secure desktop (UAC, Ctrl+Alt+Del, lock) takes the keyboard. Before
        /// trusting the table, drop keys Windows no longer reports as down.
        /// </summary>
        private void ResyncStaleKeys(int current)
        {
            for (int vk = 0; vk < 256; vk++)
            {
                if (_down[vk] && vk != current && (GetAsyncKeyState(vk) & 0x8000) == 0)
                {
                    _down[vk] = false;
                }
            }
        }

        private bool Matches(Shortcut shortcut)
        {
            bool ctrl = _down[VK_LCONTROL] || _down[VK_RCONTROL] || _down[VK_CONTROL];
            bool alt = _down[VK_LMENU] || _down[VK_RMENU] || _down[VK_MENU];
            bool shift = _down[VK_LSHIFT] || _down[VK_RSHIFT] || _down[VK_SHIFT];
            bool win = _down[VK_LWIN] || _down[VK_RWIN];
            if (ctrl != shortcut.Ctrl || alt != shortcut.Alt || shift != shortcut.Shift || win != shortcut.Win)
            {
                return false;
            }
            if (shortcut.Key != 0)
            {
                return shortcut.Key < 256 && _down[shortcut.Key];
            }
            // Modifiers-only chord: any other key held down means the user is typing something else.
            for (int vk = 0x08; vk < 256; vk++)
            {
                if (_down[vk] && !Shortcut.IsModifierKey(vk))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool TryNavigation(int vk, out NavigationKey key)
        {
            switch (vk)
            {
                case 0x1B: key = NavigationKey.Escape; return true;
                case 0x0D: key = NavigationKey.Enter; return true;
                case 0x25: key = NavigationKey.Left; return true;
                case 0x26: key = NavigationKey.Up; return true;
                case 0x27: key = NavigationKey.Right; return true;
                case 0x28: key = NavigationKey.Down; return true;
                case 0x09: key = NavigationKey.Tab; return true;
                default: key = default; return false;
            }
        }
    }
}
