#include "watermark_imaging.h"

#include <algorithm>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <vector>

namespace {
constexpr int width = 420;
constexpr int height = 280;

void add_star(std::vector<uint16_t>& image, double x, double y, double strength)
{
    for (int iy = static_cast<int>(std::floor(y)) - 3; iy <= static_cast<int>(std::floor(y)) + 3; ++iy)
    for (int ix = static_cast<int>(std::floor(x)) - 3; ix <= static_cast<int>(std::floor(x)) + 3; ++ix) {
        if (ix < 0 || iy < 0 || ix >= width || iy >= height) continue;
        const double dx = ix - x;
        const double dy = iy - y;
        const auto value = static_cast<uint16_t>(std::clamp(
            400.0 + strength * std::exp(-(dx * dx + dy * dy) / 1.5), 0.0, 65535.0));
        auto& pixel = image[static_cast<size_t>(iy) * width + ix];
        pixel = std::max(pixel, value);
    }
}
}

int main()
{
    std::vector<uint16_t> reference(static_cast<size_t>(width) * height, 400);
    std::vector<uint16_t> candidate(static_cast<size_t>(width) * height, 400);
    const std::vector<std::pair<double, double>> stars = {
        {31.4, 28.7}, {74.8, 45.3}, {119.1, 33.9}, {166.5, 61.2}, {213.6, 39.5},
        {267.2, 70.8}, {321.7, 44.1}, {378.3, 83.4}, {52.6, 104.7}, {101.2, 127.3},
        {151.8, 98.4}, {198.4, 139.8}, {246.9, 112.2}, {296.3, 146.7}, {352.1, 119.6},
        {40.5, 190.1}, {87.9, 164.6}, {139.3, 211.8}, {188.7, 181.5}, {235.1, 224.4},
        {284.8, 194.2}, {337.6, 231.3}, {385.2, 177.9}, {112.4, 252.1}, {259.5, 257.2}
    };
    constexpr double translation_x = 3.25;
    constexpr double translation_y = -2.5;
    for (size_t index = 0; index < stars.size(); ++index) {
        const double strength = 26000.0 + static_cast<double>(index % 7) * 4200.0;
        add_star(reference, stars[index].first, stars[index].second, strength);
        add_star(candidate, stars[index].first - translation_x, stars[index].second - translation_y, strength);
    }

    wmi_transform transform{};
    std::vector<wmi_star_feature> reference_features(96), candidate_features(96);
    int32_t reference_count = 0, candidate_count = 0;
    auto status = wmi_detect_stars_gray16(reference.data(), width, height,
        reference_features.data(), static_cast<int32_t>(reference_features.size()), &reference_count, nullptr);
    if (status != WMI_OK) return 3;
    status = wmi_detect_stars_gray16(candidate.data(), width, height,
        candidate_features.data(), static_cast<int32_t>(candidate_features.size()), &candidate_count, nullptr);
    if (status != WMI_OK) return 4;
    status = wmi_align_star_features(reference_features.data(), reference_count,
        candidate_features.data(), candidate_count, width, height, &transform, nullptr);
    std::printf("status=%d matches=%d rms=%.4f tx=%.4f ty=%.4f\n",
        static_cast<int>(status), transform.match_count, transform.rms_error, transform.tx, transform.ty);
    if (status != WMI_OK || transform.match_count < 12 || transform.rms_error > 0.75) return 1;
    if (std::abs(transform.tx - translation_x) > 0.35 || std::abs(transform.ty - translation_y) > 0.35) return 2;
    return 0;
}
