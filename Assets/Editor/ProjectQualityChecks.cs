using System;
using UnityEditor;
using UnityEngine;

namespace MemPalaceLLM.Editor
{
    /// <summary>Offline checks without starting the main scene, XR or external services.</summary>
    public static class ProjectQualityChecks
    {
        [MenuItem("Tools/Memory Palace/Run Project Checks")]
        public static void Run()
        {
            FurniturePrefabAvailabilityChecks.Run();
            MnemonicStoryPromptChecks.Run();
            ParticipantStoryEnglishChecks.Run();
            BuilderDeleteShortcutChecks.Run();
            VrStudyFocusChecks.Run();
            Debug.Log("PROJECT CHECKS PASSED: offline compatibility and interaction checks only.");
        }

        public static void RunBatch()
        {
            try { Run(); EditorApplication.Exit(0); }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        // Uses the existing occlusion/snapshot render checks when a GPU is available.
        public static void RunPreviewBatch()
        {
            try
            {
                FurniturePrefabAvailabilityChecks.Run();
                MnemonicStoryPromptChecks.Run();
                VrStudyFocusChecks.RunPreviewBatch();
            }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }
    }
}
