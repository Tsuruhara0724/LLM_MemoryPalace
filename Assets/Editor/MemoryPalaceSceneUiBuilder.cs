using System;
using MemPalaceLLM;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MemPalaceLLM.Editor
{
    public static class MemoryPalaceSceneUiBuilder
    {
        // Mirror the two established IMGUI palettes instead of introducing a new design.
        private static readonly Color Page = new(0.10f, 0.12f, 0.17f, 0.94f);
        private static readonly Color Card = new(0.14f, 0.17f, 0.23f, 0.95f);
        private static readonly Color Field = new(0.11f, 0.12f, 0.15f, 1f);
        private static readonly Color Primary = new(0.10f, 0.38f, 0.42f, 1f);
        private static readonly Color TextColor = new(0.95f, 0.96f, 0.99f, 1f);
        private static readonly Color Muted = new(0.70f, 0.76f, 0.84f, 1f);
        private static readonly Color Disabled = new(0.28f, 0.31f, 0.36f, 0.70f);
        private static readonly Color LightPage = new(0.94f, 0.94f, 0.91f, 0.98f);
        private static readonly Color LightCard = new(1f, 1f, 0.98f, 0.98f);
        private static readonly Color LightInk = new(0.13f, 0.18f, 0.20f, 1f);
        private static readonly Color LightMuted = new(0.34f, 0.39f, 0.39f, 1f);
        private static readonly Color LightSecondary = new(0.88f, 0.89f, 0.85f, 1f);
        private static readonly Color DarkButton = new(0.16f, 0.17f, 0.20f, 1f);
        private static Font defaultFont;
        private static Sprite defaultUiSprite;
        private static int generatedNameIndex;

        [MenuItem("Tools/Memory Palace/Bake Legacy UI Into SampleScene")]
        public static void RebuildEditableSceneUi()
        {
            var controller = Object.FindFirstObjectByType<MemoryPalaceExperimentController>();
            if (controller == null && Application.isBatchMode)
            {
                const string sampleScenePath = "Assets/Scenes/SampleScene.unity";
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(sampleScenePath) != null)
                {
                    EditorSceneManager.OpenScene(sampleScenePath, OpenSceneMode.Single);
                    controller = Object.FindFirstObjectByType<MemoryPalaceExperimentController>();
                }
            }
            if (controller == null)
            {
                if (Application.isBatchMode)
                {
                    Debug.LogError("Memory Palace Scene UI: no MemoryPalaceExperimentController exists in SampleScene.");
                    return;
                }
                EditorUtility.DisplayDialog(
                    "Memory Palace Scene UI",
                    "No MemoryPalaceExperimentController exists in the open Scene.",
                    "OK");
                return;
            }

            var oldRoot = FindSceneObjectIncludingInactive("EditableSceneUI");
            if (oldRoot != null)
            {
                Undo.DestroyObjectImmediate(oldRoot);
            }
            var oldWorldSpaceRoot = FindSceneObjectIncludingInactive("EditableWorldSpaceUiTemplates");
            if (oldWorldSpaceRoot != null)
            {
                Undo.DestroyObjectImmediate(oldWorldSpaceRoot);
            }

            defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            defaultUiSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            generatedNameIndex = 0;
            var canvasObject = CreateObject("EditableSceneUI", controller.transform.parent);
            Undo.RegisterCreatedObjectUndo(canvasObject, "Build Memory Palace Scene UI");
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 20;
            var scaler = canvasObject.AddComponent<CanvasScaler>();
            // IMGUI used screen-pixel rectangles; this preserves its original panel widths.
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;
            canvasObject.AddComponent<GraphicRaycaster>();
            Stretch(canvasObject.GetComponent<RectTransform>());

            var view = canvasObject.AddComponent<MemoryPalaceSceneUiView>();
            CreateHeader(canvasObject.transform);
            var stageRoot = CreateObject("StageRoot", canvasObject.transform);
            Stretch(stageRoot.GetComponent<RectTransform>(), 0f, 0f, 0f, 86f);

            BuildSetup(stageRoot.transform);
            BuildRoomBuilder(stageRoot.transform);
            BuildFamiliarization(stageRoot.transform);
            BuildPreTest(stageRoot.transform);
            BuildGeneration(stageRoot.transform);
            BuildStoryAuthoring(stageRoot.transform);
            BuildFurnitureAssignment(stageRoot.transform);
            BuildStudy(stageRoot.transform);
            BuildRecall(stageRoot.transform);
            BuildQuestionnaire(stageRoot.transform);
            BuildResult(stageRoot.transform);
            ApplyLegacyDarkButtonTheme(stageRoot.transform);
            ApplyLegacyLightTheme(stageRoot.transform.Find("Stage_Setup"));
            ApplyLegacyLightTheme(stageRoot.transform.Find("Stage_RoomBuilder"));
            BuildOverlays(canvasObject.transform);
            var worldSpaceTemplates = BuildWorldSpaceTemplates(controller.transform.parent);

            view.AssignSceneReferences(canvas, stageRoot);
            view.AssignWorldSpaceTemplates(
                worldSpaceTemplates.studyPanel,
                worldSpaceTemplates.startGate,
                worldSpaceTemplates.studyHud,
                worldSpaceTemplates.wordImageHud,
                worldSpaceTemplates.recallPanel);
            view.ShowOnlyStage("Stage_Setup");
            AssignControllerReference(controller, view);
            EnsureEventSystem();

            var scene = controller.gameObject.scene;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = canvasObject;
            Debug.Log(
                "Memory Palace: baked the legacy IMGUI layout into an editable Canvas and saved the Scene. " +
                "Adjust panels under EditableSceneUI/StageRoot in the Inspector; rebake only when you want to reset those edits.");
        }

        [MenuItem("Tools/Memory Palace/Validate Editable Scene UI")]
        public static void ValidateEditableSceneUi()
        {
            if (Application.isBatchMode)
            {
                const string sampleScenePath = "Assets/Scenes/SampleScene.unity";
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(sampleScenePath) != null &&
                    EditorSceneManager.GetActiveScene().path != sampleScenePath)
                {
                    EditorSceneManager.OpenScene(sampleScenePath, OpenSceneMode.Single);
                }
            }

            var controller = Object.FindFirstObjectByType<MemoryPalaceExperimentController>();
            var view = Object.FindFirstObjectByType<MemoryPalaceSceneUiView>(FindObjectsInactive.Include);
            var errors = new System.Collections.Generic.List<string>();
            if (controller == null)
            {
                errors.Add("MemoryPalaceExperimentController is missing.");
            }
            if (view == null || !view.RebuildLookup())
            {
                errors.Add("MemoryPalaceSceneUiView is missing or incomplete.");
            }

            if (view != null)
            {
                var requiredObjects = new[]
                {
                    "Stage_Setup", "Stage_RoomBuilder", "Stage_RoomFamiliarization", "Stage_PreTest",
                    "Stage_Generation", "Stage_StoryAuthoring", "Stage_SelfAuthoring", "Stage_Study",
                    "Stage_Recall", "Stage_Questionnaire", "Stage_Result", "Common_Header",
                    "Setup_Start", "Room_Done", "Familiarization_Done", "PreTest_Submit",
                    "Generation_Confirm", "Story_Continue", "Assignment_RightPanel_Scrollbar",
                    "Assignment_EnterStudy", "Study_Finish", "Recall_QuestionGroup",
                    "Questionnaire_Finish", "Result_Return", "Overlay_Context", "Overlay_Subtitle"
                };
                for (var i = 0; i < requiredObjects.Length; i++)
                {
                    if (!view.TryGetObject(requiredObjects[i], out _))
                    {
                        errors.Add("Missing UI object: " + requiredObjects[i]);
                    }
                }

                if (view.StageRoot == null || view.StageRoot.transform.childCount != 11)
                {
                    errors.Add("StageRoot must contain exactly 11 stage panels.");
                }
                if (view.Get<InputField>("Setup_ParticipantInput") == null ||
                    view.Get<InputField>("PreTest_Answer") == null ||
                    view.Get<InputField>("Story_FullText") == null)
                {
                    errors.Add("One or more required InputField controls are missing.");
                }
                if (view.Get<Slider>("Questionnaire_Mental") == null ||
                    view.Get<Slider>("Questionnaire_Trust") == null)
                {
                    errors.Add("Questionnaire sliders are missing.");
                }
                if (view.Get<Scrollbar>("Assignment_RightPanel_Scrollbar") == null)
                {
                    errors.Add("Furniture assignment right scrollbar is missing.");
                }
                if (view.VrStudyPanelTemplate == null ||
                    view.VrStudyStartGateTemplate == null ||
                    view.VrStudyHudTemplate == null ||
                    view.VrStudyWordImageHudTemplate == null ||
                    view.VrRecallPanelTemplate == null)
                {
                    errors.Add("One or more HMD UI templates are missing.");
                }

                var names = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
                var transforms = view.GetComponentsInChildren<Transform>(true);
                for (var i = 0; i < transforms.Length; i++)
                {
                    if (!names.Add(transforms[i].name))
                    {
                        errors.Add("Duplicate desktop UI object name: " + transforms[i].name);
                    }
                }
            }

            if (controller != null && view != null)
            {
                var serialized = new SerializedObject(controller);
                if (serialized.FindProperty("editableSceneUi").objectReferenceValue != view)
                {
                    errors.Add("Controller editableSceneUi reference is not assigned to the Scene view.");
                }
                if (!serialized.FindProperty("useEditableSceneUi").boolValue)
                {
                    errors.Add("Controller Use Editable Scene UI is disabled.");
                }
            }

            if (Object.FindFirstObjectByType<EventSystem>() == null)
            {
                errors.Add("EventSystem is missing.");
            }

            if (errors.Count > 0)
            {
                var message = "Memory Palace editable Scene UI validation failed:\n- " + string.Join("\n- ", errors);
                Debug.LogError(message);
                if (Application.isBatchMode)
                {
                    throw new BuildFailedException(message);
                }
                EditorUtility.DisplayDialog("Scene UI Validation", message, "OK");
                return;
            }

            Debug.Log("Memory Palace editable Scene UI validation passed: 11 desktop stages, scrollbar, overlays, controller binding, EventSystem, and 5 HMD templates are ready.");
            if (!Application.isBatchMode)
            {
                EditorUtility.DisplayDialog("Scene UI Validation", "All editable Scene UI checks passed.", "OK");
            }
        }

        private static void CreateHeader(Transform parent)
        {
            var header = CreatePanel(parent, "Common_Header", new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(16f, -72f), new Vector2(-16f, -14f), LightPage, true);
            AddTextAbsolute(header, "Common_Title", "Memory Palace", 24, FontStyle.Bold,
                new Vector2(18f, 4f), new Vector2(520f, 29f), TextAnchor.MiddleLeft, LightInk);
            AddTextAbsolute(header, "Common_Status", "Set up a vocabulary study session", 14, FontStyle.Normal,
                new Vector2(20f, 33f), new Vector2(1120f, 19f), TextAnchor.MiddleLeft, LightMuted);

            var badge = CreatePanel(header, "Common_StageBadge", Vector2.one, Vector2.one,
                new Vector2(-164f, -43f), new Vector2(-24f, -13f), LightSecondary, false);
            AddTextAbsolute(badge, "Common_Stage", "Step 1 / 7", 14, FontStyle.Normal,
                Vector2.zero, new Vector2(140f, 30f), TextAnchor.MiddleCenter, LightInk);
        }

        private static void BuildSetup(Transform stageRoot)
        {
            var content = CreateCenteredScrollStage(stageRoot, "Stage_Setup", 1080f);
            AddHeading(content, "Setup_Title", "Set up this session");
            AddBody(content, "Setup_Intro", "Choose one condition in the 2 x 2 room-source x story-source design.");
            var top = AddGrid(content, "Setup_TopRow", 2, 492f, 296f, 302f);
            var session = AddVerticalGroup(top, "Setup_SessionCard", 296f, LightCard);
            AddSectionLabel(session, "Session");
            AddBody(session, "Setup_ParticipantLabel", "Participant ID");
            AddInput(session, "Setup_ParticipantInput", "P001", false, 50f);

            var condition = AddVerticalGroup(top, "Setup_ConditionCard", 296f, LightCard);
            AddSectionLabel(condition, "Choose your study");
            var conditionGrid = AddGrid(condition, "Setup_ConditionGrid", 2, 226f, 72f, 152f);
            AddButton(conditionGrid, "Setup_Condition1", "1 - Self room\nSelf story", 70f);
            AddButton(conditionGrid, "Setup_Condition2", "2 - Self room\nLLM story", 70f);
            AddButton(conditionGrid, "Setup_Condition3", "3 - Example room\nSelf story", 70f);
            AddButton(conditionGrid, "Setup_Condition4", "4 - Example room\nLLM story", 70f);
            AddBody(condition, "Setup_ConditionDescription", string.Empty, 58f);

            var middle = AddGrid(content, "Setup_MiddleRow", 2, 492f, 272f, 278f);
            var vocabulary = AddVerticalGroup(middle, "Setup_VocabularyCard", 272f, LightCard);
            AddSectionLabel(vocabulary, "Vocabulary");
            AddText(vocabulary, "Setup_VocabularyName", "Formal 32-Word Pool", 18, FontStyle.Bold, LightInk, 40f);
            AddBody(vocabulary, "Setup_VocabularyDescription",
                "The fixed formal vocabulary pool is used for every participant session.", 84f);

            var room = AddVerticalGroup(middle, "Setup_RoomCard", 272f, LightCard);
            AddSectionLabel(room, "Room");
            AddBody(room, "Setup_RoomDescription", string.Empty, 58f);
            var saved = AddVerticalGroup(room, "Setup_SavedRoomGroup", 140f);
            AddBody(saved, "Setup_SavedRoomLabel", "Reuse saved room JSON (optional)", 30f);
            AddInput(saved, "Setup_RoomLoadInput", "P001_room.json", false, 48f);
            AddButton(saved, "Setup_StartSaved", "Start with saved room JSON", 50f);
            AddButton(room, "Setup_ReloadExample", "Reload example_room.json", 50f).gameObject.SetActive(false);

            var next = AddVerticalGroup(content, "Setup_NextCard", 202f, LightCard);
            AddSectionLabel(next, "Next");
            AddBody(next, "Setup_NextHint", string.Empty, 64f);
            AddButton(next, "Setup_Start", "Start", 52f, true, "Setup_StartLabel");
            AddButton(next, "Setup_Researcher", "Researcher settings", 44f);
        }

        private static void BuildRoomBuilder(Transform stageRoot)
        {
            var root = CreateStageRoot(stageRoot, "Stage_RoomBuilder");
            var left = CreateScrollPanel(root.transform, "Room_LeftPanel", new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(18f, 0f), new Vector2(518f, 0f), LightPage);
            AddHeading(left, "Room_LeftHeading", "Make this room yours");
            AddBody(left, "Room_LeftIntro", "Shape a familiar room and fill it with furniture you can remember. Items snap into place for you.", 64f);

            var main = AddVerticalGroup(left, "Room_MainCard", 440f, LightCard);
            var modes = AddGrid(main, "Room_ModeGrid", 4, 102f, 54f, 58f);
            AddButton(modes, "Room_ModeFloor", "1 Shape", 50f);
            AddButton(modes, "Room_ModeWall", "2 Walls", 50f);
            AddButton(modes, "Room_ModeFurniture", "3 Furnish", 50f);
            AddButton(modes, "Room_ModeSelect", "4 Finish", 50f);
            AddText(main, "Room_Title", string.Empty, 20, FontStyle.Bold, LightInk, 38f);
            AddBody(main, "Room_Instructions", string.Empty, 72f);
            AddText(main, "Room_BuildArea", string.Empty, 14, FontStyle.Italic, Primary, 58f);
            var nav = AddGrid(main, "Room_NavigationGrid", 3, 138f, 44f, 50f);
            AddButton(nav, "Room_Center", "Center view", 46f);
            AddButton(nav, "Room_Undo", "Undo", 46f);
            AddButton(nav, "Room_Redo", "Redo", 46f);
            AddButton(main, "Room_ToggleOptions", "Room options", 44f, false, "Room_ToggleOptionsLabel");
            AddText(main, "Room_Count", string.Empty, 13, FontStyle.Italic, Primary, 48f);

            var options = AddVerticalGroup(left, "Room_OptionsGroup", 570f, LightCard);
            AddSectionLabel(options, "Change the starting shape");
            var shapes = AddGrid(options, "Room_ShapeGrid", 2, 202f, 44f, 50f);
            AddButton(shapes, "Room_FillArea", "Fill build area", 46f);
            AddButton(shapes, "Room_LShape", "L-shaped room", 46f);
            AddSectionLabel(options, "Save and reuse room JSON");
            AddBody(options, "Room_SaveHint", "Every save creates a new JSON file. Finishing the room also saves automatically.", 54f);
            AddBody(options, "Room_ArchiveLabel", "Room archive name", 28f);
            AddInput(options, "Room_ArchiveName", "P001_room", false, 44f);
            var saveGrid = AddGrid(options, "Room_SaveGrid", 2, 202f, 44f, 50f);
            AddButton(saveGrid, "Room_Save", "Save named JSON", 44f);
            AddButton(saveGrid, "Room_SaveParticipant", "Save with Participant ID", 44f);
            AddText(options, "Room_LastSaved", string.Empty, 13, FontStyle.Italic, Primary, 30f);
            AddBody(options, "Room_LoadLabel", "JSON file name to load", 28f);
            AddInput(options, "Room_LoadName", "room.json", false, 44f);
            var loadGrid = AddGrid(options, "Room_LoadGrid", 2, 202f, 44f, 100f);
            AddButton(loadGrid, "Room_Load", "Load JSON by name", 44f);
            AddButton(loadGrid, "Room_LoadLatest", "Load latest JSON", 44f);
            AddButton(loadGrid, "Room_Refresh", "Refresh saved file list", 44f);
            options.gameObject.SetActive(false);

            var furnitureSection = AddVerticalGroup(left, "Room_FurnitureSection", 522f, LightCard);
            AddSectionLabel(furnitureSection, "Pick something to place");
            AddBody(furnitureSection, "Room_FurnitureHint", "Choose an item below, then click its green preview in the room.", 54f);
            var furniture = AddGrid(furnitureSection, "Room_FurnitureGrid", 2, 202f, 52f, 420f);
            var names = new[] { "Door", "Bed", "Wardrobe", "Desk", "Chair", "Bookshelf", "Bathtub", "Sofa", "Television", "Air Conditioner", "Window", "Lamp", "Plant", "Toilet" };
            for (var i = 0; i < names.Length; i++)
            {
                AddButton(furniture, $"Room_Furniture_{i}", names[i], 50f, false, $"Room_FurnitureLabel_{i}");
            }
            furnitureSection.gameObject.SetActive(false);

            var selected = AddVerticalGroup(left, "Room_SelectedGroup", 150f, LightCard);
            AddText(selected, "Room_SelectedName", "Selected furniture", 18, FontStyle.Bold, LightInk, 34f);
            var selectedGrid = AddGrid(selected, "Room_SelectedButtons", 3, 128f, 44f, 50f);
            AddButton(selectedGrid, "Room_Move", "Move", 46f, true, "Room_MoveLabel");
            AddButton(selectedGrid, "Room_Rotate", "Rotate", 46f);
            AddButton(selectedGrid, "Room_Remove", "Remove", 46f);
            selected.gameObject.SetActive(false);
            AddText(left, "Room_Status", string.Empty, 14, FontStyle.Italic, Primary, 72f);

            var hud = CreatePanel(root.transform, "Room_Hud", new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(536f, -120f), new Vector2(-18f, -12f), LightPage, true);
            AddTextAbsolute(hud, "Room_HudTitle", "Draw the room", 20, FontStyle.Bold,
                new Vector2(18f, 10f), new Vector2(630f, 30f), TextAnchor.MiddleLeft, LightInk);
            AddTextAbsolute(hud, "Room_HudHint", "Left-drag add | Right-click erase | 1-4 step | WASD move | Q/E zoom | Hold middle mouse to rotate view", 14, FontStyle.Normal,
                new Vector2(18f, 45f), new Vector2(860f, 28f), TextAnchor.MiddleLeft, LightMuted);
            AddButtonAbsolute(hud, "Room_Done", "Done - Continue", new Vector2(-208f, 28f), new Vector2(180f, 58f), true, "Room_DoneLabel");
        }

        private static void BuildFamiliarization(Transform stageRoot)
        {
            var root = CreateStageRoot(stageRoot, "Stage_RoomFamiliarization");
            var content = CreateScrollPanel(root.transform, "Familiarization_Panel", new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(18f, -248f), new Vector2(408f, -18f), Page);
            AddHeading(content, "Familiarization_Title", "Explore the example room");
            AddBody(content, "Familiarization_Instructions", "WASD: move | Right mouse drag: look. Become familiar with the furniture and layout; no target words are shown.", 90f);
            AddText(content, "Familiarization_Timer", string.Empty, 15, FontStyle.Normal, TextColor, 54f);
            AddButton(content, "Familiarization_Done", "Finish Room Familiarization - Pre-test", 60f, true);
        }

        private static void BuildPreTest(Transform stageRoot)
        {
            var content = CreateFullWidthScrollStage(stageRoot, "Stage_PreTest");
            AddHeading(content, "PreTest_Title", "Spanish Word Pre-test");
            AddBody(content, "PreTest_Instructions", "Type the English meaning. Leave it blank if you do not know. Screening stops after eight unknown words; no correctness feedback is shown.", 76f);
            var question = AddVerticalGroup(content, "PreTest_QuestionGroup", 330f, Card);
            AddText(question, "PreTest_Counter", "Candidate 1 / 8", 18, FontStyle.Bold, Muted, 34f);
            AddText(question, "PreTest_Word", "palabra", 38, FontStyle.Bold, TextColor, 64f, TextAnchor.MiddleCenter);
            AddText(question, "PreTest_Timer", string.Empty, 14, FontStyle.Normal, Muted, 38f);
            AddInput(question, "PreTest_Answer", "English meaning", false, 56f);
            AddButton(question, "PreTest_Submit", "Submit And Continue", 58f, true);
            var error = AddVerticalGroup(content, "PreTest_ErrorGroup", 120f, Card);
            AddBody(error, "PreTest_Error", string.Empty, 100f);
        }

        private static void BuildGeneration(Transform stageRoot)
        {
            var content = CreateFullWidthScrollStage(stageRoot, "Stage_Generation");
            AddHeading(content, "Generation_Title", "Choose one LLM-generated story");
            AddBody(content, "Generation_Intro", "Read three complete continuous stories and select exactly one. The selected story is used unchanged for furniture mapping and VR narration.", 72f);
            AddText(content, "Generation_Timer", string.Empty, 14, FontStyle.Normal, Muted, 34f);
            var progress = AddVerticalGroup(content, "Generation_ProgressGroup", 118f, Card);
            AddBody(progress, "Generation_Progress", string.Empty, 58f);
            AddButton(progress, "Generation_Cancel", "Cancel Story Generation", 46f);
            AddBody(content, "Generation_Error", string.Empty, 48f);
            for (var i = 0; i < 3; i++)
            {
                var card = AddVerticalGroup(content, $"Generation_Card_{i}", 590f, Card);
                AddText(card, $"Generation_CardTitle_{i}", $"Story {i + 1}", 22, FontStyle.Bold, TextColor, 38f);
                var storyText = AddText(card, $"Generation_Story_{i}", string.Empty, 18, FontStyle.Normal, TextColor, 450f);
                storyText.lineSpacing = 1.18f;
                var actions = AddGrid(card, $"Generation_Actions_{i}", 2, 590f, 52f, 52f);
                AddButton(actions, $"Generation_Copy_{i}", "Copy Story Text", 50f);
                AddButton(actions, $"Generation_Select_{i}", $"Select Story {i + 1}", 50f, false, $"Generation_SelectLabel_{i}");
            }
            AddButton(content, "Generation_Confirm", "Confirm Selected Story - Map Furniture And Words", 58f, true);
            AddButton(content, "Generation_Retry", "Retry Three Story Candidates", 52f);
            AddButton(content, "Generation_Back", "Back to Setup", 48f);
        }

        private static void BuildStoryAuthoring(Transform stageRoot)
        {
            var content = CreateFullWidthScrollStage(stageRoot, "Stage_StoryAuthoring");
            AddHeading(content, "Story_Title", "Write your story");
            AddBody(content, "Story_Intro", "Write one complete English story using every English target word. Then split it and classify every sentence.", 58f);
            AddText(content, "Story_Timer", string.Empty, 14, FontStyle.Normal, Muted, 34f);
            AddSectionLabel(content, "English target words");
            AddText(content, "Story_TargetWords", string.Empty, 18, FontStyle.Bold, Primary, 90f);
            AddInput(content, "Story_FullText", "Write the whole story here...", true, 220f);
            var buttons = AddGrid(content, "Story_ActionGrid", 2, 590f, 52f, 58f);
            AddButton(buttons, "Story_Split", "Split Story Into Sentences", 50f, false, "Story_SplitLabel");
            AddButton(buttons, "Story_ClearAssignments", "Clear Sentence Assignments", 50f);
            AddBody(content, "Story_SplitStatus", string.Empty, 48f);
            AddSectionLabel(content, "Classify each sentence");
            var sentenceRows = AddVerticalGroup(content, "Story_SentenceRows", 10f);
            Object.DestroyImmediate(sentenceRows.GetComponent<LayoutElement>());
            sentenceRows.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var templateObject = CreateObject("Story_SentenceTemplate", sentenceRows);
            var templateLayout = templateObject.AddComponent<VerticalLayoutGroup>();
            templateLayout.padding = new RectOffset(12, 12, 10, 10);
            templateLayout.spacing = 8f;
            templateLayout.childControlHeight = true;
            templateLayout.childForceExpandHeight = false;
            templateObject.AddComponent<Image>().color = Card;
            templateObject.AddComponent<LayoutElement>().preferredHeight = 128f;
            var sentenceLabel = AddText(templateObject.transform, "Story_SentenceTemplateLabel", "Sentence", 15, FontStyle.Normal, TextColor, 62f);
            var dropdown = AddDropdown(templateObject.transform, "Story_SentenceTemplateDropdown", 48f);
            var row = templateObject.AddComponent<MemoryPalaceSentenceAssignmentRow>();
            row.AssignSceneReferences(sentenceLabel, dropdown);
            templateObject.SetActive(false);
            AddSectionLabel(content, "Word segment preview");
            AddText(content, "Story_Preview", string.Empty, 15, FontStyle.Normal, TextColor, 360f);
            AddBody(content, "Story_Readiness", string.Empty, 52f);
            AddButton(content, "Story_Continue", "Continue: Assign Words To Furniture On PC", 60f, true);
            AddButton(content, "Story_Back", "Back To Setup", 48f);
        }

        private static void BuildFurnitureAssignment(Transform stageRoot)
        {
            var root = CreateStageRoot(stageRoot, "Stage_SelfAuthoring");
            var left = CreateScrollPanel(root.transform, "Assignment_LeftPanel", new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(18f, 0f), new Vector2(428f, 0f), Page);
            AddHeading(left, "Assignment_Title", "Map Furniture And Words");
            var intro = AddText(left, "Assignment_Intro", "Use WASD and right-drag to look, then click an actual furniture model. Choose one unused word in the right panel.", 18, FontStyle.Normal, Muted, 112f);
            intro.lineSpacing = 1.08f;
            AddText(left, "Assignment_Count", string.Empty, 22, FontStyle.Bold, TextColor, 44f);
            AddText(left, "Assignment_Timer", string.Empty, 18, FontStyle.Normal, Muted, 56f);
            var summary = AddText(left, "Assignment_Summary", string.Empty, 18, FontStyle.Normal, Muted, 350f);
            summary.lineSpacing = 1.12f;
            AddButton(left, "Assignment_EnterStudy", "Finish Assignment And Enter VR Study", 64f, true, "Assignment_EnterLabel");
            AddBody(left, "Assignment_Status", string.Empty, 86f);
            AddButton(left, "Assignment_Leave", "Leave without finishing", 46f);

            var right = CreateScrollPanel(root.transform, "Assignment_RightPanel", new Vector2(1f, 0f), new Vector2(1f, 1f),
                new Vector2(-448f, 0f), new Vector2(-18f, 0f), Page);
            AddText(right, "Assignment_SelectedFurniture", "Click Furniture", 28, FontStyle.Bold, TextColor, 50f);
            AddBody(right, "Assignment_SelectHint", "Click directly on a furniture model. Its two-column word card appears here.", 74f);
            var selected = AddVerticalGroup(right, "Assignment_SelectedGroup", 710f);
            AddSectionLabel(selected, "Choose one word for this furniture");
            AddBody(selected, "Assignment_Hint", "Used words are disabled. Clear an assignment first when rearranging completed choices.", 66f);
            AddText(selected, "Assignment_Current", "No word assigned", 17, FontStyle.Bold, TextColor, 42f);
            AddButton(selected, "Assignment_Clear", "Clear current assignment", 48f);
            var wordGrid = AddGrid(selected, "Assignment_WordGrid", 2, 192f, 66f, 288f);
            for (var i = 0; i < 8; i++)
            {
                AddButton(wordGrid, $"Assignment_Word_{i}", "Word", 64f, false, $"Assignment_WordLabel_{i}");
            }
            AddRawImage(selected, "Assignment_Image", 250f);
        }

        private static void BuildStudy(Transform stageRoot)
        {
            var root = CreateStageRoot(stageRoot, "Stage_Study");
            var left = CreateScrollPanel(root.transform, "Study_LeftPanel", new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(18f, -650f), new Vector2(404f, 0f), Page);
            AddHeading(left, "Study_Title", "VR Memory Palace Study");
            AddText(left, "Study_Info", string.Empty, 14, FontStyle.Normal, TextColor, 176f);
            AddSectionLabel(left, "Guided voice route");
            AddBody(left, "Study_VoiceStatus", string.Empty, 66f);
            var voice = AddGrid(left, "Study_VoiceGrid", 2, 175f, 48f, 54f);
            AddButton(voice, "Study_Replay", "Replay Voice", 46f);
            AddButton(voice, "Study_Restart", "Restart Route", 46f);
            AddText(left, "Study_Story", string.Empty, 13, FontStyle.Normal, Muted, 130f);
            AddButton(left, "Study_Finish", "Finish Study And Start Immediate Post-test", 56f, true);
            AddBody(left, "Study_ProgressHint", string.Empty, 62f);
            AddButton(left, "Study_Back", "Back to Setup", 44f);

            var right = CreateScrollPanel(root.transform, "Study_RightPanel", new Vector2(1f, 0f), new Vector2(1f, 1f),
                new Vector2(-448f, 0f), new Vector2(-18f, 0f), Page);
            var selected = AddVerticalGroup(right, "Study_SelectedGroup", 760f);
            AddText(selected, "Study_SelectedInfo", string.Empty, 16, FontStyle.Normal, TextColor, 190f);
            AddRawImage(selected, "Study_WordImage", 250f);
            AddButton(selected, "Study_Pronounce", "Pronounce Spanish Word", 48f);
            AddRawImage(selected, "Study_Snapshot", 180f);
            AddButton(selected, "Study_Capture", "Capture Scene Snapshot", 52f, true, "Study_CaptureLabel");
        }

        private static void BuildRecall(Transform stageRoot)
        {
            var content = CreateFullWidthScrollStage(stageRoot, "Stage_Recall");
            AddHeading(content, "Recall_Title", "Immediate Post-test");
            AddBody(content, "Recall_Instructions", string.Empty, 60f);
            AddText(content, "Recall_Timer", string.Empty, 14, FontStyle.Normal, Muted, 34f);
            var empty = AddVerticalGroup(content, "Recall_EmptyGroup", 190f, Card);
            AddBody(empty, "Recall_EmptyMessage", string.Empty, 80f);
            AddButton(empty, "Recall_ContinueQuestionnaire", "Continue To Questionnaire", 48f, true);
            var question = AddVerticalGroup(content, "Recall_QuestionGroup", 720f, Card);
            AddText(question, "Recall_Counter", string.Empty, 18, FontStyle.Bold, Muted, 34f);
            AddText(question, "Recall_Target", string.Empty, 30, FontStyle.Bold, TextColor, 58f);
            var typed = AddVerticalGroup(question, "Recall_TypedGroup", 130f);
            AddInput(typed, "Recall_TypedAnswer", "English meaning", false, 54f);
            AddButton(typed, "Recall_SubmitTyped", "Submit And Continue", 52f, true);
            var options = AddGrid(question, "Recall_OptionsGroup", 2, 570f, 236f, 486f);
            for (var i = 0; i < 4; i++)
            {
                var option = CreateObject($"Recall_Option_{i}", options);
                option.AddComponent<Image>().color = Card;
                option.AddComponent<Button>().targetGraphic = option.GetComponent<Image>();
                option.AddComponent<LayoutElement>().preferredHeight = 230f;
                var image = CreateObject($"Recall_OptionImage_{i}", option.transform);
                var raw = image.AddComponent<RawImage>();
                raw.color = Color.white;
                raw.raycastTarget = false;
                SetAnchored(image.GetComponent<RectTransform>(), new Vector2(0f, 0.24f), new Vector2(1f, 1f), new Vector2(8f, 4f), new Vector2(-8f, -8f));
                AddTextAbsolute(option.transform, $"Recall_OptionLabel_{i}", "Option", 14, FontStyle.Normal,
                    new Vector2(8f, 4f), new Vector2(-8f, 48f), TextAnchor.MiddleCenter, TextColor, true);
            }
            var feedback = AddVerticalGroup(question, "Recall_FeedbackGroup", 110f, Card);
            AddBody(feedback, "Recall_Feedback", string.Empty, 92f);
            AddButton(question, "Recall_Next", "Next", 52f, true, "Recall_NextLabel");
            AddButton(content, "Recall_Back", "Back to Study Room", 46f);
        }

        private static void BuildQuestionnaire(Transform stageRoot)
        {
            var content = CreateFullWidthScrollStage(stageRoot, "Stage_Questionnaire");
            AddHeading(content, "Questionnaire_Title", "Post-Session Questionnaire");
            AddBody(content, "Questionnaire_Intro", "NASA-TLX style workload ratings and study-experience quality ratings.", 54f);
            AddText(content, "Questionnaire_Timer", string.Empty, 14, FontStyle.Normal, Muted, 34f);
            AddQuestionnaireSlider(content, "Questionnaire_Mental", "Mental Demand", 0f, 20f);
            AddQuestionnaireSlider(content, "Questionnaire_Physical", "Physical Demand", 0f, 20f);
            AddQuestionnaireSlider(content, "Questionnaire_Temporal", "Temporal Demand", 0f, 20f);
            AddQuestionnaireSlider(content, "Questionnaire_Performance", "Performance", 0f, 20f);
            AddQuestionnaireSlider(content, "Questionnaire_Effort", "Effort", 0f, 20f);
            AddQuestionnaireSlider(content, "Questionnaire_Frustration", "Frustration", 0f, 20f);
            AddSectionLabel(content, "Study experience quality ratings (1-7)");
            AddQuestionnaireSlider(content, "Questionnaire_Vividness", "Vividness", 1f, 7f);
            AddQuestionnaireSlider(content, "Questionnaire_Helpfulness", "Helpfulness", 1f, 7f);
            AddQuestionnaireSlider(content, "Questionnaire_Trust", "Trust", 1f, 7f);
            AddSectionLabel(content, "Notes");
            AddInput(content, "Questionnaire_Notes", "Optional notes", true, 130f);
            AddButton(content, "Questionnaire_Finish", "Finish Session And Export", 60f, true);
            AddButton(content, "Questionnaire_Back", "Back to Study Room", 46f);
        }

        private static void BuildResult(Transform stageRoot)
        {
            var content = CreateFullWidthScrollStage(stageRoot, "Stage_Result");
            AddHeading(content, "Result_Title", "Session Summary");
            AddText(content, "Result_Summary", string.Empty, 15, FontStyle.Normal, TextColor, 330f);
            AddSectionLabel(content, "Export paths");
            AddText(content, "Result_Exports", string.Empty, 13, FontStyle.Normal, Muted, 180f);
            AddSectionLabel(content, "Story items");
            AddText(content, "Result_Items", string.Empty, 14, FontStyle.Normal, TextColor, 520f);
            AddBody(content, "Result_Warning", "This Unity Play contains one session. Stop Play before starting another participant session.", 56f);
            AddButton(content, "Result_Return", "Return to Setup", 52f);
        }

        private static void BuildOverlays(Transform canvas)
        {
            var flash = CreatePanel(canvas, "Overlay_Flash", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                new Color(1f, 1f, 1f, 0.8f), true);
            flash.gameObject.AddComponent<CanvasGroup>();
            AddTextAbsolute(flash, "Overlay_FlashText", "Snapshot Stored", 26, FontStyle.Bold,
                new Vector2(-180f, -28f), new Vector2(180f, 28f), TextAnchor.MiddleCenter, Color.black, false, true);
            flash.gameObject.SetActive(false);

            var teleport = CreatePanel(canvas, "Overlay_Teleport", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                new Color(0.05f, 0.07f, 0.12f, 0.96f), true);
            teleport.gameObject.AddComponent<CanvasGroup>();
            AddTextAbsolute(teleport, "Overlay_TeleportText", "Returning To Correct Anchor...", 26, FontStyle.Bold,
                new Vector2(-260f, -28f), new Vector2(260f, 28f), TextAnchor.MiddleCenter, TextColor, false, true);
            teleport.gameObject.SetActive(false);

            var context = CreatePanel(canvas, "Overlay_Context", new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(452f, -170f), new Vector2(872f, -84f), Page, true);
            context.gameObject.AddComponent<CanvasGroup>();
            AddTextAbsolute(context, "Overlay_ContextTitle", string.Empty, 17, FontStyle.Bold,
                new Vector2(20f, 8f), new Vector2(-16f, 34f), TextAnchor.MiddleLeft, Primary, true);
            AddTextAbsolute(context, "Overlay_ContextBody", string.Empty, 13, FontStyle.Normal,
                new Vector2(20f, 36f), new Vector2(-16f, -8f), TextAnchor.UpperLeft, Muted, true);
            context.gameObject.SetActive(false);

            var subtitle = CreatePanel(canvas, "Overlay_Subtitle", new Vector2(0.22f, 0f), new Vector2(0.78f, 0f),
                new Vector2(0f, 8f), new Vector2(0f, 118f), Page, true);
            AddTextAbsolute(subtitle, "Overlay_SubtitleText", string.Empty, 19, FontStyle.Normal,
                new Vector2(20f, 10f), new Vector2(-82f, -10f), TextAnchor.MiddleLeft, TextColor, true);
            AddButtonAbsolute(subtitle, "Overlay_SubtitleAdvance", ">", new Vector2(-68f, 26f), new Vector2(52f, 76f), true);
            subtitle.gameObject.SetActive(false);

            var wordImage = CreatePanel(canvas, "Overlay_WordImage", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-210f, -190f), new Vector2(210f, 190f), Page, true);
            AddTextAbsolute(wordImage, "Overlay_WordImageLabel", string.Empty, 19, FontStyle.Bold,
                new Vector2(12f, -62f), new Vector2(-12f, -10f), TextAnchor.MiddleCenter, TextColor, true);
            var imageObject = CreateObject("Overlay_WordImageTexture", wordImage);
            var rawImage = imageObject.AddComponent<RawImage>();
            rawImage.raycastTarget = false;
            SetAnchored(imageObject.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(12f, 12f), new Vector2(-12f, -70f));
            wordImage.gameObject.SetActive(false);
        }

        private static (
            GameObject studyPanel,
            GameObject startGate,
            GameObject studyHud,
            GameObject wordImageHud,
            GameObject recallPanel) BuildWorldSpaceTemplates(Transform parent)
        {
            var root = CreateObject("EditableWorldSpaceUiTemplates", parent);
            Undo.RegisterCreatedObjectUndo(root, "Build Memory Palace World Space UI Templates");

            var studyPanel = CreateVrCanvas(root.transform, "VRStudyPanelTemplate", new Vector2(760f, 760f), 0.00155f, 5, true);
            AddVrAudioProgress(studyPanel.transform);
            AddVrText(studyPanel.transform, "Progress", string.Empty, 16, new Rect(26f, -34f, 708f, 44f), new Color(0.76f, 0.84f, 0.94f));
            AddVrText(studyPanel.transform, "Title", "Target word", 34, new Rect(26f, -96f, 708f, 58f), Color.white);
            AddVrText(studyPanel.transform, "Meaning", "English meaning", 20, new Rect(26f, -152f, 708f, 44f), new Color(0.90f, 0.94f, 1f));
            AddVrText(studyPanel.transform, "Anchor", "Anchor", 18, new Rect(26f, -198f, 708f, 36f), new Color(0.70f, 0.78f, 0.90f));
            AddVrText(studyPanel.transform, "VoiceSubtitle", "Voice subtitle", 21, new Rect(26f, -242f, 708f, 96f), new Color(1f, 0.92f, 0.58f));
            AddVrText(studyPanel.transform, "Story", "Story segment", 17, new Rect(26f, -350f, 708f, 96f), new Color(0.94f, 0.96f, 1f));
            AddVrText(studyPanel.transform, "PreviewHeader", "Word image", 20, new Rect(26f, -462f, 708f, 28f), Color.white);
            AddVrRawImage(studyPanel.transform, "PreviewImage", new Rect(26f, -494f, 290f, 170f), new Color(0.14f, 0.16f, 0.20f, 0.98f));
            AddVrText(studyPanel.transform, "PreviewInfo", "Image cue information", 15, new Rect(336f, -494f, 398f, 84f), new Color(0.84f, 0.88f, 0.94f));
            AddVrButton(studyPanel.transform, "ReplayVoiceButton", "Replay Voice", new Rect(336f, -586f, 190f, 38f), VrPanelButtonAction.ReplayVoice);
            AddVrButton(studyPanel.transform, "RestartVoiceButton", "Restart Route", new Rect(544f, -586f, 190f, 38f), VrPanelButtonAction.RestartVoiceRoute);
            AddVrButton(studyPanel.transform, "GenerateButton", "Local Word Image", new Rect(336f, -634f, 398f, 42f), VrPanelButtonAction.GenerateImageCue);
            AddVrButton(studyPanel.transform, "CaptureButton", "Capture Scene Snapshot", new Rect(26f, -634f, 290f, 42f), VrPanelButtonAction.CaptureSnapshot);
            AddVrText(studyPanel.transform, "Action", "Action status", 16, new Rect(26f, -684f, 708f, 24f), new Color(0.95f, 0.86f, 0.48f));

            var startGate = CreateVrCanvas(root.transform, "VRStudyStartGateTemplate", new Vector2(620f, 270f), 0.0017f, 20, true);
            AddVrText(startGate.transform, "ReadyTitle", "Ready to begin?", 38, new Rect(28f, -30f, 564f, 58f), Color.white);
            AddVrText(startGate.transform, "ReadyInstruction", "Press trigger, grip, A/X, or aim at Start for one second.", 20, new Rect(38f, -96f, 544f, 52f), new Color(0.78f, 0.86f, 0.96f));
            AddVrButton(startGate.transform, "StartLearningButton", "Start learning", new Rect(110f, -174f, 400f, 68f), VrPanelButtonAction.StartLearning);
            AddVrText(startGate.transform, "RuntimeRevision", "VR runtime", 12, new Rect(420f, -246f, 172f, 18f), new Color(0.48f, 0.58f, 0.70f, 0.95f));

            var studyHud = CreateVrCanvas(root.transform, "VRStudyHudTemplate", new Vector2(1200f, 720f), 0.0008f, 50, false);
            studyHud.transform.localPosition = new Vector3(0f, 0f, 1f);
            AddVrBackground(studyHud.transform, "StudyTimerBackground", new Rect(805f, -92f, 345f, 82f), new Color(0.025f, 0.035f, 0.055f, 0.84f));
            var timer = AddVrText(studyHud.transform, "StudyTimer", "Phase 1 | 10:00 left", 16, new Rect(821f, -99f, 313f, 66f), Color.white);
            timer.alignment = TextAnchor.MiddleCenter;

            var learning = AddVrGroup(studyHud.transform, "LearningControlsGroup");
            AddVrBackground(learning.transform, "LearningControlsBackground", new Rect(50f, -92f, 720f, 92f), new Color(0.025f, 0.035f, 0.055f, 0.84f));
            var phase = AddVrText(learning.transform, "LearningPhaseLabel", "Phase 1 - Follow the route and capture every anchor.", 16, new Rect(72f, -100f, 654f, 28f), new Color(0.88f, 0.93f, 1f));
            phase.alignment = TextAnchor.MiddleLeft;
            AddVrButton(learning.transform, "HudReplayVoiceButton", "Replay voice", new Rect(72f, -134f, 196f, 42f), VrPanelButtonAction.ReplayVoice);
            AddVrButton(learning.transform, "HudRestartRouteButton", "Restart learning route", new Rect(284f, -134f, 266f, 42f), VrPanelButtonAction.RestartVoiceRoute);
            AddVrButton(learning.transform, "HudCaptureButton", "Look at furniture to capture", new Rect(566f, -134f, 182f, 42f), VrPanelButtonAction.CaptureSnapshot);

            var review = AddVrGroup(studyHud.transform, "ReviewProgressGroup");
            AddVrBackground(review.transform, "ReviewProgressBackground", new Rect(50f, -92f, 720f, 110f), new Color(0.025f, 0.035f, 0.055f, 0.84f));
            var reviewLabel = AddVrText(review.transform, "ReviewProgressLabel", "Phase 2 - Review", 16, new Rect(72f, -98f, 480f, 52f), new Color(0.88f, 0.93f, 1f));
            reviewLabel.alignment = TextAnchor.MiddleLeft;
            AddVrProgressTrack(review.transform, "ReviewProgressTrack", "ReviewProgressFill", new Vector2(72f, -166f), 480f, 12f, true);
            var finish = AddVrButton(review.transform, "FinishLearningButton", "Finish learning", new Rect(570f, -119f, 178f, 58f), VrPanelButtonAction.FinishLearning);
            finish.Background.color = new Color(0.08f, 0.48f, 0.52f, 0.98f);

            var subtitle = AddVrGroup(studyHud.transform, "SubtitleGroup");
            AddVrBackground(subtitle.transform, "SubtitleBackground", new Rect(70f, -960f, 1060f, 70f), new Color(0.015f, 0.02f, 0.03f, 0.86f));
            var subtitleText = AddVrText(subtitle.transform, "Subtitle", "Voice subtitle", 22, new Rect(94f, -968f, 900f, 52f), Color.white);
            subtitleText.alignment = TextAnchor.MiddleCenter;
            AddVrButton(subtitle.transform, "SubtitleAdvanceButton", ">", new Rect(1018f, -968f, 88f, 52f), VrPanelButtonAction.AdvanceVoiceRoute);

            var context = AddVrGroup(studyHud.transform, "ContextInstructionGroup");
            var contextGroup = context.AddComponent<CanvasGroup>();
            contextGroup.alpha = 0f;
            contextGroup.interactable = false;
            contextGroup.blocksRaycasts = false;
            AddVrBackground(context.transform, "ContextInstructionBackground", new Rect(42f, -210f, 440f, 92f), new Color(0.025f, 0.035f, 0.050f, 0.90f));
            AddVrBackground(context.transform, "ContextInstructionAccent", new Rect(42f, -210f, 5f, 92f), new Color(0.30f, 0.72f, 0.78f, 1f));
            var contextIcon = AddVrText(context.transform, "ContextInstructionIcon", "i", 24, new Rect(58f, -228f, 34f, 54f), new Color(0.72f, 0.91f, 0.96f));
            contextIcon.fontStyle = FontStyle.Bold;
            contextIcon.alignment = TextAnchor.MiddleCenter;
            var contextTitle = AddVrText(context.transform, "ContextInstructionTitle", "Instruction", 17, new Rect(104f, -219f, 354f, 27f), Color.white);
            contextTitle.fontStyle = FontStyle.Bold;
            var contextBody = AddVrText(context.transform, "ContextInstructionBody", "Instruction detail", 14, new Rect(104f, -249f, 354f, 43f), new Color(0.82f, 0.87f, 0.92f));
            contextBody.alignment = TextAnchor.UpperLeft;
            context.SetActive(false);

            var wordImageHud = CreateObject("VRStudyWordImageHudTemplate", root.transform);
            var wordCanvas = wordImageHud.AddComponent<Canvas>();
            wordCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            wordCanvas.pixelPerfect = true;
            wordCanvas.planeDistance = 1.8f;
            wordCanvas.overrideSorting = true;
            wordCanvas.sortingOrder = 900;
            var wordScaler = wordImageHud.AddComponent<CanvasScaler>();
            wordScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            wordScaler.referenceResolution = new Vector2(1200f, 720f);
            wordScaler.matchWidthOrHeight = 0.5f;
            var imagePanel = AddVrBackground(wordImageHud.transform, "StableWordImagePanel", new Rect(0f, 0f, 190f, 156f), new Color(0.015f, 0.025f, 0.045f, 0.94f));
            var imagePanelRect = imagePanel.rectTransform;
            imagePanelRect.anchorMin = imagePanelRect.anchorMax = new Vector2(0.5f, 0.5f);
            imagePanelRect.pivot = new Vector2(0.5f, 0.5f);
            imagePanelRect.anchoredPosition = new Vector2(0f, -64.8f);
            imagePanelRect.sizeDelta = new Vector2(190f, 156f);
            var wordLabel = AddVrText(imagePanel.transform, "WordAndMeaning", "palabra\nmeaning", 18, new Rect(10f, -5f, 110f, 40f), Color.white);
            wordLabel.alignment = TextAnchor.MiddleCenter;
            wordLabel.supportRichText = true;
            wordLabel.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.82f);
            AddVrButton(imagePanel.transform, "PronounceWordButton", "Play", new Rect(126f, -7f, 54f, 36f), VrPanelButtonAction.PronounceWord);
            var imageArea = AddVrBackground(imagePanel.transform, "WordImageArea", new Rect(10f, -49f, 170f, 97f), new Color(0.10f, 0.13f, 0.18f, 0.98f));
            var wordImage = CreateObject("WordImage", imageArea.transform);
            var wordRawImage = wordImage.AddComponent<RawImage>();
            wordRawImage.raycastTarget = false;
            Stretch(wordRawImage.rectTransform);
            var aspect = wordImage.AddComponent<AspectRatioFitter>();
            aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            aspect.aspectRatio = 1f;

            var recallPanel = CreateVrCanvas(root.transform, "VRRecallPanelTemplate", new Vector2(1200f, 680f), 0.00135f, 8, true);
            AddVrText(recallPanel.transform, "RecallProgress", "Immediate post-test", 18, new Rect(30f, -28f, 1140f, 36f), new Color(0.72f, 0.82f, 0.94f));
            AddVrText(recallPanel.transform, "RecallTarget", "Target Spanish word", 36, new Rect(30f, -72f, 1140f, 54f), Color.white);
            AddVrText(recallPanel.transform, "RecallInstruction", "Point to the correct image.", 20, new Rect(30f, -126f, 1140f, 42f), new Color(0.90f, 0.94f, 1f));
            AddVrText(recallPanel.transform, "RecallBlock", "Question", 17, new Rect(30f, -160f, 1140f, 28f), new Color(0.68f, 0.76f, 0.88f));
            for (var i = 0; i < 3; i++)
            {
                AddVrRecognitionOption(recallPanel.transform, i, 25f + i * 395f);
            }
            AddVrText(recallPanel.transform, "RecallFeedback", "Feedback", 24, new Rect(30f, -510f, 1140f, 52f), Color.white);
            AddVrButton(recallPanel.transform, "RecallAdvanceButton", "Continue", new Rect(410f, -580f, 380f, 54f), VrPanelButtonAction.RecognitionAdvance);

            studyPanel.SetActive(false);
            startGate.SetActive(false);
            studyHud.SetActive(false);
            wordImageHud.SetActive(false);
            recallPanel.SetActive(false);
            root.SetActive(false);
            return (studyPanel, startGate, studyHud, wordImageHud, recallPanel);
        }

        private static GameObject CreateVrCanvas(Transform parent, string name, Vector2 size, float scale, int sortingOrder, bool background)
        {
            var root = CreateObject(name, parent);
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = sortingOrder;
            var rect = root.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            root.transform.localScale = Vector3.one * scale;
            if (background)
            {
                var image = root.AddComponent<Image>();
                image.color = new Color(0.035f, 0.05f, 0.075f, 0.96f);
            }
            return root;
        }

        private static Text AddVrText(Transform parent, string name, string value, int fontSize, Rect rect, Color color)
        {
            var root = CreateObject(name, parent);
            var text = root.AddComponent<Text>();
            text.font = defaultFont;
            text.fontSize = fontSize;
            text.color = color;
            text.text = value;
            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            SetVrRect(text.rectTransform, rect);
            return text;
        }

        private static Image AddVrBackground(Transform parent, string name, Rect rect, Color color)
        {
            var root = CreateObject(name, parent);
            var image = root.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            SetVrRect(image.rectTransform, rect);
            return image;
        }

        private static RawImage AddVrRawImage(Transform parent, string name, Rect rect, Color color)
        {
            var root = CreateObject(name, parent);
            var image = root.AddComponent<RawImage>();
            image.color = color;
            image.raycastTarget = false;
            SetVrRect(image.rectTransform, rect);
            return image;
        }

        private static VrPanelButtonInteractable AddVrButton(Transform parent, string name, string label, Rect rect, VrPanelButtonAction action)
        {
            var root = CreateObject(name, parent);
            var image = root.AddComponent<Image>();
            image.color = new Color(0.18f, 0.22f, 0.28f, 0.96f);
            SetVrRect(image.rectTransform, rect);
            var labelObject = CreateObject(name + "_Label", root.transform);
            var labelText = labelObject.AddComponent<Text>();
            labelText.font = defaultFont;
            labelText.fontSize = 17;
            labelText.color = Color.white;
            labelText.alignment = TextAnchor.MiddleCenter;
            labelText.horizontalOverflow = HorizontalWrapMode.Wrap;
            labelText.verticalOverflow = VerticalWrapMode.Truncate;
            labelText.text = label;
            labelText.raycastTarget = false;
            Stretch(labelText.rectTransform, 10f, 6f, 10f, 6f);
            var collider = root.AddComponent<BoxCollider>();
            collider.center = new Vector3(rect.width * 0.5f, -rect.height * 0.5f, 0f);
            collider.size = new Vector3(rect.width, rect.height, 18f);
            var interactable = root.AddComponent<VrPanelButtonInteractable>();
            interactable.Action = action;
            interactable.Background = image;
            interactable.Label = labelText;
            return interactable;
        }

        private static GameObject AddVrGroup(Transform parent, string name)
        {
            var root = CreateObject(name, parent);
            Stretch(root.GetComponent<RectTransform>());
            return root;
        }

        private static void AddVrAudioProgress(Transform parent)
        {
            AddVrProgressTrack(parent, "VoiceProgressTrack", "VoiceProgressFill", new Vector2(26f, -18f), 610f, 8f, true);
            AddVrText(parent, "VoiceProgressTime", "0:00", 13, new Rect(646f, -26f, 88f, 22f), new Color(0.72f, 0.82f, 0.94f));
        }

        private static void AddVrProgressTrack(Transform parent, string trackName, string fillName, Vector2 position, float width, float height, bool addSegments)
        {
            var track = CreateObject(trackName, parent);
            var trackImage = track.AddComponent<Image>();
            trackImage.color = new Color(0.18f, 0.22f, 0.29f, 0.96f);
            var trackRect = track.GetComponent<RectTransform>();
            trackRect.anchorMin = trackRect.anchorMax = new Vector2(0f, 1f);
            trackRect.pivot = new Vector2(0f, 1f);
            trackRect.anchoredPosition = position;
            trackRect.sizeDelta = new Vector2(width, height);
            var fill = CreateObject(fillName, track.transform);
            var fillImage = fill.AddComponent<Image>();
            fillImage.color = new Color(0.32f, 0.72f, 0.96f, 0.98f);
            var fillRect = fill.GetComponent<RectTransform>();
            fillRect.anchorMin = fillRect.anchorMax = new Vector2(0f, 1f);
            fillRect.pivot = new Vector2(0f, 1f);
            fillRect.sizeDelta = new Vector2(0f, height);
            if (addSegments)
            {
                for (var i = 1; i < 8; i++)
                {
                    var gap = AddVrBackground(track.transform, trackName + "SegmentGap_" + i,
                        new Rect(width * i / 8f, 0f, 4f, height), new Color(0.025f, 0.035f, 0.055f, 1f));
                    gap.rectTransform.pivot = new Vector2(0.5f, 1f);
                }
            }
            var collider = track.AddComponent<BoxCollider>();
            collider.center = new Vector3(width * 0.5f, -height * 0.5f, 0f);
            collider.size = new Vector3(width, Mathf.Max(22f, height + 14f), 18f);
            var interactable = track.AddComponent<VrAudioProgressInteractable>();
            interactable.Width = width;
            interactable.Enabled = false;
        }

        private static void AddVrRecognitionOption(Transform parent, int index, float x)
        {
            var root = CreateObject($"RecognitionOption{index + 1}", parent);
            var background = root.AddComponent<Image>();
            background.color = new Color(0.18f, 0.22f, 0.28f, 0.96f);
            SetVrRect(background.rectTransform, new Rect(x, -198f, 360f, 286f));
            AddVrRawImage(root.transform, "OptionImage", new Rect(12f, -42f, 336f, 202f), new Color(0.08f, 0.10f, 0.13f, 1f));
            var caption = AddVrText(root.transform, "OptionCaption", "Option", 18, new Rect(12f, -250f, 336f, 31f), Color.white);
            caption.alignment = TextAnchor.MiddleCenter;
            var number = AddVrText(root.transform, "OptionNumber", $"Option {index + 1}", 17, new Rect(12f, -9f, 336f, 28f), new Color(0.78f, 0.86f, 0.96f));
            number.alignment = TextAnchor.MiddleLeft;
            var collider = root.AddComponent<BoxCollider>();
            collider.center = new Vector3(180f, -143f, 0f);
            collider.size = new Vector3(360f, 286f, 18f);
            var interactable = root.AddComponent<VrPanelButtonInteractable>();
            interactable.Action = VrPanelButtonAction.RecognitionOption;
            interactable.Background = background;
            interactable.Label = caption;
        }

        private static void SetVrRect(RectTransform rectTransform, Rect rect)
        {
            rectTransform.anchorMin = rectTransform.anchorMax = new Vector2(0f, 1f);
            rectTransform.pivot = new Vector2(0f, 1f);
            rectTransform.anchoredPosition = new Vector2(rect.x, rect.y);
            rectTransform.sizeDelta = new Vector2(rect.width, rect.height);
        }

        private static Transform CreateCenteredScrollStage(Transform stageRoot, string name, float width)
        {
            var root = CreateStageRoot(stageRoot, name);
            return CreateScrollPanel(root.transform, name + "_Page", new Vector2(0.5f, 0f), new Vector2(0.5f, 1f),
                new Vector2(-width * 0.5f, 0f), new Vector2(width * 0.5f, 0f), Page);
        }

        private static Transform CreateFullWidthScrollStage(Transform stageRoot, string name)
        {
            var root = CreateStageRoot(stageRoot, name);
            return CreateScrollPanel(root.transform, name + "_Page", Vector2.zero, Vector2.one,
                new Vector2(18f, 0f), new Vector2(-18f, 0f), Page);
        }

        private static GameObject CreateStageRoot(Transform parent, string name)
        {
            var root = CreateObject(name, parent);
            Stretch(root.GetComponent<RectTransform>());
            return root;
        }

        private static Transform CreateScrollPanel(
            Transform parent,
            string name,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 offsetMin,
            Vector2 offsetMax,
            Color color)
        {
            var panel = CreatePanel(parent, name, anchorMin, anchorMax, offsetMin, offsetMax, color, true);
            var viewport = CreateObject(name + "_Viewport", panel);
            var viewportImage = viewport.AddComponent<Image>();
            viewportImage.color = new Color(1f, 1f, 1f, 0.01f);
            viewport.AddComponent<RectMask2D>();
            SetAnchored(viewport.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, new Vector2(14f, 14f), new Vector2(-30f, -14f));

            var content = CreateObject(name + "_Content", viewport.transform);
            var contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = Vector2.zero;
            var layout = content.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 12, 18);
            layout.spacing = 9f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var fitter = content.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scrollbar = AddScrollbar(panel, name + "_Scrollbar");
            var scroll = panel.gameObject.AddComponent<ScrollRect>();
            scroll.content = contentRect;
            scroll.viewport = viewport.GetComponent<RectTransform>();
            scroll.vertical = true;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 32f;
            scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            scroll.verticalScrollbarSpacing = 4f;
            return content.transform;
        }

        private static Scrollbar AddScrollbar(Transform parent, string name)
        {
            var root = CreateObject(name, parent);
            root.AddComponent<Image>().color = new Color(0.05f, 0.06f, 0.09f, 0.8f);
            SetAnchored(root.GetComponent<RectTransform>(), new Vector2(1f, 0f), Vector2.one, new Vector2(-22f, 14f), new Vector2(-8f, -14f));
            var sliding = CreateObject(name + "_SlidingArea", root.transform);
            Stretch(sliding.GetComponent<RectTransform>(), 3f, 3f, 3f, 3f);
            var handle = CreateObject(name + "_Handle", sliding.transform);
            var handleImage = handle.AddComponent<Image>();
            handleImage.color = Primary;
            Stretch(handle.GetComponent<RectTransform>());
            var scrollbar = root.AddComponent<Scrollbar>();
            scrollbar.handleRect = handle.GetComponent<RectTransform>();
            scrollbar.targetGraphic = handleImage;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            return scrollbar;
        }

        private static Transform AddVerticalGroup(Transform parent, string name, float preferredHeight, Color? background = null)
        {
            var root = CreateObject(name, parent);
            if (background.HasValue)
            {
                root.AddComponent<Image>().color = background.Value;
            }
            var layout = root.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 9, 9);
            layout.spacing = 7f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            root.AddComponent<LayoutElement>().preferredHeight = preferredHeight;
            return root.transform;
        }

        private static Transform AddGrid(Transform parent, string name, int columns, float cellWidth, float cellHeight, float height)
        {
            var root = CreateObject(name, parent);
            var grid = root.AddComponent<GridLayoutGroup>();
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = columns;
            grid.cellSize = new Vector2(cellWidth, cellHeight);
            grid.spacing = new Vector2(8f, 8f);
            grid.childAlignment = TextAnchor.UpperCenter;
            root.AddComponent<LayoutElement>().preferredHeight = height;
            return root.transform;
        }

        private static void AddHeading(Transform parent, string name, string value)
        {
            AddText(parent, name, value, 30, FontStyle.Bold, TextColor, 52f);
        }

        private static void AddSectionLabel(Transform parent, string value)
        {
            AddText(parent, $"Section_{generatedNameIndex++:000}", value, 19, FontStyle.Bold, TextColor, 36f);
        }

        private static Text AddBody(Transform parent, string name, string value, float height = 46f)
        {
            return AddText(parent, name, value, 14, FontStyle.Normal, Muted, height);
        }

        private static Text AddText(
            Transform parent,
            string name,
            string value,
            int fontSize,
            FontStyle fontStyle,
            Color color,
            float preferredHeight,
            TextAnchor alignment = TextAnchor.UpperLeft)
        {
            var root = CreateObject(name, parent);
            var text = root.AddComponent<Text>();
            text.font = defaultFont;
            text.text = value;
            text.fontSize = fontSize;
            text.fontStyle = fontStyle;
            text.color = color;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            root.AddComponent<LayoutElement>().preferredHeight = preferredHeight;
            return text;
        }

        private static Button AddButton(
            Transform parent,
            string name,
            string label,
            float preferredHeight,
            bool primary = false,
            string labelName = null)
        {
            var root = CreateObject(name, parent);
            var image = root.AddComponent<Image>();
            image.color = primary ? Primary : new Color(0.22f, 0.25f, 0.30f, 1f);
            image.sprite = defaultUiSprite;
            image.type = Image.Type.Sliced;
            var button = root.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.10f, 1.10f, 1.10f, 1f);
            colors.pressedColor = new Color(0.78f, 0.82f, 0.86f, 1f);
            colors.disabledColor = Disabled;
            button.colors = colors;
            root.AddComponent<LayoutElement>().preferredHeight = preferredHeight;
            var text = CreateObject(labelName ?? name + "_Text", root.transform).AddComponent<Text>();
            text.font = defaultFont;
            text.text = label;
            text.fontSize = 15;
            text.fontStyle = primary ? FontStyle.Bold : FontStyle.Normal;
            text.color = TextColor;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            Stretch(text.rectTransform, 8f, 6f, 8f, 6f);
            return button;
        }

        private static InputField AddInput(Transform parent, string name, string placeholder, bool multiline, float height)
        {
            var root = CreateObject(name, parent);
            var image = root.AddComponent<Image>();
            image.color = Field;
            image.sprite = defaultUiSprite;
            image.type = Image.Type.Sliced;
            var input = root.AddComponent<InputField>();
            input.targetGraphic = image;
            input.lineType = multiline ? InputField.LineType.MultiLineNewline : InputField.LineType.SingleLine;
            root.AddComponent<LayoutElement>().preferredHeight = height;

            var text = CreateObject(name + "_Text", root.transform).AddComponent<Text>();
            text.font = defaultFont;
            text.fontSize = 16;
            text.color = TextColor;
            text.alignment = multiline ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft;
            text.supportRichText = false;
            Stretch(text.rectTransform, 12f, 8f, 12f, 8f);
            input.textComponent = text;

            var hint = CreateObject(name + "_Placeholder", root.transform).AddComponent<Text>();
            hint.font = defaultFont;
            hint.fontSize = 16;
            hint.fontStyle = FontStyle.Italic;
            hint.color = new Color(Muted.r, Muted.g, Muted.b, 0.55f);
            hint.text = placeholder;
            hint.alignment = multiline ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft;
            Stretch(hint.rectTransform, 12f, 8f, 12f, 8f);
            input.placeholder = hint;
            return input;
        }

        private static RawImage AddRawImage(Transform parent, string name, float height)
        {
            var root = CreateObject(name, parent);
            var image = root.AddComponent<RawImage>();
            image.color = Color.white;
            image.raycastTarget = false;
            var fitter = root.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio = 1.6f;
            root.AddComponent<LayoutElement>().preferredHeight = height;
            return image;
        }

        private static void AddQuestionnaireSlider(Transform parent, string name, string label, float min, float max)
        {
            var row = AddVerticalGroup(parent, name + "_Row", 86f, Card);
            var header = AddGrid(row, name + "_Header", 2, 390f, 28f, 32f);
            AddText(header, name + "_Label", label, 15, FontStyle.Bold, TextColor, 26f);
            AddText(header, name + "Value", "0", 15, FontStyle.Bold, Primary, 26f, TextAnchor.MiddleRight);
            var sliderObject = CreateObject(name, row);
            sliderObject.AddComponent<LayoutElement>().preferredHeight = 34f;
            var background = CreateObject(name + "_Background", sliderObject.transform);
            background.AddComponent<Image>().color = Field;
            SetAnchored(background.GetComponent<RectTransform>(), new Vector2(0f, 0.35f), new Vector2(1f, 0.65f), new Vector2(10f, 0f), new Vector2(-10f, 0f));
            var fillArea = CreateObject(name + "_FillArea", sliderObject.transform);
            SetAnchored(fillArea.GetComponent<RectTransform>(), new Vector2(0f, 0.35f), new Vector2(1f, 0.65f), new Vector2(10f, 0f), new Vector2(-10f, 0f));
            var fill = CreateObject(name + "_Fill", fillArea.transform);
            var fillImage = fill.AddComponent<Image>();
            fillImage.color = Primary;
            Stretch(fill.GetComponent<RectTransform>());
            var handleArea = CreateObject(name + "_HandleArea", sliderObject.transform);
            Stretch(handleArea.GetComponent<RectTransform>(), 10f, 0f, 10f, 0f);
            var handle = CreateObject(name + "_Handle", handleArea.transform);
            var handleImage = handle.AddComponent<Image>();
            handleImage.color = TextColor;
            var handleRect = handle.GetComponent<RectTransform>();
            handleRect.sizeDelta = new Vector2(22f, 30f);
            var slider = sliderObject.AddComponent<Slider>();
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = true;
            slider.fillRect = fill.GetComponent<RectTransform>();
            slider.handleRect = handleRect;
            slider.targetGraphic = handleImage;
        }

        private static Dropdown AddDropdown(Transform parent, string name, float height)
        {
            var root = CreateObject(name, parent);
            var rootImage = root.AddComponent<Image>();
            rootImage.color = Field;
            rootImage.sprite = defaultUiSprite;
            rootImage.type = Image.Type.Sliced;
            var dropdown = root.AddComponent<Dropdown>();
            dropdown.targetGraphic = rootImage;
            root.AddComponent<LayoutElement>().preferredHeight = height;
            var caption = CreateObject(name + "_Caption", root.transform).AddComponent<Text>();
            caption.font = defaultFont;
            caption.fontSize = 15;
            caption.color = TextColor;
            caption.alignment = TextAnchor.MiddleLeft;
            Stretch(caption.rectTransform, 12f, 4f, 42f, 4f);
            dropdown.captionText = caption;
            var arrow = CreateObject(name + "_Arrow", root.transform).AddComponent<Text>();
            arrow.font = defaultFont;
            arrow.fontSize = 18;
            arrow.text = "▼";
            arrow.color = TextColor;
            arrow.alignment = TextAnchor.MiddleCenter;
            SetAnchored(arrow.rectTransform, new Vector2(1f, 0f), Vector2.one, new Vector2(-40f, 0f), Vector2.zero);

            var template = CreateObject(name + "_Template", root.transform);
            var templateImage = template.AddComponent<Image>();
            templateImage.color = Page;
            var templateRect = template.GetComponent<RectTransform>();
            templateRect.anchorMin = new Vector2(0f, 0f);
            templateRect.anchorMax = new Vector2(1f, 0f);
            templateRect.pivot = new Vector2(0.5f, 1f);
            templateRect.anchoredPosition = new Vector2(0f, -2f);
            templateRect.sizeDelta = new Vector2(0f, 240f);
            var scroll = template.AddComponent<ScrollRect>();
            var viewport = CreateObject(name + "_DropdownViewport", template.transform);
            viewport.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.01f);
            viewport.AddComponent<Mask>().showMaskGraphic = false;
            Stretch(viewport.GetComponent<RectTransform>());
            var content = CreateObject(name + "_DropdownContent", viewport.transform);
            var contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.sizeDelta = new Vector2(0f, 36f);
            var item = CreateObject(name + "_Item", content.transform);
            var toggle = item.AddComponent<Toggle>();
            var itemBg = item.AddComponent<Image>();
            itemBg.color = Card;
            toggle.targetGraphic = itemBg;
            var check = CreateObject(name + "_ItemCheck", item.transform).AddComponent<Image>();
            check.color = Primary;
            SetAnchored(check.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(4f, 4f), new Vector2(28f, -4f));
            toggle.graphic = check;
            var itemLabel = CreateObject(name + "_ItemLabel", item.transform).AddComponent<Text>();
            itemLabel.font = defaultFont;
            itemLabel.fontSize = 14;
            itemLabel.color = TextColor;
            itemLabel.alignment = TextAnchor.MiddleLeft;
            Stretch(itemLabel.rectTransform, 36f, 2f, 4f, 2f);
            var itemRect = item.GetComponent<RectTransform>();
            itemRect.anchorMin = new Vector2(0f, 0.5f);
            itemRect.anchorMax = new Vector2(1f, 0.5f);
            itemRect.sizeDelta = new Vector2(0f, 36f);
            scroll.content = contentRect;
            scroll.viewport = viewport.GetComponent<RectTransform>();
            scroll.horizontal = false;
            dropdown.template = templateRect;
            dropdown.itemText = itemLabel;
            template.SetActive(false);
            return dropdown;
        }

        private static void ApplyLegacyDarkButtonTheme(Transform stageRoot)
        {
            if (stageRoot == null)
            {
                return;
            }

            for (var childIndex = 0; childIndex < stageRoot.childCount; childIndex++)
            {
                var stage = stageRoot.GetChild(childIndex);
                if (stage.name == "Stage_Setup" || stage.name == "Stage_RoomBuilder")
                {
                    continue;
                }

                var buttons = stage.GetComponentsInChildren<Button>(true);
                for (var i = 0; i < buttons.Length; i++)
                {
                    var image = buttons[i].GetComponent<Image>();
                    if (image != null)
                    {
                        image.color = DarkButton;
                        image.sprite = defaultUiSprite;
                        image.type = Image.Type.Sliced;
                    }

                    var label = buttons[i].GetComponentInChildren<Text>(true);
                    if (label != null)
                    {
                        label.fontSize = 13;
                        label.fontStyle = FontStyle.Normal;
                        label.color = TextColor;
                    }
                }
            }
        }

        private static void ApplyLegacyLightTheme(Transform stage)
        {
            if (stage == null)
            {
                return;
            }

            var texts = stage.GetComponentsInChildren<Text>(true);
            for (var i = 0; i < texts.Length; i++)
            {
                var text = texts[i];
                var alpha = text.color.a;
                if (ApproximatelyRgb(text.color, Muted) || ApproximatelyRgb(text.color, LightMuted))
                {
                    text.color = new Color(LightMuted.r, LightMuted.g, LightMuted.b, alpha);
                }
                else if (ApproximatelyRgb(text.color, Primary))
                {
                    text.color = new Color(Primary.r, Primary.g, Primary.b, alpha);
                }
                else
                {
                    text.color = new Color(LightInk.r, LightInk.g, LightInk.b, alpha);
                }
            }

            var images = stage.GetComponentsInChildren<Image>(true);
            for (var i = 0; i < images.Length; i++)
            {
                var image = images[i];
                var button = image.GetComponent<Button>();
                if (button != null)
                {
                    var isPrimary = ApproximatelyRgb(image.color, Primary);
                    image.color = isPrimary ? Primary : LightSecondary;
                    image.sprite = defaultUiSprite;
                    image.type = Image.Type.Sliced;
                    var label = button.GetComponentInChildren<Text>(true);
                    if (label != null)
                    {
                        label.fontSize = isPrimary ? 16 : 14;
                        label.fontStyle = isPrimary ? FontStyle.Bold : FontStyle.Normal;
                        label.color = isPrimary ? Color.white : LightInk;
                    }
                    continue;
                }

                if (image.GetComponent<InputField>() != null || image.GetComponent<Dropdown>() != null)
                {
                    image.color = Color.white;
                    image.sprite = defaultUiSprite;
                    image.type = Image.Type.Sliced;
                    continue;
                }

                if (image.color.a <= 0.02f)
                {
                    continue;
                }

                if (image.name.EndsWith("_Handle", StringComparison.Ordinal))
                {
                    image.color = Primary;
                }
                else if (image.name.Contains("Scrollbar", StringComparison.Ordinal))
                {
                    image.color = LightSecondary;
                }
                else if (ApproximatelyRgb(image.color, Card) || ApproximatelyRgb(image.color, LightCard))
                {
                    image.color = LightCard;
                }
                else if (ApproximatelyRgb(image.color, Page) || ApproximatelyRgb(image.color, LightPage))
                {
                    image.color = LightPage;
                }
            }
        }

        private static bool ApproximatelyRgb(Color a, Color b)
        {
            return Mathf.Abs(a.r - b.r) < 0.01f &&
                   Mathf.Abs(a.g - b.g) < 0.01f &&
                   Mathf.Abs(a.b - b.b) < 0.01f;
        }

        private static Transform CreatePanel(
            Transform parent,
            string name,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 offsetMin,
            Vector2 offsetMax,
            Color color,
            bool raycast)
        {
            var root = CreateObject(name, parent);
            var image = root.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = raycast;
            SetAnchored(root.GetComponent<RectTransform>(), anchorMin, anchorMax, offsetMin, offsetMax);
            return root.transform;
        }

        private static void AddTextAbsolute(
            Transform parent,
            string name,
            string value,
            int fontSize,
            FontStyle fontStyle,
            Vector2 offsetMin,
            Vector2 offsetMax,
            TextAnchor anchor,
            Color color,
            bool stretchHorizontal = false,
            bool centered = false)
        {
            var root = CreateObject(name, parent);
            var text = root.AddComponent<Text>();
            text.font = defaultFont;
            text.text = value;
            text.fontSize = fontSize;
            text.fontStyle = fontStyle;
            text.alignment = anchor;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            var rect = text.rectTransform;
            if (centered)
            {
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.offsetMin = offsetMin;
                rect.offsetMax = offsetMax;
            }
            else if (stretchHorizontal)
            {
                SetAnchored(rect, new Vector2(0f, 0f), new Vector2(1f, 1f), offsetMin, offsetMax);
            }
            else
            {
                rect.anchorMin = new Vector2(offsetMin.x < 0f ? 1f : 0f, 1f);
                rect.anchorMax = rect.anchorMin;
                rect.pivot = new Vector2(offsetMin.x < 0f ? 1f : 0f, 1f);
                rect.anchoredPosition = offsetMin.x < 0f ? new Vector2(offsetMin.x, -offsetMin.y) : new Vector2(offsetMin.x, -offsetMin.y);
                rect.sizeDelta = offsetMax;
            }
        }

        private static void AddButtonAbsolute(
            Transform parent,
            string name,
            string label,
            Vector2 offsetMin,
            Vector2 size,
            bool rightAnchored,
            string labelName = null)
        {
            var button = AddButton(parent, name, label, size.y, true, labelName);
            Object.DestroyImmediate(button.GetComponent<LayoutElement>());
            var rect = button.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rightAnchored ? new Vector2(1f, 1f) : Vector2.up;
            rect.pivot = rightAnchored ? new Vector2(1f, 1f) : Vector2.up;
            rect.anchoredPosition = rightAnchored ? new Vector2(offsetMin.x, -offsetMin.y) : new Vector2(offsetMin.x, -offsetMin.y);
            rect.sizeDelta = size;
        }

        private static GameObject CreateObject(string name, Transform parent)
        {
            var root = new GameObject(name, typeof(RectTransform));
            root.transform.SetParent(parent, false);
            return root;
        }

        private static GameObject FindSceneObjectIncludingInactive(string objectName)
        {
            var objects = Resources.FindObjectsOfTypeAll<GameObject>();
            for (var i = 0; i < objects.Length; i++)
            {
                var candidate = objects[i];
                if (candidate != null &&
                    candidate.name == objectName &&
                    candidate.scene.IsValid() &&
                    !EditorUtility.IsPersistent(candidate))
                {
                    return candidate;
                }
            }
            return null;
        }

        private static void Stretch(RectTransform rect, float left = 0f, float bottom = 0f, float right = 0f, float top = 0f)
        {
            SetAnchored(rect, Vector2.zero, Vector2.one, new Vector2(left, bottom), new Vector2(-right, -top));
        }

        private static void SetAnchored(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

        private static void AssignControllerReference(MemoryPalaceExperimentController controller, MemoryPalaceSceneUiView view)
        {
            var serialized = new SerializedObject(controller);
            serialized.FindProperty("editableSceneUi").objectReferenceValue = view;
            serialized.FindProperty("useEditableSceneUi").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(controller);
        }

        private static void EnsureEventSystem()
        {
            var eventSystem = Object.FindFirstObjectByType<EventSystem>();
            if (eventSystem == null)
            {
                var eventObject = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                Undo.RegisterCreatedObjectUndo(eventObject, "Create EventSystem");
                return;
            }

            var oldModule = eventSystem.GetComponent<StandaloneInputModule>();
            if (oldModule != null)
            {
                Object.DestroyImmediate(oldModule);
            }
            if (eventSystem.GetComponent<InputSystemUIInputModule>() == null)
            {
                eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
            }
        }
    }
}
