namespace NightGlow.Core.Lighting;

public enum EffectKind { Hardware, Software, Off }

/// <summary>
/// One entry of the effect gallery. Hardware effects run inside the light controller
/// (no CPU use, keep running after the app exits); software effects are computed by
/// <see cref="EffectEngine"/> while NightGlow runs.
/// </summary>
public sealed record EffectDefinition(
    string Id,
    string Name,
    string Description,
    EffectKind Kind,
    string Glyph,
    int ColorCount,            // 0, 1 or 2 user colors
    bool HasSpeed,
    // keyboard (API v5) whole-device effect, if any
    HardwareEffect? KeyboardEffect,
    ColorMode KeyboardColorMode,
    // per-light action for chassis lights (API v4 and older), if any
    LightActionType? ZoneAction,
    string[] PreviewColors)
{
    public string KindText => Kind switch
    {
        EffectKind.Hardware => "硬件",
        EffectKind.Software => "软件",
        _ => "",
    };
}

public sealed record EffectParameters(Rgb Color1, Rgb Color2, int Speed /* 1..10 */, byte Brightness /* 0..255 */)
{
    /// <summary>Hardware tempo byte for API v5 whole-device effects. Smaller = faster (to be verified on hardware).</summary>
    public byte KeyboardTempo => (byte)Math.Clamp(11 - Speed, 1, 10);

    /// <summary>Tempo byte for per-light actions (API v4). Smaller = faster (to be verified on hardware).</summary>
    public byte ZoneTempo => (byte)Math.Clamp(255 - Speed * 22, 20, 255);
}

public static class EffectCatalog
{
    private static readonly string[] Rainbow = { "#FF3B3B", "#FFB13B", "#F5FF3B", "#3BFF6E", "#3BD8FF", "#5B3BFF", "#E23BFF" };

    public static readonly IReadOnlyList<EffectDefinition> All = new EffectDefinition[]
    {
        new("static", "静态", "所有灯区保持同一个颜色", EffectKind.Hardware, "", 1, false,
            HardwareEffect.Static, ColorMode.One, LightActionType.Color, new[] { "#35D6E8", "#35D6E8" }),
        new("rainbow-wave", "彩虹波", "彩虹色带横向流过键盘（硬件）", EffectKind.Hardware, "", 0, true,
            HardwareEffect.SideWave, ColorMode.Spectrum, LightActionType.Spectrum, Rainbow),
        new("breathing", "呼吸", "颜色缓慢亮起再熄灭", EffectKind.Hardware, "", 1, true,
            HardwareEffect.Breathing, ColorMode.One, LightActionType.Breathing, new[] { "#101018", "#8B5CF6", "#101018" }),
        new("spectrum", "光谱循环", "所有灯区一起在全色谱中循环（硬件）", EffectKind.Hardware, "", 0, true,
            HardwareEffect.Rainbow, ColorMode.Spectrum, LightActionType.Rainbow, Rainbow),
        new("side-wave", "单色波", "单一颜色的波浪", EffectKind.Hardware, "", 1, true,
            HardwareEffect.SideWave, ColorMode.One, LightActionType.Pulse, new[] { "#101018", "#35D6E8", "#101018", "#35D6E8" }),
        new("dual-wave", "双色波", "两种颜色交替的波浪", EffectKind.Hardware, "", 2, true,
            HardwareEffect.DualWave, ColorMode.Two, LightActionType.Morph, new[] { "#35D6E8", "#8B5CF6", "#35D6E8", "#8B5CF6" }),
        new("pulse", "脉冲", "颜色快速闪烁", EffectKind.Hardware, "", 1, true,
            HardwareEffect.Pulse, ColorMode.One, LightActionType.Pulse, new[] { "#FF4D6D", "#101018", "#FF4D6D", "#101018" }),
        new("morph", "渐变", "在两种颜色之间来回过渡", EffectKind.Hardware, "", 2, true,
            HardwareEffect.Morph, ColorMode.Two, LightActionType.Morph, new[] { "#FF7A18", "#AF002D" }),
        new("bounce", "弹跳", "光点来回弹跳（键盘）", EffectKind.Hardware, "", 1, true,
            HardwareEffect.Bounce, ColorMode.One, null, new[] { "#101018", "#3BFF6E", "#101018" }),
        new("laser", "激光", "光束扫过键盘（键盘）", EffectKind.Hardware, "", 1, true,
            HardwareEffect.Laser, ColorMode.One, null, new[] { "#101018", "#FF3B3B", "#101018" }),

        new("sw-cycle", "颜色循环", "所有灯区一起平滑变换色相（软件，NightGlow 运行时生效）", EffectKind.Software, "", 0, true,
            null, ColorMode.One, null, Rainbow),
        new("sw-breath", "柔和呼吸", "在两种颜色之间柔和呼吸（软件）", EffectKind.Software, "", 2, true,
            null, ColorMode.One, null, new[] { "#0B1E3F", "#35D6E8", "#0B1E3F" }),
        new("sw-wave", "软件彩虹波", "按灯区顺序流动的彩虹（软件，需要已命名的灯区）", EffectKind.Software, "", 0, true,
            null, ColorMode.One, null, Rainbow),

        new("off", "关闭", "关闭所有灯光", EffectKind.Off, "", 0, false,
            HardwareEffect.Static, ColorMode.One, LightActionType.Color, new[] { "#000000", "#000000" }),
    };

    public static EffectDefinition? Find(string? id) => All.FirstOrDefault(e => e.Id == id);
}
