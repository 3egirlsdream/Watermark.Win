#include "watermark_imaging.h"
#include "watermark_imaging_internal.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <exception>
#include <new>
#include <numeric>
#include <random>
#include <sstream>
#include <string>
#include <thread>
#include <unordered_map>
#include <vector>

#if defined(WMI_HAS_LIBRAW)
#include <libraw/libraw.h>
#endif
#if defined(WMI_HAS_TIFF)
#include <tiffio.h>
#endif

namespace {
thread_local std::string last_error;

void set_error(const char* message) { last_error = message == nullptr ? "" : message; }
bool cancelled(const wmi_callbacks* callbacks) {
    return callbacks != nullptr && callbacks->is_cancelled != nullptr
        && callbacks->is_cancelled(callbacks->user_data) != 0;
}
void progress(const wmi_callbacks* callbacks, int stage, double value) {
    if (callbacks != nullptr && callbacks->progress != nullptr)
        callbacks->progress(callbacks->user_data, stage, value);
}
template <size_t Size> void copy_text(char (&destination)[Size], const char* source) {
    std::memset(destination, 0, Size);
    if (source != nullptr) std::strncpy(destination, source, Size - 1);
}

struct star_point { double x, y, strength; };
struct similarity_transform { double a = 1, b = 0, tx = 0, ty = 0; };

std::vector<star_point> detect_stars(const uint16_t* pixels, int width, int height) {
    struct response_point { int x, y; double value; };
    std::vector<double> responses(static_cast<size_t>(width) * height, 0.0);
    std::vector<double> sample;
    sample.reserve(static_cast<size_t>(width / 4 + 1) * (height / 4 + 1));
    for (int y = 2; y + 2 < height; ++y) for (int x = 2; x + 2 < width; ++x) {
        const size_t offset = static_cast<size_t>(y) * width + x;
        const double center = pixels[offset];
        const double ring = pixels[offset - width] + pixels[offset + width]
            + pixels[offset - 1] + pixels[offset + 1]
            + 0.5 * (pixels[offset - width - 1] + pixels[offset - width + 1]
                + pixels[offset + width - 1] + pixels[offset + width + 1]);
        responses[offset] = std::max(0.0, center * 6.0 - ring);
        if ((x & 3) == 0 && (y & 3) == 0) sample.push_back(responses[offset]);
    }
    if (sample.empty()) return {};
    auto middle = sample.begin() + static_cast<std::ptrdiff_t>(sample.size() / 2);
    std::nth_element(sample.begin(), middle, sample.end());
    const double median = *middle;
    for (double& value : sample) value = std::abs(value - median);
    std::nth_element(sample.begin(), middle, sample.end());
    const double sigma = std::max(1.0, *middle * 1.4826);
    const double threshold = median + 5.0 * sigma;
    std::vector<response_point> candidates;
    for (int y = 2; y + 2 < height; ++y) for (int x = 2; x + 2 < width; ++x) {
        const double value = responses[static_cast<size_t>(y) * width + x];
        if (value < threshold) continue;
        bool maximum = true;
        for (int dy = -1; dy <= 1 && maximum; ++dy) for (int dx = -1; dx <= 1; ++dx)
            if ((dx != 0 || dy != 0) && responses[static_cast<size_t>(y + dy) * width + x + dx] > value)
                { maximum = false; break; }
        if (maximum) candidates.push_back({x, y, value});
    }
    std::sort(candidates.begin(), candidates.end(), [](const auto& left, const auto& right) { return left.value > right.value; });
    std::vector<star_point> stars;
    constexpr int grid_columns = 8, grid_rows = 6, per_cell = 2;
    int grid_counts[grid_columns * grid_rows]{};
    for (const auto& candidate : candidates) {
        const int grid_x = std::clamp(candidate.x * grid_columns / std::max(1, width), 0, grid_columns - 1);
        const int grid_y = std::clamp(candidate.y * grid_rows / std::max(1, height), 0, grid_rows - 1);
        const int grid_index = grid_y * grid_columns + grid_x;
        if (grid_counts[grid_index] >= per_cell) continue;
        bool separated = true;
        for (const auto& star : stars) {
            const double dx = star.x - candidate.x, dy = star.y - candidate.y;
            if (dx * dx + dy * dy < 25.0) { separated = false; break; }
        }
        if (!separated) continue;
        double background = 0, weight = 0, centroid_x = 0, centroid_y = 0;
        for (int dy = -2; dy <= 2; ++dy) for (int dx = -2; dx <= 2; ++dx)
            if (std::abs(dx) == 2 || std::abs(dy) == 2)
                background += pixels[static_cast<size_t>(candidate.y + dy) * width + candidate.x + dx];
        background /= 16.0;
        for (int dy = -2; dy <= 2; ++dy) for (int dx = -2; dx <= 2; ++dx) {
            const double value = std::max(0.0, pixels[static_cast<size_t>(candidate.y + dy) * width + candidate.x + dx] - background);
            weight += value; centroid_x += (candidate.x + dx) * value; centroid_y += (candidate.y + dy) * value;
        }
        if (weight > 0) {
            stars.push_back({centroid_x / weight, centroid_y / weight, candidate.value});
            ++grid_counts[grid_index];
        }
        if (stars.size() == grid_columns * grid_rows * per_cell) break;
    }
    return stars;
}

uint32_t triangle_key(double first, double second) {
    const auto a = static_cast<uint32_t>(std::clamp(std::lround(first * 100.0), 0L, 100L));
    const auto b = static_cast<uint32_t>(std::clamp(std::lround(second * 100.0), 0L, 100L));
    return (a << 16) | b;
}

struct star_triangle { uint32_t key; int p0, p1, p2; };
std::vector<star_triangle> build_triangles(const std::vector<star_point>& points, size_t limit) {
    const int count = static_cast<int>(std::min(points.size(), limit));
    std::vector<star_triangle> result;
    for (int i = 0; i < count; ++i) {
        std::vector<std::pair<double, int>> neighbours;
        neighbours.reserve(static_cast<size_t>(count - 1));
        for (int other = 0; other < count; ++other) if (other != i) {
            const double dx = points[i].x - points[other].x;
            const double dy = points[i].y - points[other].y;
            neighbours.push_back({dx * dx + dy * dy, other});
        }
        const size_t neighbour_count = std::min<size_t>(8, neighbours.size());
        std::partial_sort(neighbours.begin(), neighbours.begin() + static_cast<std::ptrdiff_t>(neighbour_count),
                          neighbours.end());
        for (size_t first = 0; first < neighbour_count; ++first)
        for (size_t second = first + 1; second < neighbour_count; ++second) {
        const int j = neighbours[first].second;
        const int k = neighbours[second].second;
        struct vertex_side { int vertex; double opposite; } sides[3] = {
            {i, std::hypot(points[j].x - points[k].x, points[j].y - points[k].y)},
            {j, std::hypot(points[i].x - points[k].x, points[i].y - points[k].y)},
            {k, std::hypot(points[i].x - points[j].x, points[i].y - points[j].y)} };
        std::sort(std::begin(sides), std::end(sides), [](const auto& l, const auto& r) { return l.opposite < r.opposite; });
        if (sides[2].opposite < 12.0 || sides[0].opposite / sides[2].opposite < 0.12) continue;
        result.push_back({triangle_key(sides[0].opposite / sides[2].opposite,
            sides[1].opposite / sides[2].opposite), sides[0].vertex, sides[1].vertex, sides[2].vertex});
        }
    }
    return result;
}

similarity_transform estimate_pair(const star_point& c0, const star_point& c1,
                                   const star_point& r0, const star_point& r1) {
    const double cx = c1.x - c0.x, cy = c1.y - c0.y;
    const double rx = r1.x - r0.x, ry = r1.y - r0.y;
    const double denominator = cx * cx + cy * cy;
    if (denominator < 1e-9) return {};
    const double a = (cx * rx + cy * ry) / denominator;
    const double b = (cx * ry - cy * rx) / denominator;
    return {a, b, r0.x - a * c0.x + b * c0.y, r0.y - b * c0.x - a * c0.y};
}

star_point apply(const similarity_transform& transform, const star_point& point) {
    return {transform.a * point.x - transform.b * point.y + transform.tx,
        transform.b * point.x + transform.a * point.y + transform.ty, point.strength};
}

int evaluate(const similarity_transform& transform, const std::vector<star_point>& candidate,
             const std::vector<star_point>& reference, double max_distance,
             std::vector<std::pair<int, int>>* matches, double* rms) {
    std::vector<double> best_for_reference(reference.size(), max_distance * max_distance);
    std::vector<int> candidate_for_reference(reference.size(), -1);
    for (size_t ci = 0; ci < candidate.size(); ++ci) {
        const auto transformed = apply(transform, candidate[ci]);
        double best = max_distance * max_distance; int best_index = -1;
        for (size_t ri = 0; ri < reference.size(); ++ri) {
            const double dx = transformed.x - reference[ri].x, dy = transformed.y - reference[ri].y;
            const double distance = dx * dx + dy * dy;
            if (distance < best) { best = distance; best_index = static_cast<int>(ri); }
        }
        if (best_index >= 0 && best < best_for_reference[best_index]) {
            best_for_reference[best_index] = best; candidate_for_reference[best_index] = static_cast<int>(ci);
        }
    }
    int count = 0; double squared = 0;
    if (matches != nullptr) matches->clear();
    for (size_t ri = 0; ri < reference.size(); ++ri) if (candidate_for_reference[ri] >= 0) {
        ++count; squared += best_for_reference[ri];
        if (matches != nullptr) matches->push_back({candidate_for_reference[ri], static_cast<int>(ri)});
    }
    if (rms != nullptr) *rms = count == 0 ? 1e9 : std::sqrt(squared / count);
    return count;
}

similarity_transform refine(const std::vector<star_point>& candidate, const std::vector<star_point>& reference,
                            const std::vector<std::pair<int, int>>& matches) {
    if (matches.size() < 2) return {};
    double cx = 0, cy = 0, rx = 0, ry = 0;
    for (const auto& match : matches) { cx += candidate[match.first].x; cy += candidate[match.first].y; rx += reference[match.second].x; ry += reference[match.second].y; }
    const double count = static_cast<double>(matches.size()); cx /= count; cy /= count; rx /= count; ry /= count;
    double numerator_a = 0, numerator_b = 0, denominator = 0;
    for (const auto& match : matches) {
        const double x = candidate[match.first].x - cx, y = candidate[match.first].y - cy;
        const double u = reference[match.second].x - rx, v = reference[match.second].y - ry;
        numerator_a += x * u + y * v; numerator_b += x * v - y * u; denominator += x * x + y * y;
    }
    if (denominator < 1e-9) return {};
    const double a = numerator_a / denominator, b = numerator_b / denominator;
    return {a, b, rx - a * cx + b * cy, ry - b * cx - a * cy};
}
#if defined(WMI_HAS_TIFF)
struct tiff_writer_state {
    TIFF* file;
    std::string path;
    int32_t width;
    int32_t height;
    int32_t channels;
    int32_t next_row;
};
#endif
}

