#include "watermark_imaging.h"
#include "watermark_imaging_internal.h"

#include <algorithm>
#include <array>
#include <cmath>
#include <cstring>
#include <limits>
#include <memory>
#include <mutex>
#include <new>
#include <sstream>
#include <stdexcept>
#include <string>
#include <vector>

#if defined(WMI_HAS_OCIO)
#include <OpenColorIO/OpenColorIO.h>
namespace OCIO = OCIO_NAMESPACE;
#endif

namespace {

template <size_t Size>
void copy_text(char (&destination)[Size], const std::string& source) {
    std::memset(destination, 0, Size);
    std::strncpy(destination, source.c_str(), Size - 1);
}

wmi_status copy_string(const std::string& source, char* destination, int32_t capacity,
                       int32_t* required_length) {
    if (required_length == nullptr || capacity < 0) return WMI_INVALID_ARGUMENT;
    if (source.size() >= static_cast<size_t>(std::numeric_limits<int32_t>::max()))
        return WMI_OUT_OF_MEMORY;
    *required_length = static_cast<int32_t>(source.size() + 1);
    if (destination == nullptr || capacity == 0) return WMI_OK;
    if (capacity < *required_length) return WMI_INVALID_ARGUMENT;
    std::memcpy(destination, source.c_str(), static_cast<size_t>(*required_length));
    return WMI_OK;
}

#if defined(WMI_HAS_OCIO)

struct curve_storage {
    std::vector<float> points;
};

struct grade_storage {
    float exposure = 0;
    float contrast = 0;
    float highlights = 0;
    float shadows = 0;
    float whites = 0;
    float blacks = 0;
    float temperature = 0;
    float tint = 0;
    float vibrance = 0;
    float saturation = 0;
    curve_storage master;
    curve_storage red;
    curve_storage green;
    curve_storage blue;
    std::array<float, 24> hsl{};
};

struct gpu_uniform {
    std::string name;
    int32_t type = WMI_COLOR_GPU_UNIFORM_FLOAT;
    std::vector<float> values;
};

struct gpu_texture {
    std::string texture_name;
    std::string sampler_name;
    int32_t dimension = WMI_COLOR_GPU_TEXTURE_2D;
    int32_t width = 0;
    int32_t height = 1;
    int32_t depth = 1;
    int32_t channels = 1;
    int32_t interpolation = 0;
    std::vector<float> values;
};

struct color_gpu_snapshot_state {
    std::string program;
    std::string cache_id;
    std::vector<gpu_uniform> uniforms;
    std::vector<gpu_texture> textures;
};

struct color_processor_state {
    std::mutex gate;
    grade_storage current;
    std::array<float, 9> white_balance{1, 0, 0, 0, 1, 0, 0, 0, 1};
    OCIO::ConstCPUProcessorRcPtr pre_cpu;
    OCIO::ConstCPUProcessorRcPtr grade_cpu;
    OCIO::GpuShaderDescRcPtr pre_gpu;
    OCIO::GpuShaderDescRcPtr grade_gpu;
    std::string gpu_program;
    std::string gpu_cache_id;
};

curve_storage copy_curve(const wmi_color_curve& curve) {
    curve_storage result;
    if (curve.points_xy != nullptr && curve.point_count > 0) {
        const auto count = static_cast<size_t>(curve.point_count) * 2;
        result.points.assign(curve.points_xy, curve.points_xy + count);
    }
    if (result.points.size() < 4) result.points = {0.f, 0.f, 1.f, 1.f};
    return result;
}

grade_storage copy_grade(const wmi_color_grade_state* value) {
    grade_storage result;
    if (value == nullptr) {
        result.master.points = result.red.points = result.green.points = result.blue.points = {0, 0, 1, 1};
        return result;
    }
    result.exposure = std::clamp(value->exposure, -5.f, 5.f);
    result.contrast = std::clamp(value->contrast, -100.f, 100.f);
    result.highlights = std::clamp(value->highlights, -100.f, 100.f);
    result.shadows = std::clamp(value->shadows, -100.f, 100.f);
    result.whites = std::clamp(value->whites, -100.f, 100.f);
    result.blacks = std::clamp(value->blacks, -100.f, 100.f);
    result.temperature = std::clamp(value->temperature, -100.f, 100.f);
    result.tint = std::clamp(value->tint, -100.f, 100.f);
    result.vibrance = std::clamp(value->vibrance, -100.f, 100.f);
    result.saturation = std::clamp(value->saturation, -100.f, 100.f);
    result.master = copy_curve(value->master_curve);
    result.red = copy_curve(value->red_curve);
    result.green = copy_curve(value->green_curve);
    result.blue = copy_curve(value->blue_curve);
    if (value->hsl_values != nullptr && value->hsl_value_count > 0) {
        const auto count = std::min<int32_t>(value->hsl_value_count, static_cast<int32_t>(result.hsl.size()));
        std::copy_n(value->hsl_values, count, result.hsl.begin());
        for (float& item : result.hsl) item = std::clamp(item, -100.f, 100.f);
    }
    return result;
}

OCIO::ExponentWithLinearTransformRcPtr srgb_curve(OCIO::TransformDirection direction) {
    auto transform = OCIO::ExponentWithLinearTransform::Create();
    const double gamma[4] = {2.4, 2.4, 2.4, 1.0};
    const double offset[4] = {0.055, 0.055, 0.055, 0.0};
    transform->setGamma(gamma);
    transform->setOffset(offset);
    transform->setNegativeStyle(OCIO::NEGATIVE_LINEAR);
    transform->setDirection(direction);
    return transform;
}

OCIO::GradingPrimary make_primary(const grade_storage& grade) {
    OCIO::GradingPrimary result(OCIO::GRADING_LIN);
    result.m_exposure.m_master = grade.exposure;
    result.m_contrast.m_master = std::pow(2.0, grade.contrast / 100.0);
    result.m_pivot = 0.18;
    result.m_saturation = std::max(0.0, 1.0 + grade.saturation / 100.0);
    return result;
}

OCIO::GradingTone make_tone(const grade_storage& grade) {
    OCIO::GradingTone result(OCIO::GRADING_LIN);
    // OCIO's photographic controls deliberately use narrower legal ranges than
    // the public Watermark sliders.  Map the full -100...100 UI range onto the
    // complete OCIO range instead of generating invalid 0 or 2 endpoints.
    const auto master = [](float value, double radius) {
        return 1.0 + static_cast<double>(std::clamp(value, -100.f, 100.f)) / 100.0 * radius;
    };
    result.m_blacks.m_master = master(grade.blacks, 0.9);       // OCIO 0.1...1.9
    result.m_shadows.m_master = master(grade.shadows, 0.8);     // OCIO 0.2...1.8
    result.m_highlights.m_master = master(grade.highlights, 0.8);
    result.m_whites.m_master = master(grade.whites, 0.9);
    return result;
}

void set_curve(const OCIO::GradingBSplineCurveRcPtr& target, const curve_storage& source) {
    std::vector<std::pair<float, float>> points;
    points.reserve(std::max<size_t>(2, source.points.size() / 2));
    for (size_t offset = 0; offset + 1 < source.points.size(); offset += 2) {
        const auto x = std::isfinite(source.points[offset])
            ? std::clamp(source.points[offset], 0.f, 1.f)
            : 0.f;
        const auto y = std::isfinite(source.points[offset + 1])
            ? std::clamp(source.points[offset + 1], 0.f, 1.f)
            : 0.f;
        points.emplace_back(x, y);
    }
    if (points.size() < 2) points = {{0.f, 0.f}, {1.f, 1.f}};
    std::stable_sort(points.begin(), points.end(), [](const auto& left, const auto& right) {
        return left.first < right.first;
    });
    std::vector<std::pair<float, float>> unique;
    unique.reserve(points.size() + 2);
    for (const auto& point : points) {
        if (!unique.empty() && point.first == unique.back().first) unique.back() = point;
        else unique.push_back(point);
    }
    if (unique.front().first > 0.f) unique.insert(unique.begin(), {0.f, unique.front().second});
    if (unique.back().first < 1.f) unique.emplace_back(1.f, unique.back().second);
    if (unique.size() < 2) unique = {{0.f, unique.front().second}, {1.f, unique.front().second}};
    for (size_t index = 1; index < unique.size(); ++index)
        unique[index].second = std::max(unique[index - 1].second, unique[index].second);

    target->setNumControlPoints(unique.size());
    for (size_t index = 0; index < unique.size(); ++index) {
        auto& point = target->getControlPoint(index);
        point.m_x = unique[index].first;
        point.m_y = unique[index].second;
    }
}

OCIO::GradingRGBCurveRcPtr make_rgb_curves(const grade_storage& grade) {
    auto result = OCIO::GradingRGBCurve::Create(OCIO::GRADING_LIN);
    set_curve(result->getCurve(OCIO::RGB_RED), grade.red);
    set_curve(result->getCurve(OCIO::RGB_GREEN), grade.green);
    set_curve(result->getCurve(OCIO::RGB_BLUE), grade.blue);
    set_curve(result->getCurve(OCIO::RGB_MASTER), grade.master);
    result->validate();
    return result;
}

void set_hue_points(const OCIO::GradingBSplineCurveRcPtr& curve,
                    const std::vector<std::pair<float, float>>& points) {
    curve->setNumControlPoints(points.size());
    for (size_t index = 0; index < points.size(); ++index) {
        auto& point = curve->getControlPoint(index);
        point.m_x = points[index].first;
        point.m_y = points[index].second;
    }
}

OCIO::GradingHueCurveRcPtr make_hue_curves(const grade_storage& grade) {
    auto result = OCIO::GradingHueCurve::Create(OCIO::GRADING_LIN);
    static const std::array<float, 8> centers = {1.f / 12.f, 2.f / 12.f, 3.f / 12.f,
        5.f / 12.f, 7.f / 12.f, 9.f / 12.f, 10.5f / 12.f, 0.f};
    std::vector<std::pair<float, float>> hue_hue;
    std::vector<std::pair<float, float>> hue_sat;
    std::vector<std::pair<float, float>> hue_lum;
    for (size_t sorted = 0; sorted < centers.size(); ++sorted) {
        const size_t band = sorted == 0 ? 7 : sorted - 1;
        const float x = centers[band];
        const float hue_delta = grade.hsl[band * 3] * 0.3f / 360.f;
        hue_hue.emplace_back(x, x + hue_delta);
        hue_sat.emplace_back(x, std::max(0.f, 1.f + grade.hsl[band * 3 + 1] / 100.f));
        hue_lum.emplace_back(x, std::pow(2.f, grade.hsl[band * 3 + 2] / 100.f));
    }
    set_hue_points(result->getCurve(OCIO::HUE_HUE), hue_hue);
    set_hue_points(result->getCurve(OCIO::HUE_SAT), hue_sat);
    set_hue_points(result->getCurve(OCIO::HUE_LUM), hue_lum);

    std::vector<std::pair<float, float>> sat_sat;
    const float vibrance = grade.vibrance / 100.f;
    for (int index = 0; index <= 4; ++index) {
        const float input = index / 4.f;
        const float output = vibrance >= 0
            ? input + vibrance * (1.f - input) * input
            : input * (1.f + vibrance);
        sat_sat.emplace_back(input, std::clamp(output, 0.f, 1.f));
    }
    set_hue_points(result->getCurve(OCIO::SAT_SAT), sat_sat);
    result->validate();
    return result;
}

void append_grade(const OCIO::GroupTransformRcPtr& group, const grade_storage& grade, bool dynamic) {
    auto primary = OCIO::GradingPrimaryTransform::Create(OCIO::GRADING_LIN);
    primary->setValue(make_primary(grade));
    if (dynamic) primary->makeDynamic();
    group->appendTransform(primary);

    auto tone = OCIO::GradingToneTransform::Create(OCIO::GRADING_LIN);
    tone->setValue(make_tone(grade));
    if (dynamic) tone->makeDynamic();
    group->appendTransform(tone);

    auto hue = OCIO::GradingHueCurveTransform::Create(OCIO::GRADING_LIN);
    hue->setValue(make_hue_curves(grade));
    if (dynamic) hue->makeDynamic();
    group->appendTransform(hue);

    auto curves = OCIO::GradingRGBCurveTransform::Create(OCIO::GRADING_LIN);
    curves->setBypassLinToLog(true);
    curves->setValue(make_rgb_curves(grade));
    if (dynamic) curves->makeDynamic();
    group->appendTransform(curves);
}

std::array<double, 3> cct_xy(double kelvin) {
    const double t = std::clamp(kelvin, 2500.0, 20000.0);
    double x;
    if (t <= 4000.0) {
        x = -0.2661239e9 / (t * t * t) - 0.2343580e6 / (t * t) + 0.8776956e3 / t + 0.179910;
    } else if (t <= 7000.0) {
        x = -4.6070e9 / (t * t * t) + 2.9678e6 / (t * t) + 0.09911e3 / t + 0.244063;
    } else {
        x = -2.0064e9 / (t * t * t) + 1.9018e6 / (t * t) + 0.24748e3 / t + 0.237040;
    }
    double y;
    if (t <= 2222.0) y = -1.1063814 * x * x * x - 1.34811020 * x * x + 2.18555832 * x - 0.20219683;
    else if (t <= 4000.0) y = -0.9549476 * x * x * x - 1.37418593 * x * x + 2.09137015 * x - 0.16748867;
    else y = -3.0 * x * x + 2.87 * x - 0.275;
    return {x, y, 1.0};
}

std::array<double, 2> xy_to_uv(double x, double y) {
    const double denominator = -2.0 * x + 12.0 * y + 3.0;
    return {4.0 * x / denominator, 6.0 * y / denominator};
}

std::array<double, 2> uv_to_xy(double u, double v) {
    const double denominator = 2.0 * u - 8.0 * v + 4.0;
    return {3.0 * u / denominator, 2.0 * v / denominator};
}

std::array<float, 9> white_balance_matrix(float temperature, float tint) {
    const double mired = std::clamp(1000000.0 / 6504.0 + temperature, 50.0, 400.0);
    const double kelvin = 1000000.0 / mired;
    const auto xy = cct_xy(kelvin);
    auto uv = xy_to_uv(xy[0], xy[1]);
    const auto lower_xy = cct_xy(std::max(2500.0, kelvin - 5.0));
    const auto upper_xy = cct_xy(std::min(20000.0, kelvin + 5.0));
    const auto lower_uv = xy_to_uv(lower_xy[0], lower_xy[1]);
    const auto upper_uv = xy_to_uv(upper_xy[0], upper_xy[1]);
    double tx = upper_uv[0] - lower_uv[0];
    double ty = upper_uv[1] - lower_uv[1];
    const double length = std::max(1e-12, std::sqrt(tx * tx + ty * ty));
    tx /= length;
    ty /= length;
    const double duv = tint * 0.0002;
    uv[0] += -ty * duv;
    uv[1] += tx * duv;
    const auto target_xy = uv_to_xy(uv[0], uv[1]);
    const std::array<double, 3> source_xyz = {0.95047, 1.0, 1.08883};
    const std::array<double, 3> target_xyz = {
        target_xy[0] / target_xy[1], 1.0, (1.0 - target_xy[0] - target_xy[1]) / target_xy[1]};
    const double bradford[9] = {0.8951, 0.2664, -0.1614, -0.7502, 1.7135, 0.0367,
        0.0389, -0.0685, 1.0296};
    const double inverse[9] = {0.9869929, -0.1470543, 0.1599627, 0.4323053, 0.5183603, 0.0492912,
        -0.0085287, 0.0400428, 0.9684867};
    auto mul3 = [](const double* matrix, const std::array<double, 3>& value) {
        return std::array<double, 3>{
            matrix[0] * value[0] + matrix[1] * value[1] + matrix[2] * value[2],
            matrix[3] * value[0] + matrix[4] * value[1] + matrix[5] * value[2],
            matrix[6] * value[0] + matrix[7] * value[1] + matrix[8] * value[2]};
    };
    const auto source_lms = mul3(bradford, source_xyz);
    const auto target_lms = mul3(bradford, target_xyz);
    double scaled[9];
    for (int row = 0; row < 3; ++row)
        for (int column = 0; column < 3; ++column)
            scaled[row * 3 + column] = bradford[row * 3 + column] * target_lms[row] / source_lms[row];
    std::array<float, 9> result{};
    for (int row = 0; row < 3; ++row)
        for (int column = 0; column < 3; ++column) {
            double value = 0;
            for (int inner = 0; inner < 3; ++inner)
                value += inverse[row * 3 + inner] * scaled[inner * 3 + column];
            result[row * 3 + column] = static_cast<float>(value);
        }
    return result;
}

OCIO::MatrixTransformRcPtr matrix_transform(const std::array<float, 9>& matrix) {
    auto transform = OCIO::MatrixTransform::Create();
    double values[16] = {
        matrix[0], matrix[1], matrix[2], 0,
        matrix[3], matrix[4], matrix[5], 0,
        matrix[6], matrix[7], matrix[8], 0,
        0, 0, 0, 1};
    transform->setMatrix(values);
    return transform;
}

void append_reference_lut(const OCIO::GroupTransformRcPtr& group, const wmi_color_pipeline_desc& pipeline) {
    if (pipeline.reference_lut_rgb == nullptr || pipeline.reference_lut_size < 2) return;
    group->appendTransform(srgb_curve(OCIO::TRANSFORM_DIR_INVERSE));
    auto lut = OCIO::Lut3DTransform::Create(static_cast<unsigned long>(pipeline.reference_lut_size));
    lut->setInterpolation(OCIO::INTERP_TETRAHEDRAL);
    const int size = pipeline.reference_lut_size;
    for (int blue = 0; blue < size; ++blue)
        for (int green = 0; green < size; ++green)
            for (int red = 0; red < size; ++red) {
                const size_t offset = static_cast<size_t>(((blue * size + green) * size + red) * 3);
                lut->setValue(red, green, blue, pipeline.reference_lut_rgb[offset],
                    pipeline.reference_lut_rgb[offset + 1], pipeline.reference_lut_rgb[offset + 2]);
            }
    group->appendTransform(lut);
    group->appendTransform(srgb_curve(OCIO::TRANSFORM_DIR_FORWARD));
}

template <typename Target>
void update_dynamic_properties(Target& target, const grade_storage& grade) {
    auto primary_property = target->getDynamicProperty(OCIO::DYNAMIC_PROPERTY_GRADING_PRIMARY);
    auto primary = OCIO::DynamicPropertyValue::AsGradingPrimary(primary_property);
    primary->setValue(make_primary(grade));
    auto tone_property = target->getDynamicProperty(OCIO::DYNAMIC_PROPERTY_GRADING_TONE);
    auto tone = OCIO::DynamicPropertyValue::AsGradingTone(tone_property);
    tone->setValue(make_tone(grade));
    auto hue_property = target->getDynamicProperty(OCIO::DYNAMIC_PROPERTY_GRADING_HUECURVE);
    auto hue = OCIO::DynamicPropertyValue::AsGradingHueCurve(hue_property);
    hue->setValue(make_hue_curves(grade));
    auto curve_property = target->getDynamicProperty(OCIO::DYNAMIC_PROPERTY_GRADING_RGBCURVE);
    auto curves = OCIO::DynamicPropertyValue::AsGradingRGBCurve(curve_property);
    curves->setValue(make_rgb_curves(grade));
}

OCIO::GpuShaderDescRcPtr make_gpu_desc(const OCIO::ConstProcessorRcPtr& processor,
                                       const char* function_name, const char* prefix) {
    auto result = OCIO::GpuShaderDesc::CreateShaderDesc();
    result->setLanguage(OCIO::GPU_LANGUAGE_GLSL_ES_3_0);
    result->setFunctionName(function_name);
    result->setResourcePrefix(prefix);
    result->setTextureMaxWidth(4096);
    result->setAllowTexture1D(false);
    auto gpu = processor->getDefaultGPUProcessor();
    gpu->extractGpuShaderInfo(result);
    return result;
}

void collect_uniforms(const OCIO::GpuShaderDescRcPtr& description, std::vector<gpu_uniform>& output) {
    for (unsigned index = 0; index < description->getNumUniforms(); ++index) {
        OCIO::GpuShaderDesc::UniformData data;
        const char* name = description->getUniform(index, data);
        gpu_uniform item;
        item.name = name == nullptr ? "" : name;
        switch (data.m_type) {
        case OCIO::UNIFORM_DOUBLE:
            item.type = WMI_COLOR_GPU_UNIFORM_FLOAT;
            item.values = {static_cast<float>(data.m_getDouble())};
            break;
        case OCIO::UNIFORM_BOOL:
            item.type = WMI_COLOR_GPU_UNIFORM_BOOL;
            item.values = {data.m_getBool() ? 1.f : 0.f};
            break;
        case OCIO::UNIFORM_FLOAT3: {
            item.type = WMI_COLOR_GPU_UNIFORM_FLOAT3;
            const auto& value = data.m_getFloat3();
            item.values = {value[0], value[1], value[2]};
            break;
        }
        case OCIO::UNIFORM_VECTOR_FLOAT: {
            item.type = WMI_COLOR_GPU_UNIFORM_FLOAT_VECTOR;
            const int count = data.m_vectorFloat.m_getSize();
            const float* value = data.m_vectorFloat.m_getVector();
            if (value != nullptr && count > 0) item.values.assign(value, value + count);
            break;
        }
        case OCIO::UNIFORM_VECTOR_INT: {
            item.type = WMI_COLOR_GPU_UNIFORM_INT_VECTOR;
            const int count = data.m_vectorInt.m_getSize();
            const int* value = data.m_vectorInt.m_getVector();
            item.values.reserve(std::max(0, count));
            for (int offset = 0; value != nullptr && offset < count; ++offset)
                item.values.push_back(static_cast<float>(value[offset]));
            break;
        }
        default:
            continue;
        }
        output.push_back(std::move(item));
    }
}

void collect_textures(const OCIO::GpuShaderDescRcPtr& description, std::vector<gpu_texture>& output) {
    for (unsigned index = 0; index < description->getNumTextures(); ++index) {
        const char* texture_name = nullptr;
        const char* sampler_name = nullptr;
        unsigned width = 0, height = 0;
        OCIO::GpuShaderCreator::TextureType channel;
        OCIO::GpuShaderCreator::TextureDimensions dimensions;
        OCIO::Interpolation interpolation;
        description->getTexture(index, texture_name, sampler_name, width, height, channel, dimensions, interpolation);
        const float* values = nullptr;
        description->getTextureValues(index, values);
        gpu_texture item;
        item.texture_name = texture_name == nullptr ? "" : texture_name;
        item.sampler_name = sampler_name == nullptr ? "" : sampler_name;
        item.dimension = dimensions == OCIO::GpuShaderCreator::TEXTURE_1D
            ? WMI_COLOR_GPU_TEXTURE_1D : WMI_COLOR_GPU_TEXTURE_2D;
        item.width = static_cast<int32_t>(width);
        item.height = static_cast<int32_t>(height);
        item.channels = channel == OCIO::GpuShaderCreator::TEXTURE_RGB_CHANNEL ? 3 : 1;
        item.interpolation = static_cast<int32_t>(interpolation);
        const size_t count = static_cast<size_t>(width) * std::max(1u, height) * item.channels;
        if (values != nullptr) item.values.assign(values, values + count);
        output.push_back(std::move(item));
    }
    for (unsigned index = 0; index < description->getNum3DTextures(); ++index) {
        const char* texture_name = nullptr;
        const char* sampler_name = nullptr;
        unsigned edge = 0;
        OCIO::Interpolation interpolation;
        description->get3DTexture(index, texture_name, sampler_name, edge, interpolation);
        const float* values = nullptr;
        description->get3DTextureValues(index, values);
        gpu_texture item;
        item.texture_name = texture_name == nullptr ? "" : texture_name;
        item.sampler_name = sampler_name == nullptr ? "" : sampler_name;
        item.dimension = WMI_COLOR_GPU_TEXTURE_3D;
        item.width = item.height = item.depth = static_cast<int32_t>(edge);
        item.channels = 3;
        item.interpolation = static_cast<int32_t>(interpolation);
        const size_t count = static_cast<size_t>(edge) * edge * edge * 3;
        if (values != nullptr) item.values.assign(values, values + count);
        output.push_back(std::move(item));
    }
}

bool is_dynamic_curve_float_array(const gpu_uniform& uniform) {
    if (uniform.type != WMI_COLOR_GPU_UNIFORM_FLOAT_VECTOR) return false;
    const bool curve = uniform.name.find("_grading_huecurve_") != std::string::npos
        || uniform.name.find("_grading_rgbcurve_") != std::string::npos;
    const auto has_suffix = [&uniform](const char* suffix) {
        const auto length = std::strlen(suffix);
        return uniform.name.size() >= length
            && uniform.name.compare(uniform.name.size() - length, length, suffix) == 0;
    };
    const bool samples = has_suffix("_knots") || has_suffix("_coefs");
    return curve && samples;
}

void replace_array_reads(std::string& shader, const std::string& array_name,
                         const std::string& sampler_name) {
    const std::string marker = array_name + "[";
    size_t cursor = 0;
    while ((cursor = shader.find(marker, cursor)) != std::string::npos) {
        const size_t expression_start = cursor + marker.size();
        size_t expression_end = expression_start;
        int nested = 0;
        for (; expression_end < shader.size(); ++expression_end) {
            if (shader[expression_end] == '[') ++nested;
            else if (shader[expression_end] == ']') {
                if (nested == 0) break;
                --nested;
            }
        }
        if (expression_end == shader.size())
            throw std::runtime_error("OpenColorIO curve array access is malformed.");
        const auto expression = shader.substr(expression_start, expression_end - expression_start);
        const std::string replacement = "texelFetch(" + sampler_name
            + ", ivec2(int(" + expression + "), 0), 0).r";
        shader.replace(cursor, expression_end - cursor + 1, replacement);
        cursor += replacement.size();
    }
}

void promote_dynamic_curve_arrays(std::string& shader, std::vector<gpu_uniform>& uniforms,
                                  std::vector<gpu_texture>& textures, bool rewrite_shader) {
    for (auto iterator = uniforms.begin(); iterator != uniforms.end();) {
        if (!is_dynamic_curve_float_array(*iterator)) {
            ++iterator;
            continue;
        }

        const std::string sampler_name = "wm_dynamic_" + iterator->name;
        if (rewrite_shader) {
            const std::string declaration_start = "uniform float " + iterator->name + "[";
            const auto declaration = shader.find(declaration_start);
            const auto declaration_end = declaration == std::string::npos
                ? std::string::npos : shader.find("];", declaration + declaration_start.size());
            if (declaration == std::string::npos || declaration_end == std::string::npos)
                throw std::runtime_error("OpenColorIO curve uniform declaration was not found.");
            shader.replace(declaration, declaration_end - declaration + 2,
                "uniform highp sampler2D " + sampler_name + ";");
            replace_array_reads(shader, iterator->name, sampler_name);
        }

        gpu_texture texture;
        texture.texture_name = sampler_name + "_data";
        texture.sampler_name = sampler_name;
        texture.dimension = WMI_COLOR_GPU_TEXTURE_2D;
        texture.width = static_cast<int32_t>(std::max<size_t>(1, iterator->values.size()));
        texture.height = 1;
        texture.depth = 1;
        texture.channels = 1;
        texture.interpolation = OCIO::INTERP_NEAREST;
        texture.values = iterator->values.empty() ? std::vector<float>{0.f} : iterator->values;
        textures.push_back(std::move(texture));
        iterator = uniforms.erase(iterator);
    }
}

std::string complete_shader(const OCIO::GpuShaderDescRcPtr& pre,
                            const OCIO::GpuShaderDescRcPtr& grade) {
    std::ostringstream shader;
    shader << "#version 300 es\nprecision highp float;\nprecision highp sampler3D;\n"
           << "in vec2 v_uv;\nout vec4 outColor;\nuniform sampler2D wm_source;\n"
           << "uniform mat3 wm_white_balance;\n"
           << pre->getShaderText() << "\n" << grade->getShaderText() << "\n"
           << "void main(){vec4 c=texture(wm_source,v_uv);c=wm_ocio_pre(c);"
           << "c.rgb=wm_white_balance*c.rgb;c=wm_ocio_grade(c);outColor=c;}\n";
    return shader.str();
}

void apply_white_balance(std::vector<float>& pixels, const std::array<float, 9>& matrix) {
    for (size_t offset = 0; offset + 3 < pixels.size(); offset += 4) {
        const float red = pixels[offset];
        const float green = pixels[offset + 1];
        const float blue = pixels[offset + 2];
        pixels[offset] = matrix[0] * red + matrix[1] * green + matrix[2] * blue;
        pixels[offset + 1] = matrix[3] * red + matrix[4] * green + matrix[5] * blue;
        pixels[offset + 2] = matrix[6] * red + matrix[7] * green + matrix[8] * blue;
    }
}

float normalized_channel(const uint8_t* row, int x, int channel, int pixel_format) {
    if (pixel_format == WMI_COLOR_PIXEL_RGBA8 || pixel_format == WMI_COLOR_PIXEL_BGRA8) {
        const int source_channel = pixel_format == WMI_COLOR_PIXEL_BGRA8 && channel < 3 ? 2 - channel : channel;
        return row[x * 4 + source_channel] / 255.f;
    }
    const auto* values = reinterpret_cast<const uint16_t*>(row);
    const int channels = pixel_format == WMI_COLOR_PIXEL_RGB16 ? 3 : 4;
    return values[x * channels + channel] / 65535.f;
}

void write_channel(uint8_t* row, int x, int channel, int pixel_format, float value) {
    value = std::clamp(value, 0.f, 1.f);
    if (pixel_format == WMI_COLOR_PIXEL_RGBA8 || pixel_format == WMI_COLOR_PIXEL_BGRA8) {
        const int target_channel = pixel_format == WMI_COLOR_PIXEL_BGRA8 && channel < 3 ? 2 - channel : channel;
        row[x * 4 + target_channel] = static_cast<uint8_t>(std::lround(value * 255.f));
        return;
    }
    auto* values = reinterpret_cast<uint16_t*>(row);
    const int channels = pixel_format == WMI_COLOR_PIXEL_RGB16 ? 3 : 4;
    values[x * channels + channel] = static_cast<uint16_t>(std::lround(value * 65535.f));
}

bool valid_image(const wmi_color_image_desc& image) {
    if (image.pixels == nullptr || image.width <= 0 || image.height <= 0 || image.row_bytes <= 0) return false;
    switch (image.pixel_format) {
    case WMI_COLOR_PIXEL_RGBA8:
    case WMI_COLOR_PIXEL_BGRA8:
        return image.row_bytes >= image.width * 4;
    case WMI_COLOR_PIXEL_RGB16:
        return image.row_bytes >= image.width * 3 * 2;
    case WMI_COLOR_PIXEL_RGBA16:
        return image.row_bytes >= image.width * 4 * 2;
    case WMI_COLOR_PIXEL_RGB_F32:
        return image.row_bytes >= image.width * 3 * 4;
    case WMI_COLOR_PIXEL_RGBA_F32:
        return image.row_bytes >= image.width * 4 * 4;
    default:
        return false;
    }
}

#endif

} // namespace

