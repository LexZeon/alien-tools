/*
 * NightGlow core - thin C ABI over the alien-tools hardware SDKs.
 *
 * Copyright (c) 2026 NightGlow contributors. MIT License (see LICENSE in this folder).
 * Wraps AlienRGB_SDK and alienfan-SDK_v2 from alien-tools (c) T-Troll and contributors, MIT License.
 *
 * Design rules:
 *  - Plain C types only, so the DLL can be called from C# (LibraryImport) or any other language.
 *  - Every function is thread-safe (one global lock per subsystem).
 *  - Return value: >= 0 success (or a count/value), < 0 error (see NG_E_*).
 *  - Lighting needs no administrator rights. Thermal/power needs administrator rights
 *    (the BIOS WMI interface rejects normal users), and must be called from an MTA thread.
 *  - Only vendor power presets are exposed. Manual fan boost, TCC offset and XMP from the
 *    underlying SDK are intentionally NOT exported.
 */
#pragma once
#include <stdint.h>

#ifdef NIGHTGLOWCORE_EXPORTS
#define NG_API extern "C" __declspec(dllexport)
#else
#define NG_API extern "C" __declspec(dllimport)
#endif

#define NG_CORE_ABI_VERSION 1

/* Error codes */
#define NG_OK              0
#define NG_E_FAIL         -1  /* generic device/WMI failure */
#define NG_E_NOT_OPEN     -2  /* subsystem not opened */
#define NG_E_RANGE        -3  /* index or argument out of range */
#define NG_E_UNSUPPORTED  -4  /* device/API version does not support the call */
#define NG_E_ACCESS       -5  /* administrator rights required */
#define NG_E_NOT_FOUND    -6  /* no compatible hardware */
#define NG_E_REJECTED     -7  /* value not in the vendor preset list */

/* Lighting API versions (same numbers as AlienRGB_SDK::Afx_Version) */
#define NG_API_V2 2
#define NG_API_V3 3
#define NG_API_V4 4   /* AW-ELC chassis lights */
#define NG_API_V5 5   /* per-key RGB keyboards (Darfon) */
#define NG_API_V6 6   /* monitors */
#define NG_API_V7 7   /* mice */
#define NG_API_V8 8   /* external keyboards */

/* Per-light action types (same numbers as AlienRGB_SDK::Action) */
#define NG_ACT_COLOR     0
#define NG_ACT_PULSE     1
#define NG_ACT_MORPH     2
#define NG_ACT_BREATHING 3
#define NG_ACT_SPECTRUM  4
#define NG_ACT_RAINBOW   5

/* Hardware (whole-device) effect types for API v5 keyboards (from alien-tools ge_types5) */
#define NG_GFX_OFF        0   /* back to per-key mode */
#define NG_GFX_STATIC     1
#define NG_GFX_BREATHING  2
#define NG_GFX_SIDE_WAVE  3
#define NG_GFX_DUAL_WAVE  4
#define NG_GFX_PULSE      8
#define NG_GFX_MORPH      9
#define NG_GFX_BOUNCE    10
#define NG_GFX_LASER     11
#define NG_GFX_RAINBOW   14

/* Color mode for hardware effects: number of colors (3 = full spectrum) */
#define NG_COLORS_ONE      1
#define NG_COLORS_TWO      2
#define NG_COLORS_SPECTRUM 3

#pragma pack(push, 1)
typedef struct ng_device_info {
    uint16_t vid;
    uint16_t pid;
    int32_t  api_version;       /* NG_API_V* */
    int32_t  light_count;       /* named lights known from mappings (may be 0 before calibration) */
    uint8_t  has_global_effects;/* 1 if NG_GFX_* effects are supported */
    uint8_t  brightness;        /* last brightness 0..255 */
    uint8_t  present;           /* 1 if currently connected */
    uint8_t  reserved;
    char     name[128];         /* UTF-8 */
} ng_device_info;

