using UnityEngine;
using UnityEngine.UI;

namespace MemPalaceLLM
{
    public sealed partial class MemoryPalaceExperimentController
    {
        private bool TryBuildVrStudyPanelFromSceneTemplate()
        {
            var template = editableSceneUi != null ? editableSceneUi.VrStudyPanelTemplate : null;
            if (template == null || runtimeCamera == null)
            {
                return false;
            }

            vrWorldUiRoot = new GameObject("VRStudyWorldUI").transform;
            var panel = Instantiate(template, vrWorldUiRoot, false);
            panel.name = "VRMnemonicPanel";
            vrStudyPanelRoot = panel.transform;
            SetTemplateCanvasCamera(panel, runtimeCamera);

            vrAudioProgressRoot = FindTemplateObject(panel, "VoiceProgressTrack");
            vrAudioProgressFill = FindTemplateComponent<Image>(panel, "VoiceProgressFill");
            vrAudioProgressInteractable = FindTemplateComponent<VrAudioProgressInteractable>(panel, "VoiceProgressTrack");
            vrAudioTimeText = FindTemplateComponent<Text>(panel, "VoiceProgressTime");
            vrProgressText = FindTemplateComponent<Text>(panel, "Progress");
            vrTitleText = FindTemplateComponent<Text>(panel, "Title");
            vrMeaningText = FindTemplateComponent<Text>(panel, "Meaning");
            vrAnchorText = FindTemplateComponent<Text>(panel, "Anchor");
            vrSubtitleText = FindTemplateComponent<Text>(panel, "VoiceSubtitle");
            vrCueText = FindTemplateComponent<Text>(panel, "Story");
            vrStoryText = null;
            vrPreviewHeaderText = FindTemplateComponent<Text>(panel, "PreviewHeader");
            vrPreviewImage = FindTemplateComponent<RawImage>(panel, "PreviewImage");
            vrPreviewInfoText = FindTemplateComponent<Text>(panel, "PreviewInfo");
            vrReplayVoiceButton = FindTemplateComponent<VrPanelButtonInteractable>(panel, "ReplayVoiceButton");
            vrRestartVoiceButton = FindTemplateComponent<VrPanelButtonInteractable>(panel, "RestartVoiceButton");
            vrGenerateButton = FindTemplateComponent<VrPanelButtonInteractable>(panel, "GenerateButton");
            vrCaptureButton = FindTemplateComponent<VrPanelButtonInteractable>(panel, "CaptureButton");
            vrPreviousImageButton = null;
            vrNextImageButton = null;
            vrAdvanceButton = null;
            vrActionText = FindTemplateComponent<Text>(panel, "Action");

            panel.SetActive(true);
            BuildVrStudyStartGate();
            BuildVrPersistentStudyHud();
            UpdateVrStudyPanelPose();
            Debug.Log("Memory Palace VR: using the Scene-authored study panel template.");
            return true;
        }

        private bool TryBuildVrStudyStartGateFromSceneTemplate()
        {
            var template = editableSceneUi != null ? editableSceneUi.VrStudyStartGateTemplate : null;
            if (template == null || vrWorldUiRoot == null || runtimeCamera == null)
            {
                return false;
            }

            var gate = Instantiate(template, vrWorldUiRoot, false);
            gate.name = "VRStudyStartGate";
            vrStudyStartPanelRoot = gate.transform;
            SetTemplateCanvasCamera(gate, runtimeCamera);
            vrStartLearningButton = FindTemplateComponent<VrPanelButtonInteractable>(gate, "StartLearningButton");
            var instruction = FindTemplateComponent<Text>(gate, "ReadyInstruction");
            if (instruction != null)
            {
                instruction.text =
                    $"Press trigger, grip, A/X, or aim at Start for one second.\n" +
                    $"Physical walking is {vrPhysicalWalkingGain:F2}x; comfort vignette is enabled.";
            }
            var revision = FindTemplateComponent<Text>(gate, "RuntimeRevision");
            if (revision != null)
            {
                revision.text = VrRuntimeRevision;
            }

            gate.SetActive(studyAwaitingParticipantStart);
            if (studyAwaitingParticipantStart && vrStudyPanelRoot != null)
            {
                vrStudyPanelRoot.gameObject.SetActive(false);
            }
            return true;
        }