wmi_status wmi_color_processor_create(const wmi_color_pipeline_desc* pipeline,
                                       const wmi_color_grade_state* initial_state,
                                       wmi_color_processor* processor) {
    if (processor == nullptr) return WMI_INVALID_ARGUMENT;
    *processor = nullptr;
#if defined(WMI_HAS_OCIO)
    if (pipeline == nullptr || pipeline->struct_size < sizeof(wmi_color_pipeline_desc)
        || initial_state == nullptr || initial_state->struct_size < sizeof(wmi_color_grade_state)
        || pipeline->pipeline_version != 3
        || (pipeline->input_encoding != WMI_COLOR_ENCODING_SRGB
            && pipeline->input_encoding != WMI_COLOR_ENCODING_LINEAR_SRGB)
        || (pipeline->output_encoding != WMI_COLOR_ENCODING_SRGB
            && pipeline->output_encoding != WMI_COLOR_ENCODING_LINEAR_SRGB)
        || pipeline->reference_lut_size < 0 || pipeline->reference_lut_size > 129)
        return WMI_INVALID_ARGUMENT;
    try {
        auto state = std::make_unique<color_processor_state>();
        state->current = copy_grade(initial_state);
        state->white_balance = white_balance_matrix(state->current.temperature, state->current.tint);
        const auto automatic = copy_grade(pipeline->automatic_grade);

        auto pre_group = OCIO::GroupTransform::Create();
        if (pipeline->input_encoding == WMI_COLOR_ENCODING_SRGB)
            pre_group->appendTransform(srgb_curve(OCIO::TRANSFORM_DIR_FORWARD));
        if (pipeline->automatic_grade != nullptr) {
            pre_group->appendTransform(matrix_transform(
                white_balance_matrix(automatic.temperature, automatic.tint)));
            append_grade(pre_group, automatic, false);
        }
        append_reference_lut(pre_group, *pipeline);

        auto grade_group = OCIO::GroupTransform::Create();
        append_grade(grade_group, state->current, true);
        if (pipeline->output_encoding == WMI_COLOR_ENCODING_SRGB)
            grade_group->appendTransform(srgb_curve(OCIO::TRANSFORM_DIR_INVERSE));

        auto config = OCIO::Config::CreateRaw();
        auto pre_processor = config->getProcessor(pre_group);
        auto grade_processor = config->getProcessor(grade_group);
        state->pre_cpu = pre_processor->getDefaultCPUProcessor();
        state->grade_cpu = grade_processor->getDefaultCPUProcessor();
        state->pre_gpu = make_gpu_desc(pre_processor, "wm_ocio_pre", "wm_pre_");
        state->grade_gpu = make_gpu_desc(grade_processor, "wm_ocio_grade", "wm_grade_");
        update_dynamic_properties(state->grade_cpu, state->current);
        update_dynamic_properties(state->grade_gpu, state->current);
        state->gpu_program = complete_shader(state->pre_gpu, state->grade_gpu);
        state->gpu_cache_id = std::string(state->pre_gpu->getCacheID()) + "|"
            + state->grade_gpu->getCacheID() + "|wb-bradford-v1|dynamic-curve-textures-v1";
        std::vector<gpu_uniform> initial_uniforms;
        std::vector<gpu_texture> initial_textures;
        collect_uniforms(state->pre_gpu, initial_uniforms);
        collect_uniforms(state->grade_gpu, initial_uniforms);
        promote_dynamic_curve_arrays(state->gpu_program, initial_uniforms, initial_textures, true);
        *processor = state.release();
        return WMI_OK;
    } catch (const std::bad_alloc&) {
        return WMI_OUT_OF_MEMORY;
    } catch (const std::exception& error) {
        wmi_set_last_error_internal(error.what());
        return WMI_INTERNAL_ERROR;
    }
#else
    (void)pipeline;
    (void)initial_state;
    wmi_set_last_error_internal("OpenColorIO backend is not compiled into this binary.");
    return WMI_UNSUPPORTED;
#endif
}

