using UnityEngine;

namespace MemPalaceLLM
{
    public sealed partial class MemoryPalaceExperimentController
    {
        private enum VrComfortVignetteMode
        {
            [InspectorName("Opaque Vignette (Original) / 黑色遮罩（原方案）")]
            OpaqueVignette = 0,
            [InspectorName("Blurred Vignette / 外围模糊＋黑边")]
            BlurredVignette = 1
        }

        [Header("VR Comfort Vignette")]
        [Tooltip("Opaque keeps the original black mask unchanged. Blurred preserves the sharp center and softly blurs and darkens the periphery. Both activate only during joystick translation.")]
        [InspectorName("Vignette Mode")]
        [SerializeField]
        private VrComfortVignetteMode vrComfortVignetteMode = VrComfortVignetteMode.OpaqueVignette;

        [Tooltip("Blurred mode only: radius of the fully sharp center, relative to half the eye image height. Larger values preserve more clear vision.")]
        [InspectorName("Blur: Clear Radius")]
        [SerializeField, Range(0.2f, 0.85f)]
        private float vrPeripheralClearRadius = 0.5f;

        [Tooltip("Blurred mode only: soft transition from the sharp center to the blurred periphery.")]
        [InspectorName("Blur: Transition Width")]
        [SerializeField, Range(0.05f, 0.6f)]
        private float vrPeripheralTransitionWidth = 0.35f;

        [Tooltip("Blurred mode only: Gaussian blur radius in quarter-resolution pixels. Edge darkness is controlled separately.")]
        [InspectorName("Blur: Radius")]
        [SerializeField, Range(0.25f, 4f)]
        private float vrPeripheralBlurRadius = 1.75f;

        [Tooltip("Blurred mode only: black edge strength, using the same soft radial transition as the blur. 0 keeps the original scene colors; 1 makes the outer edge fully black. The sharp center is unaffected.")]
        [InspectorName("Blur: Edge Darkness / 黑边强度")]
        [SerializeField, Range(0f, 1f)]
        private float vrPeripheralDarkness = 1f;

        private VrPeripheralBlur vrPeripheralBlur;

        private bool UpdateVrPeripheralBlur(bool isMoving)
        {
            if (vrComfortVignetteMode != VrComfortVignetteMode.BlurredVignette)
            {
                if (vrPeripheralBlur != null)
                {
                    vrPeripheralBlur.SetBlend(0f, vrPeripheralClearRadius, vrPeripheralTransitionWidth, vrPeripheralBlurRadius, vrPeripheralDarkness);
                    vrPeripheralBlur.enabled = false;
                }
                return false;
            }

            // The original canvas/texture remains intact, ready to switch back to.
            vrComfortVignetteGroup.alpha = 0f;
            vrComfortVignetteAlpha = isMoving
                ? Mathf.MoveTowards(vrComfortVignetteAlpha, VrComfortVignetteMaxAlpha,
                    Time.unscaledDeltaTime / Mathf.Max(0.001f, VrComfortVignetteFadeInSeconds))
                : 0f;

            if (runtimeCamera == null) return true;
            if (vrPeripheralBlur == null || vrPeripheralBlur.gameObject != runtimeCamera.gameObject)
            {
                ReleaseVrPeripheralBlur();
                vrPeripheralBlur = runtimeCamera.GetComponent<VrPeripheralBlur>();
                if (vrPeripheralBlur == null)
                    vrPeripheralBlur = runtimeCamera.gameObject.AddComponent<VrPeripheralBlur>();
            }
            vrPeripheralBlur.enabled = true;
            vrPeripheralBlur.SetBlend(vrComfortVignetteAlpha, vrPeripheralClearRadius, vrPeripheralTransitionWidth, vrPeripheralBlurRadius, vrPeripheralDarkness);
            return true;
        }

        private void ReleaseVrPeripheralBlur()
        {
            if (vrPeripheralBlur == null) return;
            vrPeripheralBlur.SetBlend(0f, vrPeripheralClearRadius, vrPeripheralTransitionWidth, vrPeripheralBlurRadius, vrPeripheralDarkness);
            vrPeripheralBlur.enabled = false;
            Destroy(vrPeripheralBlur);
            vrPeripheralBlur = null;
        }
    }
}
