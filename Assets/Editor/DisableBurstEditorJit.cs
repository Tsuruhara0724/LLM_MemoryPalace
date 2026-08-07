#if UNITY_EDITOR
using Unity.Burst;
using UnityEditor;

namespace MemPalaceLLM.Editor
{
    /// <summary>
    /// Windows Smart App Control blocks Burst's randomly named editor JIT DLLs on
    /// this workstation. Keep editor JIT disabled so project import and Play Mode
    /// remain stable. Platform AOT Burst settings for Quest builds are unaffected.
    /// </summary>
    [InitializeOnLoad]
    internal static class DisableBurstEditorJit
    {
        private const string BurstCompilationEditorPref = "BurstCompilation";

        static DisableBurstEditorJit()
        {
            EditorPrefs.SetBool(BurstCompilationEditorPref, false);
            BurstCompiler.Options.EnableBurstCompilation = false;
        }
    }
}
#endif