wmi_status wmi_color_processor_update(wmi_color_processor processor,
                                       const wmi_color_grade_state* value) {
#if defined(WMI_HAS_OCIO)
    if (processor == nullptr || value == nullptr || value->struct_size < sizeof(wmi_color_grade_state))
        return WMI_INVALID_ARGUMENT;
    try {
        auto* state = static_cast<color_processor_state*>(processor);
        std::scoped_lock lock(state->gate);
        state->current = copy_grade(value);
        state->white_balance = white_balance_matrix(state->current.temperature, state->current.tint);
        update_dynamic_properties(state->grade_cpu, state->current);
        update_dynamic_properties(state->grade_gpu, state->current);
        return WMI_OK;
    } catch (const std::bad_alloc&) {
        return WMI_OUT_OF_MEMORY;
    } catch (const std::exception& error) {
        wmi_set_last_error_internal(error.what());
        return WMI_INTERNAL_ERROR;
    }
#else
    (void)processor;
    (void)value;
    return WMI_UNSUPPORTED;
#endif
}

wmi_status wmi_color_processor_apply(wmi_color_processor processor,
                                      const wmi_color_image_desc* image,
                                      const wmi_callbacks* callbacks) {
#if defined(WMI_HAS_OCIO)
    if (processor == nullptr || image == nullptr || image->struct_size < sizeof(wmi_color_image_desc)
        || !valid_image(*image)) return WMI_INVALID_ARGUMENT;
    if (callbacks != nullptr && callbacks->is_cancelled != nullptr
        && callbacks->is_cancelled(callbacks->user_data) != 0) return WMI_CANCELLED;
    try {
        auto* state = static_cast<color_processor_state*>(processor);
        std::scoped_lock lock(state->gate);
        const size_t pixel_count = static_cast<size_t>(image->width) * image->height;
        std::vector<float> pixels(pixel_count * 4, 1.f);
        for (int y = 0; y < image->height; ++y) {
            const auto* row = static_cast<const uint8_t*>(image->pixels) + static_cast<size_t>(y) * image->row_bytes;
            if (image->pixel_format == WMI_COLOR_PIXEL_RGB_F32 || image->pixel_format == WMI_COLOR_PIXEL_RGBA_F32) {
                const auto* source = reinterpret_cast<const float*>(row);
                const int channels = image->pixel_format == WMI_COLOR_PIXEL_RGB_F32 ? 3 : 4;
                for (int x = 0; x < image->width; ++x)
                    for (int channel = 0; channel < channels; ++channel)
                        pixels[(static_cast<size_t>(y) * image->width + x) * 4 + channel] = source[x * channels + channel];
            } else {
                const int channels = image->pixel_format == WMI_COLOR_PIXEL_RGB16 ? 3 : 4;
                for (int x = 0; x < image->width; ++x)
                    for (int channel = 0; channel < channels; ++channel)
                        pixels[(static_cast<size_t>(y) * image->width + x) * 4 + channel]
                            = normalized_channel(row, x, channel, image->pixel_format);
            }
        }
        OCIO::PackedImageDesc packed(pixels.data(), image->width, image->height, 4);
        state->pre_cpu->apply(packed);
        apply_white_balance(pixels, state->white_balance);
        state->grade_cpu->apply(packed);
        if (callbacks != nullptr && callbacks->is_cancelled != nullptr
            && callbacks->is_cancelled(callbacks->user_data) != 0) return WMI_CANCELLED;
        for (int y = 0; y < image->height; ++y) {
            auto* row = static_cast<uint8_t*>(image->pixels) + static_cast<size_t>(y) * image->row_bytes;
            if (image->pixel_format == WMI_COLOR_PIXEL_RGB_F32 || image->pixel_format == WMI_COLOR_PIXEL_RGBA_F32) {
                auto* destination = reinterpret_cast<float*>(row);
                const int channels = image->pixel_format == WMI_COLOR_PIXEL_RGB_F32 ? 3 : 4;
                for (int x = 0; x < image->width; ++x)
                    for (int channel = 0; channel < channels; ++channel)
                        destination[x * channels + channel]
                            = pixels[(static_cast<size_t>(y) * image->width + x) * 4 + channel];
            } else {
                const int channels = image->pixel_format == WMI_COLOR_PIXEL_RGB16 ? 3 : 4;
                for (int x = 0; x < image->width; ++x)
                    for (int channel = 0; channel < channels; ++channel)
                        write_channel(row, x, channel, image->pixel_format,
                            pixels[(static_cast<size_t>(y) * image->width + x) * 4 + channel]);
            }
        }
        if (callbacks != nullptr && callbacks->progress != nullptr)
            callbacks->progress(callbacks->user_data, 4, 1.0);
        return WMI_OK;
    } catch (const std::bad_alloc&) {
        return WMI_OUT_OF_MEMORY;
    } catch (const std::exception& error) {
        wmi_set_last_error_internal(error.what());
        return WMI_INTERNAL_ERROR;
    }
#else
    (void)processor;
    (void)image;
    (void)callbacks;
    return WMI_UNSUPPORTED;
#endif
}

