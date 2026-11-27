# NightGlow hardware self-test. Calls nightglow_core.dll directly and logs every return code.
#
#   powershell -ExecutionPolicy Bypass -File NightGlow\tools\hw-test.ps1 -Part info     # list devices, no changes
#   powershell -ExecutionPolicy Bypass -File NightGlow\tools\hw-test.ps1 -Part lights   # changes lights; watch them
#   powershell -ExecutionPolicy Bypass -File NightGlow\tools\hw-test.ps1 -Part power    # asks for admin (UAC);
#       cycles through the vendor power presets the BIOS reports, reads fans and the GPU limits in each,
#       then switches back to the preset that was active at the start.
#
# The log goes to %LOCALAPPDATA%\NightGlow\hw-test.log (appended).
param(
    [ValidateSet('info', 'lights', 'power')][string]$Part = 'info',
    [int]$SettleSeconds = 6
)
$ErrorActionPreference = 'Stop'
$log = Join-Path $env:LOCALAPPDATA 'NightGlow\hw-test.log'
New-Item -ItemType Directory -Force (Split-Path $log) | Out-Null
function Write-Log([string]$text) {
    $line = "[{0:HH:mm:ss}] {1}" -f (Get-Date), $text
    Write-Host $line
    Add-Content -Path $log -Value $line -Encoding UTF8
}

# power needs admin and an MTA thread (BIOS WMI); relaunch if needed
if ($Part -eq 'power') {
    $admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    $mta = [Threading.Thread]::CurrentThread.GetApartmentState() -eq 'MTA'
    if (-not $admin -or -not $mta) {
        $argLine = "-MTA -NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`" -Part power -SettleSeconds $SettleSeconds"
        if ($admin) { Start-Process powershell -ArgumentList $argLine -Wait }
        else { Start-Process powershell -ArgumentList $argLine -Verb RunAs -Wait }
        return
    }
}

$root = Split-Path $PSScriptRoot -Parent
$dll = @("$root\publish\nightglow_core.dll", "$root\bin\Release\nightglow_core.dll", "$root\bin\Debug\nightglow_core.dll") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $dll) { throw "nightglow_core.dll not found; run NightGlow\build.ps1 first" }

Add-Type -TypeDefinition (@'
using System; using System.Runtime.InteropServices;
public static class Ng {
  const string D = @"__DLL__";
  [StructLayout(LayoutKind.Sequential, Pack = 1)]
  public struct DeviceInfo {
    public ushort vid, pid; public int api, lights; public byte global, brightness, present, reserved;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 128)] public byte[] name;
  }
  [StructLayout(LayoutKind.Sequential, Pack = 1)]
  public struct Action { public byte type, time, tempo, r, g, b; }
  [DllImport(D)] public static extern int ng_core_abi_version();
  [DllImport(D)] public static extern int ng_lights_open();
  [DllImport(D)] public static extern void ng_lights_close();
  [DllImport(D)] public static extern int ng_lights_get_device(int device, ref DeviceInfo info);
  [DllImport(D)] public static extern int ng_lights_set_color(int device, ushort id, byte r, byte g, byte b);
  [DllImport(D)] public static extern int ng_lights_set_action(int device, ushort id, Action[] acts, int count);
  [DllImport(D)] public static extern int ng_lights_set_global_effect(int device, byte effect, byte tempo, byte mode, uint rgb1, uint rgb2);
  [DllImport(D)] public static extern int ng_lights_update(int device);
  [DllImport(D)] public static extern int ng_thermal_open();
  [DllImport(D)] public static extern void ng_thermal_close();
  [DllImport(D)] public static extern int ng_thermal_system_id();
  [DllImport(D)] public static extern int ng_thermal_fan_count();
  [DllImport(D)] public static extern int ng_thermal_fan_rpm(int fan);
  [DllImport(D)] public static extern int ng_thermal_fan_percent(int fan);
  [DllImport(D)] public static extern int ng_thermal_sensor_count();
  [DllImport(D)] public static extern int ng_thermal_sensor_name(int sensor, byte[] buf, int len);
  [DllImport(D)] public static extern int ng_thermal_sensor_temp(int sensor);
  [DllImport(D)] public static extern int ng_power_mode_count();
  [DllImport(D)] public static extern int ng_power_mode_code(int index);
  [DllImport(D)] public static extern int ng_power_get_mode();
  [DllImport(D)] public static extern int ng_power_set_mode(byte code);
}
'@ -replace '__DLL__', $dll)

