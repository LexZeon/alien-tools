using NightGlow.Core.Interop;

namespace NightGlow.Core.Thermal;

public enum ThermalOpenResult { Ok, NeedAdmin, NotFound, Failed, NoCore }

/// <summary>Fan/temperature/power access. Called only from the thermal service's MTA thread.</summary>
public interface IThermalBackend
{
    bool IsSimulated { get; }
    ThermalOpenResult Open();
    void Close();
    IReadOnlyList<FanReading> ReadFans();
    IReadOnlyList<SensorReading> ReadSensors();
    IReadOnlyList<byte> PowerModes();
    int GetPowerMode();
    bool SetPowerMode(byte code);
}

public sealed class NativeThermalBackend : IThermalBackend
{
    private string[] _sensorNames = Array.Empty<string>();
    private int _fanCount;

    public bool IsSimulated => false;

    public ThermalOpenResult Open()
    {
        int r = NativeMethods.ng_thermal_open();
        if (r == NativeMethods.NG_E_ACCESS) return ThermalOpenResult.NeedAdmin;
        if (r == NativeMethods.NG_E_NOT_FOUND) return ThermalOpenResult.NotFound;
        if (r < 0) return ThermalOpenResult.Failed;

        _fanCount = Math.Max(0, NativeMethods.ng_thermal_fan_count());
        int sc = Math.Max(0, NativeMethods.ng_thermal_sensor_count());
        _sensorNames = new string[sc];
        for (int i = 0; i < sc; i++) _sensorNames[i] = SensorName(i);
        return ThermalOpenResult.Ok;
    }

    private static unsafe string SensorName(int i)
    {
        byte* buf = stackalloc byte[128];
        int len = NativeMethods.ng_thermal_sensor_name(i, buf, 128);
        string raw = len > 0 ? System.Text.Encoding.UTF8.GetString(buf, len) : $"传感器 {i}";
        return raw switch
        {
            "CPU Internal Thermistor" => "CPU",
            "GPU Internal Thermistor" => "GPU",
            "Motherboard Thermistor" => "主板",
            _ => raw,
        };
    }

    public void Close() => NativeMethods.ng_thermal_close();

    public IReadOnlyList<FanReading> ReadFans()
    {
        var list = new List<FanReading>(_fanCount);
        for (int i = 0; i < _fanCount; i++)
        {
            int rpm = NativeMethods.ng_thermal_fan_rpm(i);
            int max = NativeMethods.ng_thermal_fan_max_rpm(i);
            int pct = NativeMethods.ng_thermal_fan_percent(i);
            list.Add(new FanReading(i, $"风扇 {i + 1}", Math.Max(rpm, 0), Math.Max(max, 0), Math.Clamp(pct, 0, 100)));
        }
        return list;
    }

    public IReadOnlyList<SensorReading> ReadSensors()
    {
        var list = new List<SensorReading>(_sensorNames.Length);
        for (int i = 0; i < _sensorNames.Length; i++)
        {
            int t = NativeMethods.ng_thermal_sensor_temp(i);
            if (t > 0 && t < 150) list.Add(new SensorReading(i, _sensorNames[i], t));
        }
        return list;
    }

    public IReadOnlyList<byte> PowerModes()
    {
        int n = NativeMethods.ng_power_mode_count();
        var list = new List<byte>();
        for (int i = 0; i < n; i++)
        {
            int code = NativeMethods.ng_power_mode_code(i);
            if (code > 0 && code < 256) list.Add((byte)code);
        }
        return list;
    }

    public int GetPowerMode() => NativeMethods.ng_power_get_mode();

    public bool SetPowerMode(byte code) => NativeMethods.ng_power_set_mode(code) == NativeMethods.NG_OK;
}

/// <summary>Simulated readings for demo mode. Values are fake and the UI says so.</summary>
public sealed class DemoThermalBackend : IThermalBackend
{
    private readonly Random _rnd = new(7);
    private byte _mode = 0xA4;
    private double _phase;

    public bool IsSimulated => true;
    public ThermalOpenResult Open() => ThermalOpenResult.Ok;
    public void Close() { }

    public IReadOnlyList<FanReading> ReadFans()
    {
        _phase += 0.15;
        int a = 3600 + (int)(400 * Math.Sin(_phase)) + _rnd.Next(-60, 60);
        int b = 3000 + (int)(300 * Math.Cos(_phase)) + _rnd.Next(-60, 60);
        return new[]
        {
            new FanReading(0, "风扇 1", a, 5200, a * 100 / 5200),
            new FanReading(1, "风扇 2", b, 4300, b * 100 / 4300),
            new FanReading(2, "风扇 3", a - 150, 5200, (a - 150) * 100 / 5200),
            new FanReading(3, "风扇 4", b - 120, 4300, (b - 120) * 100 / 4300),
        };
    }

    public IReadOnlyList<SensorReading> ReadSensors() => new[]
    {
        new SensorReading(0, "CPU", 62 + (int)(6 * Math.Sin(_phase * 0.7))),
        new SensorReading(1, "GPU", 58 + (int)(5 * Math.Cos(_phase * 0.5))),
        new SensorReading(2, "主板", 44),
    };

    public IReadOnlyList<byte> PowerModes() => new byte[] { 0xA0, 0xA1, 0xA2, 0xA3, 0xA4 };
    public int GetPowerMode() => _mode;
    public bool SetPowerMode(byte code) { _mode = code; return true; }
}
