using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MemPalaceLLM
{
    /// <summary>
    /// Scene-owned UGUI references for the desktop operator interface.
    ///
    /// Controls are resolved by their unique GameObject names. This keeps the hierarchy fully
    /// editable in Unity: designers can change anchors, sizes, fonts, colours and layout groups
    /// without touching the experiment controller. Keep the control names unchanged so the
    /// presenter can continue to bind data and actions.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MemoryPalaceSceneUiView : MonoBehaviour
    {
        [Header("Scene UI")]
        [SerializeField] private Canvas canvas;
        [SerializeField] private GameObject stageRoot;

        [Header("World Space UI Templates")]
        [SerializeField] private GameObject vrStudyPanelTemplate;
        [SerializeField] private GameObject vrStudyStartGateTemplate;
        [SerializeField] private GameObject vrStudyHudTemplate;
        [SerializeField] private GameObject vrStudyWordImageHudTemplate;
        [SerializeField] private GameObject vrRecallPanelTemplate;

        [Header("Theme (used by the rebuild tool)")]
        [SerializeField] private Color pageColor = new(0.075f, 0.095f, 0.14f, 0.96f);
        [SerializeField] private Color cardColor = new(0.12f, 0.145f, 0.20f, 0.97f);
        [SerializeField] private Color primaryColor = new(0.32f, 0.62f, 0.65f, 1f);
        [SerializeField] private Color textColor = new(0.96f, 0.97f, 0.99f, 1f);
        [SerializeField] private Color mutedTextColor = new(0.68f, 0.74f, 0.82f, 1f);

        private readonly Dictionary<string, GameObject> objectsByName = new(StringComparer.Ordinal);
        private bool initialized;

        public Canvas Canvas => canvas;
        public GameObject StageRoot => stageRoot;
        public Color PageColor => pageColor;
        public Color CardColor => cardColor;
        public Color PrimaryColor => primaryColor;
        public Color TextColor => textColor;
        public Color MutedTextColor => mutedTextColor;
        public GameObject VrStudyPanelTemplate => vrStudyPanelTemplate;
        public GameObject VrStudyStartGateTemplate => vrStudyStartGateTemplate;
        public GameObject VrStudyHudTemplate => vrStudyHudTemplate;
        public GameObject VrStudyWordImageHudTemplate => vrStudyWordImageHudTemplate;
        public GameObject VrRecallPanelTemplate => vrRecallPanelTemplate;
        public bool IsReady => initialized && canvas != null && stageRoot != null;

        private void Awake()
        {
            RebuildLookup();
        }

        public void AssignSceneReferences(Canvas sceneCanvas, GameObject sceneStageRoot)
        {
            canvas = sceneCanvas;
            stageRoot = sceneStageRoot;
            RebuildLookup();
        }

        public void AssignWorldSpaceTemplates(
            GameObject studyPanel,
            GameObject startGate,
            GameObject studyHud,
            GameObject wordImageHud,
            GameObject recallPanel)
        {
            vrStudyPanelTemplate = studyPanel;
            vrStudyStartGateTemplate = startGate;
            vrStudyHudTemplate = studyHud;
            vrStudyWordImageHudTemplate = wordImageHud;
            vrRecallPanelTemplate = recallPanel;
        }

        public bool RebuildLookup()
        {
            objectsByName.Clear();
            var transforms = GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < transforms.Length; i++)
            {
                var current = transforms[i];
                if (current == null || objectsByName.ContainsKey(current.name))
                {
                    continue;
                }

                objectsByName.Add(current.name, current.gameObject);
            }

            if (canvas == null)
            {
                canvas = GetComponentInParent<Canvas>();
            }

            if (stageRoot == null && objectsByName.TryGetValue("StageRoot", out var foundStageRoot))
            {
                stageRoot = foundStageRoot;
            }

            initialized = canvas != null && stageRoot != null;
            return initialized;
        }

        public bool TryGetObject(string objectName, out GameObject result)
        {
            if (!initialized)
            {
                RebuildLookup();
            }

            return objectsByName.TryGetValue(objectName, out result) && result != null;
        }

        public T Get<T>(string objectName) where T : Component
        {
            return TryGetObject(objectName, out var target) ? target.GetComponent<T>() : null;
        }

        public Transform GetTransform(string objectName)
        {
            return TryGetObject(objectName, out var target) ? target.transform : null;
        }

        public void SetInterfaceVisible(bool visible)
        {
            if (canvas != null && canvas.gameObject.activeSelf != visible)
            {
                canvas.gameObject.SetActive(visible);
            }
        }

        public void ShowOnlyStage(string panelName)
        {
            if (stageRoot == null)
            {
                return;
            }

            for (var i = 0; i < stageRoot.transform.childCount; i++)
            {
                var child = stageRoot.transform.GetChild(i).gameObject;
                child.SetActive(string.Equals(child.name, panelName, StringComparison.Ordinal));
            }
        }

        public void SetVisible(string objectName, bool visible)
        {
            if (TryGetObject(objectName, out var target) && target.activeSelf != visible)
            {
                target.SetActive(visible);
            }
        }

        public void SetText(string objectName, string value)
        {
            var label = Get<Text>(objectName);
            if (label != null && !string.Equals(label.text, value ?? string.Empty, StringComparison.Ordinal))
            {
                label.text = value ?? string.Empty;
            }
        }

        public void SetInputText(string objectName, string value)
        {
            var input = Get<InputField>(objectName);
            if (input != null && !string.Equals(input.text, value ?? string.Empty, StringComparison.Ordinal))
            {
                input.SetTextWithoutNotify(value ?? string.Empty);
            }
        }

        public void SetInteractable(string objectName, bool interactable)
        {
            var selectable = Get<Selectable>(objectName);
            if (selectable != null)
            {
                selectable.interactable = interactable;
            }
        }

        public void SetSelected(string objectName, bool selected)
        {
            var selectable = Get<Selectable>(objectName);
            if (selectable == null)
            {
                return;
            }

            var colors = selectable.colors;
            colors.normalColor = selected ? primaryColor : Color.white;
            colors.selectedColor = selected ? primaryColor : Color.white;
            selectable.colors = colors;
        }

        public void SetSliderValue(string objectName, float value)
        {
            var slider = Get<Slider>(objectName);
            if (slider != null && !Mathf.Approximately(slider.value, value))
            {
                slider.SetValueWithoutNotify(value);
            }
        }

        public void SetRawImage(string objectName, Texture texture)
        {
            var image = Get<RawImage>(objectName);
            if (image != null && image.texture != texture)
            {
                image.texture = texture;
                image.enabled = texture != null;
            }
        }
    }

    /// <summary>
    /// Editable Scene template used for each sentence in the self-story classification list.
    /// Runtime only duplicates this template; its visual layout remains authored in the Scene.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MemoryPalaceSentenceAssignmentRow : MonoBehaviour
    {
        [SerializeField] private Text sentenceLabel;
        [SerializeField] private Dropdown wordDropdown;
        private int configuredSentenceIndex = -1;
        private int configuredSelection = int.MinValue;
        private string configuredSentence = string.Empty;
        private string configuredOptions = string.Empty;

        public void AssignSceneReferences(Text label, Dropdown dropdown)
        {
            sentenceLabel = label;
            wordDropdown = dropdown;
        }

        public void Configure(
            int sentenceIndex,
            string sentence,
            IList<string> optionLabels,
            int selectedWordIndex,
            Action<int, int> selectionChanged)
        {
            var optionSignature = string.Join("\u001f", optionLabels);
            if (configuredSentenceIndex == sentenceIndex &&
                configuredSelection == selectedWordIndex &&
                string.Equals(configuredSentence, sentence ?? string.Empty, StringComparison.Ordinal) &&
                string.Equals(configuredOptions, optionSignature, StringComparison.Ordinal))
            {
                return;
            }

            configuredSentenceIndex = sentenceIndex;
            configuredSelection = selectedWordIndex;
            configuredSentence = sentence ?? string.Empty;
            configuredOptions = optionSignature;
            if (sentenceLabel != null)
            {
                sentenceLabel.text = $"Sentence {sentenceIndex + 1}\n{sentence}";
            }

            if (wordDropdown == null)
            {
                return;
            }

            wordDropdown.onValueChanged.RemoveAllListeners();
            wordDropdown.ClearOptions();
            wordDropdown.AddOptions(new List<string>(optionLabels));
            wordDropdown.SetValueWithoutNotify(Mathf.Clamp(selectedWordIndex + 1, 0, optionLabels.Count - 1));
            wordDropdown.onValueChanged.AddListener(optionIndex => selectionChanged?.Invoke(sentenceIndex, optionIndex - 1));
        }
    }
}