void wmi_set_last_error_internal(const char* message) { set_error(message); }

uint32_t wmi_get_abi_version(void) { return WMI_ABI_VERSION; }

uint32_t wmi_get_capabilities(void) {
    uint32_t result = 0;
#if defined(WMI_HAS_LIBRAW)
    result |= WMI_CAP_RAW;
#endif
    result |= WMI_CAP_STAR_ALIGNMENT | WMI_CAP_WARP | WMI_CAP_PREVIEW_PIPELINE;
#if defined(WMI_HAS_TIFF)
    result |= WMI_CAP_TIFF16 | WMI_CAP_BIGTIFF;
#endif
#if defined(WMI_HAS_OCIO)
    result |= WMI_CAP_COLOR_OCIO;
#endif
    return result;
}

const char* wmi_get_backend_version(void) {
    static const std::string version = [] {
        std::ostringstream value;
        value << "Watermark.Imaging.Native/1.0 ABI/" << WMI_ABI_VERSION;
#if defined(WMI_HAS_LIBRAW)
        value << " LibRaw/" << LIBRAW_VERSION_STR;
#endif
        value << " StarAlign/2 Preview/1";
#if defined(__ARM_NEON) || defined(__ARM_NEON__)
        value << " SIMD/NEON";
#elif defined(__AVX2__)
        value << " SIMD/AVX2";
#elif defined(__SSE4_1__)
        value << " SIMD/SSE4.1";
#else
        value << " SIMD/scalar";
#endif
#if defined(WMI_HAS_TIFF)
        value << " LibTIFF/4.7.2";
#endif
#if defined(WMI_HAS_OCIO)
        value << " OpenColorIO/2.5.2";
#endif
        return value.str();
    }();
    return version.c_str();
}

