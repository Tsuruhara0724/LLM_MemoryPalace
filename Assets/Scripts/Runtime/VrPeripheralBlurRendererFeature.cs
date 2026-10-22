using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace MemPalaceLLM
{
    // Installed in both PC and Mobile renderer assets. The original mode does
    // not enqueue a pass or require an intermediate RT.
    // Blurred mode retains a stable color buffer even at rest, avoiding resolution
    // jumps on mobile renderScale; its three filtering passes still run only in motion.
    public sealed class VrPeripheralBlurRendererFeature : ScriptableRendererFeature
    {
        [SerializeField] private Shader blurShader;
        private Material material;
        private PeripheralBlurPass pass;

        public override void Create() => pass = new PeripheralBlurPass();

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var camera = renderingData.cameraData.camera;
            if (camera.cameraType != CameraType.Game ||
                !camera.TryGetComponent<VrPeripheralBlur>(out var effect) ||
                !effect.isActiveAndEnabled)
                return;

            if (material == null)
            {
                if (blurShader == null) blurShader = Resources.Load<Shader>("Shaders/VrPeripheralBlur");
                if (blurShader == null || !blurShader.isSupported) return;
                material = CoreUtils.CreateEngineMaterial(blurShader);
            }
            pass.Setup(material, effect);
            renderer.EnqueuePass(pass);
        }

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(material);
            material = null;
        }

        private sealed class PeripheralBlurPass : ScriptableRenderPass
        {
            private static readonly int BlurStepId = Shader.PropertyToID("_MemPalaceBlurStep");
            private static readonly int BlurParametersId = Shader.PropertyToID("_MemPalaceBlurParameters");
            private static readonly int BlurTextureId = Shader.PropertyToID("_MemPalacePeripheralBlurTexture");
            private static readonly int EdgeDarknessId = Shader.PropertyToID("_MemPalacePeripheralDarkness");
            private Material material;
            private VrPeripheralBlur effect;

            private sealed class PassData
            {
                public TextureHandle source, blurred;
                public Material material;
                public Vector4 parameters;
                public float edgeDarkness;
                public int shaderPass;
            }

            public PeripheralBlurPass()
            {
                renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
                requiresIntermediateTexture = true;
            }

            public void Setup(Material blurMaterial, VrPeripheralBlur cameraEffect)
            {
                material = blurMaterial;
                effect = cameraEffect;
            }

            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                if (resources.isActiveTargetBackBuffer || effect == null || effect.Blend <= 0f) return;

                var source = resources.activeColorTexture;
                var descriptor = graph.GetTextureDesc(source);
                var cameraDescriptor = frameData.Get<UniversalCameraData>().cameraTargetDescriptor;
                // Imported camera RTHandles can retain a larger allocation from a previous
                // camera/quality level. Scratch textures must use the current eye viewport,
                // not that allocation, or Mobile renderScale can zoom/soften the sharp center.
                descriptor.sizeMode = TextureSizeMode.Explicit;
                descriptor.width = cameraDescriptor.width;
                descriptor.height = cameraDescriptor.height;
                descriptor.clearBuffer = false;
                descriptor.depthBufferBits = DepthBits.None;
                descriptor.msaaSamples = MSAASamples.None;
                descriptor.filterMode = FilterMode.Bilinear;
                descriptor.name = "MemPalace Peripheral Composite";
                var output = graph.CreateTexture(descriptor);
                var width = Mathf.Max(1, descriptor.width);
                var height = Mathf.Max(1, descriptor.height);
                descriptor.width = Mathf.Max(1, width / 4);
                descriptor.height = Mathf.Max(1, height / 4);
                descriptor.name = "MemPalace Peripheral Horizontal";
                var horizontal = graph.CreateTexture(descriptor);
                descriptor.name = "MemPalace Peripheral Vertical";
                var vertical = graph.CreateTexture(descriptor);
                // Preserve descriptor.dimension/slices: each XR eye is filtered independently.
                AddPass(graph, "Peripheral blur: horizontal", source, horizontal, default, 0,
                    new Vector4(effect.Radius / descriptor.width, 0f, 0f, 0f));
                AddPass(graph, "Peripheral blur: vertical", horizontal, vertical, default, 0,
                    new Vector4(0f, effect.Radius / descriptor.height, 0f, 0f));
                AddPass(graph, "Peripheral blur: composite", source, output, vertical, 1,
                    new Vector4(effect.ClearRadius, effect.ClearRadius + effect.TransitionWidth,
                        effect.Blend, (float)width / height), effect.EdgeDarkness);
                resources.cameraColor = output;
            }

            private void AddPass(RenderGraph graph, string name, TextureHandle source, TextureHandle destination,
                TextureHandle blurred, int shaderPass, Vector4 parameters, float edgeDarkness = 0f)
            {
                using var builder = graph.AddRasterRenderPass<PassData>(name, out var data);
                data.source = source;
                data.blurred = blurred;
                data.material = material;
                data.parameters = parameters;
                data.edgeDarkness = edgeDarkness;
                data.shaderPass = shaderPass;
                builder.UseTexture(source, AccessFlags.Read);
                if (shaderPass == 1) builder.UseTexture(blurred, AccessFlags.Read);
                builder.SetRenderAttachment(destination, 0, AccessFlags.WriteAll);
                // Explicitly declare our uniquely named shader globals to RenderGraph.
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (PassData passData, RasterGraphContext context) =>
                {
                    if (passData.shaderPass == 1)
                    {
                        context.cmd.SetGlobalVector(BlurParametersId, passData.parameters);
                        context.cmd.SetGlobalFloat(EdgeDarknessId, passData.edgeDarkness);
                        context.cmd.SetGlobalTexture(BlurTextureId, passData.blurred);
                    }
                    else context.cmd.SetGlobalVector(BlurStepId, passData.parameters);
                    RTHandle sourceTexture = passData.source;
                    var scale = sourceTexture.useScaling
                        ? sourceTexture.rtHandleProperties.rtHandleScale
                        : new Vector4(1f, 1f, 0f, 0f);
                    Blitter.BlitTexture(context.cmd, sourceTexture, new Vector4(scale.x, scale.y, 0f, 0f),
                        passData.material, passData.shaderPass);
                });
            }
        }
    }
}
