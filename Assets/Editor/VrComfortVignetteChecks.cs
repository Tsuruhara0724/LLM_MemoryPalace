using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace MemPalaceLLM.Editor
{
    public static class VrComfortVignetteChecks
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private static int checks;

        [MenuItem("Tools/Memory Palace/Check VR Comfort Vignette")]
        public static void Run()
        {
            checks = 0;
            if (Application.isPlaying) throw new InvalidOperationException("Run these isolated state checks outside Play Mode.");
            CheckController();
            CheckRenderersAndShader();
            Debug.Log("VR comfort vignette checks passed: " + checks + ". These state checks do not validate headset stereo rendering or frame time.");
        }

        private static void CheckController()
        {
            var root = new GameObject("Vignette compatibility check") { hideFlags = HideFlags.HideAndDontSave };
            var cameraRoot = new GameObject("Vignette check camera") { hideFlags = HideFlags.HideAndDontSave };
            var panelRoot = new GameObject("Vignette check panel") { hideFlags = HideFlags.HideAndDontSave };
            MemoryPalaceExperimentController controller = null;
            GameObject maskRoot = null;
            Texture2D maskTexture = null;
            try
            {
                controller = root.AddComponent<MemoryPalaceExperimentController>();
                var serialized = new SerializedObject(controller);
                Expect(serialized.FindProperty("vrComfortVignetteMode").enumValueIndex == 0, "Original opaque mode remains the default.");
                Expect(Mathf.Approximately(serialized.FindProperty("vrPeripheralClearRadius").floatValue, 0.5f), "New mode has an independently larger clear area.");
                Expect(Mathf.Approximately(serialized.FindProperty("vrPeripheralDarkness").floatValue, 1f), "Blurred mode defaults to a fully black outer edge.");
                Expect(serialized.FindProperty("vrComfortVignetteMode").enumDisplayNames.Length == 2, "Inspector exposes both modes.");

                var camera = cameraRoot.AddComponent<Camera>();
                camera.enabled = false;
                Set(controller, "runtimeCamera", camera);
                Set(controller, "vrWorldUiRoot", panelRoot.transform);
                Call(controller, "BuildVrComfortVignette");
                maskRoot = (GameObject)Get(controller, "vrComfortVignetteRoot");
                maskTexture = (Texture2D)Get(controller, "vrComfortVignetteTexture");
                var group = (CanvasGroup)Get(controller, "vrComfortVignetteGroup");
                Expect(maskRoot != null && maskTexture != null, "Original mask is built normally.");

                Set(controller, "vrComfortVignetteAlpha", 0.6f);
                Call(controller, "UpdateVrComfortVignette", true);
                Expect(group.alpha >= 0.6f, "Opaque movement uses the original canvas alpha.");
                Expect(camera.GetComponent<VrPeripheralBlur>() == null, "Default mode creates no blur component.");
                Call(controller, "UpdateVrComfortVignette", false);
                Expect(group.alpha == 0f, "Opaque mask clears immediately when the joystick stops.");

                var mode = Field("vrComfortVignetteMode");
                mode.SetValue(controller, Enum.ToObject(mode.FieldType, 1));
                Set(controller, "vrPeripheralDarkness", 0.65f);
                Set(controller, "vrComfortVignetteAlpha", 0.6f);
                Call(controller, "UpdateVrComfortVignette", true);
                var blur = camera.GetComponent<VrPeripheralBlur>();
                Expect(blur != null && blur.Blend >= 0.6f, "New mode sends movement blend to the camera.");
                Expect(Mathf.Approximately(blur.EdgeDarkness, 0.65f), "Inspector black edge strength reaches the camera effect.");
                Expect(group.alpha == 0f, "Blurred mode does not stack the black mask on top.");
                Expect(ReferenceEquals(maskTexture, Get(controller, "vrComfortVignetteTexture")), "Switching does not replace the original mask texture.");
                Call(controller, "UpdateVrComfortVignette", false);
                Expect(blur.Blend == 0f && group.alpha == 0f, "Stopping restores an unobstructed view immediately.");

                Set(controller, "vrComfortVignetteAlpha", 0.6f);
                Call(controller, "UpdateVrComfortVignette", true);
                mode.SetValue(controller, Enum.ToObject(mode.FieldType, 0));
                Call(controller, "UpdateVrComfortVignette", true);
                Expect(!blur.enabled && blur.Blend == 0f && group.alpha >= 0.6f, "Switching back restores the original mask and removes the intermediate-buffer requirement.");
                Call(controller, "UpdateVrComfortVignette", false);
                blur.SetBlend(2f, 0f, 0f, 100f, 2f);
                Expect(blur.Blend == 1f && blur.ClearRadius >= 0.2f && blur.TransitionWidth >= 0.05f && blur.Radius <= 4f && blur.EdgeDarkness == 1f, "Blur and black edge parameters are clamped.");
                blur.SetBlend(1f, 0.5f, 0.35f, 1.75f, -1f);
                Expect(blur.EdgeDarkness == 0f, "Zero darkness remains available for color-only blur.");
                blur.enabled = false;
                // A non-ExecuteAlways component receives this callback only in Play Mode.
                typeof(VrPeripheralBlur).GetMethod("OnDisable", PrivateInstance).Invoke(blur, null);
                Expect(blur.Blend == 0f, "Disabled camera effect cannot leave stale blur.");
            }
            finally
            {
                // Production lifecycle uses deferred Destroy; EditMode checks clean up immediately.
                if (controller != null) Set(controller, "vrPeripheralBlur", null);
                if (maskRoot != null) UnityEngine.Object.DestroyImmediate(maskRoot);
                if (maskTexture != null) UnityEngine.Object.DestroyImmediate(maskTexture);
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(cameraRoot);
                UnityEngine.Object.DestroyImmediate(panelRoot);
            }
        }

        private static void CheckRenderersAndShader()
        {
            var shader = Resources.Load<Shader>("Shaders/VrPeripheralBlur");
            Expect(shader != null, "Blur shader is included as a Resource for player builds.");
            Expect(ShaderUtil.GetShaderMessages(shader).All(message => message.severity.ToString() != "Error"), "Shader importer reports no compilation errors.");
            foreach (var assetName in new[] { "PC_Renderer", "Mobile_Renderer" })
            {
                var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/" + assetName + ".asset");
                Expect(renderer != null, assetName + " exists.");
                var features = renderer.rendererFeatures.OfType<VrPeripheralBlurRendererFeature>().ToArray();
                Expect(features.Length == 1 && features[0].isActive, assetName + " contains one enabled peripheral blur feature.");
                var serialized = new SerializedObject(features[0]);
                Expect(serialized.FindProperty("blurShader").objectReferenceValue == shader, assetName + " references the blur shader explicitly.");
            }
        }

        private static FieldInfo Field(string name) => typeof(MemoryPalaceExperimentController).GetField(name, PrivateInstance)
            ?? throw new InvalidOperationException("Missing field: " + name);
        private static object Get(object target, string name) => Field(name).GetValue(target);
        private static void Set(object target, string name, object value) => Field(name).SetValue(target, value);
        private static void Call(object target, string name, params object[] arguments) =>
            typeof(MemoryPalaceExperimentController).GetMethod(name, PrivateInstance).Invoke(target, arguments);
        private static void Expect(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            checks++;
        }

        public static void RunBatch()
        {
            try { Run(); EditorApplication.Exit(0); }
            catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
        }
    }
}
