#ifndef SZA_CHARACTER_PORTAL_CLIP_INCLUDED
#define SZA_CHARACTER_PORTAL_CLIP_INCLUDED

// These properties belong to UnityPerMaterial in every participating pass.
// The entrance supplies them per renderer, so shared materials remain unchanged.
void SzaClipAtPortal(float3 positionWS)
{
    if (_SzaPortalClipEnabled > 0.5)
        clip(dot(float4(positionWS, 1.0), _SzaPortalPlaneWS));
}
#endif