void wmi_color_processor_destroy(wmi_color_processor processor) {
#if defined(WMI_HAS_OCIO)
    delete static_cast<color_processor_state*>(processor);
#else
    (void)processor;
#endif
}

wmi_status wmi_color_gpu_snapshot_create(wmi_color_processor processor,
                                          wmi_color_gpu_snapshot* snapshot) {
    if (snapshot == nullptr) return WMI_INVALID_ARGUMENT;
    *snapshot = nullptr;
#if defined(WMI_HAS_OCIO)
    if (processor == nullptr) return WMI_INVALID_ARGUMENT;
    try {
        auto* state = static_cast<color_processor_state*>(processor);
        std::scoped_lock lock(state->gate);
        auto result = std::make_unique<color_gpu_snapshot_state>();
        result->program = state->gpu_program;
        result->cache_id = state->gpu_cache_id;
        gpu_uniform white_balance;
        white_balance.name = "wm_white_balance";
        white_balance.type = WMI_COLOR_GPU_UNIFORM_MATRIX3;
        white_balance.values.assign(state->white_balance.begin(), state->white_balance.end());
        result->uniforms.push_back(std::move(white_balance));
        collect_uniforms(state->pre_gpu, result->uniforms);
        collect_uniforms(state->grade_gpu, result->uniforms);
        collect_textures(state->pre_gpu, result->textures);
        collect_textures(state->grade_gpu, result->textures);
        promote_dynamic_curve_arrays(result->program, result->uniforms, result->textures, false);
        *snapshot = result.release();
        return WMI_OK;
    } catch (const std::bad_alloc&) {
        return WMI_OUT_OF_MEMORY;
    } catch (const std::exception& error) {
        wmi_set_last_error_internal(error.what());
        return WMI_INTERNAL_ERROR;
    }
#else
    (void)processor;
    return WMI_UNSUPPORTED;
#endif
}

