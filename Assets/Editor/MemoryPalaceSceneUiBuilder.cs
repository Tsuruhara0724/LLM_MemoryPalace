using System;
using MemPalaceLLM;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MemPalaceLLM.Editor
{
    public static class MemoryPalaceSceneUiBuilder
    {
        private static readonly Color Page = new(0.075f, 0.095f, 0.14f, 0.96f);
        private static readonly Color Card = new(0.12f, 0.145f, 0.20f, 0.97f);
        private static readonly Color Field = new(0.07f, 0.08f, 0.11f, 1f);
        private static readonly Color Primary = new(0.32f, 0.62f, 0.65f, 1f);
        private static readonly Color TextColor = new(0.96f, 0.97f, 0.99f, 1f);
        private static readonly Color Muted = new(0.68f, 0.74f, 0.82f, 1f);
        private static readonly Color Disabled = new(0.28f, 0.31f, 0.36f, 0.70f);
        private static Font defaultFont;
        private static int generatedNameIndex;

        [InitializeOnLoadMethod]
        private static void BuildMissingSceneUiAfterScriptReload()
        {
            EditorApplication.delayCall += TryBuildMissingSceneUi;
        }

        private static void TryBuildMissingSceneUi()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += TryBuildMissingSceneUi;
                return;
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode || GameObject.Find("EditableSceneUI") != null)
            {
                return;
            }

            var controller = Object.FindFirstObjectByType<MemoryPalaceExperimentController>();
            if (controller != null && controller.gameObject.scene.IsValid())
            {
                RebuildEditableSceneUi();
            }
        }

        [MenuItem("Tools/Memory Palace/Rebuild Editable Scene UI")]
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
            generatedNameIndex = 0;
            var canvasObject = CreateObject("EditableSceneUI", controller.transform.parent);
            Undo.RegisterCreatedObjectUndo(canvasObject, "Build Memory Palace Scene UI");
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 20;
            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            canvasObject.AddComponent<GraphicRaycaster>();
            Stretch(canvasObject.GetComponent<RectTransform>());

            var view = canvasObject.AddComponent<MemoryPalaceSceneUiView>();
            CreateHeader(canvasObject.transform);
            var stageRoot = CreateObject("StageRoot", canvasObject.transform);
            Stretch(stageRoot.GetComponent<RectTransform>(), 0f, 0f, 0f, 72f);

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
                "Memory Palace: rebuilt editable UGUI hierarchy and saved the open Scene. " +
                "Adjust panels under EditableSceneUI/StageRoot in the Inspector.");
        }

        private static void CreateHeader(Transform parent)
        {
            var header = CreatePanel(parent, "Common_Header", new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -72f), Vector2.zero, Page, true);
            AddTextAbsolute(header, "Common_Title", "MEMORY PALACE EXPERIMENT", 26, FontStyle.Bold,
                new Vector2(24f, 12f), new Vector2(500f, 34f), TextAnchor.MiddleLeft, TextColor);
            AddTextAbsolute(header, "Common_Stage", "SESSION SETUP", 16, FontStyle.Bold,
                new Vector2(530f, 10f), new Vector2(330f, 28f), TextAnchor.MiddleLeft, Primary);
            AddTextAbsolute(header, "Common_Status", string.Empty, 13, FontStyle.Normal,
                new Vector2(530f, 36f), new Vector2(900f, 27f), TextAnchor.MiddleLeft, Muted);
            AddButtonAbsolute(header, "Common_LegacyUi", "Legacy UI (safety)",
                new Vector2(-220f, 14f), new Vector2(190f, 42f), true);
        }

        private static void BuildSetup(Transform stageRoot)
        {
            var content = CreateCenteredScrollStage(stageRoot, "Stage_Setup", 1160f);
            AddHeading(content, "Setup_Title", "Set up this session");
            AddBody(content, "Setup_Intro", "Choose one condition in the 2 x 2 room-source x story-source design.");
            AddSectionLabel(content, "Session");
            AddBody(content, "Setup_ParticipantLabel", "Participant ID");
            AddInput(content, "Setup_ParticipantInput", "P001", false, 50f);
            AddSectionLabel(content, "Choose your study");
            var conditionGrid = AddGrid(content, "Setup_ConditionGrid", 2, 520f, 86f, 184f);
            AddButton(conditionGrid, "Setup_Condition1", "Self room\nSelf story", 82f);
            AddButton(conditionGrid, "Setup_Condition2", "Self room\nLLM story", 82f);
            AddButton(conditionGrid, "Setup_Condition3", "Example room\nSelf story", 82f);
            AddButton(conditionGrid, "Setup_Condition4", "Example room\nLLM story", 82f);
            AddBody(content, "Setup_ConditionDescription", string.Empty, 68f);
            AddSectionLabel(content, "Room");
            AddBody(content, "Setup_RoomDescription", string.Empty, 58f);
            var saved = AddVerticalGroup(content, "Setup_SavedRoomGroup", 154f);
            AddBody(saved, "Setup_SavedRoomLabel", "Reuse saved room JSON (optional)", 30f);
            AddInput(saved, "Setup_RoomLoadInput", "P001_room.json", false, 48f);
            AddButton(saved, "Setup_StartSaved", "Start with saved room JSON", 50f);
            AddButton(content, "Setup_ReloadExample", "Reload example_room.json", 50f);
            AddSectionLabel(content, "Next");
            AddBody(content, "Setup_NextHint", string.Empty, 76f);
            AddButton(content, "Setup_Start", "Start", 62f, true, "Setup_StartLabel");
        }

        private static void BuildRoomBuilder(Transform stageRoot)
        {
            var root = CreateStageRoot(stageRoot, "Stage_RoomBuilder");
            var left = CreateScrollPanel(root.transform, "Room_LeftPanel", new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(18f, 18f), new Vector2(530f, -18f), Page);
            AddHeading(left, "Room_LeftHeading", "Make this room yours");
            AddBody(left, "Room_LeftIntro", "Draw a familiar room, add optional inside walls, then place at least eight different furniture items.", 74f);
            var modes = AddGrid(left, "Room_ModeGrid", 2, 235f, 54f, 128f);
            AddButton(modes, "Room_ModeFloor", "1 Shape", 50f);
            AddButton(modes, "Room_ModeWall", "2 Walls", 50f);
            AddButton(modes, "Room_ModeFurniture", "3 Furnish", 50f);
            AddButton(modes, "Room_ModeSelect", "4 Finish", 50f);
            AddSectionLabel(left, "Current step");
            AddText(left, "Room_Title", string.Empty, 20, FontStyle.Bold, TextColor, 38f);
            AddBody(left, "Room_Instructions", string.Empty, 80f);
            AddText(left, "Room_BuildArea", string.Empty, 14, FontStyle.Italic, Primary, 56f);
            var nav = AddGrid(left, "Room_NavigationGrid", 3, 148f, 48f, 54f);
            AddButton(nav, "Room_Center", "Center view", 46f);
            AddButton(nav, "Room_Undo", "Undo", 46f);
            AddButton(nav, "Room_Redo", "Redo", 46f);
            AddSectionLabel(left, "Starting shape");
            var shapes = AddGrid(left, "Room_ShapeGrid", 2, 225f, 48f, 54f);
            AddButton(shapes, "Room_FillArea", "Fill build area", 46f);
            AddButton(shapes, "Room_LShape", "L-shaped room", 46f);
            AddSectionLabel(left, "Furniture catalog (one of each)");
            var furniture = AddGrid(left, "Room_FurnitureGrid", 2, 225f, 52f, 384f);
            var names = new[] { "Door", "Bed", "Wardrobe", "Desk", "Chair", "Bookshelf", "Bathtub", "Sofa", "Television", "Air Conditioner", "Window", "Lamp", "Plant", "Toilet" };
            for (var i = 0; i < names.Length; i++)
            {
                AddButton(furniture, $"Room_Furniture_{i}", names[i], 50f, false, $"Room_FurnitureLabel_{i}");
            }
            var selected = AddVerticalGroup(left, "Room_SelectedGroup", 148f, Card);
            AddText(selected, "Room_SelectedName", "Selected furniture", 18, FontStyle.Bold, TextColor, 34f);
            var selectedGrid = AddGrid(selected, "Room_SelectedButtons", 3, 138f, 48f, 54f);
            AddButton(selectedGrid, "Room_Move", "Move", 46f, true, "Room_MoveLabel");
            AddButton(selectedGrid, "Room_Rotate", "Rotate", 46f);
            AddButton(selectedGrid, "Room_Remove", "Remove", 46f);
            AddSectionLabel(left, "Save and reuse room JSON");
            AddInput(left, "Room_ArchiveName", "P001_room", false, 48f);
            var saveGrid = AddGrid(left, "Room_SaveGrid", 2, 225f, 48f, 54f);
            AddButton(saveGrid, "Room_Save", "Save named JSON", 46f);
            AddButton(saveGrid, "Room_SaveParticipant", "Save with Participant ID", 46f);
            AddText(left, "Room_LastSaved", string.Empty, 13, FontStyle.Normal, Muted, 34f);
            AddInput(left, "Room_LoadName", "room.json", false, 48f);
            var loadGrid = AddGrid(left, "Room_LoadGrid", 2, 225f, 48f, 108f);
            AddButton(loadGrid, "Room_Load", "Load JSON by name", 46f);
            AddButton(loadGrid, "Room_LoadLatest", "Load latest JSON", 46f);
            AddButton(loadGrid, "Room_Refresh", "Refresh file list", 46f);
            AddText(left, "Room_Count", string.Empty, 13, FontStyle.Normal, Muted, 48f);
            AddText(left, "Room_Status", string.Empty, 14, FontStyle.Italic, Primary, 72f);

            var hud = CreatePanel(root.transform, "Room_Hud", new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(560f, -120f), new Vector2(-24f, -18f), Page, true);
            AddTextAbsolute(hud, "Room_HudTitle", "Build within the yellow boundary", 20, FontStyle.Bold,
                new Vector2(18f, 12f), new Vector2(630f, 34f), TextAnchor.MiddleLeft, TextColor);
            AddTextAbsolute(hud, "Room_HudHint", "WASD move | Q lower | E raise | Right-drag look | Wheel zoom", 14, FontStyle.Normal,
                new Vector2(18f, 50f), new Vector2(760f, 30f), TextAnchor.MiddleLeft, Muted);
            AddButtonAbsolute(hud, "Room_Done", "Done - Continue", new Vector2(-208f, 28f), new Vector2(180f, 58f), true, "Room_DoneLabel");
        }

        private static void BuildFamiliarization(Transform stageRoot)
        {
            var root = CreateStageRoot(stageRoot, "Stage_RoomFamiliarization");
            var content = CreateScrollPanel(root.transform, "Familiarization_Panel", new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(18f, -330f), new Vector2(430f, -18f), Page);
            AddHeading(content, "Familiarization_Title", "Explore the example room");
            AddBody(content, "Familiarization_Instructions", "WASD: move | Right mouse drag: look. Become familiar with the furniture and layout; no target words are shown.", 90f);
            AddText(content, "Familiarization_Timer", string.Empty, 15, FontStyle.Normal, TextColor, 54f);
            AddButton(content, "Familiarization_Done", "Finish Room Familiarization - Pre-test", 60f, true);
        }

        private static void BuildPreTest(Transform stageRoot)
        {
            var content = CreateCenteredScrollStage(stageRoot, "Stage_PreTest", 920f);
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
            var content = CreateCenteredScrollStage(stageRoot, "Stage_Generation", 1260f);
            AddHeading(content, "Generation_Title", "Choose one LLM-generated story");
            AddBody(content, "Generation_Intro", "Read three complete continuous stories and select exactly one. The selected story is used unchanged for furniture mapping and VR narration.", 72f);
            AddText(content, "Generation_Timer", string.Empty, 14, FontStyle.Normal, Muted, 34f);
            var progress = AddVerticalGroup(content, "Generation_ProgressGroup", 118f, Card);
            AddBody(progress, "Generation_Progress", string.Empty, 58f);
            AddButton(progress, "Generation_Cancel", "Cancel Story Generation", 46f);
            AddBody(content, "Generation_Error", string.Empty, 48f);
            for (var i = 0; i < 3; i++)
            {
                var card = AddVerticalGroup(content, $"Generation_Card_{i}", 310f, Card);
                AddText(card, $"Generation_CardTitle_{i}", $"Story {i + 1}", 20, FontStyle.Bold, TextColor, 34f);
                AddText(card, $"Generation_Story_{i}", string.Empty, 15, FontStyle.Normal, TextColor, 206f);
                AddButton(card, $"Generation_Select_{i}", $"Select Story {i + 1}", 50f, false, $"Generation_SelectLabel_{i}");
            }
            AddButton(content, "Generation_Confirm", "Confirm Selected Story - Map Furniture And Words", 58f, true);
            AddButton(content, "Generation_Retry", "Retry Three Story Candidates", 52f);
            AddButton(content, "Generation_Back", "Back to Setup", 48f);
        }

        private static void BuildStoryAuthoring(Transform stageRoot)
        {
            var content = CreateCenteredScrollStage(stageRoot, "Stage_StoryAuthoring", 1280f);
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
                new Vector2(18f, 18f), new Vector2(430f, -18f), Page);
            AddHeading(left, "Assignment_Title", "Map Furniture And Words");
            AddBody(left, "Assignment_Intro", "Use WASD and right-drag to look, then click an actual furniture model. Choose one unused word in the right panel.", 84f);
            AddText(left, "Assignment_Count", string.Empty, 17, FontStyle.Bold, TextColor, 34f);
            AddText(left, "Assignment_Timer", string.Empty, 14, FontStyle.Normal, Muted, 48f);
            AddText(left, "Assignment_Summary", string.Empty, 14, FontStyle.Normal, Muted, 270f);
            AddButton(left, "Assignment_WritePackage", "Write Quest Package For HMD", 50f);
            AddText(left, "Assignment_PackagePath", string.Empty, 12, FontStyle.Normal, Muted, 62f);
            AddButton(left, "Assignment_EnterStudy", "Finish Assignment And Enter VR Study", 58f, true, "Assignment_EnterLabel");
            AddButton(left, "Assignment_DebugEnter", "DEBUG: Skip Voice And Enter VR Now", 48f);
            AddBody(left, "Assignment_Status", string.Empty, 74f);
            AddButton(left, "Assignment_Leave", "Leave without finishing", 46f);

            var right = CreateScrollPanel(root.transform, "Assignment_RightPanel", new Vector2(1f, 0f), new Vector2(1f, 1f),
                new Vector2(-468f, 18f), new Vector2(-18f, -18f), Page);
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
                new Vector2(18f, -650f), new Vector2(418f, -18f), Page);
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
                new Vector2(-468f, 18f), new Vector2(-18f, -18f), Page);
            var selected = AddVerticalGroup(right, "Study_SelectedGroup", 760f);
            AddText(selected, "Study_SelectedInfo", string.Empty, 16, FontStyle.Normal, TextColor, 190f);
            AddRawImage(selected, "Study_WordImage", 250f);
            AddButton(selected, "Study_Pronounce", "Pronounce Spanish Word", 48f);
            AddRawImage(selected, "Study_Snapshot", 180f);
            AddButton(selected, "Study_Capture", "Capture Scene Snapshot", 52f, true, "Study_CaptureLabel");
        }

        private static void BuildRecall(Transform stageRoot)
        {
            var content = CreateCenteredScrollStage(stageRoot, "Stage_Recall", 1320f);
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
            var content = CreateCenteredScrollStage(stageRoot, "Stage_Questionnaire", 980f);
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
            var content = CreateCenteredScrollStage(stageRoot, "Stage_Result", 1100f);
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
                new Vector2(-width * 0.5f, 18f), new Vector2(width * 0.5f, -18f), Page);
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