const char* wmi_get_last_error(void) { return last_error.c_str(); }

wmi_status wmi_probe_raw(const char* path, wmi_raw_info* info) {
    if (path == nullptr || info == nullptr) return WMI_INVALID_ARGUMENT;
    std::memset(info, 0, sizeof(*info));
#if defined(WMI_HAS_LIBRAW)
    try {
        LibRaw processor;
        const int status = processor.open_file(path);
        if (status != LIBRAW_SUCCESS) {
            set_error(libraw_strerror(status));
            return status == LIBRAW_FILE_UNSUPPORTED ? WMI_UNSUPPORTED : WMI_CORRUPTED_INPUT;
        }
        info->width = static_cast<int32_t>(processor.imgdata.sizes.iwidth);
        info->height = static_cast<int32_t>(processor.imgdata.sizes.iheight);
        if (processor.imgdata.sizes.flip & 4) std::swap(info->width, info->height);
        // dcraw_make_mem_image applies the camera flip, so the managed WM16 must
        // be tagged Top-Left and must never apply EXIF orientation a second time.
        info->orientation = 1;
        info->raw_count = processor.imgdata.idata.raw_count;
        copy_text(info->make, processor.imgdata.idata.make);
        copy_text(info->model, processor.imgdata.idata.model);
        return WMI_OK;
    } catch (const std::bad_alloc&) { return WMI_OUT_OF_MEMORY; }
      catch (const std::exception& error) { set_error(error.what()); return WMI_INTERNAL_ERROR; }
#else
    set_error("LibRaw backend is not compiled into this binary.");
    return WMI_UNSUPPORTED;
#endif
}

