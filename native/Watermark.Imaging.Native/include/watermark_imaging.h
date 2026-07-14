#ifndef WATERMARK_IMAGING_H
#define WATERMARK_IMAGING_H

#include <stddef.h>
#include <stdint.h>

#if defined(_WIN32)
#  if defined(WMI_BUILDING_LIBRARY)
#    define WMI_API __declspec(dllexport)
#  else
#    define WMI_API __declspec(dllimport)
#  endif
#elif defined(__APPLE__)
/* The Apple package is a static XCFramework. `retain` prevents ld64's
   dead-strip pass from removing C ABI entry points resolved by .NET P/Invoke. */
#  define WMI_API __attribute__((visibility("default"), used, retain))
#else
#  define WMI_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

#define WMI_ABI_VERSION 3u

typedef enum wmi_status {
    WMI_OK = 0,
    WMI_UNSUPPORTED = 1,
    WMI_INVALID_ARGUMENT = 2,
    WMI_IO_ERROR = 3,
    WMI_CORRUPTED_INPUT = 4,
    WMI_OUT_OF_MEMORY = 5,
    WMI_CANCELLED = 6,
    WMI_ALIGNMENT_FAILED = 7,
    WMI_INTERNAL_ERROR = 255
} wmi_status;

typedef enum wmi_capability {
    WMI_CAP_RAW = 1u << 0,
    WMI_CAP_SCENE_ALIGNMENT = 1u << 1,
    WMI_CAP_STAR_ALIGNMENT = 1u << 2,
    WMI_CAP_WARP = 1u << 3,
    WMI_CAP_TIFF16 = 1u << 4,
    WMI_CAP_BIGTIFF = 1u << 5,
    WMI_CAP_PREVIEW_PIPELINE = 1u << 6
} wmi_capability;

typedef int32_t (*wmi_cancel_callback)(void* user_data);
typedef void (*wmi_progress_callback)(void* user_data, int32_t stage, double progress);
typedef wmi_status (*wmi_tile_callback)(void* user_data, int32_t row_start, int32_t row_count,
                                       int32_t width, int32_t channels, const uint16_t* samples,
                                       const uint8_t* validity_mask);

typedef struct wmi_callbacks {
    void* user_data;
    wmi_cancel_callback is_cancelled;
    wmi_progress_callback progress;
} wmi_callbacks;

typedef struct wmi_raw_info {
    int32_t width;
    int32_t height;
    int32_t orientation;
    uint32_t raw_count;
    char make[64];
    char model[128];
} wmi_raw_info;

typedef struct wmi_decode_options {
    int32_t tile_height;
    int32_t apply_orientation;
    int32_t use_camera_white_balance;
    int32_t demosaic_quality;
    float highlight_headroom;
    int32_t max_edge;
} wmi_decode_options;

typedef struct wmi_transform {
    double m11, m12, m21, m22, tx, ty;
    double score;
    double rms_error;
    int32_t match_count;
} wmi_transform;

typedef struct wmi_star_feature {
    double x;
    double y;
    double strength;
} wmi_star_feature;

typedef void* wmi_tiff_writer;

WMI_API uint32_t wmi_get_abi_version(void);
WMI_API uint32_t wmi_get_capabilities(void);
WMI_API const char* wmi_get_backend_version(void);
WMI_API const char* wmi_get_last_error(void);
WMI_API wmi_status wmi_probe_raw(const char* utf8_path, wmi_raw_info* info);
WMI_API wmi_status wmi_decode_raw_rgb16(const char* utf8_path, const wmi_decode_options* options,
                                        wmi_tile_callback output, const wmi_callbacks* callbacks);
WMI_API wmi_status wmi_detect_stars_gray16(const uint16_t* pixels, int32_t width, int32_t height,
                                           wmi_star_feature* features, int32_t capacity,
                                           int32_t* feature_count, const wmi_callbacks* callbacks);
WMI_API wmi_status wmi_align_star_features(const wmi_star_feature* reference, int32_t reference_count,
                                           const wmi_star_feature* candidate, int32_t candidate_count,
                                           int32_t width, int32_t height, wmi_transform* transform,
                                           const wmi_callbacks* callbacks);
WMI_API wmi_status wmi_warp_preview_rgb16(const uint16_t* source, int32_t width, int32_t height,
                                          int32_t channels, const wmi_transform* transform,
                                          uint16_t* output, uint8_t* validity_mask,
                                          const wmi_callbacks* callbacks);
WMI_API wmi_status wmi_warp_lanczos3_tile_rgb16(const uint16_t* source_rows,
                                                 int32_t source_row_start,
                                                 int32_t source_row_count,
                                                 int32_t width, int32_t height,
                                                 int32_t channels,
                                                 int32_t output_row_start,
                                                 int32_t output_row_count,
                                                 const wmi_transform* transform,
                                                 uint16_t* output,
                                                 uint8_t* validity_mask,
                                                 const wmi_callbacks* callbacks);
WMI_API wmi_status wmi_stack_preview_tile_rgb16(const uint16_t* frame_samples,
                                                 const uint8_t* frame_masks,
                                                 const double* exposure_multipliers,
                                                 int32_t frame_count, int32_t pixel_count,
                                                 int32_t channels, int32_t reduction_mode,
                                                 double sigma_low, double sigma_high,
                                                 int32_t sigma_iterations, int32_t worker_count,
                                                 uint16_t* output,
                                                 uint8_t* output_mask,
                                                 const wmi_callbacks* callbacks);
WMI_API wmi_status wmi_tiff16_open(const char* utf8_path, int32_t width, int32_t height,
                                   int32_t channels, int32_t big_tiff, wmi_tiff_writer* writer);
WMI_API wmi_status wmi_tiff16_set_icc(wmi_tiff_writer writer, const uint8_t* profile,
                                      int32_t profile_length);
WMI_API wmi_status wmi_tiff16_write(wmi_tiff_writer writer, int32_t row_start, int32_t row_count,
                                    const uint16_t* samples, const wmi_callbacks* callbacks);
WMI_API wmi_status wmi_tiff16_close(wmi_tiff_writer writer, int32_t commit);

#ifdef __cplusplus
}
#endif
#endif
