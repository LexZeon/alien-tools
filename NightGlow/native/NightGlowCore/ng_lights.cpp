// NightGlow core - lighting part. MIT License, see LICENSE in this folder.
// Thin wrapper over AlienRGB_SDK (alien-tools, MIT). No protocol logic lives here.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <mutex>
#include <string>
#include <vector>
#include <cstring>

#include "AlienRGB_SDK.h"
#include "nightglow_core.h"

using namespace AlienRGB_SDK;

namespace {
    std::mutex g_lock;
    Mappings* g_map = nullptr;

    // alien-tools stores names in the ANSI code page; the C ABI speaks UTF-8.
    std::string AcpToUtf8(const std::string& s) {
        if (s.empty()) return s;
        int wlen = MultiByteToWideChar(CP_ACP, 0, s.c_str(), (int)s.size(), nullptr, 0);
        std::wstring w(wlen, L'\0');
        MultiByteToWideChar(CP_ACP, 0, s.c_str(), (int)s.size(), &w[0], wlen);
        int len = WideCharToMultiByte(CP_UTF8, 0, w.c_str(), wlen, nullptr, 0, nullptr, nullptr);
        std::string out(len, '\0');
        WideCharToMultiByte(CP_UTF8, 0, w.c_str(), wlen, &out[0], len, nullptr, nullptr);
        return out;
    }

    std::string Utf8ToAcp(const char* s) {
        if (!s || !*s) return std::string();
        int wlen = MultiByteToWideChar(CP_UTF8, 0, s, -1, nullptr, 0);
        std::wstring w(wlen, L'\0');
        MultiByteToWideChar(CP_UTF8, 0, s, -1, &w[0], wlen);
        int len = WideCharToMultiByte(CP_ACP, 0, w.c_str(), -1, nullptr, 0, nullptr, nullptr);
        std::string out(len, '\0');
        WideCharToMultiByte(CP_ACP, 0, w.c_str(), -1, &out[0], len, nullptr, nullptr);
        if (!out.empty() && out.back() == '\0') out.pop_back();
        return out;
    }

    void CopyStr(char* dst, size_t cap, const std::string& src) {
        size_t n = src.size() < cap - 1 ? src.size() : cap - 1;
        memcpy(dst, src.data(), n);
        dst[n] = '\0';
    }

    // Returns the device at index, or nullptr. Caller holds g_lock.
    Afx_device* DeviceAt(int32_t index) {
        if (!g_map || index < 0 || index >= (int32_t)g_map->fxdevs.size()) return nullptr;
        return &g_map->fxdevs[index];
    }

    Afx_device* ActiveDeviceAt(int32_t index) {
        Afx_device* d = DeviceAt(index);
        return (d && d->present && d->dev) ? d : nullptr;
    }
}

NG_API int32_t ng_core_abi_version(void) { return NG_CORE_ABI_VERSION; }

NG_API int32_t ng_lights_open(void) {
    std::lock_guard<std::mutex> lk(g_lock);
    if (!g_map) {
        g_map = new Mappings();
        g_map->LoadMappings();
    }
    g_map->AlienFXEnumDevices();
    return (int32_t)g_map->fxdevs.size();
}

NG_API void ng_lights_close(void) {
    std::lock_guard<std::mutex> lk(g_lock);
    delete g_map;   // closes HID handles
    g_map = nullptr;
}

NG_API int32_t ng_lights_device_count(void) {
    std::lock_guard<std::mutex> lk(g_lock);
    return g_map ? (int32_t)g_map->fxdevs.size() : NG_E_NOT_OPEN;
}

NG_API int32_t ng_lights_get_device(int32_t device, ng_device_info* out) {
    if (!out) return NG_E_RANGE;
    std::lock_guard<std::mutex> lk(g_lock);
    if (!g_map) return NG_E_NOT_OPEN;
    Afx_device* d = DeviceAt(device);
    if (!d) return NG_E_RANGE;
    memset(out, 0, sizeof(*out));
    out->vid = d->vid;
    out->pid = d->pid;
    out->api_version = d->dev ? d->dev->version : API_UNKNOWN;
    out->light_count = (int32_t)d->lights.size();
    out->has_global_effects = (d->dev && d->dev->IsHaveGlobal()) ? 1 : 0;
    out->brightness = d->brightness;
    out->present = d->present ? 1 : 0;
    std::string name = d->name.empty() && d->dev ? d->dev->description : d->name;
    CopyStr(out->name, sizeof(out->name), AcpToUtf8(name));
    return NG_OK;
}

NG_API int32_t ng_lights_get_light(int32_t device, int32_t light, ng_light_info* out) {
    if (!out) return NG_E_RANGE;
    std::lock_guard<std::mutex> lk(g_lock);
    if (!g_map) return NG_E_NOT_OPEN;
    Afx_device* d = DeviceAt(device);
    if (!d || light < 0 || light >= (int32_t)d->lights.size()) return NG_E_RANGE;
    const Afx_light& l = d->lights[light];
    memset(out, 0, sizeof(*out));
    out->light_id = l.lightid;
    out->flags = l.flags;
    CopyStr(out->name, sizeof(out->name), AcpToUtf8(l.name));
    return NG_OK;
}

