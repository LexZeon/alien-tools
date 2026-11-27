// NightGlow core - thermal / power part. MIT License, see LICENSE in this folder.
// Thin wrapper over alienfan-SDK_v2 (alien-tools, MIT), which talks to the BIOS through
// the root\WMI AWCCWmiMethodFunction class. Requires administrator rights and an MTA thread.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <mutex>
#include <vector>
#include <cstring>

#include "alienfan-SDK.h"   // alienfan-SDK_v2 (defines byte as BYTE)
#include "nightglow_core.h"

using namespace AlienFan_SDK;

namespace {
    std::mutex t_lock;
    Control* g_ctl = nullptr;
    std::vector<uint8_t> g_modes; // vendor presets only (manual mode 0 removed)

    bool IsElevated() {
        HANDLE token = nullptr;
        TOKEN_ELEVATION elev{};
        DWORD len = 0;
        bool result = false;
        if (OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token)) {
            if (GetTokenInformation(token, TokenElevation, &elev, sizeof(elev), &len))
                result = elev.TokenIsElevated != 0;
            CloseHandle(token);
        }
        return result;
    }

    bool InModeList(uint8_t code) {
        for (auto m : g_modes) if (m == code) return true;
        return false;
    }
}

NG_API int32_t ng_thermal_open(void) {
    std::lock_guard<std::mutex> lk(t_lock);
    if (g_ctl && g_ctl->isSupported) return 1;
    if (!IsElevated()) return NG_E_ACCESS;
    if (!g_ctl) {
        g_ctl = new Control();
        // The SDK calls CoInitializeSecurity, which fails if the host process already set COM
        // security (WinUI/.NET may). Make sure the WMI proxy can impersonate anyway.
        if (g_ctl->m_WbemServices)
            CoSetProxyBlanket(g_ctl->m_WbemServices, RPC_C_AUTHN_WINNT, RPC_C_AUTHZ_NONE, nullptr,
                RPC_C_AUTHN_LEVEL_CALL, RPC_C_IMP_LEVEL_IMPERSONATE, nullptr, EOAC_NONE);
    }
    if (!g_ctl->Probe(false)) {
        bool found = g_ctl->isAlienware;
        delete g_ctl;
        g_ctl = nullptr;
        return found ? NG_E_FAIL : NG_E_NOT_FOUND;
    }
    g_modes.clear();
    for (auto p : g_ctl->powers)
        if (p != 0) g_modes.push_back(p);
    return 1;
}

NG_API void ng_thermal_close(void) {
    std::lock_guard<std::mutex> lk(t_lock);
    delete g_ctl;
    g_ctl = nullptr;
    g_modes.clear();
}

#define NEED_CTL() if (!g_ctl) return NG_E_NOT_OPEN

NG_API int32_t ng_thermal_system_id(void) {
    std::lock_guard<std::mutex> lk(t_lock); NEED_CTL();
    return (int32_t)g_ctl->systemID;
}

NG_API int32_t ng_thermal_fan_count(void) {
    std::lock_guard<std::mutex> lk(t_lock); NEED_CTL();
    return (int32_t)g_ctl->fans.size();
}

NG_API int32_t ng_thermal_fan_rpm(int32_t fan) {
    std::lock_guard<std::mutex> lk(t_lock); NEED_CTL();
    if (fan < 0 || fan >= (int32_t)g_ctl->fans.size()) return NG_E_RANGE;
    return g_ctl->GetFanRPM(fan);
}

NG_API int32_t ng_thermal_fan_max_rpm(int32_t fan) {
    std::lock_guard<std::mutex> lk(t_lock); NEED_CTL();
    if (fan < 0 || fan >= (int32_t)g_ctl->fans.size()) return NG_E_RANGE;
    return g_ctl->GetMaxRPM(fan);
}

NG_API int32_t ng_thermal_fan_percent(int32_t fan) {
    std::lock_guard<std::mutex> lk(t_lock); NEED_CTL();
    if (fan < 0 || fan >= (int32_t)g_ctl->fans.size()) return NG_E_RANGE;
    return g_ctl->GetFanPercent(fan);
}

NG_API int32_t ng_thermal_sensor_count(void) {
    std::lock_guard<std::mutex> lk(t_lock); NEED_CTL();
    return (int32_t)g_ctl->sensors.size();
}

NG_API int32_t ng_thermal_sensor_name(int32_t sensor, char* buf, int32_t buf_len) {
    std::lock_guard<std::mutex> lk(t_lock); NEED_CTL();
    if (sensor < 0 || sensor >= (int32_t)g_ctl->sensors.size() || !buf || buf_len <= 0) return NG_E_RANGE;
    const std::string& n = g_ctl->sensors[sensor].name;
    int32_t len = (int32_t)n.size() < buf_len - 1 ? (int32_t)n.size() : buf_len - 1;
    memcpy(buf, n.data(), len);
    buf[len] = '\0';
    return len;
}

NG_API int32_t ng_thermal_sensor_temp(int32_t sensor) {
    std::lock_guard<std::mutex> lk(t_lock); NEED_CTL();
    if (sensor < 0 || sensor >= (int32_t)g_ctl->sensors.size()) return NG_E_RANGE;
    return g_ctl->GetTempValue(sensor);
}

NG_API int32_t ng_power_mode_count(void) {
    std::lock_guard<std::mutex> lk(t_lock); NEED_CTL();
    return (int32_t)g_modes.size();
}

NG_API int32_t ng_power_mode_code(int32_t index) {
    std::lock_guard<std::mutex> lk(t_lock); NEED_CTL();
    if (index < 0 || index >= (int32_t)g_modes.size()) return NG_E_RANGE;
    return g_modes[index];
}

NG_API int32_t ng_power_get_mode(void) {
    std::lock_guard<std::mutex> lk(t_lock); NEED_CTL();
    return g_ctl->GetPower(true);
}

NG_API int32_t ng_power_set_mode(uint8_t code) {
    std::lock_guard<std::mutex> lk(t_lock); NEED_CTL();
    if (code == 0 || !InModeList(code)) return NG_E_REJECTED;
    return g_ctl->SetPower(code) < 0 ? NG_E_FAIL : NG_OK;
}

// Conservative: G-mode is only offered when the BIOS has the method AND lists the
// G-mode preset (0xAB) among its power modes. Some models (e.g. x16 R2) expose the
// method but not the preset, and AWCC does not offer G-mode there either.
static bool GModeUsable() {
    return g_ctl && g_ctl->isGmode && InModeList(0xAB);
}

NG_API int32_t ng_power_gmode_supported(void) {
    std::lock_guard<std::mutex> lk(t_lock); NEED_CTL();
    return GModeUsable() ? 1 : 0;
}

NG_API int32_t ng_power_get_gmode(void) {
    std::lock_guard<std::mutex> lk(t_lock); NEED_CTL();
    if (!GModeUsable()) return NG_E_UNSUPPORTED;
    return g_ctl->GetGMode();
}

NG_API int32_t ng_power_set_gmode(uint8_t enabled) {
    std::lock_guard<std::mutex> lk(t_lock); NEED_CTL();
    if (!GModeUsable()) return NG_E_UNSUPPORTED;
    return g_ctl->SetGMode(enabled != 0) < 0 ? NG_E_FAIL : NG_OK;
}
