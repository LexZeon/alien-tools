// P/Invoke surface of nightglow_core.dll (see NightGlow/native/NightGlowCore/nightglow_core.h).
// Keep this file in sync with the header; the structs are packed to 1 byte there.
using System.Runtime.InteropServices;
using System.Text;

namespace NightGlow.Core.Interop;

internal static partial class NativeMethods
{
    public const string Dll = "nightglow_core.dll";
    public const int AbiVersion = 1;

    public const int NG_OK = 0;
    public const int NG_E_FAIL = -1;
    public const int NG_E_NOT_OPEN = -2;
    public const int NG_E_RANGE = -3;
    public const int NG_E_UNSUPPORTED = -4;
    public const int NG_E_ACCESS = -5;
    public const int NG_E_NOT_FOUND = -6;
    public const int NG_E_REJECTED = -7;

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct DeviceInfo
    {
        public ushort Vid;
        public ushort Pid;
        public int ApiVersion;
        public int LightCount;
        public byte HasGlobalEffects;
        public byte Brightness;
        public byte Present;
        public byte Reserved;
        public fixed byte Name[128];

        public string GetName()
        {
            fixed (byte* p = Name) return Utf8(p, 128);
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct LightInfo
    {
        public ushort LightId;
        public ushort Flags;
        public fixed byte Name[64];

        public string GetName()
        {
            fixed (byte* p = Name) return Utf8(p, 64);
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct LightColor
    {
        public ushort LightId;
        public byte R, G, B;
        public byte Reserved;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct Action
    {
        public byte Type;
        public byte Time;
        public byte Tempo;
        public byte R, G, B;
    }

    private static unsafe string Utf8(byte* p, int cap)
    {
        int len = 0;
        while (len < cap && p[len] != 0) len++;
        return Encoding.UTF8.GetString(p, len);
    }

    [LibraryImport(Dll)] public static partial int ng_core_abi_version();

    // lighting
    [LibraryImport(Dll)] public static partial int ng_lights_open();
    [LibraryImport(Dll)] public static partial void ng_lights_close();
    [LibraryImport(Dll)] public static partial int ng_lights_device_count();
    [LibraryImport(Dll)] public static partial int ng_lights_get_device(int device, out DeviceInfo info);
    [LibraryImport(Dll)] public static partial int ng_lights_get_light(int device, int light, out LightInfo info);
    [LibraryImport(Dll)] public static partial int ng_lights_set_color(int device, ushort lightId, byte r, byte g, byte b);
    [LibraryImport(Dll)] public static partial int ng_lights_set_colors(int device, [In] LightColor[] items, int count);
    [LibraryImport(Dll)] public static partial int ng_lights_set_action(int device, ushort lightId, [In] Action[] actions, int count);
    [LibraryImport(Dll)] public static partial int ng_lights_set_global_effect(int device, byte effect, byte tempo, byte colorMode, uint rgb1, uint rgb2);
    [LibraryImport(Dll)] public static partial int ng_lights_set_brightness(int device, byte brightness, byte includePower);
    [LibraryImport(Dll)] public static partial int ng_lights_update(int device);
    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int ng_lights_set_light_name(int device, ushort lightId, string name, ushort flags);

    // thermal / power (admin + MTA thread)
    [LibraryImport(Dll)] public static partial int ng_thermal_open();
    [LibraryImport(Dll)] public static partial void ng_thermal_close();
    [LibraryImport(Dll)] public static partial int ng_thermal_system_id();
    [LibraryImport(Dll)] public static partial int ng_thermal_fan_count();
    [LibraryImport(Dll)] public static partial int ng_thermal_fan_rpm(int fan);
    [LibraryImport(Dll)] public static partial int ng_thermal_fan_max_rpm(int fan);
    [LibraryImport(Dll)] public static partial int ng_thermal_fan_percent(int fan);
    [LibraryImport(Dll)] public static partial int ng_thermal_sensor_count();
    [LibraryImport(Dll)] public static unsafe partial int ng_thermal_sensor_name(int sensor, byte* buf, int bufLen);
    [LibraryImport(Dll)] public static partial int ng_thermal_sensor_temp(int sensor);
    [LibraryImport(Dll)] public static partial int ng_power_mode_count();
    [LibraryImport(Dll)] public static partial int ng_power_mode_code(int index);
    [LibraryImport(Dll)] public static partial int ng_power_get_mode();
    [LibraryImport(Dll)] public static partial int ng_power_set_mode(byte code);
    [LibraryImport(Dll)] public static partial int ng_power_gmode_supported();
    [LibraryImport(Dll)] public static partial int ng_power_get_gmode();
    [LibraryImport(Dll)] public static partial int ng_power_set_gmode(byte enabled);

    /// <summary>True when nightglow_core.dll can be loaded and speaks our ABI.</summary>
    public static bool TryProbe(out string reason)
    {
        try
        {
            int v = ng_core_abi_version();
            if (v != AbiVersion)
            {
                reason = $"核心库版本不匹配（需要 {AbiVersion}，实际 {v}）";
                return false;
            }
            reason = "";
            return true;
        }
        catch (DllNotFoundException)
        {
            reason = "未找到 nightglow_core.dll";
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            reason = "nightglow_core.dll 版本不对";
            return false;
        }
        catch (BadImageFormatException)
        {
            reason = "nightglow_core.dll 不是 x64 版本";
            return false;
        }
    }
}