NG_API int32_t ng_lights_set_color(int32_t device, uint16_t light_id, uint8_t r, uint8_t g, uint8_t b) {
    std::lock_guard<std::mutex> lk(g_lock);
    if (!g_map) return NG_E_NOT_OPEN;
    Afx_device* d = ActiveDeviceAt(device);
    if (!d) return NG_E_RANGE;
    Afx_action c{ AlienFX_A_Color, 0, 0, r, g, b };
    return d->dev->SetColor((BYTE)light_id, c) ? NG_OK : NG_E_FAIL;
}

NG_API int32_t ng_lights_set_colors(int32_t device, const ng_light_color* items, int32_t count) {
    if (!items || count <= 0) return NG_E_RANGE;
    std::lock_guard<std::mutex> lk(g_lock);
    if (!g_map) return NG_E_NOT_OPEN;
    Afx_device* d = ActiveDeviceAt(device);
    if (!d) return NG_E_RANGE;
    std::vector<Afx_lightblock> blocks;
    blocks.reserve(count);
    for (int32_t i = 0; i < count; i++) {
        Afx_lightblock lb;
        lb.index = (BYTE)items[i].light_id;
        lb.act.push_back({ AlienFX_A_Color, 0, 0, items[i].r, items[i].g, items[i].b });
        blocks.push_back(lb);
    }
    bool ok = d->dev->SetMultiAction(&blocks, false);
    ok = d->dev->UpdateColors() && ok;
    return ok ? NG_OK : NG_E_FAIL;
}

NG_API int32_t ng_lights_set_action(int32_t device, uint16_t light_id, const ng_action* actions, int32_t count) {
    if (!actions || count <= 0) return NG_E_RANGE;
    std::lock_guard<std::mutex> lk(g_lock);
    if (!g_map) return NG_E_NOT_OPEN;
    Afx_device* d = ActiveDeviceAt(device);
    if (!d) return NG_E_RANGE;
    Afx_lightblock lb;
    lb.index = (BYTE)light_id;
    for (int32_t i = 0; i < count; i++) {
        if (actions[i].type > NG_ACT_RAINBOW) return NG_E_RANGE;
        lb.act.push_back({ actions[i].type, actions[i].time, actions[i].tempo, actions[i].r, actions[i].g, actions[i].b });
    }
    return d->dev->SetAction(&lb) ? NG_OK : NG_E_FAIL;
}

NG_API int32_t ng_lights_set_global_effect(int32_t device, uint8_t effect, uint8_t tempo, uint8_t color_mode, uint32_t rgb1, uint32_t rgb2) {
    std::lock_guard<std::mutex> lk(g_lock);
    if (!g_map) return NG_E_NOT_OPEN;
    Afx_device* d = ActiveDeviceAt(device);
    if (!d) return NG_E_RANGE;
    if (!d->dev->IsHaveGlobal()) return NG_E_UNSUPPORTED;
    if (color_mode < NG_COLORS_ONE || color_mode > NG_COLORS_SPECTRUM) return NG_E_RANGE;
    // Afx_colorcode layout is b,g,r,brightness -> 0x00RRGGBB maps directly.
    return d->dev->SetGlobalEffects(effect, 1 /* permanent */, color_mode, tempo,
        Afx_colorcode(rgb1 & 0xffffff), Afx_colorcode(rgb2 & 0xffffff)) ? NG_OK : NG_E_FAIL;
}

NG_API int32_t ng_lights_set_brightness(int32_t device, uint8_t brightness, uint8_t include_power) {
    std::lock_guard<std::mutex> lk(g_lock);
    if (!g_map) return NG_E_NOT_OPEN;
    Afx_device* d = ActiveDeviceAt(device);
    if (!d) return NG_E_RANGE;
    d->brightness = brightness;
    return g_map->SetDeviceBrightness(d, brightness, include_power != 0) ? NG_OK : NG_E_FAIL;
}

NG_API int32_t ng_lights_update(int32_t device) {
    std::lock_guard<std::mutex> lk(g_lock);
    if (!g_map) return NG_E_NOT_OPEN;
    Afx_device* d = ActiveDeviceAt(device);
    if (!d) return NG_E_RANGE;
    return d->dev->UpdateColors() ? NG_OK : NG_E_FAIL;
}

NG_API int32_t ng_lights_set_light_name(int32_t device, uint16_t light_id, const char* utf8_name, uint16_t flags) {
    std::lock_guard<std::mutex> lk(g_lock);
    if (!g_map) return NG_E_NOT_OPEN;
    Afx_device* d = DeviceAt(device);
    if (!d) return NG_E_RANGE;
    std::string name = Utf8ToAcp(utf8_name);
    Afx_light* l = g_map->GetMappingByDev(d, light_id);
    if (name.empty()) {
        if (l) g_map->RemoveMapping(d, light_id);
    } else if (l) {
        l->name = name;
        l->flags = flags;
    } else {
        Afx_light nl{};
        nl.lightid = (BYTE)light_id;
        nl.flags = flags;
        nl.scancode = 0;
        nl.name = name;
        d->lights.push_back(nl);
    }
    g_map->SaveMappings();
    return NG_OK;
}
