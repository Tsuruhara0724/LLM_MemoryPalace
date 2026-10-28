using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem.XR;
using UnityEngine.UI;

namespace MemPalaceLLM.Editor
{
    public static class VrStudyFocusChecks
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static int checks;
        private static bool exportPreview;

        [MenuItem("Tools/Memory Palace/Check VR Click-to-Learn")]
        public static void Run()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Run outside Play Mode.");
            checks = 0;
            var originalRoom = RoomSpecCatalog.CurrentRoom;
            var root = new GameObject("VR focus check") { hideFlags = HideFlags.HideAndDontSave };
            var rig = new GameObject("VR focus check rig") { hideFlags = HideFlags.HideAndDontSave };
            var cameraObject = new GameObject("VR focus check camera", typeof(Camera));
            cameraObject.transform.SetParent(rig.transform, false);
            var camera = cameraObject.GetComponent<Camera>();
            camera.enabled = false;
            camera.transform.position = new Vector3(0f, 1.55f, 0f);
            var poseDriver = cameraObject.AddComponent<TrackedPoseDriver>();
            MemoryPalaceExperimentController controller = null;
            var image = new Texture2D(24, 24);
            try
            {
                controller = root.AddComponent<MemoryPalaceExperimentController>();
                Set(controller, "stage", ExperimentStage.Study);
                Set(controller, "enableVrStudyMode", true);
                Set(controller, "vrHeadTrackingActive", true);
                Set(controller, "enableVoiceGuidance", false);
                Set(controller, "runtimeCamera", camera);
                Set(controller, "vrRigRoot", rig.transform);
                Set(controller, "vrTrackedPoseDriver", poseDriver);
                Phase(controller, "Disabled");
                var first = new MnemonicItemData { word = "estrella", meaning = "star", anchorId = "focus_desk", anchorLabel = "Desk", mnemonic = "You see a bright estrella (star) beside your notebook." };
                var second = new MnemonicItemData { word = "espejo", meaning = "mirror", anchorId = "focus_chair", anchorLabel = "Chair" };
                Set(controller, "currentItems", new List<MnemonicItemData> { first, second });
                RoomSpecCatalog.SetCurrentRoom(new RoomSpecDefinition
                {
                    roomId = "focus_check", roomName = "Focus check",
                    anchors = new List<AnchorDefinition>
                    {
                        new AnchorDefinition { id = first.anchorId, label = "Desk", position = new Vector3(0f, 0f, 1.5f), scale = Vector3.one },
                        new AnchorDefinition { id = second.anchorId, label = "Chair", position = new Vector3(1.5f, 0f, 1.5f), scale = Vector3.one }
                    }
                });
                var points = (Dictionary<string, Vector3>)Get(controller, "studyItemRevealPoints");
                points[first.word] = new Vector3(0f, 1.55f, 1.5f);
                points[second.word] = new Vector3(1.5f, 1.55f, 1.5f);
                var pictures = (Dictionary<string, Texture2D>)Get(controller, "mnemonicImageCues");
                pictures[first.word] = pictures[second.word] = image;
                var furniture = GameObject.CreatePrimitive(PrimitiveType.Cube);
                furniture.name = "Clickable furniture";
                furniture.transform.SetParent(root.transform);
                furniture.transform.position = new Vector3(0f, 1.1f, 1.5f);
                furniture.transform.localScale = new Vector3(0.8f, 1f, 0.8f);
                var furnitureRoots = (Dictionary<string, Transform>)Get(controller, "studyFurnitureRoots");
                furnitureRoots[first.anchorId] = furniture.transform;
                var marker = new GameObject("Old world marker");
                marker.transform.SetParent(root.transform);
                ((Dictionary<string, Transform>)Get(controller, "studyItemTargets"))[first.word] = marker.transform;

                Expect(Bool(controller, "UsesVrClickToStudy"), "VR story mode uses manual selection.");
                Call(controller, "UpdateStudyItemMarkerVisibility");
                Expect(Get(controller, "selectedStudyItem") == null && !Bool(controller, "IsVrStudyFocusActive"), "Gaze/proximity alone does not start learning.");
                Expect((string)Get(controller, "highlightedStudyAnchorId") == first.anchorId, "Nearby looked-at furniture retains its highlight.");
                Expect(!marker.activeSelf && Get(controller, "vrFocusedStudyHud") == null, "Searching reveals neither a world word image nor a learning card.");
                Call(controller, "TryAutoInspectVisibleStudyMarker", first);
                Expect(Get(controller, "selectedStudyItem") == null, "Legacy auto-inspect cannot bypass the click.");

                Physics.SyncTransforms();
                var ray = new Ray(camera.transform.position, Vector3.forward);
                var rayArguments = new object[] { ray, 8f, Vector3.zero };
                Expect(ReferenceEquals(Call(controller, "FindVrStudyFurniture", rayArguments), first), "Controller ray hits the actual furniture model, not its hidden marker.");
                var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.transform.SetParent(root.transform);
                wall.transform.position = new Vector3(0f, 1.55f, 0.6f);
                wall.transform.localScale = new Vector3(1f, 1f, 0.1f);
                Physics.SyncTransforms();
                Expect(Call(controller, "FindVrStudyFurniture", rayArguments) == null, "A solid wall blocks selection through it.");
                UnityEngine.Object.DestroyImmediate(wall);
                camera.transform.position = new Vector3(0f, 1.55f, -5f);
                Expect(!Bool(controller, "IsVrStudyFurnitureEligible", first), "Furniture outside the existing 2.2m reach cannot open the card.");
                camera.transform.position = new Vector3(0f, 1.55f, 0f);
                Set(controller, "enableVoiceGuidance", true);
                Set(controller, "voiceRouteIndex", 0);
                Phase(controller, "WaitingForTargetView");
                Expect(!Bool(controller, "IsVrStudyFurnitureEligible", second), "The first spoken pass cannot jump to a different route item.");

                var originalPosition = camera.transform.position;
                var originalRotation = camera.transform.rotation;
                Call(controller, "SelectStudyItem", first, "Offline click check");
                Expect(Bool(controller, "IsVrStudyFocusItem", first) && ReferenceEquals(Get(controller, "selectedStudyItem"), first), "Explicit selection pins the clicked target.");
                Expect(camera.transform.position == originalPosition && camera.transform.rotation == originalRotation && poseDriver.enabled, "Entering learning does not move/rotate the camera or disable native pose tracking.");
                Expect(Bool(controller, "ShouldBlockVrStudyNavigation", Vector2.up, Vector2.right), "Both translation and artificial turning are blocked in learning.");
                Call(controller, "SelectStudyItem", second, "Must be ignored");
                camera.transform.rotation = Quaternion.Euler(0f, 135f, 0f);
                Call(controller, "UpdateStudyItemMarkerVisibility");
                Call(controller, "UpdateSelectedStudyItemVisibility");
                Expect(ReferenceEquals(Get(controller, "selectedStudyItem"), first) && !Bool(controller, "ShouldHideSelectedStudyItem"), "Turning the head cannot replace or hide the pinned word.");
                camera.transform.rotation = originalRotation;
                Set(controller, "vrPhysicalWalkingGain", 8f);
                Set(controller, "vrPhysicalWalkingInitialized", true);
                Set(controller, "vrPreviousPhysicalHeadLocalPosition", camera.transform.localPosition);
                Set(controller, "vrPhysicalWalkingFrame", -1);
                var rigPosition = rig.transform.position;
                Call(controller, "ApplyVrPhysicalWalkingGain", camera.transform.localPosition + new Vector3(0.1f, 0f, 0.1f), Quaternion.Euler(0f, 10f, 0f));
                Expect(rig.transform.position == rigPosition && poseDriver.enabled, "Learning suppresses amplified walking without freezing natural head motion.");
                Expect((Vector3)Get(controller, "vrPreviousPhysicalHeadLocalPosition") == camera.transform.localPosition + new Vector3(0.1f, 0f, 0.1f), "Physical samples keep updating to prevent a displacement jump after release.");

                Phase(controller, "PlayingStorySegment");
                Expect(!Bool(controller, "CanLeaveVrStudyFocus"), "Next remains disabled during narration.");
                Call(controller, "AdvanceVoiceRouteAfterStorySegment", true);
                Expect(Bool(controller, "IsVrStudyFocusActive") && (int)Get(controller, "voiceRouteIndex") == 0, "Direct legacy advance cannot skip the current focused narration.");
                Call(controller, "UpdateVrStudyFocusHud");
                var hud = (GameObject)Get(controller, "vrFocusedStudyHud");
                Expect(hud != null && hud.activeSelf && hud.GetComponent<Canvas>().renderMode == RenderMode.ScreenSpaceCamera, "Learning uses an independent screen-space-camera canvas, not furniture World UI.");
                Expect(hud.transform.parent == null && hud.GetComponent<Canvas>().worldCamera == camera, "The XR screen canvas is top-level and bound to the participant camera.");
                Expect(((Text)Get(controller, "vrFocusedStudyTitle")).text == first.word && ((Text)Get(controller, "vrFocusedStudyStory")).text.Contains("estrella"), "Word and story contents match the clicked target.");
                var shader = Shader.Find("MemoryPalace/VR Study Overlay");
                Expect(shader != null && !ShaderUtil.ShaderHasError(shader), "Foreground UI shader is included and has no reported import errors.");
                foreach (var graphic in hud.GetComponentsInChildren<Graphic>(true))
                    Expect(graphic.material.shader == shader, "Every learning graphic uses the non-occluded foreground material.");
                var next = (VrPanelButtonInteractable)Get(controller, "vrFocusedStudyNext");
                Expect(!next.Enabled, "The focused Next button follows narration readiness.");
                Phase(controller, "WaitingForStoryAdvance");
                Call(controller, "UpdateVrStudyFocusHud");
                Expect(next.Enabled && next.Label.text == "Next furniture", "Narration completion enables an explicit next-furniture action.");
                if (exportPreview) SavePreview(controller, camera, hud, (RawImage)Get(controller, "vrFocusedStudyImage"));
                Set(controller, "isCapturingSnapshot", true);
                Expect(!Bool(controller, "CanLeaveVrStudyFocus"), "Snapshot saving cannot race a change to another word.");
                Set(controller, "isCapturingSnapshot", false);

                var clicks = new object[] { true, false, false };
                Call(controller, "ReadVrStudyClickEdges", clicks);
                Expect((bool)clicks[0], "A trigger press produces one click.");
                clicks = new object[] { true, false, false };
                Call(controller, "ReadVrStudyClickEdges", clicks);
                Expect(!(bool)clicks[0], "Holding the trigger does not auto-click again.");
                Call(controller, "ReadVrStudyClickEdges", new object[] { false, false, false });
                clicks = new object[] { true, true, true };
                Call(controller, "ReadVrStudyClickEdges", clicks);
                Expect((bool)clicks[0] && (bool)clicks[1] && (bool)clicks[2], "Released trigger/A-X/grip can be pressed again.");

                Call(controller, "ContinueFromVrStudyFocus");
                Expect(!Bool(controller, "IsVrStudyFocusActive") && !hud.activeSelf && (int)Get(controller, "voiceRouteIndex") == 1, "Next releases the card and advances to the next furniture.");
                Expect(Bool(controller, "ShouldBlockVrStudyNavigation", Vector2.up, Vector2.right), "A stick held through card closing cannot cause an immediate movement burst.");
                Expect(!Bool(controller, "ShouldBlockVrStudyNavigation", Vector2.zero, Vector2.zero) &&
                       !Bool(controller, "ShouldBlockVrStudyNavigation", Vector2.up, Vector2.zero), "Recentering restores normal room navigation.");
                Set(controller, "enableVoiceGuidance", false);
                Phase(controller, "Disabled");
                Call(controller, "SelectStudyItem", second, "No-voice check");
                Expect(Bool(controller, "CanLeaveVrStudyFocus"), "No-voice/debug sessions have a usable return action.");
                Call(controller, "ContinueFromVrStudyFocus");
                Expect(!Bool(controller, "IsVrStudyFocusActive"), "No-voice return cannot trap the participant.");
                Call(controller, "SelectStudyItem", first, "Tracking check");
                Set(controller, "vrHeadTrackingActive", false);
                Call(controller, "UpdateVrStudyFocusHud");
                Expect(!hud.activeSelf && Bool(controller, "IsVrStudyFocusActive"), "Tracking loss hides the card without replacing its selected word.");
                Set(controller, "vrHeadTrackingActive", true);
                Call(controller, "UpdateVrStudyFocusHud");
                Expect(hud.activeSelf && Bool(controller, "IsVrStudyFocusItem", first), "Tracking recovery restores the same learning card.");
                Phase(controller, "Error");
                Expect(Bool(controller, "CanLeaveVrStudyFocus"), "Audio failures expose a safe return path.");
                Call(controller, "EndVrStudyFocus", "Checks complete");
                Set(controller, "enableVrStudyMode", false);
                Expect(!Bool(controller, "UsesVrClickToStudy"), "Desktop study keeps its existing interaction mode.");
                Debug.Log("VR CLICK-TO-LEARN CHECK PASSED: " + checks + "; no Play Mode, no XR initialization, no scene/package writes.");
            }
            finally
            {
                if (controller != null)
                {
                    DestroyField(controller, "vrFocusedStudyHud");
                    DestroyField(controller, "vrFocusedStudyUiMaterial");
                    DestroyField(controller, "studyFurnitureOutlineRoot");
                    DestroyField(controller, "studyFurnitureOutlineMaterial");
                    Set(controller, "vrFocusedStudyItem", null);
                    Set(controller, "vrTrackedPoseDriver", null);
                }
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(rig);
                UnityEngine.Object.DestroyImmediate(image);
                RoomSpecCatalog.SetCurrentRoom(originalRoom);
            }
        }

        private static void SavePreview(MemoryPalaceExperimentController controller, Camera camera, GameObject hud, RawImage wordImage)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                throw new InvalidOperationException("Preview rendering requires a graphics device, not -nographics.");
            var target = new RenderTexture(1440, 1280, 24);
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            var previousTexture = wordImage.texture;
            var photo = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            var occluder = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var material = new Material(Shader.Find("Unlit/Color"));
            try
            {
                var resource = Resources.Load<Texture2D>("WordImages/estrella");
                if (resource != null) wordImage.texture = resource;
                material.color = new Color(0.8f, 0.82f, 0.84f);
                occluder.GetComponent<Renderer>().sharedMaterial = material;
                occluder.transform.position = camera.transform.position + camera.transform.forward * 0.5f;
                occluder.transform.localScale = new Vector3(10f, 10f, 0.1f);
                camera.targetTexture = target;
                camera.aspect = (float)target.width / target.height;
                camera.stereoTargetEye = StereoTargetEyeMask.None;
                Canvas.ForceUpdateCanvases();
                camera.Render();
                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture.active = target;
                photo.ReadPixels(new Rect(0f, 0f, target.width, target.height), 0, 0);
                photo.Apply();
                var card = (RectTransform)hud.transform.Find("LearningCard");
                var sample = camera.WorldToViewportPoint(card.TransformPoint(new Vector3(card.rect.xMin + 10f, 0f, 0f)));
                var color = photo.GetPixel(Mathf.Clamp((int)(sample.x * target.width), 0, target.width - 1),
                    Mathf.Clamp((int)(sample.y * target.height), 0, target.height - 1));
                Expect(color.r < 0.3f && color.g < 0.3f && color.b < 0.35f,
                    "Actual render keeps the dark learning card visible in front of nearer opaque room geometry. Sample=" + color);
                var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../vr-click-study-preview.png"));
                System.IO.File.WriteAllBytes(path, photo.EncodeToPNG());
                Debug.Log("VR FOCUS PREVIEW SAVED: " + path);
                var cleanSnapshot = (Texture2D)Call(controller, "CaptureCurrentCameraSnapshot", target.width, target.height);
                try
                {
                    var cleanPixel = cleanSnapshot.GetPixel((int)(sample.x * target.width), (int)(sample.y * target.height));
                    Expect(cleanPixel.r > 0.45f && cleanPixel.g > 0.45f && cleanPixel.b > 0.45f,
                        "The actual snapshot renderer omits the new foreground learning card. Sample=" + cleanPixel);
                    Expect(hud.activeSelf && hud.GetComponent<Canvas>().enabled,
                        "The learning card is restored after clean snapshot capture.");
                }
                finally { UnityEngine.Object.DestroyImmediate(cleanSnapshot); }
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                wordImage.texture = previousTexture;
                UnityEngine.Object.DestroyImmediate(occluder);
                UnityEngine.Object.DestroyImmediate(material);
                UnityEngine.Object.DestroyImmediate(photo);
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        private static FieldInfo Field(string name) => typeof(MemoryPalaceExperimentController).GetField(name, Private);
        private static object Get(object target, string name) => Field(name).GetValue(target);
        private static void Set(object target, string name, object value) => Field(name).SetValue(target, value);
        private static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private).Invoke(target, args);
        private static bool Bool(object target, string name, params object[] args) => (bool)Call(target, name, args);
        private static void Phase(object target, string name) => Set(target, "voiceRoutePhase", Enum.Parse(Field("voiceRoutePhase").FieldType, name));
        private static void DestroyField(object target, string name)
        {
            var value = Get(target, name) as UnityEngine.Object;
            if (value != null) UnityEngine.Object.DestroyImmediate(value);
            Set(target, name, null);
        }
        private static void Expect(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            checks++;
        }
        public static void RunBatch()
        {
            try
            {
                Run();
                ParticipantStoryEnglishChecks.Run();
                BuilderDeleteShortcutChecks.Run();
                EditorApplication.Exit(0);
            }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }
        public static void RunPreviewBatch()
        {
            exportPreview = true;
            RunBatch();
        }
    }
}
