namespace NightGlow.Core.Lighting;

/// <summary>
/// Physical key positions of a typical laptop keyboard with a number pad, keyed by Windows
/// scan code (set 1; extended keys carry 0xE000). Positions are in key units from the
/// top-left corner. Used to place keys that the user mapped in key calibration, and to
/// draw the keyboard on the canvas. It is a schematic, not an exact copy of any model.
/// </summary>
public static class KeyboardLayout
{
    public readonly record struct Key(int Code, string Label, double X, double Y, double W);

    public const double Width = 19.5;   // main block 15 + gap 0.5 + number pad 4
    public const double Height = 6.0;

    public static readonly IReadOnlyList<Key> Keys = Build();
    private static readonly Dictionary<int, Key> ByCode = Keys.ToDictionary(k => k.Code);

    public static bool TryGet(int code, out Key key) => ByCode.TryGetValue(code, out key);

    /// <summary>Scan code of a key event: extended keys get 0xE000 added.</summary>
    public static int CodeOf(uint scanCode, bool extended) => (int)scanCode | (extended ? 0xE000 : 0);

    public static string LabelOf(int code) => ByCode.TryGetValue(code, out var k) ? k.Label : $"0x{code:X}";

    private static List<Key> Build()
    {
        var list = new List<Key>();
        void Row(double y, double x, params (int Code, string Label, double W)[] keys)
        {
            foreach (var (code, label, w) in keys)
            {
                if (code != 0) list.Add(new Key(code, label, x, y, w));
                x += w;
            }
        }
        const double np = 15.5; // number pad left edge

        Row(0, 0, (0x01, "Esc", 1), (0x3B, "F1", 1), (0x3C, "F2", 1), (0x3D, "F3", 1), (0x3E, "F4", 1),
            (0x3F, "F5", 1), (0x40, "F6", 1), (0x41, "F7", 1), (0x42, "F8", 1), (0x43, "F9", 1), (0x44, "F10", 1),
            (0x57, "F11", 1), (0x58, "F12", 1), (0xE052, "Ins", 1), (0xE053, "Del", 1));
        Row(0, np, (0xE047, "Home", 1), (0xE04F, "End", 1), (0xE049, "PgUp", 1), (0xE051, "PgDn", 1));

        Row(1, 0, (0x29, "`", 1), (0x02, "1", 1), (0x03, "2", 1), (0x04, "3", 1), (0x05, "4", 1), (0x06, "5", 1),
            (0x07, "6", 1), (0x08, "7", 1), (0x09, "8", 1), (0x0A, "9", 1), (0x0B, "0", 1), (0x0C, "-", 1),
            (0x0D, "=", 1), (0x0E, "Backspace", 2));
        Row(1, np, (0x45, "Num", 1), (0xE035, "/", 1), (0x37, "*", 1), (0x4A, "-", 1));

        Row(2, 0, (0x0F, "Tab", 1.5), (0x10, "Q", 1), (0x11, "W", 1), (0x12, "E", 1), (0x13, "R", 1), (0x14, "T", 1),
            (0x15, "Y", 1), (0x16, "U", 1), (0x17, "I", 1), (0x18, "O", 1), (0x19, "P", 1), (0x1A, "[", 1),
            (0x1B, "]", 1), (0x2B, "\\", 1.5));
        Row(2, np, (0x47, "7", 1), (0x48, "8", 1), (0x49, "9", 1), (0x4E, "+", 1));

        Row(3, 0, (0x3A, "Caps", 1.75), (0x1E, "A", 1), (0x1F, "S", 1), (0x20, "D", 1), (0x21, "F", 1), (0x22, "G", 1),
            (0x23, "H", 1), (0x24, "J", 1), (0x25, "K", 1), (0x26, "L", 1), (0x27, ";", 1), (0x28, "'", 1),
            (0x1C, "Enter", 2.25));
        Row(3, np, (0x4B, "4", 1), (0x4C, "5", 1), (0x4D, "6", 1));

        Row(4, 0, (0x2A, "Shift", 1.25), (0x2C, "Z", 1), (0x2D, "X", 1), (0x2E, "C", 1), (0x2F, "V", 1), (0x30, "B", 1),
            (0x31, "N", 1), (0x32, "M", 1), (0x33, ",", 1), (0x34, ".", 1), (0x35, "/", 1), (0x36, "Shift", 1.75),
            (0xE048, "↑", 1));
        Row(4, np, (0x4F, "1", 1), (0x50, "2", 1), (0x51, "3", 1), (0xE01C, "Enter", 1));

        Row(5, 0, (0x1D, "Ctrl", 1.25), (0, "Fn", 1), (0xE05B, "Win", 1), (0x38, "Alt", 1.25), (0x39, "Space", 5.5),
            (0xE038, "Alt", 1), (0xE01D, "Ctrl", 1), (0xE04B, "←", 1), (0xE050, "↓", 1), (0xE04D, "→", 1));
        Row(5, np, (0x52, "0", 2), (0x53, ".", 1));
        return list;
    }
}
