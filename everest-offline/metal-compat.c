/* Compatibility for the pinned legacy Metal driver only.
 * New FNA 24.01 capability queries must not claim unsupported formats.
 * Texture names are diagnostic labels, not rendering state.
 * This intentionally does not implement BC7/sRGB texture support.
 */
#include "FNA3D.h"
FNA3DAPI uint8_t FNA3D_SupportsBC7(FNA3D_Device *device)
{
    (void) device;
    return 0;
}
FNA3DAPI uint8_t FNA3D_SupportsSRGBRenderTargets(FNA3D_Device *device)
{
    (void) device;
    return 0;
}
FNA3DAPI void FNA3D_SetTextureName(FNA3D_Device *device, FNA3D_Texture *texture, const char *text)
{
    (void) device;
    (void) texture;
    (void) text;
}
