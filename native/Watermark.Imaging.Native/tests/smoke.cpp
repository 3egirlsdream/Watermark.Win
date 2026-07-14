#include "watermark_imaging.h"

#include <cstdio>

int main()
{
    const auto abi = wmi_get_abi_version();
    const auto capabilities = wmi_get_capabilities();
    const auto* version = wmi_get_backend_version();
    std::printf("abi=%u capabilities=%u version=%s\n", abi, capabilities,
                version == nullptr ? "" : version);
    return abi == WMI_ABI_VERSION && (capabilities & WMI_CAP_RAW) != 0 ? 0 : 1;
}
