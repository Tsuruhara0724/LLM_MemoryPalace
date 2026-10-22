using UnityEngine;

namespace MemPalaceLLM
{
    // Per-camera state only. No rendering work or persistent render textures here.
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class VrPeripheralBlur : MonoBehaviour
    {
        [field: System.NonSerialized]
        public float Blend { get; private set; }
        public float ClearRadius { get; private set; } = 0.5f;
        public float TransitionWidth { get; private set; } = 0.35f;
        public float Radius { get; private set; } = 1.75f;
        public float EdgeDarkness { get; private set; } = 1f;

        public void SetBlend(float blend, float clearRadius, float transitionWidth, float radius, float edgeDarkness)
        {
            Blend = Mathf.Clamp01(blend);
            ClearRadius = Mathf.Clamp(clearRadius, 0.2f, 0.85f);
            TransitionWidth = Mathf.Clamp(transitionWidth, 0.05f, 0.6f);
            Radius = Mathf.Clamp(radius, 0.25f, 4f);
            EdgeDarkness = Mathf.Clamp01(edgeDarkness);
        }

        private void OnDisable() => Blend = 0f;
    }
}