wmi_status wmi_decode_raw_rgb16(const char* path, const wmi_decode_options* options,
                                wmi_tile_callback output, const wmi_callbacks* callbacks) {
    if (path == nullptr || options == nullptr || output == nullptr || options->tile_height <= 0)
        return WMI_INVALID_ARGUMENT;
#if defined(WMI_HAS_LIBRAW)
    try {
        LibRaw processor;
        processor.imgdata.params.output_bps = 16;
        processor.imgdata.params.gamm[0] = 1.0;
        processor.imgdata.params.gamm[1] = 1.0;
        processor.imgdata.params.use_camera_wb = options->use_camera_white_balance != 0;
        processor.imgdata.params.no_auto_bright = 1;
        processor.imgdata.params.output_color = 1;
        processor.imgdata.params.user_qual = options->demosaic_quality;
        processor.imgdata.params.fbdd_noiserd = 0;
        processor.imgdata.params.med_passes = 0;
        processor.imgdata.params.bright = 1.0f / std::max(1.0f, options->highlight_headroom);
        if (options->apply_orientation == 0) processor.imgdata.params.user_flip = 0;
        int status = processor.open_file(path);
        if (status == LIBRAW_SUCCESS && options->max_edge > 0) {
            const int source_max_edge = std::max(processor.imgdata.sizes.iwidth, processor.imgdata.sizes.iheight);
            processor.imgdata.params.half_size = source_max_edge > options->max_edge * 2 ? 1 : 0;
        }
        if (status == LIBRAW_SUCCESS) status = processor.unpack();
        if (status == LIBRAW_SUCCESS) status = processor.dcraw_process();
        if (status != LIBRAW_SUCCESS) { set_error(libraw_strerror(status)); return WMI_CORRUPTED_INPUT; }
        int error = LIBRAW_SUCCESS;
        libraw_processed_image_t* image = processor.dcraw_make_mem_image(&error);
        if (image == nullptr || error != LIBRAW_SUCCESS || image->bits != 16 || image->colors < 3) {
            if (image != nullptr) LibRaw::dcraw_clear_mem(image);
            set_error("LibRaw did not return a 16-bit RGB image."); return WMI_INTERNAL_ERROR;
        }
        const int source_width = static_cast<int>(image->width);
        const int source_height = static_cast<int>(image->height);
        const double preview_scale = options->max_edge > 0
            ? std::min(1.0, static_cast<double>(options->max_edge) / std::max(source_width, source_height))
            : 1.0;
        const int width = std::max(1, static_cast<int>(std::lround(source_width * preview_scale)));
        const int height = std::max(1, static_cast<int>(std::lround(source_height * preview_scale)));
        const int colors = static_cast<int>(image->colors);
        const auto* source = reinterpret_cast<const uint16_t*>(image->data);
        const int tile_height = std::max(1, options->tile_height);
        std::vector<uint16_t> rgba;
        for (int row = 0; row < height; row += tile_height) {
            if (cancelled(callbacks)) { LibRaw::dcraw_clear_mem(image); return WMI_CANCELLED; }
            const int rows = std::min(tile_height, height - row);
            rgba.resize(static_cast<size_t>(width) * rows * 4);
            for (int y = 0; y < rows; ++y) for (int x = 0; x < width; ++x) {
                const double source_y = ((row + y) + 0.5) * source_height / height - 0.5;
                const double source_x = (x + 0.5) * source_width / width - 0.5;
                const int sy = std::clamp(static_cast<int>(std::lround(source_y)), 0, source_height - 1);
                const int sx = std::clamp(static_cast<int>(std::lround(source_x)), 0, source_width - 1);
                const size_t src = (static_cast<size_t>(sy) * source_width + sx) * colors;
                const size_t dst = (static_cast<size_t>(y) * width + x) * 4;
                rgba[dst] = source[src]; rgba[dst + 1] = source[src + 1]; rgba[dst + 2] = source[src + 2];
                rgba[dst + 3] = UINT16_MAX;
            }
            status = output(callbacks == nullptr ? nullptr : callbacks->user_data,
                            row, rows, width, 4, rgba.data(), nullptr);
            if (status != WMI_OK) {
                LibRaw::dcraw_clear_mem(image);
                return static_cast<wmi_status>(status);
            }
            progress(callbacks, 1, static_cast<double>(row + rows) / height);
        }
        LibRaw::dcraw_clear_mem(image);
        return WMI_OK;
    } catch (const std::bad_alloc&) { return WMI_OUT_OF_MEMORY; }
      catch (const std::exception& error) { set_error(error.what()); return WMI_INTERNAL_ERROR; }
#else
    (void)callbacks; set_error("LibRaw backend is not compiled into this binary."); return WMI_UNSUPPORTED;
#endif
}

wmi_status wmi_detect_stars_gray16(const uint16_t* pixels, int32_t width, int32_t height,
                                   wmi_star_feature* features, int32_t capacity,
                                   int32_t* feature_count, const wmi_callbacks* callbacks) {
    if (pixels == nullptr || features == nullptr || feature_count == nullptr || width <= 0 || height <= 0 || capacity <= 0)
        return WMI_INVALID_ARGUMENT;
    if (cancelled(callbacks)) return WMI_CANCELLED;
    try {
        const auto stars = detect_stars(pixels, width, height);
        *feature_count = std::min<int32_t>(capacity, static_cast<int32_t>(stars.size()));
        for (int32_t index = 0; index < *feature_count; ++index)
            features[index] = {stars[index].x, stars[index].y, stars[index].strength};
        return *feature_count >= 12 ? WMI_OK : WMI_ALIGNMENT_FAILED;
    } catch (const std::bad_alloc&) { return WMI_OUT_OF_MEMORY; }
      catch (const std::exception& error) { set_error(error.what()); return WMI_ALIGNMENT_FAILED; }
}