void wmi_color_gpu_snapshot_destroy(wmi_color_gpu_snapshot snapshot) {
#if defined(WMI_HAS_OCIO)
    delete static_cast<color_gpu_snapshot_state*>(snapshot);
#else
    (void)snapshot;
#endif
}

wmi_status wmi_color_gpu_snapshot_get_program(wmi_color_gpu_snapshot snapshot, char* program,
                                               int32_t capacity, int32_t* required_length) {
#if defined(WMI_HAS_OCIO)
    if (snapshot == nullptr) return WMI_INVALID_ARGUMENT;
    return copy_string(static_cast<color_gpu_snapshot_state*>(snapshot)->program,
        program, capacity, required_length);
#else
    (void)snapshot; (void)program; (void)capacity; (void)required_length;
    return WMI_UNSUPPORTED;
#endif
}

wmi_status wmi_color_gpu_snapshot_get_cache_id(wmi_color_gpu_snapshot snapshot, char* cache_id,
                                                int32_t capacity, int32_t* required_length) {
#if defined(WMI_HAS_OCIO)
    if (snapshot == nullptr) return WMI_INVALID_ARGUMENT;
    return copy_string(static_cast<color_gpu_snapshot_state*>(snapshot)->cache_id,
        cache_id, capacity, required_length);
#else
    (void)snapshot; (void)cache_id; (void)capacity; (void)required_length;
    return WMI_UNSUPPORTED;
#endif
}

