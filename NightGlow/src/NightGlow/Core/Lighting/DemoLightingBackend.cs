namespace NightGlow.Core.Lighting;

/// <summary>
/// Used when nightglow_core.dll is missing. Mirrors the device set of an x16 R2 class laptop
/// so the UI can be explored; nothing is sent to hardware. The UI labels this mode clearly.
/// </summary>
public sealed class DemoLightingBackend : ILightingBackend
{
    private readonly object _gate = new();
    private readonly Dictionary<(int, ushort), Rgb> _state = new();

    public bool IsSimulated => true;

    /// <summary>Last colors "sent", so the UI preview can show something meaningful.</summary>
    public IReadOnlyDictionary<(int Device, ushort Light), Rgb> State
    {
        get { lock (_gate) return new Dictionary<(int, ushort), Rgb>(_state); }
    }

    public IReadOnlyList<LightDevice> Open()
    {
        var elc = new LightDevice { Index = 0, Vid = 0x187C, Pid = 0x0551, Api = LightApi.V4, Name = "AW-ELC（演示）" };
        elc.Zones.Add(new LightZone { Id = 0, Name = "背部灯带 左" });
        elc.Zones.Add(new LightZone { Id = 1, Name = "背部灯带 右" });
        elc.Zones.Add(new LightZone { Id = 2, Name = "屏幕徽标" });
        elc.Zones.Add(new LightZone { Id = 3, Name = "电源键", Flags = 1 });
        var kb = new LightDevice { Index = 1, Vid = 0x0D62, Pid = 0x2ABE, Api = LightApi.V5, Name = "Darfon Keyboard（演示）", HasHardwareEffects = true };
        var mouse = new LightDevice { Index = 2, Vid = 0x0461, Pid = 0x4E9D, Api = LightApi.V7, Name = "610M 鼠标（演示）" };
        mouse.Zones.Add(new LightZone { Id = 0, Name = "滚轮" });
        mouse.Zones.Add(new LightZone { Id = 1, Name = "Logo" });
        return new[] { elc, kb, mouse };
    }

    public void Close() { }

    public bool SetColor(int device, ushort lightId, Rgb color)
    {
        lock (_gate) _state[(device, lightId)] = color;
        return true;
    }

    public bool SetColors(int device, IReadOnlyList<(ushort Id, Rgb Color)> items)
    {
        lock (_gate)
            foreach (var (id, c) in items) _state[(device, id)] = c;
        return true;
    }

    public bool SetAction(int device, ushort lightId, IReadOnlyList<LightAction> actions)
    {
        if (actions.Count > 0) SetColor(device, lightId, actions[0].Color);
        return true;
    }

    public bool SetHardwareEffect(int device, HardwareEffect effect, byte tempo, ColorMode mode, Rgb c1, Rgb c2)
    {
        lock (_gate) _state[(device, 0xFFFF)] = c1;
        return true;
    }

    public bool SetBrightness(int device, byte brightness, bool includePower) => true;
    public bool Update(int device) => true;
    public bool SetLightName(int device, ushort lightId, string name, ushort flags) => true;
}