wmi_status wmi_align_star_features(const wmi_star_feature* reference, int32_t reference_count,
                                   const wmi_star_feature* candidate, int32_t candidate_count,
                                   int32_t width, int32_t height, wmi_transform* transform,
                                   const wmi_callbacks* callbacks) {
    if (reference == nullptr || candidate == nullptr || transform == nullptr || width <= 0 || height <= 0
        || reference_count <= 0 || candidate_count <= 0)
        return WMI_INVALID_ARGUMENT;
    if (cancelled(callbacks)) return WMI_CANCELLED;
    try {
        std::vector<star_point> reference_stars, candidate_stars;
        reference_stars.reserve(static_cast<size_t>(reference_count));
        candidate_stars.reserve(static_cast<size_t>(candidate_count));
        for (int32_t index = 0; index < reference_count; ++index)
            reference_stars.push_back({reference[index].x, reference[index].y, reference[index].strength});
        for (int32_t index = 0; index < candidate_count; ++index)
            candidate_stars.push_back({candidate[index].x, candidate[index].y, candidate[index].strength});
        if (reference_stars.size() < 12 || candidate_stars.size() < 12) {
            set_error("Fewer than 12 usable stars were detected."); return WMI_ALIGNMENT_FAILED;
        }
        const auto reference_triangles = build_triangles(reference_stars, 48);
        const auto candidate_triangles = build_triangles(candidate_stars, 48);
        std::unordered_multimap<uint32_t, star_triangle> reference_by_key;
        for (const auto& triangle : reference_triangles) reference_by_key.emplace(triangle.key, triangle);
        similarity_transform best; int best_count = 0; double best_rms = 1e9; int hypotheses = 0;
        for (const auto& candidate_triangle : candidate_triangles) {
            if (cancelled(callbacks)) return WMI_CANCELLED;
            const int key_a = static_cast<int>((candidate_triangle.key >> 16) & 0xffffu);
            const int key_b = static_cast<int>(candidate_triangle.key & 0xffffu);
            for (int delta_a = -1; delta_a <= 1 && hypotheses < 1500; ++delta_a)
            for (int delta_b = -1; delta_b <= 1 && hypotheses < 1500; ++delta_b) {
                const auto adjacent_key = (static_cast<uint32_t>(std::clamp(key_a + delta_a, 0, 100)) << 16)
                    | static_cast<uint32_t>(std::clamp(key_b + delta_b, 0, 100));
                const auto range = reference_by_key.equal_range(adjacent_key);
                for (auto iterator = range.first; iterator != range.second && hypotheses < 1500; ++iterator, ++hypotheses) {
                const auto& reference_triangle = iterator->second;
                auto estimate = estimate_pair(candidate_stars[candidate_triangle.p0], candidate_stars[candidate_triangle.p1],
                    reference_stars[reference_triangle.p0], reference_stars[reference_triangle.p1]);
                const auto third = apply(estimate, candidate_stars[candidate_triangle.p2]);
                if (std::hypot(third.x - reference_stars[reference_triangle.p2].x,
                    third.y - reference_stars[reference_triangle.p2].y) > 2.5) continue;
                double rms = 0;
                const int count = evaluate(estimate, candidate_stars, reference_stars, 2.5, nullptr, &rms);
                if (count > best_count || (count == best_count && rms < best_rms)) { best = estimate; best_count = count; best_rms = rms; }
                }
            }
            if (hypotheses >= 1500) break;
        }
        std::vector<std::pair<int, int>> matches;
        evaluate(best, candidate_stars, reference_stars, 2.5, &matches, nullptr);
        best = refine(candidate_stars, reference_stars, matches);
        best_count = evaluate(best, candidate_stars, reference_stars, 1.5, &matches, &best_rms);
        best = refine(candidate_stars, reference_stars, matches);
        best_count = evaluate(best, candidate_stars, reference_stars, 1.5, nullptr, &best_rms);
        transform->m11 = best.a; transform->m12 = -best.b;
        transform->m21 = best.b; transform->m22 = best.a;
        transform->tx = best.tx; transform->ty = best.ty;
        transform->score = static_cast<double>(best_count) / std::min(reference_stars.size(), candidate_stars.size());
        transform->rms_error = best_rms; transform->match_count = best_count;
        if (best_count < 12 || best_rms > 0.75) {
            set_error("Star transform did not meet the 12-inlier / 0.75px RMS threshold.");
            return WMI_ALIGNMENT_FAILED;
        }
        return WMI_OK;
    } catch (const std::bad_alloc&) { return WMI_OUT_OF_MEMORY; }
      catch (const std::exception& error) { set_error(error.what()); return WMI_ALIGNMENT_FAILED; }
}

wmi_status wmi_warp_preview_rgb16(const uint16_t* source, int32_t width, int32_t height,
                                  int32_t channels, const wmi_transform* transform,
                                  uint16_t* output, uint8_t* validity_mask,
                                  const wmi_callbacks* callbacks) {
    if (source == nullptr || transform == nullptr || output == nullptr || validity_mask == nullptr
        || width <= 0 || height <= 0 || (channels != 3 && channels != 4)) return WMI_INVALID_ARGUMENT;
    try {
        const double determinant = transform->m11 * transform->m22 - transform->m12 * transform->m21;
        if (std::abs(determinant) < 1e-12) return WMI_INVALID_ARGUMENT;
        auto cubic = [](double value) {
            value = std::abs(value);
            if (value <= 1.0) return (1.5 * value - 2.5) * value * value + 1.0;
            if (value < 2.0) return ((-0.5 * value + 2.5) * value - 4.0) * value + 2.0;
            return 0.0;
        };
        for (int y = 0; y < height; ++y) {
            if (cancelled(callbacks)) return WMI_CANCELLED;
            for (int x = 0; x < width; ++x) {
                const double translated_x = x - transform->tx;
                const double translated_y = y - transform->ty;
                const double source_x = (transform->m22 * translated_x - transform->m12 * translated_y) / determinant;
                const double source_y = (-transform->m21 * translated_x + transform->m11 * translated_y) / determinant;
                const size_t output_pixel = static_cast<size_t>(y) * width + x;
                if (source_x < 1.0 || source_y < 1.0 || source_x >= width - 2.0 || source_y >= height - 2.0) {
                    validity_mask[output_pixel] = 0;
                    continue;
                }
                const int center_x = static_cast<int>(std::floor(source_x));
                const int center_y = static_cast<int>(std::floor(source_y));
                double weights_x[4], weights_y[4];
                for (int index = 0; index < 4; ++index) {
                    weights_x[index] = cubic(source_x - (center_x - 1 + index));
                    weights_y[index] = cubic(source_y - (center_y - 1 + index));
                }
                for (int channel = 0; channel < channels; ++channel) {
                    double weighted = 0.0, weight_total = 0.0;
                    uint16_t local_minimum = UINT16_MAX, local_maximum = 0;
                    for (int sample_y = center_y - 1; sample_y <= center_y + 2; ++sample_y) {
                        const double weight_y = weights_y[sample_y - center_y + 1];
                        for (int sample_x = center_x - 1; sample_x <= center_x + 2; ++sample_x) {
                            const double weight = weights_x[sample_x - center_x + 1] * weight_y;
                            const uint16_t value = source[(static_cast<size_t>(sample_y) * width + sample_x) * channels + channel];
                            weighted += value * weight;
                            weight_total += weight;
                            local_minimum = std::min(local_minimum, value);
                            local_maximum = std::max(local_maximum, value);
                        }
                    }
                    const double value = std::clamp(weighted / std::max(1e-12, weight_total),
                                                    static_cast<double>(local_minimum), static_cast<double>(local_maximum));
                    output[output_pixel * channels + channel] = static_cast<uint16_t>(std::clamp(std::lround(value), 0L, 65535L));
                }
                validity_mask[output_pixel] = 255;
            }
            progress(callbacks, 3, static_cast<double>(y + 1) / height);
        }
        return WMI_OK;
    } catch (const std::bad_alloc&) { return WMI_OUT_OF_MEMORY; }
      catch (const std::exception& error) { set_error(error.what()); return WMI_INTERNAL_ERROR; }
}

