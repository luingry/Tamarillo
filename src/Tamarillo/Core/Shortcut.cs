using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Windows.Input;
using InputKey = System.Windows.Input.Key;

namespace Tamarillo.Core
{
    /// <summary>
    /// The chord that opens the wheel: one or more modifiers, optionally plus one key.
    /// Matching is exact on modifiers (Ctrl+Alt+Shift does not fire while Win is also down, which keeps the
    /// Office key and other system chords out of the way).
    /// </summary>
    [DataContract]
    public sealed class Shortcut
    {
        [DataMember(Order = 0)] public bool Ctrl { get; set; }

        [DataMember(Order = 1)] public bool Alt { get; set; }

        [DataMember(Order = 2)] public bool Shift { get; set; }

        [DataMember(Order = 3)] public bool Win { get; set; }

        /// <summary>Virtual-key code of the non-modifier key, or 0 for a modifiers-only chord.</summary>
        [DataMember(Order = 4)] public int Key { get; set; }

        public static Shortcut Default => new Shortcut { Ctrl = true, Alt = true, Shift = true };

        public int ModifierCount => (Ctrl ? 1 : 0) + (Alt ? 1 : 0) + (Shift ? 1 : 0) + (Win ? 1 : 0);

        /// <summary>
        /// A usable chord needs a modifier, and a modifiers-only chord needs two (a lone Ctrl or Shift fires
        /// all the time while dragging, which also changes the drop effect).
        /// </summary>
        public bool IsValid => Key == 0 ? ModifierCount >= 2 : ModifierCount >= 1 && !IsModifierKey(Key);

        public Shortcut Clone() => new Shortcut { Ctrl = Ctrl, Alt = Alt, Shift = Shift, Win = Win, Key = Key };

        public bool SameAs(Shortcut other) =>
            other != null && Ctrl == other.Ctrl && Alt == other.Alt && Shift == other.Shift && Win == other.Win && Key == other.Key;

        /// <summary>Keycap labels in display order, e.g. ["Ctrl", "Alt", "Shift"].</summary>
        public IReadOnlyList<string> Keycaps
        {
            get
            {
                var caps = new List<string>(5);
                if (Ctrl)
                {
                    caps.Add("Ctrl");
                }
                if (Alt)
                {
                    caps.Add("Alt");
                }
                if (Shift)
                {
                    caps.Add("Shift");
                }
                if (Win)
                {
                    caps.Add("Win");
                }
                if (Key != 0)
                {
                    caps.Add(KeyName(Key));
                }
                return caps;
            }
        }

        public override string ToString() => string.Join(" + ", Keycaps);

        public static bool IsModifierKey(int vk) =>
            vk == 0x10 || vk == 0x11 || vk == 0x12 || (vk >= 0xA0 && vk <= 0xA5) || vk == 0x5B || vk == 0x5C;

        public static string KeyName(int vk)
        {
            if ((vk >= 0x30 && vk <= 0x39) || (vk >= 0x41 && vk <= 0x5A))
            {
                return ((char)vk).ToString();
            }
            if (vk >= 0x70 && vk <= 0x87)
            {
                return "F" + (vk - 0x6F);
            }
            switch (vk)
            {
                case 0x20: return "Espaço";
                case 0x09: return "Tab";
                case 0x0D: return "Enter";
                case 0x08: return "Backspace";
                case 0xC0: return "`";
                case 0xBA: return ";";
                case 0xBB: return "=";
                case 0xBC: return ",";
                case 0xBD: return "-";
                case 0xBE: return ".";
                case 0xBF: return "/";
                case 0xDB: return "[";
                case 0xDC: return "\\";
                case 0xDD: return "]";
                case 0xDE: return "'";
            }
            InputKey key = KeyInterop.KeyFromVirtualKey(vk);
            return key == InputKey.None ? "0x" + vk.ToString("X2") : key.ToString();
        }
    }
}