        private bool TryBuildVrPersistentHudFromSceneTemplate()
        {
            var template = editableSceneUi != null ? editableSceneUi.VrStudyHudTemplate : null;
            if (template == null || runtimeCamera == null || stage != ExperimentStage.Study)
            {
                return false;
            }

            var hud = Instantiate(template, runtimeCamera.transform, false);
            hud.name = "VRStudyPersistentHUD";
            vrStudyHudRoot = hud;
            SetTemplateCanvasCamera(hud, runtimeCamera);
            vrStudyHudTimerText = FindTemplateComponent<Text>(hud, "StudyTimer");
            vrStudyHudLearningControlsRoot = FindTemplateObject(hud, "LearningControlsGroup");
            vrStudyHudLearningPhaseText = FindTemplateComponent<Text>(hud, "LearningPhaseLabel");
            vrStudyHudReplayButton = FindTemplateComponent<VrPanelButtonInteractable>(hud, "HudReplayVoiceButton");
            vrStudyHudRestartButton = FindTemplateComponent<VrPanelButtonInteractable>(hud, "HudRestartRouteButton");
            vrStudyHudCaptureButton = FindTemplateComponent<VrPanelButtonInteractable>(hud, "HudCaptureButton");
            vrStudyHudProgressRoot = FindTemplateObject(hud, "ReviewProgressGroup");
            vrStudyHudProgressText = FindTemplateComponent<Text>(hud, "ReviewProgressLabel");
            vrStudyHudProgressFill = FindTemplateComponent<Image>(hud, "ReviewProgressFill");
            vrStudyHudProgressInteractable = FindTemplateComponent<VrAudioProgressInteractable>(hud, "ReviewProgressTrack");
            vrStudyHudFinishButton = FindTemplateComponent<VrPanelButtonInteractable>(hud, "FinishLearningButton");
            vrStudyHudSubtitleRoot = FindTemplateObject(hud, "SubtitleGroup");
            vrStudyHudSubtitleText = FindTemplateComponent<Text>(hud, "Subtitle");
            vrStudyHudAdvanceButton = FindTemplateComponent<VrPanelButtonInteractable>(hud, "SubtitleAdvanceButton");
            vrContextInstructionRoot = FindTemplateObject(hud, "ContextInstructionGroup");
            vrContextInstructionGroup = FindTemplateComponent<CanvasGroup>(hud, "ContextInstructionGroup");
            vrContextInstructionIcon = FindTemplateComponent<Text>(hud, "ContextInstructionIcon");
            vrContextInstructionTitle = FindTemplateComponent<Text>(hud, "ContextInstructionTitle");
            vrContextInstructionBody = FindTemplateComponent<Text>(hud, "ContextInstructionBody");

            hud.SetActive(true);
            BuildVrStudyWordImageHud();
            UpdateVrPersistentStudyHud();
            Debug.Log("Memory Palace VR: using the Scene-authored persistent HUD template.");
            return true;
        }