int32_t wmi_color_gpu_snapshot_get_uniform_count(wmi_color_gpu_snapshot snapshot) {
#if defined(WMI_HAS_OCIO)
    if (snapshot == nullptr) return -1;
    return static_cast<int32_t>(static_cast<color_gpu_snapshot_state*>(snapshot)->uniforms.size());
#else
    (void)snapshot;
    return -1;
#endif
}

wmi_status wmi_color_gpu_snapshot_get_uniform(wmi_color_gpu_snapshot snapshot, int32_t index,
                                               wmi_color_gpu_uniform_info* info,
                                               float* values, int32_t capacity) {
#if defined(WMI_HAS_OCIO)
    if (snapshot == nullptr || info == nullptr || index < 0 || capacity < 0) return WMI_INVALID_ARGUMENT;
    auto* state = static_cast<color_gpu_snapshot_state*>(snapshot);
    if (static_cast<size_t>(index) >= state->uniforms.size()) return WMI_INVALID_ARGUMENT;
    const auto& source = state->uniforms[static_cast<size_t>(index)];
    copy_text(info->name, source.name);
    info->type = source.type;
    info->element_count = static_cast<int32_t>(source.values.size());
    if (values == nullptr || capacity == 0) return WMI_OK;
    if (capacity < info->element_count) return WMI_INVALID_ARGUMENT;
    std::copy(source.values.begin(), source.values.end(), values);
    return WMI_OK;
#else
    (void)snapshot; (void)index; (void)info; (void)values; (void)capacity;
    return WMI_UNSUPPORTED;
#endif
}