wmi_status wmi_warp_lanczos3_tile_rgb16(const uint16_t* source_rows,
                                         int32_t source_row_start, int32_t source_row_count,
                                         int32_t width, int32_t height, int32_t channels,
                                         int32_t output_row_start, int32_t output_row_count,
                                         const wmi_transform* transform, uint16_t* output,
                                         uint8_t* validity_mask, const wmi_callbacks* callbacks) {
    if (source_rows == nullptr || transform == nullptr || output == nullptr || validity_mask == nullptr
        || source_row_start < 0 || source_row_count <= 0 || output_row_start < 0 || output_row_count <= 0
        || source_row_start + source_row_count > height || output_row_start + output_row_count > height
        || width <= 0 || height <= 0 || (channels != 3 && channels != 4)) return WMI_INVALID_ARGUMENT;
    try {
        const double determinant = transform->m11 * transform->m22 - transform->m12 * transform->m21;
        if (std::abs(determinant) < 1e-12) return WMI_INVALID_ARGUMENT;
        auto lanczos3 = [](double value) {
            value = std::abs(value);
            if (value < 1e-12) return 1.0;
            if (value >= 3.0) return 0.0;
            const double radians = 3.14159265358979323846 * value;
            return std::sin(radians) / radians * (std::sin(radians / 3.0) / (radians / 3.0));
        };
        for (int local_y = 0; local_y < output_row_count; ++local_y) {
            if (cancelled(callbacks)) return WMI_CANCELLED;
            const int destination_y = output_row_start + local_y;
            for (int x = 0; x < width; ++x) {
                const double translated_x = x - transform->tx;
                const double translated_y = destination_y - transform->ty;
                const double source_x = (transform->m22 * translated_x - transform->m12 * translated_y) / determinant;
                const double source_y = (-transform->m21 * translated_x + transform->m11 * translated_y) / determinant;
                const size_t output_pixel = static_cast<size_t>(local_y) * width + x;
                const int x0 = static_cast<int>(std::floor(source_x));
                const int y0 = static_cast<int>(std::floor(source_y));
                if (source_x < 0 || source_y < 0 || source_x > width - 1 || source_y > height - 1
                    || std::max(0, y0 - 2) < source_row_start
                    || std::min(height - 1, y0 + 3) >= source_row_start + source_row_count) {
                    validity_mask[output_pixel] = 0;
                    continue;
                }
                const int minimum_x = std::max(0, x0 - 2);
                const int maximum_x = std::min(width - 1, x0 + 3);
                const int minimum_y = std::max(0, y0 - 2);
                const int maximum_y = std::min(height - 1, y0 + 3);
                double weights_x[6]{}, weights_y[6]{};
                for (int sample_x = minimum_x; sample_x <= maximum_x; ++sample_x)
                    weights_x[sample_x - minimum_x] = lanczos3(source_x - sample_x);
                for (int sample_y = minimum_y; sample_y <= maximum_y; ++sample_y)
                    weights_y[sample_y - minimum_y] = lanczos3(source_y - sample_y);
                for (int channel = 0; channel < channels; ++channel) {
                    double weighted = 0.0, weight_total = 0.0;
                    uint16_t local_minimum = UINT16_MAX, local_maximum = 0;
                    for (int sample_y = minimum_y; sample_y <= maximum_y; ++sample_y) {
                        const double weight_y = weights_y[sample_y - minimum_y];
                        for (int sample_x = minimum_x; sample_x <= maximum_x; ++sample_x) {
                            const double weight = weights_x[sample_x - minimum_x] * weight_y;
                            const size_t source_pixel = (static_cast<size_t>(sample_y - source_row_start) * width + sample_x) * channels;
                            const uint16_t value = source_rows[source_pixel + channel];
                            weighted += value * weight;
                            weight_total += weight;
                            local_minimum = std::min(local_minimum, value);
                            local_maximum = std::max(local_maximum, value);
                        }
                    }
                    const double value = std::clamp(weighted / std::max(1e-12, weight_total),
                                                    static_cast<double>(local_minimum), static_cast<double>(local_maximum));
                    output[output_pixel * channels + channel] = static_cast<uint16_t>(std::clamp(std::lround(value), 0L, 65535L));
                }
                validity_mask[output_pixel] = 255;
            }
            progress(callbacks, 4, static_cast<double>(local_y + 1) / output_row_count);
        }
        return WMI_OK;
    } catch (const std::bad_alloc&) { return WMI_OUT_OF_MEMORY; }
      catch (const std::exception& error) { set_error(error.what()); return WMI_INTERNAL_ERROR; }
}