typedef struct ng_light_info {
    uint16_t light_id;
    uint16_t flags;             /* 1 = power button, 2 = indicator */
    char     name[64];          /* UTF-8 */
} ng_light_info;

typedef struct ng_light_color {
    uint16_t light_id;
    uint8_t  r, g, b;
    uint8_t  reserved;
} ng_light_color;

typedef struct ng_action {
    uint8_t type;   /* NG_ACT_* */
    uint8_t time;   /* how long this phase stays */
    uint8_t tempo;  /* transition speed */
    uint8_t r, g, b;
} ng_action;
#pragma pack(pop)

/* ---- general ---- */
NG_API int32_t ng_core_abi_version(void);

/* ---- lighting (no admin needed) ---- */
/* Enumerate HID light devices and load saved light names. Returns device count. */
NG_API int32_t ng_lights_open(void);
NG_API void    ng_lights_close(void);
NG_API int32_t ng_lights_device_count(void);
NG_API int32_t ng_lights_get_device(int32_t device, ng_device_info* out);
NG_API int32_t ng_lights_get_light(int32_t device, int32_t light, ng_light_info* out);
/* Set one light to a static color. Call ng_lights_update afterwards. */
NG_API int32_t ng_lights_set_color(int32_t device, uint16_t light_id, uint8_t r, uint8_t g, uint8_t b);
/* Set many lights at once (bulk where the device supports it). Applies immediately. */
NG_API int32_t ng_lights_set_colors(int32_t device, const ng_light_color* items, int32_t count);
/* Hardware per-light action sequence (pulse, morph, breathing, spectrum, rainbow). */
NG_API int32_t ng_lights_set_action(int32_t device, uint16_t light_id, const ng_action* actions, int32_t count);
/* Whole-device hardware effect (API v5/v8). rgb = 0x00RRGGBB. */
NG_API int32_t ng_lights_set_global_effect(int32_t device, uint8_t effect, uint8_t tempo, uint8_t color_mode, uint32_t rgb1, uint32_t rgb2);
/* Hardware brightness 0..255. include_power: also dim power button / indicators. */
NG_API int32_t ng_lights_set_brightness(int32_t device, uint8_t brightness, uint8_t include_power);
NG_API int32_t ng_lights_update(int32_t device);
/* Name a light (stored in the same registry place as alien-tools, so both tools share names). */
NG_API int32_t ng_lights_set_light_name(int32_t device, uint16_t light_id, const char* utf8_name, uint16_t flags);

/* ---- thermal / power (administrator + MTA thread) ---- */
/* Returns 1 when the BIOS interface is usable, NG_E_ACCESS without admin rights, NG_E_NOT_FOUND otherwise. */
NG_API int32_t ng_thermal_open(void);
NG_API void    ng_thermal_close(void);
NG_API int32_t ng_thermal_system_id(void);
NG_API int32_t ng_thermal_fan_count(void);
NG_API int32_t ng_thermal_fan_rpm(int32_t fan);
NG_API int32_t ng_thermal_fan_max_rpm(int32_t fan);
NG_API int32_t ng_thermal_fan_percent(int32_t fan);
NG_API int32_t ng_thermal_sensor_count(void);
/* Copies a UTF-8 name into buf; returns name length or error. */
NG_API int32_t ng_thermal_sensor_name(int32_t sensor, char* buf, int32_t buf_len);
NG_API int32_t ng_thermal_sensor_temp(int32_t sensor);
/* Vendor power presets reported by the BIOS (raw codes, e.g. 0xA0 balanced ... 0xA4 full speed). */
NG_API int32_t ng_power_mode_count(void);
NG_API int32_t ng_power_mode_code(int32_t index);
NG_API int32_t ng_power_get_mode(void);
/* Only accepts a code returned by ng_power_mode_code (never 0 = manual fan mode). */
NG_API int32_t ng_power_set_mode(uint8_t code);
NG_API int32_t ng_power_gmode_supported(void);
NG_API int32_t ng_power_get_gmode(void);
NG_API int32_t ng_power_set_gmode(uint8_t enabled);
