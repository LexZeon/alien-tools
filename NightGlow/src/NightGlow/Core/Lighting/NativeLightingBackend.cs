using NightGlow.Core.Interop;

namespace NightGlow.Core.Lighting;

/// <summary>Lights through nightglow_core.dll (alien-tools AlienRGB_SDK). No admin rights needed.</summary>
public sealed class NativeLightingBackend : ILightingBackend
{
    public bool IsSimulated => false;

    public IReadOnlyList<LightDevice> Open()
    {
        int count = NativeMethods.ng_lights_open();
        var list = new List<LightDevice>();
        for (int i = 0; i < count; i++)
        {
            if (NativeMethods.ng_lights_get_device(i, out var info) != NativeMethods.NG_OK) continue;
            var dev = new LightDevice
            {
                Index = i,
                Vid = info.Vid,
                Pid = info.Pid,
                Api = Enum.IsDefined(typeof(LightApi), info.ApiVersion) ? (LightApi)info.ApiVersion : LightApi.Unknown,
                Name = info.GetName() is { Length: > 0 } n ? n : $"设备 {info.Vid:X4}:{info.Pid:X4}",
                Present = info.Present != 0,
                HasHardwareEffects = info.HasGlobalEffects != 0,
                Brightness = info.Brightness,
            };
            for (int l = 0; l < info.LightCount; l++)
            {
                if (NativeMethods.ng_lights_get_light(i, l, out var li) != NativeMethods.NG_OK) continue;
                dev.Zones.Add(new LightZone { Id = li.LightId, Name = li.GetName(), Flags = li.Flags });
            }
            if (dev.Present) list.Add(dev);
        }
        return list;
    }

    public void Close() => NativeMethods.ng_lights_close();

    public bool SetColor(int device, ushort lightId, Rgb c) =>
        NativeMethods.ng_lights_set_color(device, lightId, c.R, c.G, c.B) == NativeMethods.NG_OK;

    public bool SetColors(int device, IReadOnlyList<(ushort Id, Rgb Color)> items)
    {
        if (items.Count == 0) return true;
        var arr = new NativeMethods.LightColor[items.Count];
        for (int i = 0; i < items.Count; i++)
            arr[i] = new NativeMethods.LightColor { LightId = items[i].Id, R = items[i].Color.R, G = items[i].Color.G, B = items[i].Color.B };
        return NativeMethods.ng_lights_set_colors(device, arr, arr.Length) == NativeMethods.NG_OK;
    }

    public bool SetAction(int device, ushort lightId, IReadOnlyList<LightAction> actions)
    {
        if (actions.Count == 0) return false;
        var arr = actions.Select(a => new NativeMethods.Action
        {
            Type = (byte)a.Type, Time = a.Time, Tempo = a.Tempo, R = a.Color.R, G = a.Color.G, B = a.Color.B,
        }).ToArray();
        return NativeMethods.ng_lights_set_action(device, lightId, arr, arr.Length) == NativeMethods.NG_OK;
    }

    public bool SetHardwareEffect(int device, HardwareEffect effect, byte tempo, ColorMode mode, Rgb c1, Rgb c2) =>
        NativeMethods.ng_lights_set_global_effect(device, (byte)effect, tempo, (byte)mode, c1.ToUInt(), c2.ToUInt()) == NativeMethods.NG_OK;

    public bool SetBrightness(int device, byte brightness, bool includePower) =>
        NativeMethods.ng_lights_set_brightness(device, brightness, (byte)(includePower ? 1 : 0)) == NativeMethods.NG_OK;

    public bool Update(int device) => NativeMethods.ng_lights_update(device) == NativeMethods.NG_OK;

    public bool SetLightName(int device, ushort lightId, string name, ushort flags) =>
        NativeMethods.ng_lights_set_light_name(device, lightId, name, flags) == NativeMethods.NG_OK;
}