wmi_status wmi_stack_preview_tile_rgb16(const uint16_t* frame_samples,
                                         const uint8_t* frame_masks,
                                         const double* exposure_multipliers,
                                         int32_t frame_count, int32_t pixel_count,
                                         int32_t channels, int32_t reduction_mode,
                                         double sigma_low, double sigma_high,
                                         int32_t sigma_iterations, int32_t worker_count,
                                         uint16_t* output,
                                         uint8_t* output_mask,
                                         const wmi_callbacks* callbacks) {
    if (frame_samples == nullptr || frame_count <= 0 || pixel_count <= 0 || (channels != 3 && channels != 4)
        || output == nullptr || output_mask == nullptr) return WMI_INVALID_ARGUMENT;
    try {
        const size_t samples_per_frame = static_cast<size_t>(pixel_count) * channels;
        const int workers = std::clamp(worker_count, 1,
            std::max(1, std::min(pixel_count, (pixel_count + 4095) / 4096)));
        std::vector<std::vector<float>> scratch(static_cast<size_t>(workers),
                                                std::vector<float>(static_cast<size_t>(frame_count)));
        std::atomic<bool> was_cancelled{false};
        auto reduce_range = [&](int worker, int begin, int end) {
          auto& values = scratch[static_cast<size_t>(worker)];
          for (int pixel = begin; pixel < end; ++pixel) {
            if ((pixel & 0x0fff) == 0 && cancelled(callbacks)) {
                was_cancelled.store(true, std::memory_order_relaxed);
                return;
            }
            bool any_valid = false;
            for (int frame = 0; frame < frame_count; ++frame)
                if (frame_masks == nullptr || frame_masks[static_cast<size_t>(frame) * pixel_count + pixel] != 0)
                    { any_valid = true; break; }
            output_mask[pixel] = any_valid ? 255 : 0;
            if (!any_valid) continue;
            for (int channel = 0; channel < channels; ++channel) {
                int count = 0;
                for (int frame = 0; frame < frame_count; ++frame) {
                    if (frame_masks != nullptr && frame_masks[static_cast<size_t>(frame) * pixel_count + pixel] == 0) continue;
                    const float multiplier = exposure_multipliers == nullptr
                        ? 1.0f : static_cast<float>(exposure_multipliers[frame]);
                    values[count++] = static_cast<float>(frame_samples[static_cast<size_t>(frame) * samples_per_frame
                        + static_cast<size_t>(pixel) * channels + channel]) * multiplier;
                }
                float result = 0.0f;
                if (reduction_mode <= 1) {
                    result = *std::max_element(values.begin(), values.begin() + count);
                } else if (reduction_mode == 2) {
                    std::sort(values.begin(), values.begin() + count);
                    if (count >= 3) { values[0] = values[1]; values[count - 1] = values[count - 2]; }
                    result = std::accumulate(values.begin(), values.begin() + count, 0.0f) / count;
                } else {
                    float lower = -1e30f, upper = 1e30f, mean = 0.0f;
                    int active_count = count;
                    for (int iteration = 0; iteration < std::max(1, sigma_iterations); ++iteration) {
                        mean = 0.0f; float m2 = 0.0f; active_count = 0;
                        for (int index = 0; index < count; ++index) {
                            const float value = values[index];
                            if (value < lower || value > upper) continue;
                            const float delta = value - mean;
                            mean += delta / ++active_count;
                            m2 += delta * (value - mean);
                        }
                        if (active_count == 0 || iteration + 1 >= std::max(1, sigma_iterations)) break;
                        const float deviation = active_count > 1 ? std::sqrt(m2 / (active_count - 1)) : 0.0f;
                        lower = mean - static_cast<float>(sigma_low) * deviation;
                        upper = mean + static_cast<float>(sigma_high) * deviation;
                    }
                    result = active_count == 0 ? 0.0f : mean;
                }
                output[static_cast<size_t>(pixel) * channels + channel] =
                    static_cast<uint16_t>(std::clamp(std::lround(result), 0L, 65535L));
            }
          }
        };
        if (workers == 1) {
            reduce_range(0, 0, pixel_count);
        } else {
            std::vector<std::thread> threads;
            threads.reserve(static_cast<size_t>(workers));
            for (int worker = 0; worker < workers; ++worker) {
                const int begin = pixel_count * worker / workers;
                const int end = pixel_count * (worker + 1) / workers;
                threads.emplace_back(reduce_range, worker, begin, end);
            }
            for (auto& thread : threads) thread.join();
        }
        if (was_cancelled.load(std::memory_order_relaxed)) return WMI_CANCELLED;
        progress(callbacks, 5, 1.0);
        return WMI_OK;
    } catch (const std::bad_alloc&) { return WMI_OUT_OF_MEMORY; }
      catch (const std::exception& error) { set_error(error.what()); return WMI_INTERNAL_ERROR; }
}

