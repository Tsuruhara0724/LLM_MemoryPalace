using System;
using UnityEngine;
using UnityEngine.UI;

namespace MemPalaceLLM
{
    public sealed partial class MemoryPalaceExperimentController
    {
        private MnemonicItemData vrFocusedStudyItem;
        private bool vrStudyNavigationNeedsNeutral;
        private bool vrStudyTriggerWasHeld;
        private bool vrStudyPrimaryWasHeld;
        private bool vrStudyGripWasHeld;
        private GameObject vrFocusedStudyHud;
        private Material vrFocusedStudyUiMaterial;
        private Text vrFocusedStudyTitle;
        private Text vrFocusedStudyMeaning;
        private Text vrFocusedStudyStory;
        private Text vrFocusedStudyStatus;
        private RawImage vrFocusedStudyImage;
        private VrPanelButtonInteractable vrFocusedStudyReplay;
        private VrPanelButtonInteractable vrFocusedStudyPronounce;
        private VrPanelButtonInteractable vrFocusedStudyCapture;
        private VrPanelButtonInteractable vrFocusedStudyNext;

        private bool UsesVrClickToStudy()
        {
            return stage == ExperimentStage.Study && enableVrStudyMode &&
                   (vrHeadTrackingActive || vrFocusedStudyItem != null) &&
                   ConditionUsesStoryNarration() && !allPhotoShowcaseActive;
        }

        private bool IsVrStudyFocusActive()
        {
            return UsesVrClickToStudy() && !studyAwaitingParticipantStart && vrFocusedStudyItem != null;
        }

