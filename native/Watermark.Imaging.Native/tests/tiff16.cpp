#include "watermark_imaging.h"

#include <tiffio.h>

#include <algorithm>
#include <array>
#include <cstdint>
#include <cstdio>
#include <filesystem>
#include <iostream>
#include <string>

int main() {
    const auto path = (std::filesystem::temp_directory_path() / "wmi-tiff16-test.tiff").string();
    std::filesystem::remove(path);
    if ((wmi_get_capabilities() & WMI_CAP_TIFF16) == 0) return 2;

    wmi_tiff_writer writer = nullptr;
    if (wmi_tiff16_open(path.c_str(), 4, 3, 3, 0, &writer) != WMI_OK || writer == nullptr) return 3;
    constexpr std::array<std::uint8_t, 8> icc{{0, 1, 2, 3, 4, 5, 6, 7}};
    if (wmi_tiff16_set_icc(writer, icc.data(), static_cast<int32_t>(icc.size())) != WMI_OK) return 9;
    std::array<std::uint16_t, 4 * 3 * 3> samples{};
    for (std::size_t i = 0; i < samples.size(); ++i) samples[i] = static_cast<std::uint16_t>(i * 257u);
    if (wmi_tiff16_write(writer, 0, 3, samples.data(), nullptr) != WMI_OK) return 4;
    if (wmi_tiff16_close(writer, 1) != WMI_OK) return 5;

    TIFF* image = TIFFOpen(path.c_str(), "r");
    if (image == nullptr) return 6;
    std::uint32_t width = 0, height = 0;
    std::uint16_t bits = 0, channels = 0, compression = 0, predictor = 0;
    std::uint32_t icc_length = 0;
    void* icc_data = nullptr;
    TIFFGetField(image, TIFFTAG_IMAGEWIDTH, &width);
    TIFFGetField(image, TIFFTAG_IMAGELENGTH, &height);
    TIFFGetField(image, TIFFTAG_BITSPERSAMPLE, &bits);
    TIFFGetField(image, TIFFTAG_SAMPLESPERPIXEL, &channels);
    TIFFGetField(image, TIFFTAG_COMPRESSION, &compression);
    TIFFGetField(image, TIFFTAG_PREDICTOR, &predictor);
    const auto has_icc = TIFFGetField(image, TIFFTAG_ICCPROFILE, &icc_length, &icc_data) == 1;
    std::array<std::uint8_t, icc.size()> read_icc{};
    if (has_icc && icc_length == icc.size())
        std::copy_n(static_cast<std::uint8_t*>(icc_data), read_icc.size(), read_icc.begin());
    std::array<std::uint16_t, 4 * 3> row{};
    const auto read_ok = TIFFReadScanline(image, row.data(), 0, 0) >= 0;
    TIFFClose(image);
    std::filesystem::remove(path);

    if (width != 4 || height != 3 || bits != 16 || channels != 3 ||
        compression != COMPRESSION_ADOBE_DEFLATE || predictor != PREDICTOR_HORIZONTAL || !read_ok ||
        !has_icc || icc_length != icc.size()) return 7;
    if (read_icc != icc) return 10;
    for (std::size_t i = 0; i < row.size(); ++i) {
        if (row[i] != samples[i]) return 8;
    }
    std::cout << "TIFF16 Deflate/predictor round-trip passed\n";
    return 0;
}
