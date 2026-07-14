#include "watermark_imaging.h"

#include <algorithm>
#include <cstdint>
#include <iostream>
#include <vector>

namespace {

bool near(uint16_t actual, uint16_t expected, uint16_t tolerance = 1) {
    return actual >= expected - std::min(expected, tolerance)
        && actual <= expected + tolerance;
}

} // namespace

int main() {
    constexpr int width = 12;
    constexpr int height = 10;
    constexpr int channels = 3;
    constexpr int pixels = width * height;

    std::vector<uint16_t> source(static_cast<size_t>(pixels) * channels);
    for (int y = 0; y < height; ++y) {
        for (int x = 0; x < width; ++x) {
            const uint16_t value = static_cast<uint16_t>(1000 + y * 100 + x * 10);
            for (int channel = 0; channel < channels; ++channel)
                source[(static_cast<size_t>(y) * width + x) * channels + channel] = value + channel;
        }
    }

    wmi_transform identity{};
    identity.m11 = 1.0;
    identity.m22 = 1.0;

    std::vector<uint16_t> warped(source.size(), 0);
    std::vector<uint8_t> mask(pixels, 0);
    auto status = wmi_warp_preview_rgb16(source.data(), width, height, channels,
                                         &identity, warped.data(), mask.data(), nullptr);
    if (status != WMI_OK) return 1;
    const int interior = 4 * width + 5;
    if (mask[interior] == 0 || !near(warped[interior * channels], source[interior * channels])) return 2;
    if (mask[0] != 0) return 3;

    std::fill(warped.begin(), warped.end(), 0);
    std::fill(mask.begin(), mask.end(), 0);
    status = wmi_warp_lanczos3_tile_rgb16(source.data(), 0, height, width, height, channels,
                                          0, height, &identity, warped.data(), mask.data(), nullptr);
    if (status != WMI_OK || mask[interior] == 0
        || !near(warped[interior * channels], source[interior * channels])) return 4;

    constexpr int frame_count = 5;
    constexpr int stack_pixels = 2;
    std::vector<uint16_t> frames(static_cast<size_t>(frame_count) * stack_pixels * channels);
    const uint16_t frame_values[frame_count] = { 1000, 1010, 990, 1020, 60000 };
    for (int frame = 0; frame < frame_count; ++frame) {
        for (int pixel = 0; pixel < stack_pixels; ++pixel) {
            for (int channel = 0; channel < channels; ++channel) {
                frames[(static_cast<size_t>(frame) * stack_pixels + pixel) * channels + channel]
                    = static_cast<uint16_t>(frame_values[frame] + pixel * 100 + channel);
            }
        }
    }
    std::vector<uint8_t> frame_masks(static_cast<size_t>(frame_count) * stack_pixels, 255);
    std::vector<uint16_t> stacked(static_cast<size_t>(stack_pixels) * channels, 0);
    std::vector<uint8_t> stacked_mask(stack_pixels, 0);
    status = wmi_stack_preview_tile_rgb16(frames.data(), frame_masks.data(), nullptr,
                                          frame_count, stack_pixels, channels, 3,
                                          1.5, 1.5, 2, 2, stacked.data(), stacked_mask.data(), nullptr);
    if (status != WMI_OK || stacked_mask[0] == 0 || stacked[0] > 1030) return 5;

    status = wmi_stack_preview_tile_rgb16(frames.data(), frame_masks.data(), nullptr,
                                          frame_count, stack_pixels, channels, 0,
                                          3.0, 3.0, 2, 2, stacked.data(), stacked_mask.data(), nullptr);
    if (status != WMI_OK || stacked[0] != 60000) return 6;

    std::cout << "preview pipeline kernels passed\n";
    return 0;
}