        private bool IsVrStudyFocusItem(MnemonicItemData item)
        {
            return IsVrStudyFocusActive() && item != null &&
                   string.Equals(vrFocusedStudyItem.word, item.word, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(vrFocusedStudyItem.anchorId, item.anchorId, StringComparison.OrdinalIgnoreCase);
        }

        private bool IsVrStudyFurnitureEligible(MnemonicItemData item)
        {
            if (!UsesVrClickToStudy() || studyAwaitingParticipantStart || item == null || runtimeCamera == null ||
                currentItems == null || !currentItems.Contains(item) ||
                !studyItemRevealPoints.TryGetValue(item.word, out var revealPoint) ||
                !IsWithinStudyMarkerRevealDistance(item, revealPoint)) return false;

            // Preserve the first-pass route order. Review and no-voice sessions remain free-choice.
            var routeItem = GetCurrentVoiceRouteItem();
            var routeRequiresTarget = enableVoiceGuidance && voiceRoutePhase != VoiceRoutePhase.Disabled &&
                                      voiceRoutePhase != VoiceRoutePhase.Complete && routeItem != null;
            return !routeRequiresTarget || string.Equals(routeItem.word, item.word, StringComparison.OrdinalIgnoreCase);
        }

        private bool TryEnterVrStudyFocus(MnemonicItemData item, string details)
        {
            if (IsVrStudyFocusActive()) return IsVrStudyFocusItem(item);
            if (!IsVrStudyFurnitureEligible(item)) return false;

            vrFocusedStudyItem = item;
            vrStudyNavigationNeedsNeutral = true;
            vrSnapTurnReady = false;
            vrControllerMoveInputActive = false;
            vrMovementActiveThisFrame = false;
            UpdateVrComfortVignette(false);
            LogInteraction("vr_study_focus_enter", item.word, item.anchorId,
                "Participant clicked furniture; virtual locomotion paused, native head tracking retained. " + details);
            return true;
        }

        private void EndVrStudyFocus(string reason)
        {
            var item = vrFocusedStudyItem;
            vrFocusedStudyItem = null;
            if (vrFocusedStudyHud != null) vrFocusedStudyHud.SetActive(false);
            if (item == null) return;

            // Do not let a held stick cause a movement/turn jump when the learning card closes.
            vrStudyNavigationNeedsNeutral = true;
            vrSnapTurnReady = false;
            selectedStudyItem = null;
            vrFurnitureImageFocusVisible = false;
            vrFurnitureFocusModeActive = false;
            ResetStableStudyWordImageFocus();
            UpdateVrStudyWordImageHud(null, false);
            LogInteraction("vr_study_focus_exit", item.word, item.anchorId, reason);
        }

        private bool ShouldBlockVrStudyNavigation(Vector2 moveAxis, Vector2 turnAxis)
        {
            if (IsVrStudyFocusActive())
            {
                vrStudyNavigationNeedsNeutral = true;
                return true;
            }
            if (!vrStudyNavigationNeedsNeutral) return false;
            if (moveAxis.sqrMagnitude >= 0.04f || Mathf.Abs(turnAxis.x) > 0.25f) return true;
            vrStudyNavigationNeedsNeutral = false;
            return false;
        }

        private void ReadVrStudyClickEdges(ref bool trigger, ref bool primary, ref bool grip)
        {
            var triggerHeld = trigger;
            var primaryHeld = primary;
            var gripHeld = grip;
            trigger &= !vrStudyTriggerWasHeld;
            primary &= !vrStudyPrimaryWasHeld;
            grip &= !vrStudyGripWasHeld;
            vrStudyTriggerWasHeld = triggerHeld;
            vrStudyPrimaryWasHeld = primaryHeld;
            vrStudyGripWasHeld = gripHeld;
        }

        private MnemonicItemData FindVrStudyFurniture(Ray ray, float maxDistance, out Vector3 hitPoint)
        {
            hitPoint = ray.GetPoint(maxDistance);
            if (!UsesVrClickToStudy() || IsVrStudyFocusActive()) return null;
            var hits = Physics.RaycastAll(ray, maxDistance, ~0, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var hit in hits)
            {
                var hitTransform = hit.collider.transform;
                if (hit.collider.GetComponentInParent<VrPanelButtonInteractable>() != null ||
                    hit.collider.GetComponentInParent<VrAudioProgressInteractable>() != null) continue;

                foreach (var pair in studyFurnitureRoots)
                {
                    if (pair.Value == null || (hitTransform != pair.Value && !hitTransform.IsChildOf(pair.Value))) continue;
                    hitPoint = hit.point;
                    foreach (var item in currentItems)
                    {
                        if (string.Equals(item.anchorId, pair.Key, StringComparison.OrdinalIgnoreCase) &&
                            IsVrStudyFurnitureEligible(item)) return item;
                    }
                    return null; // The nearest furniture occludes furniture behind it, even if unassigned.
                }
                // A wall/other solid object blocks selection; do not click through it.
                hitPoint = hit.point;
                return null;
            }
            return null;
        }

        private void UpdateVrClickStudyMarkers()
        {
            // Only the existing proximity/gaze highlight is automatic. No world word/image markers.
            foreach (var marker in studyItemTargets.Values)
                if (marker != null && marker.gameObject.activeSelf) marker.gameObject.SetActive(false);
            UpdateVrStudyWordImageHud(null, false);

            var routeItem = GetCurrentVoiceRouteItem();
            var routeIsActive = enableVoiceGuidance && voiceRoutePhase != VoiceRoutePhase.Disabled &&
                                voiceRoutePhase != VoiceRoutePhase.Complete && routeItem != null;
            var item = IsVrStudyFocusActive() ? vrFocusedStudyItem :
                routeIsActive ? routeItem : ResolveVisibleStudyMarkerCandidate();
            if (!IsVrStudyFocusActive()) selectedStudyItem = null;
            var highlight = IsVrStudyFocusActive() ||
                            (item != null && IsVrStudyFurnitureEligible(item) &&
                             studyItemRevealPoints.TryGetValue(item.word, out var point) &&
                             IsLookingAtStudyRevealTarget(item, point));
            UpdateStudyFurnitureGazeHighlight(highlight ? item : null);
            vrFurnitureImageFocusVisible = IsVrStudyFocusActive();
        }

        private bool CanLeaveVrStudyFocus()
        {
            return IsVrStudyFocusActive() && !isCapturingSnapshot &&
                   (!enableVoiceGuidance || voiceRoutePhase == VoiceRoutePhase.Disabled ||
                    voiceRoutePhase == VoiceRoutePhase.Complete || voiceRoutePhase == VoiceRoutePhase.Error ||
                    voiceRoutePhase == VoiceRoutePhase.WaitingForStoryAdvance);
        }

        private void ContinueFromVrStudyFocus()
        {
            if (!CanLeaveVrStudyFocus()) return;
            if (voiceRoutePhase == VoiceRoutePhase.WaitingForStoryAdvance &&
                IsVrStudyFocusItem(GetCurrentVoiceRouteItem()))
            {
                AdvanceVoiceRouteAfterStorySegment(true);
                return;
            }
            EndVrStudyFocus("Participant returned to the room from review/no-voice/error mode.");
        }

        private void ReplayVrStudyFocusVoice()
        {
            if (!IsVrStudyFocusActive() || !enableVoiceGuidance || debugSkipVoiceAudioForCurrentStudy) return;
            if (voiceRoutePhase == VoiceRoutePhase.Complete || voiceRoutePhase == VoiceRoutePhase.Disabled ||
                voiceRoutePhase == VoiceRoutePhase.Error)
            {
                // In review replay the clicked word, rather than restarting at furniture one.
                voiceRouteIndex = currentItems.IndexOf(vrFocusedStudyItem);
                SpeakCurrentWordPronunciation(false);
                return;
            }
            ReplayCurrentVoiceStep();
        }

        private void EnsureVrStudyFocusHud()
        {
            if (vrFocusedStudyHud != null || runtimeCamera == null) return;
            vrFocusedStudyHud = new GameObject("VRFocusedStudyHUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            var canvas = vrFocusedStudyHud.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = runtimeCamera;
            canvas.targetDisplay = runtimeCamera.targetDisplay;
            canvas.planeDistance = Mathf.Max(1.8f, runtimeCamera.nearClipPlane + 0.2f);
            canvas.overrideSorting = true;
            canvas.sortingOrder = 1200;
            var scaler = vrFocusedStudyHud.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1200f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var panel = new GameObject("LearningCard", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(vrFocusedStudyHud.transform, false);
            var rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(680f, 510f);
            panel.GetComponent<Image>().color = new Color(0.025f, 0.04f, 0.06f, 0.97f);
            var heading = CreateVrPanelText(panel.transform, "Furniture", 18, new Rect(32f, -20f, 616f, 28f), new Color(0.55f, 0.84f, 0.88f));
            heading.text = "LEARNING · Movement paused";
            vrFocusedStudyTitle = CreateVrPanelText(panel.transform, "Word", 34, new Rect(32f, -58f, 616f, 48f), Color.white);
            vrFocusedStudyMeaning = CreateVrPanelText(panel.transform, "Meaning", 22, new Rect(32f, -110f, 616f, 34f), new Color(0.8f, 0.88f, 0.94f));
            // Put the aspect fitter in its own fixed-size container so it cannot grow across the card.
            var imageContainer = new GameObject("ImageArea", typeof(RectTransform));
            imageContainer.transform.SetParent(panel.transform, false);
            var imageRect = imageContainer.GetComponent<RectTransform>();
            imageRect.anchorMin = imageRect.anchorMax = imageRect.pivot = new Vector2(0f, 1f);
            imageRect.anchoredPosition = new Vector2(32f, -158f);
            imageRect.sizeDelta = new Vector2(180f, 170f);
            vrFocusedStudyImage = CreateVrPanelImage(imageRect, "WordImage", new Rect(0f, 0f, 180f, 170f), Color.white);
            var aspect = vrFocusedStudyImage.gameObject.AddComponent<AspectRatioFitter>();
            aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            vrFocusedStudyStory = CreateVrPanelText(panel.transform, "Story", 22, new Rect(240f, -158f, 408f, 174f), Color.white);
            vrFocusedStudyStory.resizeTextForBestFit = true;
            vrFocusedStudyStory.resizeTextMinSize = 17;
            vrFocusedStudyStory.resizeTextMaxSize = 22;
            vrFocusedStudyStatus = CreateVrPanelText(panel.transform, "Status", 17, new Rect(32f, -350f, 616f, 48f), new Color(0.84f, 0.9f, 0.92f));
            vrFocusedStudyReplay = CreateVrPanelButton(panel.transform, "Replay", "Replay voice", new Rect(32f, -420f, 145f, 52f), VrPanelButtonAction.ReplayVoice);
            vrFocusedStudyPronounce = CreateVrPanelButton(panel.transform, "Pronounce", "Play word", new Rect(189f, -420f, 145f, 52f), VrPanelButtonAction.PronounceWord);
            vrFocusedStudyCapture = CreateVrPanelButton(panel.transform, "Capture", "Capture view", new Rect(346f, -420f, 145f, 52f), VrPanelButtonAction.CaptureSnapshot);
            vrFocusedStudyNext = CreateVrPanelButton(panel.transform, "Next", "Next furniture", new Rect(503f, -420f, 145f, 52f), VrPanelButtonAction.AdvanceVoiceRoute);

            var shader = Shader.Find("MemoryPalace/VR Study Overlay");
            if (shader != null)
            {
                vrFocusedStudyUiMaterial = new Material(shader);
                foreach (var graphic in vrFocusedStudyHud.GetComponentsInChildren<Graphic>(true))
                    graphic.material = vrFocusedStudyUiMaterial;
            }
            foreach (var graphic in vrFocusedStudyHud.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
            vrFocusedStudyHud.SetActive(false);
        }

        private void UpdateVrStudyFocusHud()
        {
            var visible = IsVrStudyFocusActive() && vrHeadTrackingActive;
            if (!visible)
            {
                if (vrFocusedStudyHud != null) vrFocusedStudyHud.SetActive(false);
                return;
            }
            EnsureVrStudyFocusHud();
            if (vrStudyHudRoot != null) vrStudyHudRoot.SetActive(false);
            vrFocusedStudyHud.SetActive(true);
            var item = vrFocusedStudyItem;
            vrFocusedStudyTitle.text = item.word;
            vrFocusedStudyMeaning.text = item.meaning + "  ·  " + item.anchorLabel;
            vrFocusedStudyStory.text = ResolveVoiceStorySegment(item);
            var texture = GetStudyWordImageTexture(item);
            vrFocusedStudyImage.texture = texture;
            if (texture != null) vrFocusedStudyImage.GetComponent<AspectRatioFitter>().aspectRatio = (float)texture.width / Mathf.Max(1, texture.height);
            var canLeave = CanLeaveVrStudyFocus();
            vrFocusedStudyStatus.text = voiceRoutePhase == VoiceRoutePhase.Error
                ? "Audio unavailable. Replay to retry, or return to the room."
                : canLeave
                    ? (HasSavedSnapshot(item) ? "View saved. Press Next when ready." : "A / X: capture this view. Press Next when ready.")
                    : "Listen to the word and story. A / X captures this furniture view.";
            SetVrButtonState(vrFocusedStudyReplay, enableVoiceGuidance && !debugSkipVoiceAudioForCurrentStudy, "Replay voice");
            SetVrButtonState(vrFocusedStudyPronounce, CanPlayStudyWordPronunciation(item), "Play word");
            SetVrButtonState(vrFocusedStudyCapture, !isCapturingSnapshot, HasSavedSnapshot(item) ? "Replace view" : "Capture view");
            SetVrButtonState(vrFocusedStudyNext, canLeave,
                voiceRoutePhase == VoiceRoutePhase.WaitingForStoryAdvance ? "Next furniture" : "Back to room");
        }

        private void ClearVrStudyFocusRuntime()
        {
            EndVrStudyFocus("VR study runtime cleared.");
            vrStudyNavigationNeedsNeutral = false;
            vrStudyTriggerWasHeld = vrStudyPrimaryWasHeld = vrStudyGripWasHeld = false;
            if (vrFocusedStudyHud != null) Destroy(vrFocusedStudyHud);
            if (vrFocusedStudyUiMaterial != null) Destroy(vrFocusedStudyUiMaterial);
            vrFocusedStudyHud = null;
            vrFocusedStudyUiMaterial = null;
        }
    }
}
