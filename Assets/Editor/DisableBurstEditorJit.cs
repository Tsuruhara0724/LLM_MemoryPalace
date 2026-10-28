#if UNITY_EDITOR
using Unity.Burst;
using UnityEditor;

namespace MemPalaceLLM.Editor
{
    /// <summary>
    /// Optional workaround for machines that block Burst's editor JIT DLLs.
    /// Changing the shared Editor preference requires an explicit menu action;
    /// opening this project must not change another developer's Burst settings.
    /// Platform AOT Burst settings for Quest builds are unaffected.
    /// </summary>
    internal static class DisableBurstEditorJit
    {
        private const string BurstCompilationEditorPref = "BurstCompilation";

        [MenuItem("Tools/Memory Palace/Compatibility/Disable Editor Burst JIT")]
        private static void DisableEditorBurstJit()
        {
            SetEditorBurstJit(false);
        }

        [MenuItem("Tools/Memory Palace/Compatibility/Enable Editor Burst JIT")]
        private static void EnableEditorBurstJit()
        {
            SetEditorBurstJit(true);
        }

        private static void SetEditorBurstJit(bool enabled)
        {
            var action = enabled ? "Enable" : "Disable";
            if (!EditorUtility.DisplayDialog(
                    action + " Editor Burst JIT",
                    "This changes Burst JIT in the current Editor and saves an Editor preference " +
                    "shared by Unity Editors under this OS user. It may affect other projects.\n\n" +
                    "Quest / Android Player AOT settings are not changed. " +
                    "This project will not automatically enable or restore this preference.\n\n" +
                    "Continue?",
                    action + " Editor Burst JIT",
                    "Cancel"))
            {
                return;
            }

            EditorPrefs.SetBool(BurstCompilationEditorPref, enabled);
            BurstCompiler.Options.EnableBurstCompilation = enabled;
            UnityEngine.Debug.Log(
                "Editor Burst JIT " + (enabled ? "enabled" : "disabled") +
                " by explicit compatibility menu action. The preference is shared by this OS user; " +
                "Quest / Android Player AOT settings are unchanged.");
        }
    }
}
#endif