int32_t wmi_color_gpu_snapshot_get_texture_count(wmi_color_gpu_snapshot snapshot) {
#if defined(WMI_HAS_OCIO)
    if (snapshot == nullptr) return -1;
    return static_cast<int32_t>(static_cast<color_gpu_snapshot_state*>(snapshot)->textures.size());
#else
    (void)snapshot;
    return -1;
#endif
}

wmi_status wmi_color_gpu_snapshot_get_texture(wmi_color_gpu_snapshot snapshot, int32_t index,
                                               wmi_color_gpu_texture_info* info,
                                               float* values, int32_t capacity) {
#if defined(WMI_HAS_OCIO)
    if (snapshot == nullptr || info == nullptr || index < 0 || capacity < 0) return WMI_INVALID_ARGUMENT;
    auto* state = static_cast<color_gpu_snapshot_state*>(snapshot);
    if (static_cast<size_t>(index) >= state->textures.size()) return WMI_INVALID_ARGUMENT;
    const auto& source = state->textures[static_cast<size_t>(index)];
    copy_text(info->texture_name, source.texture_name);
    copy_text(info->sampler_name, source.sampler_name);
    info->dimension = source.dimension;
    info->width = source.width;
    info->height = source.height;
    info->depth = source.depth;
    info->channels = source.channels;
    info->interpolation = source.interpolation;
    info->element_count = static_cast<int32_t>(source.values.size());
    if (values == nullptr || capacity == 0) return WMI_OK;
    if (capacity < info->element_count) return WMI_INVALID_ARGUMENT;
    std::copy(source.values.begin(), source.values.end(), values);
    return WMI_OK;
#else
    (void)snapshot; (void)index; (void)info; (void)values; (void)capacity;
    return WMI_UNSUPPORTED;
#endif
}