        private bool TryBuildVrWordImageHudFromSceneTemplate()
        {
            var template = editableSceneUi != null ? editableSceneUi.VrStudyWordImageHudTemplate : null;
            if (template == null || runtimeCamera == null || stage != ExperimentStage.Study)
            {
                return false;
            }

            var root = Instantiate(template);
            root.name = "VRStudyWordImageHUD";
            vrStudyWordImageHudRoot = root;
            var canvas = root.GetComponent<Canvas>();
            if (canvas != null)
            {
                canvas.worldCamera = runtimeCamera;
                canvas.targetDisplay = runtimeCamera.targetDisplay;
                canvas.planeDistance = Mathf.Max(1.80f, runtimeCamera.nearClipPlane + 0.20f);
            }

            vrStudyWordImagePanel = FindTemplateComponent<RectTransform>(root, "StableWordImagePanel");
            vrStudyWordImage = FindTemplateComponent<RawImage>(root, "WordImage");
            vrStudyWordImageLabel = FindTemplateComponent<Text>(root, "WordAndMeaning");
            vrStudyWordPronunciationButton = FindTemplateComponent<VrPanelButtonInteractable>(root, "PronounceWordButton");
            vrStudyWordImageAspect = FindTemplateComponent<AspectRatioFitter>(root, "WordImage");
            root.SetActive(false);
            Debug.Log("Memory Palace VR: using the Scene-authored word-image HUD template.");
            return true;
        }

        private bool TryBuildVrRecallPanelFromSceneTemplate()
        {
            var template = editableSceneUi != null ? editableSceneUi.VrRecallPanelTemplate : null;
            if (template == null || runtimeCamera == null)
            {
                return false;
            }

            vrWorldUiRoot = new GameObject("VRRecallWorldUI").transform;
            var panel = Instantiate(template, vrWorldUiRoot, false);
            panel.name = "VRImmediatePostTestPanel";
            vrStudyPanelRoot = panel.transform;
            SetTemplateCanvasCamera(panel, runtimeCamera);
            vrProgressText = FindTemplateComponent<Text>(panel, "RecallProgress");
            vrTitleText = FindTemplateComponent<Text>(panel, "RecallTarget");
            vrMeaningText = FindTemplateComponent<Text>(panel, "RecallInstruction");
            vrAnchorText = FindTemplateComponent<Text>(panel, "RecallBlock");
            vrActionText = FindTemplateComponent<Text>(panel, "RecallFeedback");
            vrAdvanceButton = FindTemplateComponent<VrPanelButtonInteractable>(panel, "RecallAdvanceButton");
            vrRecognitionOptionButtons.Clear();
            vrRecognitionOptionImages.Clear();
            vrRecognitionOptionCaptions.Clear();
            for (var i = 0; i < 3; i++)
            {
                var option = FindTemplateObject(panel, $"RecognitionOption{i + 1}");
                if (option == null)
                {
                    continue;
                }
                vrRecognitionOptionButtons.Add(option.GetComponent<VrPanelButtonInteractable>());
                vrRecognitionOptionImages.Add(FindTemplateComponent<RawImage>(option, "OptionImage"));
                vrRecognitionOptionCaptions.Add(FindTemplateComponent<Text>(option, "OptionCaption"));
            }
            vrCueText = null;
            vrSubtitleText = null;
            vrStoryText = null;
            vrPreviewHeaderText = null;
            vrPreviewImage = null;
            vrPreviewInfoText = null;
            vrGenerateButton = null;
            vrPreviousImageButton = null;
            vrNextImageButton = null;
            vrCaptureButton = null;
            vrReplayVoiceButton = null;
            vrRestartVoiceButton = null;
            panel.SetActive(true);
            UpdateVrStudyPanelPose();
            Debug.Log("Memory Palace VR: using the Scene-authored recall panel template.");
            return true;
        }

        private static GameObject FindTemplateObject(GameObject root, string objectName)
        {
            if (root == null)
            {
                return null;
            }
            var transforms = root.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < transforms.Length; i++)
            {
                if (transforms[i].name == objectName)
                {
                    return transforms[i].gameObject;
                }
            }
            return null;
        }

        private static T FindTemplateComponent<T>(GameObject root, string objectName) where T : Component
        {
            var target = FindTemplateObject(root, objectName);
            return target != null ? target.GetComponent<T>() : null;
        }

        private static void SetTemplateCanvasCamera(GameObject root, Camera camera)
        {
            var canvases = root.GetComponentsInChildren<Canvas>(true);
            for (var i = 0; i < canvases.Length; i++)
            {
                canvases[i].worldCamera = camera;
            }
        }
    }
}
