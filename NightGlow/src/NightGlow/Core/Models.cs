using Windows.UI;

namespace NightGlow.Core;

/// <summary>Simple RGB triple used across the app (no alpha).</summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    public static readonly Rgb Black = new(0, 0, 0);
    public static readonly Rgb White = new(255, 255, 255);

    public uint ToUInt() => ((uint)R << 16) | ((uint)G << 8) | B;
    public Color ToColor() => Color.FromArgb(255, R, G, B);
    public static Rgb FromColor(Color c) => new(c.R, c.G, c.B);
    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";

    public static bool TryParse(string? hex, out Rgb rgb)
    {
        rgb = Black;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        hex = hex.Trim().TrimStart('#');
        if (hex.Length != 6 || !uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out uint v)) return false;
        rgb = new Rgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return true;
    }

    /// <summary>HSV to RGB. h in [0,360), s and v in [0,1].</summary>
    public static Rgb FromHsv(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360;
        double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
        (double r, double g, double b) = h switch
        {
            < 60 => (c, x, 0d),
            < 120 => (x, c, 0d),
            < 180 => (0d, c, x),
            < 240 => (0d, x, c),
            < 300 => (x, 0d, c),
            _ => (c, 0d, x),
        };
        return new Rgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }

    public Rgb Scale(double k)
    {
        k = Math.Clamp(k, 0, 1);
        return new Rgb((byte)(R * k), (byte)(G * k), (byte)(B * k));
    }

    public static Rgb Lerp(Rgb a, Rgb b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return new Rgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
    }
}

public enum LightApi
{
    Unknown = -1,
    V2 = 2, V3 = 3, V4 = 4, V5 = 5, V6 = 6, V7 = 7, V8 = 8,
}

public sealed class LightZone
{
    public required ushort Id { get; init; }
    public required string Name { get; set; }
    public ushort Flags { get; init; }
    public bool IsPowerButton => (Flags & 1) != 0;
    public bool IsIndicator => (Flags & 2) != 0;
}

public sealed class LightDevice
{
    public required int Index { get; init; }
    public required ushort Vid { get; init; }
    public required ushort Pid { get; init; }
    public required LightApi Api { get; init; }
    public required string Name { get; init; }
    public bool Present { get; init; } = true;
    public bool HasHardwareEffects { get; init; }
    public byte Brightness { get; set; } = 255;
    public List<LightZone> Zones { get; } = new();

    public string IdText => $"{Vid:X4}:{Pid:X4}";

    /// <summary>Human-readable kind derived from VID and API version.</summary>
    public string Kind => (Vid, Api) switch
    {
        (0x0D62, _) => "单键 RGB 键盘",
        (0x187C, LightApi.V4) => "机身灯光控制器",
        (0x187C, _) => "灯光控制器",
        (0x0461, _) or (_, LightApi.V7) => "鼠标",
        (0x04F2, _) or (_, LightApi.V8) => "外接键盘",
        (0x0424, _) or (_, LightApi.V6) => "显示器",
        _ => "灯光设备",
    };

    public string ApiText => Api == LightApi.Unknown ? "未知接口" : $"API v{(int)Api}";
    public string Glyph => Kind switch
    {
        "单键 RGB 键盘" or "外接键盘" => "",
        "鼠标" => "",
        "显示器" => "",
        _ => "",
    };
}

/// <summary>Per-light hardware action types (API v4 and older).</summary>
public enum LightActionType : byte { Color = 0, Pulse = 1, Morph = 2, Breathing = 3, Spectrum = 4, Rainbow = 5 }

public readonly record struct LightAction(LightActionType Type, byte Time, byte Tempo, Rgb Color);

/// <summary>Whole-device hardware effects (API v5 keyboards), numbers from alien-tools.</summary>
public enum HardwareEffect : byte
{
    Off = 0, Static = 1, Breathing = 2, SideWave = 3, DualWave = 4, Pulse = 8, Morph = 9, Bounce = 10, Laser = 11, Rainbow = 14,
}

public enum ColorMode : byte { One = 1, Two = 2, Spectrum = 3 }

public sealed record FanReading(int Index, string Name, int Rpm, int MaxRpm, int Percent);
public sealed record SensorReading(int Index, string Name, int Celsius);

public sealed record PowerModeInfo(byte Code, string Name, string Description, string Glyph)
{
    public static PowerModeInfo FromCode(byte code) => code switch
    {
        0xA0 => new(code, "均衡", "日常使用，噪音和性能折中", ""),
        0xA1 => new(code, "性能", "提高性能上限，风扇更积极", ""),
        0xA2 => new(code, "冷却", "优先降温", ""),
        0xA3 => new(code, "静音", "限制功耗，安静优先", ""),
        0xA4 => new(code, "全速", "厂商最高档，风扇全速", ""),
        0xA5 => new(code, "省电", "电池续航优先", ""),
        0xAB => new(code, "G 模式", "厂商超频预设", ""),
        0x96 => new(code, "静音", "旧版 BIOS 静音模式", ""),
        0x97 => new(code, "均衡", "旧版 BIOS 均衡模式", ""),
        0x98 => new(code, "均衡性能", "旧版 BIOS 均衡性能模式", ""),
        0x99 => new(code, "性能", "旧版 BIOS 性能模式", ""),
        _ => new(code, $"模式 0x{code:X2}", "BIOS 报告的其他预设", ""),
    };
}