Write-Log "=== hw-test part=$Part dll=$dll abi=$([Ng]::ng_core_abi_version())"

function Get-Devices {
    $n = [Ng]::ng_lights_open()
    $list = @()
    for ($i = 0; $i -lt $n; $i++) {
        $info = New-Object Ng+DeviceInfo
        $info.name = New-Object byte[] 128
        $rc = [Ng]::ng_lights_get_device($i, [ref]$info)
        $name = [Text.Encoding]::UTF8.GetString($info.name).TrimEnd([char]0)
        $list += [pscustomobject]@{ Index = $i; Vid = '{0:X4}' -f $info.vid; Pid = '{0:X4}' -f $info.pid; Api = $info.api; Lights = $info.lights; Global = $info.global; Present = $info.present; Name = $name; Rc = $rc }
    }
    return $list
}

function Get-GpuLimits {
    $q = nvidia-smi -q -d TEMPERATURE,POWER 2>$null
    $t = ($q | Select-String 'GPU Target Temperature Specification\s*:\s*(.+)$' | Select-Object -First 1).Matches.Groups[1].Value
    $cur = ($q | Select-String '^\s+Current Power Limit\s*:\s*([\d\.]+ W)' | Select-Object -First 1).Matches.Groups[1].Value
    $max = ($q | Select-String '^\s+Max Power Limit\s*:\s*([\d\.]+ W)' | Select-Object -First 1).Matches.Groups[1].Value
    return "GPU target=$t powerLimit=$cur max=$max"
}

$devices = Get-Devices
foreach ($d in $devices) { Write-Log ("device {0}: {1}:{2} api v{3} lights={4} global={5} present={6} name='{7}' rc={8}" -f $d.Index, $d.Vid, $d.Pid, $d.Api, $d.Lights, $d.Global, $d.Present, $d.Name, $d.Rc) }

