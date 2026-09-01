using System;
using System.Collections.Generic;
using MemPalaceLLM;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MemPalaceLLM.Editor
{
    [CustomEditor(typeof(MemoryPalaceSceneUiView))]
    public sealed class MemoryPalaceSceneUiViewEditor : UnityEditor.Editor
    {
        private static readonly string[] StageNames =
        {
            "Setup",
            "RoomBuilder",
            "RoomFamiliarization",
            "PreTest",
            "Generation",
            "StoryAuthoring",
            "SelfAuthoring",
            "Study",
            "Recall",
            "Questionnaire",
            "Result"
        };

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.Space(12f);
            EditorGUILayout.LabelField("Stage Preview", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Choose a stage to edit its Scene-owned Panel. Keep control GameObject names unchanged; " +
                "anchors, sizes, layout groups, fonts, colours and images are safe to customize.",
                MessageType.Info);

            var view = (MemoryPalaceSceneUiView)target;
            for (var row = 0; row < StageNames.Length; row += 2)
            {
                EditorGUILayout.BeginHorizontal();
                for (var column = 0; column < 2 && row + column < StageNames.Length; column++)
                {
                    var stageName = StageNames[row + column];
                    if (GUILayout.Button(stageName, GUILayout.Height(30f)))
                    {
                        ShowStage(view, stageName);
                    }
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("World-space / HMD Template Preview", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Study panel")) ShowWorldTemplate(view, view.VrStudyPanelTemplate);
            if (GUILayout.Button("Ready gate")) ShowWorldTemplate(view, view.VrStudyStartGateTemplate);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Persistent HUD")) ShowWorldTemplate(view, view.VrStudyHudTemplate);
            if (GUILayout.Button("Word image HUD")) ShowWorldTemplate(view, view.VrStudyWordImageHudTemplate);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Recall panel")) ShowWorldTemplate(view, view.VrRecallPanelTemplate);
            if (GUILayout.Button("Hide HMD templates")) HideWorldTemplates(view);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8f);
            if (GUILayout.Button("Validate Unique Control Names", GUILayout.Height(32f)))
            {
                ValidateUniqueNames(view);
            }
            if (GUILayout.Button("Rebuild Entire Editable Scene UI", GUILayout.Height(32f)))
            {
                if (EditorUtility.DisplayDialog(
                        "Rebuild Editable Scene UI",
                        "This replaces the existing EditableSceneUI hierarchy and discards manual layout changes. Continue?",
                        "Rebuild",
                        "Cancel"))
                {
                    MemoryPalaceSceneUiBuilder.RebuildEditableSceneUi();
                }
            }
        }

        private static void ShowStage(MemoryPalaceSceneUiView view, string stageName)
        {
            if (view == null || view.StageRoot == null)
            {
                return;
            }

            var children = new GameObject[view.StageRoot.transform.childCount];
            for (var i = 0; i < children.Length; i++)
            {
                children[i] = view.StageRoot.transform.GetChild(i).gameObject;
            }
            Undo.RecordObjects(children, "Preview Memory Palace UI Stage");
            view.ShowOnlyStage("Stage_" + stageName);
            EditorSceneManager.MarkSceneDirty(view.gameObject.scene);
            SceneView.RepaintAll();
        }

        private static void ValidateUniqueNames(MemoryPalaceSceneUiView view)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            var transforms = view.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < transforms.Length; i++)
            {
                var name = transforms[i].name;
                counts.TryGetValue(name, out var count);
                counts[name] = count + 1;
            }

            var duplicates = new List<string>();
            foreach (var pair in counts)
            {
                if (pair.Value > 1)
                {
                    duplicates.Add($"{pair.Key} ({pair.Value})");
                }
            }

            if (duplicates.Count == 0)
            {
                EditorUtility.DisplayDialog("Scene UI Validation", "All control GameObject names are unique.", "OK");
                return;
            }

            duplicates.Sort(StringComparer.Ordinal);
            EditorUtility.DisplayDialog(
                "Scene UI Validation",
                "Duplicate names can break runtime binding:\n\n" + string.Join("\n", duplicates),
                "OK");
        }

        private static void ShowWorldTemplate(MemoryPalaceSceneUiView view, GameObject selectedTemplate)
        {
            if (view == null || selectedTemplate == null || selectedTemplate.transform.parent == null)
            {
                return;
            }

            var templateRoot = selectedTemplate.transform.parent.gameObject;
            Undo.RecordObject(templateRoot, "Preview Memory Palace HMD Template");
            var children = new GameObject[templateRoot.transform.childCount];
            for (var i = 0; i < children.Length; i++)
            {
                children[i] = templateRoot.transform.GetChild(i).gameObject;
            }
            Undo.RecordObjects(children, "Preview Memory Palace HMD Template");
            templateRoot.SetActive(true);
            for (var i = 0; i < children.Length; i++)
            {
                children[i].SetActive(children[i] == selectedTemplate);
            }
            Selection.activeGameObject = selectedTemplate;
            SceneView.lastActiveSceneView?.FrameSelected();
            EditorSceneManager.MarkSceneDirty(view.gameObject.scene);
        }

        private static void HideWorldTemplates(MemoryPalaceSceneUiView view)
        {
            var template = view != null ? view.VrStudyPanelTemplate : null;
            if (template == null || template.transform.parent == null)
            {
                return;
            }
            var templateRoot = template.transform.parent.gameObject;
            Undo.RecordObject(templateRoot, "Hide Memory Palace HMD Templates");
            templateRoot.SetActive(false);
            EditorSceneManager.MarkSceneDirty(view.gameObject.scene);
        }
    }
}
