#include "watermark_imaging.h"

#include <array>
#include <cmath>
#include <cstring>
#include <iostream>
#include <string>
#include <vector>

namespace {

bool near_byte(uint8_t actual, uint8_t expected, int tolerance = 2) {
    return std::abs(static_cast<int>(actual) - static_cast<int>(expected)) <= tolerance;
}

bool near_word(uint16_t actual, uint16_t expected, int tolerance = 514) {
    return std::abs(static_cast<int>(actual) - static_cast<int>(expected)) <= tolerance;
}

int32_t always_cancelled(void*) { return 1; }

wmi_color_grade_state identity_grade(const float* identity_curve) {
    wmi_color_grade_state grade{};
    grade.struct_size = sizeof(grade);
    grade.master_curve = {identity_curve, 2};
    grade.red_curve = {identity_curve, 2};
    grade.green_curve = {identity_curve, 2};
    grade.blue_curve = {identity_curve, 2};
    return grade;
}

bool require(bool condition, const char* message) {
    if (condition) return true;
    std::cerr << message << ": " << wmi_get_last_error() << '\n';
    return false;
}

} // namespace

int main() {
    if (!require(wmi_get_abi_version() == WMI_ABI_VERSION, "unexpected ABI version")) return 1;

    if ((wmi_get_capabilities() & WMI_CAP_COLOR_OCIO) == 0) {
        std::cout << "OpenColorIO support is disabled; test skipped\n";
        return 0;
    }

    const std::array<float, 4> identity_curve{0.f, 0.f, 1.f, 1.f};
    auto grade = identity_grade(identity_curve.data());
    wmi_color_pipeline_desc pipeline{};
    pipeline.struct_size = sizeof(pipeline);
    pipeline.pipeline_version = 3;
    pipeline.input_encoding = WMI_COLOR_ENCODING_SRGB;
    pipeline.output_encoding = WMI_COLOR_ENCODING_SRGB;

    if (!require(wmi_color_processor_create(nullptr, &grade, nullptr) == WMI_INVALID_ARGUMENT,
                 "invalid create arguments were accepted")) return 1;

    wmi_color_processor processor = nullptr;
    if (!require(wmi_color_processor_create(&pipeline, &grade, &processor) == WMI_OK,
                 "processor creation failed")) return 1;

    std::array<uint8_t, 8> pixels{32, 96, 180, 73, 220, 120, 12, 201};
    const auto original = pixels;
    wmi_color_image_desc image{};
    image.struct_size = sizeof(image);
    image.pixels = pixels.data();
    image.width = 2;
    image.height = 1;
    image.row_bytes = 8;
    image.pixel_format = WMI_COLOR_PIXEL_RGBA8;
    if (!require(wmi_color_processor_apply(processor, &image, nullptr) == WMI_OK,
                 "identity apply failed")) return 1;

    for (size_t index = 0; index < pixels.size(); ++index) {
        const int tolerance = index % 4 == 3 ? 0 : 2;
        if (!require(near_byte(pixels[index], original[index], tolerance),
                     "identity processor changed pixels")) return 1;
    }

    std::array<uint8_t, 4> cancelled_pixel{41, 82, 123, 177};
    const auto cancelled_original = cancelled_pixel;
    image.pixels = cancelled_pixel.data();
    image.width = 1;
    image.row_bytes = 4;
    wmi_callbacks cancelled_callbacks{nullptr, always_cancelled, nullptr};
    if (!require(wmi_color_processor_apply(processor, &image, &cancelled_callbacks) == WMI_CANCELLED,
                 "pre-cancelled apply did not stop")) return 1;
    if (!require(cancelled_pixel == cancelled_original,
                 "pre-cancelled apply changed pixels")) return 1;

    std::array<uint8_t, 4> bgra{180, 96, 32, 149};
    const auto original_bgra = bgra;
    image.pixels = bgra.data();
    image.pixel_format = WMI_COLOR_PIXEL_BGRA8;
    if (!require(wmi_color_processor_apply(processor, &image, nullptr) == WMI_OK,
                 "BGRA8 identity apply failed")) return 1;
    for (size_t index = 0; index < bgra.size(); ++index) {
        const int tolerance = index == 3 ? 0 : 2;
        if (!require(near_byte(bgra[index], original_bgra[index], tolerance),
                     "BGRA8 identity changed pixels")) return 1;
    }

    std::array<uint16_t, 3> rgb16{4096, 32768, 61440};
    const auto original_rgb16 = rgb16;
    image.pixels = rgb16.data();
    image.row_bytes = 3 * static_cast<int32_t>(sizeof(uint16_t));
    image.pixel_format = WMI_COLOR_PIXEL_RGB16;
    if (!require(wmi_color_processor_apply(processor, &image, nullptr) == WMI_OK,
                 "RGB16 identity apply failed")) return 1;
    for (size_t index = 0; index < rgb16.size(); ++index)
        if (!require(near_word(rgb16[index], original_rgb16[index]),
                     "RGB16 identity changed pixels")) return 1;

    std::array<uint16_t, 4> rgba16{1024, 24576, 64512, 42421};
    const auto original_rgba16 = rgba16;
    image.pixels = rgba16.data();
    image.row_bytes = 4 * static_cast<int32_t>(sizeof(uint16_t));
    image.pixel_format = WMI_COLOR_PIXEL_RGBA16;
    if (!require(wmi_color_processor_apply(processor, &image, nullptr) == WMI_OK,
                 "RGBA16 identity apply failed")) return 1;
    for (size_t index = 0; index < rgba16.size(); ++index) {
        const int tolerance = index == 3 ? 0 : 514;
        if (!require(near_word(rgba16[index], original_rgba16[index], tolerance),
                     "RGBA16 identity changed pixels")) return 1;
    }

    std::array<float, 4> rgba_float{0.08f, 0.42f, 0.91f, 0.37f};
    const auto original_rgba_float = rgba_float;
    image.pixels = rgba_float.data();
    image.row_bytes = 4 * static_cast<int32_t>(sizeof(float));
    image.pixel_format = WMI_COLOR_PIXEL_RGBA_F32;
    if (!require(wmi_color_processor_apply(processor, &image, nullptr) == WMI_OK,
                 "RGBA float identity apply failed")) return 1;
    for (size_t index = 0; index < rgba_float.size(); ++index) {
        // The identity topology still performs the required sRGB -> linear ->
        // sRGB round trip.  Match the production CPU/GPU acceptance threshold
        // (2 code values at 8-bit precision) while keeping alpha bit-stable.
        const float tolerance = index == 3 ? 0.000001f : 2.f / 255.f;
        if (!require(std::abs(rgba_float[index] - original_rgba_float[index]) <= tolerance,
                     "RGBA float identity changed pixels")) return 1;
    }

    grade.exposure = 1.f;
    if (!require(wmi_color_processor_update(processor, &grade) == WMI_OK,
                 "dynamic update failed")) return 1;
    pixels = {80, 80, 80, 19, 80, 80, 80, 231};
    image.pixels = pixels.data();
    image.width = 2;
    image.row_bytes = 8;
    image.pixel_format = WMI_COLOR_PIXEL_RGBA8;
    if (!require(wmi_color_processor_apply(processor, &image, nullptr) == WMI_OK,
                 "exposure apply failed")) return 1;
    if (!require(pixels[0] > 100 && pixels[1] > 100 && pixels[2] > 100,
                 "positive exposure did not brighten the image")) return 1;
    if (!require(pixels[3] == 19 && pixels[7] == 231, "alpha was not passed through")) return 1;

    for (const float endpoint : {-100.f, 100.f}) {
        auto endpoint_grade = identity_grade(identity_curve.data());
        endpoint_grade.shadows = endpoint;
        endpoint_grade.highlights = endpoint;
        endpoint_grade.blacks = endpoint;
        endpoint_grade.whites = endpoint;
        if (!require(wmi_color_processor_update(processor, &endpoint_grade) == WMI_OK,
                     "tone endpoint update failed")) return 1;
        std::array<uint8_t, 8> endpoint_pixels{16, 32, 64, 211, 192, 208, 224, 73};
        image.pixels = endpoint_pixels.data();
        image.width = 2;
        image.row_bytes = 8;
        if (!require(wmi_color_processor_apply(processor, &image, nullptr) == WMI_OK,
                     "tone endpoint apply failed")) return 1;
        if (!require(endpoint_pixels[3] == 211 && endpoint_pixels[7] == 73,
                     "tone endpoint changed alpha")) return 1;
    }

    const std::array<float, 8> descending_curve{
        0.f, 0.f, 0.25f, 0.5576f, 0.5f, 0.5f, 1.f, 1.f};
    auto descending_grade = identity_grade(identity_curve.data());
    descending_grade.master_curve = {descending_curve.data(), 4};
    if (!require(wmi_color_processor_update(processor, &descending_grade) == WMI_OK,
                 "descending curve was not normalized")) return 1;

    auto darker_grade = identity_grade(identity_curve.data());
    darker_grade.exposure = -1.f;
    wmi_color_processor second_processor = nullptr;
    if (!require(wmi_color_processor_create(&pipeline, &darker_grade, &second_processor) == WMI_OK,
                 "second processor creation failed")) return 1;
    std::array<uint8_t, 4> darker_pixel{128, 128, 128, 99};
    wmi_color_image_desc second_image{};
    second_image.struct_size = sizeof(second_image);
    second_image.pixels = darker_pixel.data();
    second_image.width = 1;
    second_image.height = 1;
    second_image.row_bytes = 4;
    second_image.pixel_format = WMI_COLOR_PIXEL_RGBA8;
    if (!require(wmi_color_processor_apply(second_processor, &second_image, nullptr) == WMI_OK,
                 "second processor apply failed")) return 1;
    if (!require(darker_pixel[0] < 128 && darker_pixel[3] == 99,
                 "independent processor state was not respected")) return 1;
    wmi_color_processor_destroy(second_processor);

    wmi_color_gpu_snapshot snapshot = nullptr;
    if (!require(wmi_color_gpu_snapshot_create(processor, &snapshot) == WMI_OK,
                 "GPU snapshot creation failed")) return 1;
    int32_t program_length = 0;
    if (!require(wmi_color_gpu_snapshot_get_program(snapshot, nullptr, 0, &program_length) == WMI_OK
                 && program_length > 1, "GPU program length query failed")) return 1;
    std::vector<char> program(static_cast<size_t>(program_length));
    if (!require(wmi_color_gpu_snapshot_get_program(snapshot, program.data(), program_length,
                                                      &program_length) == WMI_OK,
                 "GPU program query failed")) return 1;
    const std::string shader(program.data());
    if (!require(shader.find("wm_ocio_pre") != std::string::npos
                 && shader.find("wm_ocio_grade") != std::string::npos,
                 "GPU program is missing OCIO stages")) return 1;
    if (!require(shader.find("uniform float wm_grade_grading_huecurve_knots[") == std::string::npos
                 && shader.find("uniform float wm_grade_grading_rgbcurve_coefs[") == std::string::npos
                 && shader.find("texelFetch(wm_dynamic_") != std::string::npos,
                 "dynamic curve arrays were not promoted to textures")) return 1;
    if (!require(wmi_color_gpu_snapshot_get_texture_count(snapshot) >= 4,
                 "dynamic curve textures are missing")) return 1;
    for (int32_t index = 0; index < wmi_color_gpu_snapshot_get_texture_count(snapshot); ++index) {
        wmi_color_gpu_texture_info info{};
        if (!require(wmi_color_gpu_snapshot_get_texture(snapshot, index, &info, nullptr, 0) == WMI_OK
                     && info.width > 0 && info.element_count > 0,
                     "GPU texture metadata is invalid")) return 1;
    }
    std::cout << "OCIO snapshot: "
              << wmi_color_gpu_snapshot_get_uniform_count(snapshot) << " uniforms, "
              << wmi_color_gpu_snapshot_get_texture_count(snapshot) << " textures\n";

    wmi_color_gpu_snapshot_destroy(snapshot);
    wmi_color_processor_destroy(processor);
    return 0;
}