if ($Part -eq 'lights') {
    $kb = $devices | Where-Object { $_.Api -eq 5 } | Select-Object -First 1
    $elc = $devices | Where-Object { $_.Api -eq 4 } | Select-Object -First 1
    $mouse = $devices | Where-Object { $_.Api -eq 7 } | Select-Object -First 1
    function Step([string]$what, [scriptblock]$do, [int]$seconds = 5) {
        $rc = & $do
        Write-Log ("STEP {0} -> rc={1}  (look now, {2} s)" -f $what, ($rc -join ','), $seconds)
        Start-Sleep $seconds
    }
    if ($kb) {
        Step 'keyboard: all keys RED (hardware static)' { [Ng]::ng_lights_set_global_effect($kb.Index, 1, 5, 1, 0xFF0000, 0xFF0000) }
        Step 'keyboard: all keys GREEN' { [Ng]::ng_lights_set_global_effect($kb.Index, 1, 5, 1, 0x00FF00, 0x00FF00) }
        Step 'keyboard: hardware rainbow side wave (spectrum)' { [Ng]::ng_lights_set_global_effect($kb.Index, 3, 5, 3, 0xFF0000, 0x0000FF) } 10
        Step 'keyboard: key id 0 WHITE only (per-key path)' { [Ng]::ng_lights_set_global_effect($kb.Index, 1, 5, 1, 0, 0); [Ng]::ng_lights_set_color($kb.Index, 0, 255, 255, 255); [Ng]::ng_lights_update($kb.Index) } 6
    }
    if ($elc) {
        Step 'chassis: light ids 0..63 BLUE' { $r = 0; for ($id = 0; $id -le 63; $id++) { $r = [Ng]::ng_lights_set_color($elc.Index, $id, 0, 80, 255) }; [Ng]::ng_lights_update($elc.Index) } 6
        Step 'chassis: light ids 0..63 RED' { for ($id = 0; $id -le 63; $id++) { [void][Ng]::ng_lights_set_color($elc.Index, $id, 255, 0, 0) }; [Ng]::ng_lights_update($elc.Index) } 6
        Step 'chassis: hardware morph chain (rainbow, phase offset by id)' {
            for ($id = 0; $id -le 63; $id++) {
                $acts = for ($j = 0; $j -lt 6; $j++) {
                    $h = (($j / 6.0) + $id / 16.0) % 1.0
                    $rr = [Math]::Max(0, [Math]::Min(255, [int](255 * [Math]::Abs($h * 6 - 3) - 255)))
                    $gg = [Math]::Max(0, [Math]::Min(255, [int](510 - 255 * [Math]::Abs($h * 6 - 2))))
                    $bb = [Math]::Max(0, [Math]::Min(255, [int](510 - 255 * [Math]::Abs($h * 6 - 4))))
                    $a = New-Object Ng+Action; $a.type = 2; $a.time = 7; $a.tempo = 140; $a.r = $rr; $a.g = $gg; $a.b = $bb; $a
                }
                [void][Ng]::ng_lights_set_action($elc.Index, $id, [Ng+Action[]]$acts, $acts.Count)
            }
            [Ng]::ng_lights_update($elc.Index)
        } 12
    }
    if ($mouse) {
        Step 'mouse: light ids 0..3 GREEN' { for ($id = 0; $id -le 3; $id++) { [void][Ng]::ng_lights_set_color($mouse.Index, $id, 0, 255, 0) }; [Ng]::ng_lights_update($mouse.Index) } 6
    }
    Write-Log 'lights part done (lights keep the last state)'
}

if ($Part -eq 'power') {
    $open = [Ng]::ng_thermal_open()
    Write-Log "thermal_open=$open system_id=0x$('{0:X}' -f [Ng]::ng_thermal_system_id())"
    if ($open -lt 1) { Write-Log 'thermal interface not usable; stopping'; Start-Sleep 5; return }
    function Read-State([string]$tag) {
        $fans = for ($f = 0; $f -lt [Ng]::ng_thermal_fan_count(); $f++) { "fan$f=$([Ng]::ng_thermal_fan_rpm($f))rpm/$([Ng]::ng_thermal_fan_percent($f))%" }
        $temps = for ($s = 0; $s -lt [Ng]::ng_thermal_sensor_count(); $s++) {
            $buf = New-Object byte[] 64; [void][Ng]::ng_thermal_sensor_name($s, $buf, 64)
            "{0}={1}C" -f ([Text.Encoding]::UTF8.GetString($buf).TrimEnd([char]0)), [Ng]::ng_thermal_sensor_temp($s)
        }
        Write-Log ("{0}: mode=0x{1:X2} | {2} | {3} | {4}" -f $tag, [Ng]::ng_power_get_mode(), ($fans -join ' '), ($temps -join ' '), (Get-GpuLimits))
    }
    $codes = for ($i = 0; $i -lt [Ng]::ng_power_mode_count(); $i++) { [Ng]::ng_power_mode_code($i) }
    Write-Log ("presets reported by BIOS: " + (($codes | ForEach-Object { '0x{0:X2}' -f $_ }) -join ', '))
    $start = [Ng]::ng_power_get_mode()
    Read-State 'start'
    foreach ($c in $codes) {
        $rc = [Ng]::ng_power_set_mode([byte]$c)
        Start-Sleep $SettleSeconds
        Read-State ("set 0x{0:X2} rc={1}" -f $c, $rc)
    }
    if ($start -ge 0) {
        $rc = [Ng]::ng_power_set_mode([byte]$start)
        Start-Sleep $SettleSeconds
        Read-State ("restored 0x{0:X2} rc={1}" -f $start, $rc)
    }
    [Ng]::ng_thermal_close()
    Write-Log 'power part done'
    Start-Sleep 3
}