wmi_status wmi_tiff16_open(const char* path, int32_t width, int32_t height,
                           int32_t channels, int32_t big_tiff, wmi_tiff_writer* output) {
    if (path == nullptr || output == nullptr || width <= 0 || height <= 0 || (channels != 3 && channels != 4))
        return WMI_INVALID_ARGUMENT;
    *output = nullptr;
#if defined(WMI_HAS_TIFF)
    auto* state = new (std::nothrow) tiff_writer_state{};
    if (state == nullptr) return WMI_OUT_OF_MEMORY;
    state->file = TIFFOpen(path, big_tiff != 0 ? "w8" : "w");
    if (state->file == nullptr) { delete state; set_error("Unable to create TIFF output."); return WMI_IO_ERROR; }
    state->path = path; state->width = width; state->height = height; state->channels = channels; state->next_row = 0;
    TIFFSetField(state->file, TIFFTAG_IMAGEWIDTH, static_cast<uint32_t>(width));
    TIFFSetField(state->file, TIFFTAG_IMAGELENGTH, static_cast<uint32_t>(height));
    TIFFSetField(state->file, TIFFTAG_SAMPLESPERPIXEL, channels);
    TIFFSetField(state->file, TIFFTAG_BITSPERSAMPLE, 16);
    TIFFSetField(state->file, TIFFTAG_SAMPLEFORMAT, SAMPLEFORMAT_UINT);
    TIFFSetField(state->file, TIFFTAG_PLANARCONFIG, PLANARCONFIG_CONTIG);
    TIFFSetField(state->file, TIFFTAG_PHOTOMETRIC, PHOTOMETRIC_RGB);
    TIFFSetField(state->file, TIFFTAG_ORIENTATION, ORIENTATION_TOPLEFT);
    TIFFSetField(state->file, TIFFTAG_COMPRESSION, COMPRESSION_ADOBE_DEFLATE);
    TIFFSetField(state->file, TIFFTAG_PREDICTOR, PREDICTOR_HORIZONTAL);
    TIFFSetField(state->file, TIFFTAG_ROWSPERSTRIP, TIFFDefaultStripSize(state->file, width * channels * 2 * 64));
    if (channels == 4) { uint16_t extra = EXTRASAMPLE_UNASSALPHA; TIFFSetField(state->file, TIFFTAG_EXTRASAMPLES, 1, &extra); }
    *output = state;
    return WMI_OK;
#else
    (void)big_tiff; set_error("LibTIFF backend is not compiled into this binary."); return WMI_UNSUPPORTED;
#endif
}

wmi_status wmi_tiff16_set_icc(wmi_tiff_writer writer, const uint8_t* profile, int32_t profile_length) {
    if (writer == nullptr || profile == nullptr || profile_length <= 0) return WMI_INVALID_ARGUMENT;
#if defined(WMI_HAS_TIFF)
    auto* state = static_cast<tiff_writer_state*>(writer);
    if (state->next_row != 0) return WMI_INVALID_ARGUMENT;
    if (TIFFSetField(state->file, TIFFTAG_ICCPROFILE, static_cast<uint32_t>(profile_length), profile) != 1) {
        set_error("LibTIFF failed to attach the ICC profile.");
        return WMI_IO_ERROR;
    }
    return WMI_OK;
#else
    set_error("LibTIFF backend is not compiled into this binary."); return WMI_UNSUPPORTED;
#endif
}

wmi_status wmi_tiff16_write(wmi_tiff_writer writer, int32_t row_start, int32_t row_count,
                            const uint16_t* samples, const wmi_callbacks* callbacks) {
    if (writer == nullptr || samples == nullptr || row_start < 0 || row_count <= 0) return WMI_INVALID_ARGUMENT;
#if defined(WMI_HAS_TIFF)
    auto* state = static_cast<tiff_writer_state*>(writer);
    if (row_start != state->next_row || row_start + row_count > state->height) return WMI_INVALID_ARGUMENT;
    for (int row = 0; row < row_count; ++row) {
        if (cancelled(callbacks)) return WMI_CANCELLED;
        auto* scanline = const_cast<uint16_t*>(samples + static_cast<size_t>(row) * state->width * state->channels);
        if (TIFFWriteScanline(state->file, scanline, static_cast<uint32_t>(row_start + row), 0) < 0) {
            set_error("LibTIFF failed to write a scanline."); return WMI_IO_ERROR;
        }
        state->next_row++;
    }
    progress(callbacks, 3, static_cast<double>(state->next_row) / state->height);
    return WMI_OK;
#else
    (void)callbacks; set_error("LibTIFF backend is not compiled into this binary."); return WMI_UNSUPPORTED;
#endif
}

wmi_status wmi_tiff16_close(wmi_tiff_writer writer, int32_t commit) {
    if (writer == nullptr) return WMI_INVALID_ARGUMENT;
#if defined(WMI_HAS_TIFF)
    auto* state = static_cast<tiff_writer_state*>(writer);
    const bool complete = state->next_row == state->height;
    TIFFClose(state->file);
    const std::string path = state->path;
    delete state;
    if (commit == 0 || !complete) { std::remove(path.c_str()); return commit == 0 ? WMI_OK : WMI_IO_ERROR; }
    return WMI_OK;
#else
    (void)commit; return WMI_UNSUPPORTED;
#endif
}
