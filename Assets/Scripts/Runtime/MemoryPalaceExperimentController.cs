using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Networking;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityEngine.XR;

namespace MemPalaceLLM
{
    public sealed class MemoryPalaceExperimentController : MonoBehaviour
    {
        private const float StudyMoveSpeed = 4.5f;
        private const float StudyLookSpeed = 0.15f;
        private const float ContentTop = 86f;
        private const float BottomMargin = 18f;
        private const int MidTestTriggerCount = 3;
        private const int RandomAdvancedWordCount = 8;
        private const string AdvancedPoolSetId = "advanced_pool";
        private const float BuilderAxisDragScale = 0.012f;
        private const float BuilderRotateDragScale = 0.45f;
        private const float BuilderScaleDragScale = 0.01f;
        private const float VrDefaultHeadHeight = 1.62f;
        private const float VrPointerDistance = 8f;
        private const float VrActionCooldownSeconds = 0.35f;

        private enum BuilderToolMode
        {
            Select,
            Move,
            Rotate,
            Scale
        }

        private enum BuilderWizardStep
        {
            Layout,
            CoreFurniture,
            CustomFurniture,
            TopDownConfirm,
            EntrancePreview
        }

        private static readonly string[] RoomLayoutOptions =
        {
            "Single Room", "Studio", "Square Studio", "Gallery",
            "1K", "1DK", "1LDK", "Long 1LDK",
            "L-Shape", "Loft", "Courtyard"
        };

        private static readonly string[] RoomLayoutDefaultDescriptions =
        {
            "A single open room for a desktop memory-palace demo: no corridor and no partitions, with clear walking space and 6-10 memorable furniture anchors.",
            "A compact studio apartment with one open living-sleeping-study space, a small kitchen wall, one window, and distinct furniture anchors arranged around the room.",
            "A square studio room with balanced wall placement: bed on one wall, desk and computer on another, bookshelf/wardrobe near a corner, and an open center for movement.",
            "A continuous gallery-like room with memorable anchors arranged along both side walls, leaving a clear exhibition route through the middle.",
            "A small Japanese 1K apartment: entrance door, compact kitchen corridor, one main room beyond it, bathroom/toilet zone near the entrance, and furniture anchors mostly in the main room.",
            "A 1DK apartment with an entrance/kitchen-dining area and one separate bedroom/study area, using door, dining table, stove, desk, bed, wardrobe, window, and bookshelf as anchors.",
            "A 1LDK apartment with entrance, compact kitchen/dining zone, living area, bedroom area, bathroom/toilet zone, and a balcony/window; keep the layout readable from a top-down view.",
            "A long narrow Japanese 1LDK apartment with a corridor-like route from entrance to balcony: door, toilet/bathroom near entrance, kitchen, living area, bedroom, and balcony window in sequence.",
            "An L-shaped apartment room where one arm is the living/study area and the other arm is the sleeping/storage area, with clear corner transitions and no furniture piled in the bend.",
            "A small loft-style room with lower living/study/kitchen area, a raised sleeping loft or platform, visible stairs/ladder, and anchors distributed between lower and upper zones.",
            "A compact apartment organized around a small courtyard or inner light-well, with windows facing the courtyard and furniture anchors arranged around the perimeter."
        };

        private static readonly string[] GuidedRoomShapeOptions =
        {
            "Square", "Rectangle", "L-Shape"
        };

        private static readonly string[] BuilderWizardStepLabels =
        {
            "1 Layout", "2 Main Items", "3 Adjust", "4 Plan Check", "5 Entrance"
        };

        private static readonly string[] GuidedFurnitureLabels =
        {
            "Bed", "Bookshelf", "Desk", "Wardrobe", "Window", "Computer",
            "Television", "Air Conditioner", "Dining Table", "Stove", "Toilet"
        };

        private static readonly string[] GuidedFurnitureZones =
        {
            "Left Wall", "Right Wall", "Back Wall", "Front Wall", "Center", "Bathroom"
        };

        private static readonly string[] GuidedBathroomZones =
        {
            "Front Left", "Front Right", "Back Left", "Back Right"
        };

        private static readonly FurnitureTemplate[] FurnitureTemplates =
        {
            new("Desk", "desk", "Cube", "#7A5A3B", new Vector3(1.5f, 0.22f, 0.85f), 0.72f, 0.78f),
            new("Chair", "chair", "Cube", "#5C6A7A", new Vector3(0.58f, 0.8f, 0.58f), 0.45f, 0.65f),
            new("Shelf", "shelf", "Cube", "#8B6A3A", new Vector3(1.1f, 2.2f, 0.38f), 1.1f, 1.25f),
            new("Table", "table", "Cube", "#8B6A3A", new Vector3(1.55f, 0.22f, 1.05f), 0.7f, 0.72f),
            new("Sofa", "sofa", "Cube", "#6A5C72", new Vector3(2.0f, 0.9f, 0.85f), 0.55f, 0.85f),
            new("Bed", "bed", "Cube", "#D8D6D0", new Vector3(2.4f, 0.45f, 1.65f), 0.45f, 0.62f),
            new("Plant", "plant", "Cylinder", "#6F8F5D", new Vector3(0.55f, 1.2f, 0.55f), 0.68f, 0.88f),
            new("Lamp", "lamp", "Sphere", "#FFD98A", new Vector3(0.42f, 0.42f, 0.42f), 1.25f, 0.0f)
        };

        private DemoDataLibrary library;
        private ExperimentStage stage = ExperimentStage.Setup;
        private ExperimentCondition condition = ExperimentCondition.LlmGenerated;
        private LlmProviderMode providerMode = LlmProviderMode.OllamaLocal;

        private Camera runtimeCamera;
        private Light runtimeLight;
        private Transform roomRoot;
        private Transform vrRigRoot;
        private Transform vrWorldUiRoot;
        private LineRenderer vrPointerLine;
        private GameObject vrPointerReticle;
        private Text vrProgressText;
        private Text vrTitleText;
        private Text vrMeaningText;
        private Text vrAnchorText;
        private Text vrCueText;
        private Text vrStoryText;
        private Text vrActionText;
        private Font labelFont;

        private readonly List<Rect> guiBlockRects = new();
        private readonly HashSet<string> viewedWords = new();
        private readonly HashSet<string> memorizedWords = new();
        private readonly List<InteractionLog> interactionLogs = new();
        private readonly List<RecallResponse> recallResponses = new();
        private readonly List<SnapshotTestResponse> snapshotTestResponses = new();
        private readonly Dictionary<string, Texture2D> memorySnapshots = new();
        private readonly Dictionary<string, Texture2D> mnemonicImageCues = new();
        private readonly HashSet<string> generatingImageCueWords = new();
        private readonly List<string> recognitionQueue = new();
        private readonly List<string> recognitionOptions = new();

        private GUIStyle titleStyle;
        private GUIStyle panelStyle;
        private GUIStyle sectionStyle;
        private GUIStyle labelStyle;
        private GUIStyle mutedStyle;
        private GUIStyle subtitleStyle;
        private GUIStyle smallTitleStyle;
        private GUIStyle guideStyle;
        private GUIStyle buttonStyle;
        private GUIStyle textAreaStyle;
        private GUIStyle badgeStyle;

        private Vector2 setupScroll;
        private Vector2 authoringScroll;
        private Vector2 generationScroll;
        private Vector2 studyInfoScroll;
        private Vector2 studyDetailScroll;
        private Vector2 recallScroll;
        private Vector2 questionnaireScroll;
        private Vector2 resultScroll;
        private Vector2 roomBuilderScroll;

        private int selectedWordSetIndex;
        private int selectedRoomLayoutIndex = 0;
        private int selectedFurnitureTemplateIndex;
        private int selectedBuilderAnchorIndex = -1;
        private int selectedRoomPrimitiveIndex = -1;
        private BuilderToolMode builderToolMode = BuilderToolMode.Select;
        private BuilderWizardStep builderWizardStep = BuilderWizardStep.Layout;
        private bool isBuilderDragging;
        private int builderDragAnchorIndex = -1;
        private int builderDragPrimitiveIndex = -1;
        private Vector3 builderDragAxis = Vector3.zero;
        private Vector2 builderDragStartMouse;
        private Vector3 builderDragStartPosition;
        private Vector3 builderDragStartRotation;
        private Vector3 builderDragStartScale;
        private Vector3 builderDragStartPlanePoint;
        private Vector3 builderDragAnchorOffset;
        private bool isFloorPatchPlacementActive;
        private int activeFloorPatchIndex = -1;
        private bool activeFloorPatchWasNew;
        private bool hasFloorPatchDropPreview;
        private Vector3 floorPatchDropPreviewPosition;
        private bool isWallSegmentPlacementActive;
        private int activeWallSegmentIndex = -1;
        private bool activeWallSegmentWasNew;
        private bool hasWallSegmentDropPreview;
        private Vector3 wallSegmentDropPreviewPosition;
        private Vector3 wallSegmentDropPreviewRotation;
        private Vector3 wallSegmentDropPreviewScale;
        private bool isDrawingShellWall;
        private bool hasShellWallStart;
        private Vector3 shellWallStartPoint;
        private Vector3 shellWallPreviewPoint;
        private int builderFeedbackAnchorIndex = -1;
        private float builderFeedbackUntil;
        private string participantId = "P001";
        private string ollamaBaseUrl = "http://localhost:11434/api/generate";
        private string ollamaModel = "qwen3:8b";
        private string imageGenerationEndpoint = "http://127.0.0.1:7860/sdapi/v1/txt2img";
        private string imageGenerationCheckpoint = string.Empty;
        private bool enableVrStudyMode = true;
        private bool showAbstractMnemonicProps = false;
        private bool showLegacyRoomGenerator = false;
        private bool showAdvancedRoomEditing = false;
        private string customFurnitureName = "Toilet";
        private string roomDescription = "A single open room for a desktop memory-palace demo: no corridor and no partitions, with clear walking space and 6-10 memorable furniture anchors.";
        private int guidedRoomShapeIndex = 1;
        private bool guidedHasBathroom = true;
        private bool guidedHasToilet = true;
        private int guidedBathroomZoneIndex = 0;
        private float guidedRoomWidthAdjustment;
        private float guidedRoomDepthAdjustment;
        private readonly bool[] guidedFurnitureIncluded =
        {
            true, true, true, true, true, true, true, true, false, false, true
        };
        private readonly int[] guidedFurnitureZoneIndexes =
        {
            3, 1, 0, 1, 2, 0, 2, 2, 4, 0, 5
        };
        private string statusMessage = "Ready to configure the experiment.";
        private string generationError = string.Empty;
        private string roomGenerationError = string.Empty;
        private string imageGenerationStatus = string.Empty;
        private string exportMessage = string.Empty;
        private string customCsvText = string.Empty;
        private bool useCustomCsv;
        private bool isGenerating;
        private bool isGeneratingRoom;
        private bool isGeneratingFurniture;
        private bool isGeneratingGuidedFurnitureLayout;
        private bool usedLiveLlmForCurrentSession;
        private bool usingRandomAdvancedWordSet;

        private WordSetDefinition activeWordSet;
        private List<MnemonicItemData> currentItems = new();
        private MnemonicItemData selectedStudyItem;
        private QuestionnaireResponse questionnaire = new();

        private string sessionId = string.Empty;
        private float studyStartTime;
        private float studyDurationSeconds;
        private float cameraYaw;
        private float cameraPitch;
        private int recognitionIndex;
        private bool isFinalRecognitionPhase;
        private bool awaitingRecognitionAdvance;
        private bool midTestCompleted;
        private bool finalTestCompleted;
        private bool isCapturingSnapshot;
        private float flashOverlayAlpha;
        private float teleportOverlayAlpha;
        private float nextVrActionTime;
        private bool vrHeadTrackingActive;
        private string currentRecognitionTargetWord = string.Empty;
        private string recognitionFeedback = string.Empty;
        private string lastJsonExportPath = string.Empty;
        private string lastCsvExportPath = string.Empty;

        private float ContentHeight => Screen.height - ContentTop - BottomMargin;

        private void Awake()
        {
            LoadDemoLibrary();
            EnsureSceneScaffold();
            SyncWordSetSelection();
            if (activeWordSet != null && activeWordSet.words.Count > 0)
            {
                customCsvText = BuildCsvText(activeWordSet);
            }
            MoveCameraToOverview();
        }

        private void Update()
        {
            EnsureSceneScaffold();

            if (stage == ExperimentStage.Study)
            {
                if (enableVrStudyMode)
                {
                    HandleVrStudyRuntime();
                }

                HandleStudyControls();
                return;
            }

            if (stage == ExperimentStage.RoomBuilder)
            {
                HandleRoomBuilderControls();
                return;
            }

            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        private void OnGUI()
        {
            EnsureStyles();

            if (Event.current.type == EventType.Layout)
            {
                guiBlockRects.Clear();
            }

            DrawTopBanner();

            switch (stage)
            {
                case ExperimentStage.Setup:
                    DrawSetupView();
                    break;
                case ExperimentStage.RoomBuilder:
                    DrawRoomBuilderView();
                    break;
                case ExperimentStage.Generation:
                    DrawGenerationView();
                    break;
                case ExperimentStage.SelfAuthoring:
                    DrawSelfAuthoringView();
                    break;
                case ExperimentStage.Study:
                    DrawStudyView();
                    break;
                case ExperimentStage.Recall:
                    DrawRecallView();
                    break;
                case ExperimentStage.Questionnaire:
                    DrawQuestionnaireView();
                    break;
                case ExperimentStage.Result:
                    DrawResultView();
                    break;
            }

            DrawScreenEffects();
        }

        private void LoadDemoLibrary()
        {
            var textAsset = Resources.Load<TextAsset>("MemPalaceDemoData");
            if (textAsset == null)
            {
                Debug.LogError("MemPalaceDemoData.json was not found in Resources.");
                library = new DemoDataLibrary();
                return;
            }

            library = JsonUtility.FromJson<DemoDataLibrary>(textAsset.text) ?? new DemoDataLibrary();
        }

        private void EnsureSceneScaffold()
        {
            if (runtimeCamera == null)
            {
                runtimeCamera = Camera.main;
                if (runtimeCamera == null)
                {
                    var cameraObject = new GameObject("Main Camera");
                    cameraObject.tag = "MainCamera";
                    runtimeCamera = cameraObject.AddComponent<Camera>();
                    cameraObject.AddComponent<AudioListener>();
                }

                runtimeCamera.clearFlags = CameraClearFlags.SolidColor;
                runtimeCamera.backgroundColor = new Color(0.08f, 0.11f, 0.16f);
                runtimeCamera.fieldOfView = 60f;
            }

            if (runtimeLight == null)
            {
                var lightObject = new GameObject("Runtime Directional Light");
                runtimeLight = lightObject.AddComponent<Light>();
                runtimeLight.type = LightType.Directional;
                runtimeLight.intensity = 1.1f;
                runtimeLight.color = new Color(1f, 0.96f, 0.88f);
                lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            }

            RenderSettings.ambientLight = new Color(0.6f, 0.62f, 0.68f);
        }

        private void EnsureStyles()
        {
            if (titleStyle != null
                && panelStyle != null
                && sectionStyle != null
                && labelStyle != null
                && mutedStyle != null
                && subtitleStyle != null
                && smallTitleStyle != null
                && guideStyle != null
                && buttonStyle != null
                && textAreaStyle != null
                && badgeStyle != null)
            {
                return;
            }

            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 26,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };

            panelStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(18, 18, 18, 18),
                normal = { background = MakeTexture(new Color(0.10f, 0.12f, 0.17f, 0.94f)) }
            };

            sectionStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(12, 12, 12, 12),
                margin = new RectOffset(0, 0, 6, 6),
                normal = { background = MakeTexture(new Color(0.14f, 0.17f, 0.23f, 0.95f)) }
            };

            labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                wordWrap = true,
                normal = { textColor = new Color(0.95f, 0.96f, 0.99f) }
            };

            mutedStyle = new GUIStyle(labelStyle)
            {
                fontSize = 12,
                normal = { textColor = new Color(0.70f, 0.76f, 0.84f) }
            };

            subtitleStyle = new GUIStyle(mutedStyle)
            {
                fontSize = 12,
                normal = { textColor = new Color(0.80f, 0.84f, 0.91f) }
            };

            smallTitleStyle = new GUIStyle(labelStyle)
            {
                fontSize = 17,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };

            guideStyle = new GUIStyle(labelStyle)
            {
                fontSize = 12,
                wordWrap = true,
                normal = { textColor = new Color(0.90f, 0.93f, 0.98f) }
            };

            buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 13,
                fixedHeight = 32
            };

            textAreaStyle = new GUIStyle(GUI.skin.textArea)
            {
                wordWrap = true,
                fontSize = 13
            };

            badgeStyle = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 12,
                padding = new RectOffset(10, 10, 5, 5),
                normal = { background = MakeTexture(new Color(0.17f, 0.35f, 0.58f, 0.95f)), textColor = Color.white }
            };
        }

        private void DrawTopBanner()
        {
            var bannerRect = new Rect(16f, 14f, Screen.width - 32f, 58f);
            GUI.Box(bannerRect, GUIContent.none, panelStyle);
            RegisterGuiRect(bannerRect);

            GUI.Label(new Rect(28f, 18f, 520f, 28f), "LLM Memory Palace Experiment Demo", titleStyle);
            GUI.Label(new Rect(30f, 46f, 980f, 18f), "Flow: Setup / Room Builder -> Preview / Author -> Study -> Mid Test -> Final Test -> Questionnaire -> Export", subtitleStyle);
            GUI.Box(new Rect(Screen.width - 282f, 24f, 252f, 24f), $"Step {GetStageStepNumber()} / 7 - {GetStageDisplayName()}", badgeStyle);
        }

        private int GetStageStepNumber()
        {
            switch (stage)
            {
                case ExperimentStage.Setup:
                case ExperimentStage.RoomBuilder:
                    return 1;
                case ExperimentStage.Generation:
                case ExperimentStage.SelfAuthoring:
                    return 2;
                case ExperimentStage.Study:
                    return 3;
                case ExperimentStage.Recall:
                    return isFinalRecognitionPhase ? 5 : 4;
                case ExperimentStage.Questionnaire:
                    return 6;
                case ExperimentStage.Result:
                    return 7;
                default:
                    return 1;
            }
        }

        private string GetStageDisplayName()
        {
            switch (stage)
            {
                case ExperimentStage.Setup:
                    return "Setup";
                case ExperimentStage.RoomBuilder:
                    return "Room Builder";
                case ExperimentStage.Generation:
                    return "Mnemonic Preview";
                case ExperimentStage.SelfAuthoring:
                    return "Author Mnemonics";
                case ExperimentStage.Study:
                    return "Study Room";
                case ExperimentStage.Recall:
                    return isFinalRecognitionPhase ? "Final Image Test" : "Mid Image Test";
                case ExperimentStage.Questionnaire:
                    return "Questionnaire";
                case ExperimentStage.Result:
                    return "Results";
                default:
                    return stage.ToString();
            }
        }

        private string GetConditionDisplayName()
        {
            return condition == ExperimentCondition.LlmGenerated ? "LLM Generated" : "Self Generated";
        }

        private string GetStudyProgressHint()
        {
            if (!midTestCompleted)
            {
                var needed = Mathf.Max(0, MidTestTriggerCount - memorizedWords.Count);
                return needed > 0
                    ? $"Capture {needed} more snapshot(s) to unlock the mid test."
                    : "Mid test ready. Start it when you want to probe the stored scenes.";
            }

            if (!finalTestCompleted)
            {
                var needed = Mathf.Max(0, currentItems.Count - memorizedWords.Count);
                return needed > 0
                    ? $"Capture {needed} more snapshot(s) to unlock the final test."
                    : "All snapshots captured. Final test is ready.";
            }

            return "All image tests are complete. Continue to the questionnaire when ready.";
        }

        private void DrawScreenEffects()
        {
            if (flashOverlayAlpha > 0.001f)
            {
                GUI.color = new Color(1f, 1f, 1f, flashOverlayAlpha);
                GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
                GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(flashOverlayAlpha));
                GUI.Box(new Rect(Screen.width * 0.5f - 110f, Screen.height * 0.5f - 24f, 220f, 34f), "Snapshot Stored", badgeStyle);
                GUI.color = Color.white;
            }

            if (teleportOverlayAlpha > 0.001f)
            {
                GUI.color = new Color(0.05f, 0.07f, 0.12f, teleportOverlayAlpha);
                GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
                GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(teleportOverlayAlpha));
                GUI.Box(new Rect(Screen.width * 0.5f - 145f, Screen.height * 0.5f - 24f, 290f, 34f), "Returning To Correct Anchor...", badgeStyle);
                GUI.color = Color.white;
            }
        }

        private void DrawSetupView()
        {
            var panelRect = new Rect(18f, ContentTop, Screen.width - 36f, ContentHeight);
            GUI.Box(panelRect, GUIContent.none, panelStyle);
            RegisterGuiRect(panelRect);

            GUILayout.BeginArea(panelRect);
            setupScroll = GUILayout.BeginScrollView(setupScroll);

            GUILayout.Label("Step 1 of 7 - Experiment Setup", titleStyle);
            GUILayout.Label("Configure the participant, choose the condition, and prepare the local Ollama generation settings for the mnemonic condition.", mutedStyle);
            GUILayout.Space(10);

            GUILayout.BeginVertical(sectionStyle);
            GUILayout.Label("Demo Flow", smallTitleStyle);
            GUILayout.Label("1. Setup the participant and select a condition.\n2. Preview LLM-generated cues or author them manually.\n3. Enter the room and capture memory snapshots.\n4. Run a mid image-choice test.\n5. Run a final image-choice test and teleport back to the correct anchor.\n6. Collect questionnaire ratings.\n7. Export JSON and CSV results.", guideStyle);
            GUILayout.EndVertical();

            GUILayout.BeginVertical(sectionStyle);
            GUILayout.Label("Participant", smallTitleStyle);
            participantId = DrawLabeledTextField("Participant ID", participantId);
            GUILayout.EndVertical();

            GUILayout.BeginVertical(sectionStyle);
            GUILayout.Label("Room Design", smallTitleStyle);
            GUILayout.Label(RoomSpecCatalog.RoomName, labelStyle);
            GUILayout.Label(RoomSpecCatalog.CurrentRoom.generatedBy, mutedStyle);
            GUILayout.Label(RoomSpecCatalog.CurrentRoom.summary, mutedStyle);
            GUILayout.Space(4);
            GUILayout.Label("Main flow: open the Guided Room Builder, confirm only Square / Rectangle / L-Shape plus bathroom/toilet, then ask Ollama to place selected furniture from rough zones.", mutedStyle);
            GUILayout.Label($"Anchors available: {RoomSpecCatalog.AnchorCount}", mutedStyle);
            GUILayout.Label($"Active Room Source: {RoomSpecCatalog.CurrentRoom.generatedBy}", labelStyle);
            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Open Room Builder", buttonStyle))
            {
                OpenRoomBuilder();
            }

            if (GUILayout.Button("Reload Default Room", buttonStyle))
            {
                RoomSpecCatalog.ReloadResourceRoom();
                ClearStudyRoom();
                MoveCameraToOverview();
                selectedBuilderAnchorIndex = -1;
                statusMessage = "Default resource room reloaded.";
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            showLegacyRoomGenerator = GUILayout.Toggle(showLegacyRoomGenerator, "Show legacy text-description room generator (advanced)");
            if (showLegacyRoomGenerator)
            {
                GUILayout.Space(8);
                GUILayout.Label("Legacy Layout Preset", mutedStyle);
                var previousRoomLayoutIndex = Mathf.Clamp(selectedRoomLayoutIndex, 0, RoomLayoutOptions.Length - 1);
                selectedRoomLayoutIndex = GUILayout.SelectionGrid(previousRoomLayoutIndex, RoomLayoutOptions, 4);
                if (selectedRoomLayoutIndex != previousRoomLayoutIndex)
                {
                    roomDescription = GetDefaultRoomDescription(selectedRoomLayoutIndex);
                    roomGenerationError = string.Empty;
                    statusMessage = $"Loaded default description for {RoomLayoutOptions[selectedRoomLayoutIndex]}.";
                }

                GUILayout.Label("Legacy Room Description", mutedStyle);
                roomDescription = GUILayout.TextArea(roomDescription ?? string.Empty, textAreaStyle, GUILayout.MinHeight(70f));
                if (GUILayout.Button("Reset Description To Selected Layout Default", buttonStyle))
                {
                    roomDescription = GetDefaultRoomDescription(selectedRoomLayoutIndex);
                    statusMessage = $"Reset description to the {RoomLayoutOptions[Mathf.Clamp(selectedRoomLayoutIndex, 0, RoomLayoutOptions.Length - 1)]} default.";
                }

                GUI.enabled = !isGeneratingRoom;
                if (GUILayout.Button(isGeneratingRoom ? "Generating Room..." : "Generate Legacy Room From Description", buttonStyle))
                {
                    BeginRoomGeneration();
                }
                GUI.enabled = true;

                if (isGeneratingRoom && GUILayout.Button("Cancel Room Generation", buttonStyle))
                {
                    CancelRoomGeneration();
                }
                if (!string.IsNullOrWhiteSpace(roomGenerationError))
                {
                    GUILayout.Label(roomGenerationError, labelStyle);
                }
            }
            GUILayout.EndVertical();

            GUILayout.BeginVertical(sectionStyle);
            GUILayout.Label("Condition", smallTitleStyle);
            condition = (ExperimentCondition)GUILayout.Toolbar((int)condition, new[] { "LLM Generated", "Self Generated" });
            GUILayout.Space(8);

            if (condition == ExperimentCondition.LlmGenerated)
            {
                providerMode = LlmProviderMode.OllamaLocal;
                ollamaBaseUrl = DrawLabeledTextField("Ollama Endpoint", ollamaBaseUrl);
                ollamaModel = DrawLabeledTextField("Model", ollamaModel);
                imageGenerationEndpoint = DrawLabeledTextField("Image Endpoint", imageGenerationEndpoint);
                imageGenerationCheckpoint = DrawLabeledTextField("Image Checkpoint", imageGenerationCheckpoint);
                showAbstractMnemonicProps = GUILayout.Toggle(showAbstractMnemonicProps, "Show experimental 3D proxy props in the room");
                GUILayout.Label("Mnemonic text uses local Ollama. Cue images use a local Stable Diffusion WebUI endpoint; set a checkpoint here if you want a more object-focused image model.", mutedStyle);
            }
            else
            {
                GUILayout.Label("Self Generated condition lets the participant assign anchors and type their own bilingual scenes and memory links before entering the room.", mutedStyle);
            }

            enableVrStudyMode = GUILayout.Toggle(enableVrStudyMode, "Use VR study runtime after entering the room");
            GUILayout.Label("Desktop setup, room generation, and authoring stay unchanged. Study controls add XR head/controller support when a headset is active.", mutedStyle);

            GUILayout.EndVertical();

            GUILayout.BeginVertical(sectionStyle);
            GUILayout.Label("Word Material", smallTitleStyle);

            var wordSetNames = BuildWordSetNames();
            if (wordSetNames.Length > 0)
            {
                var newSelectedIndex = GUILayout.Toolbar(selectedWordSetIndex, wordSetNames);
                if (newSelectedIndex != selectedWordSetIndex)
                {
                    selectedWordSetIndex = newSelectedIndex;
                    usingRandomAdvancedWordSet = false;
                    SyncWordSetSelection();
                }
            }

            GUILayout.Space(6);
            GUILayout.Label(activeWordSet.description, mutedStyle);
            GUILayout.Space(6);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button($"Random Spanish Nouns {Mathf.Min(RandomAdvancedWordCount, RoomSpecCatalog.AnchorCount)}", buttonStyle))
            {
                GenerateRandomAdvancedWordSet();
            }

            GUI.enabled = usingRandomAdvancedWordSet;
            if (GUILayout.Button("Back To Preset Set", buttonStyle))
            {
                usingRandomAdvancedWordSet = false;
                SyncWordSetSelection();
                customCsvText = BuildCsvText(activeWordSet);
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            if (usingRandomAdvancedWordSet)
            {
                GUILayout.Label("Random Spanish noun sample active: " + BuildWordListSummary(activeWordSet.words), mutedStyle);
                GUILayout.Space(6);
            }

            useCustomCsv = GUILayout.Toggle(useCustomCsv, "Use custom CSV/pasted word list instead of the default word set");
            if (useCustomCsv)
            {
                GUILayout.Label("Format: word,meaning,meaning_ja", mutedStyle);
                customCsvText = GUILayout.TextArea(customCsvText, textAreaStyle, GUILayout.MinHeight(180f));
            }

            GUILayout.EndVertical();

            GUILayout.BeginVertical(sectionStyle);
            GUILayout.Label("Next Step", smallTitleStyle);

            GUI.enabled = !isGeneratingRoom;
            if (condition == ExperimentCondition.LlmGenerated)
            {
                if (GUILayout.Button("Next: Generate Mnemonics", buttonStyle))
                {
                    BeginLlmFlow();
                }
            }
            else
            {
                if (GUILayout.Button("Next: Open Authoring Workspace", buttonStyle))
                {
                    BeginSelfAuthoring();
                }
            }
            GUI.enabled = true;

            if (isGeneratingRoom)
            {
                GUILayout.Label("Room generation is still running. Wait for the room name to change, or cancel it before continuing.", mutedStyle);
            }

            GUILayout.Space(8);
            GUILayout.Label(statusMessage, mutedStyle);
            if (!string.IsNullOrWhiteSpace(generationError))
            {
                GUILayout.Label(generationError, labelStyle);
            }

            GUILayout.EndVertical();

            GUILayout.EndScrollView();
            GUILayout.EndArea();

            MoveCameraToOverview();
        }

        private void DrawRoomBuilderView()
        {
            var panelRect = new Rect(18f, ContentTop, 430f, ContentHeight);
            GUI.Box(panelRect, GUIContent.none, panelStyle);
            RegisterGuiRect(panelRect);

            GUILayout.BeginArea(panelRect);
            roomBuilderScroll = GUILayout.BeginScrollView(roomBuilderScroll, false, true);

            GUILayout.Label("Room Builder", titleStyle);
            GUILayout.Label("Create a clean open-roof room preview first. Advanced gizmo editing is available only if you expand the manual editor.", mutedStyle);
            GUILayout.Space(8);

            DrawGuidedRoomBuilderWizard();
            GUILayout.Space(8);

            GUILayout.BeginVertical(sectionStyle);
            GUILayout.Label("Current Room", smallTitleStyle);
            GUILayout.Label(RoomSpecCatalog.RoomName, labelStyle);
            GUILayout.Label($"Anchors: {RoomSpecCatalog.AnchorCount}", mutedStyle);
            GUILayout.Label(RoomSpecCatalog.CurrentRoom.summary, mutedStyle);
            GUILayout.EndVertical();

            showAdvancedRoomEditing = GUILayout.Toggle(showAdvancedRoomEditing, "Show advanced manual editor (gizmos / furniture list / shell repair)");
            if (showAdvancedRoomEditing)
            {
            GUILayout.BeginVertical(sectionStyle);
            GUILayout.Label("Runtime Editor Tools", smallTitleStyle);
            GUILayout.Label($"Active Tool: {builderToolMode}", labelStyle);
            GUILayout.Label("Q Select  |  W Move  |  E Rotate  |  R Scale", mutedStyle);
            GUILayout.Label("Move: drag on floor or drag colored axis. Rotate/Scale: horizontal drag adjusts the selected furniture.", mutedStyle);
            if (GUILayout.Button("Clean Up Layout / Snap To Floor", buttonStyle))
            {
                CleanUpCurrentRoomLayout();
            }
            GUILayout.EndVertical();

            GUILayout.BeginVertical(sectionStyle);
            GUILayout.Label("Add Furniture Anchor", smallTitleStyle);
            selectedFurnitureTemplateIndex = GUILayout.SelectionGrid(
                Mathf.Clamp(selectedFurnitureTemplateIndex, 0, FurnitureTemplates.Length - 1),
                BuildFurnitureTemplateNames(),
                2);
            if (GUILayout.Button("Add In Front Of Camera", buttonStyle))
            {
                AddFurnitureAnchor(FurnitureTemplates[selectedFurnitureTemplateIndex]);
            }
            GUILayout.Space(6);
            customFurnitureName = DrawLabeledTextField("Custom Furniture Name", customFurnitureName);
            GUI.enabled = !isGeneratingFurniture;
            if (GUILayout.Button(isGeneratingFurniture ? "Generating Custom Furniture..." : "Add Custom Furniture With LLM", buttonStyle))
            {
                BeginCustomFurnitureGeneration();
            }
            GUI.enabled = true;
            GUILayout.EndVertical();

            GUILayout.BeginVertical(sectionStyle);
            GUILayout.Label("Furniture Anchors", smallTitleStyle);
            var anchors = RoomSpecCatalog.CurrentRoom.anchors;
            for (int i = 0; i < anchors.Count; i++)
            {
                var prefix = i == selectedBuilderAnchorIndex ? "> " : string.Empty;
                if (GUILayout.Button(prefix + anchors[i].label, buttonStyle))
                {
                    selectedBuilderAnchorIndex = i;
                    selectedRoomPrimitiveIndex = -1;
                    FocusCameraOnAnchor(anchors[i]);
                    BuildRoomBuilderPreview();
                }
            }
            GUILayout.EndVertical();

            if (TryGetSelectedBuilderAnchor(out var selectedAnchor))
            {
                GUILayout.BeginVertical(sectionStyle);
                GUILayout.Label("Selected Furniture", smallTitleStyle);
                selectedAnchor.label = DrawLabeledTextField("Label", selectedAnchor.label);
                GUILayout.Label($"ID: {selectedAnchor.id}", mutedStyle);
                GUILayout.Label($"Position: x {selectedAnchor.position.x:F2}, y {selectedAnchor.position.y:F2}, z {selectedAnchor.position.z:F2}", mutedStyle);
                GUILayout.Label($"Rotation Y: {selectedAnchor.rotationEuler.y:F0} deg", mutedStyle);
                GUILayout.Label($"Scale: x {selectedAnchor.scale.x:F2}, y {selectedAnchor.scale.y:F2}, z {selectedAnchor.scale.z:F2}", mutedStyle);
                GUILayout.Label("Use Q/W/E/R and drag in the scene for the main editing flow. The buttons below are just backup controls.", mutedStyle);

                GUILayout.Space(6);
                GUILayout.Label("Move", mutedStyle);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("X-", buttonStyle)) AdjustSelectedAnchorPosition(new Vector3(-0.25f, 0f, 0f));
                if (GUILayout.Button("X+", buttonStyle)) AdjustSelectedAnchorPosition(new Vector3(0.25f, 0f, 0f));
                if (GUILayout.Button("Z-", buttonStyle)) AdjustSelectedAnchorPosition(new Vector3(0f, 0f, -0.25f));
                if (GUILayout.Button("Z+", buttonStyle)) AdjustSelectedAnchorPosition(new Vector3(0f, 0f, 0.25f));
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Down", buttonStyle)) AdjustSelectedAnchorPosition(new Vector3(0f, -0.15f, 0f));
                if (GUILayout.Button("Up", buttonStyle)) AdjustSelectedAnchorPosition(new Vector3(0f, 0.15f, 0f));
                GUILayout.EndHorizontal();

                GUILayout.Label("Rotate / Scale", mutedStyle);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Rotate -15", buttonStyle)) RotateSelectedAnchor(-15f);
                if (GUILayout.Button("Rotate +15", buttonStyle)) RotateSelectedAnchor(15f);
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Smaller", buttonStyle)) ScaleSelectedAnchor(0.9f);
                if (GUILayout.Button("Bigger", buttonStyle)) ScaleSelectedAnchor(1.1f);
                GUILayout.EndHorizontal();

                if (GUILayout.Button("Delete Selected Furniture", buttonStyle))
                {
                    DeleteSelectedAnchor();
                }
                GUILayout.EndVertical();
            }

            GUILayout.BeginVertical(sectionStyle);
            GUILayout.Label("Room Shell Sandbox", smallTitleStyle);
            GUILayout.Label("Click walls, floors, or bathroom blocks in the scene, then use W/E/R to move, rotate, or scale them. Floor patches enter a snap-preview placement mode so the room can be extended like a sandbox builder.", mutedStyle);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Add Floor Patch", buttonStyle))
            {
                AddFloorPatchPrimitive();
            }

            if (GUILayout.Button("Add Wall Segment", buttonStyle))
            {
                AddWallSegmentPrimitive();
            }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(isDrawingShellWall ? "Drawing Wall..." : "Draw Wall By Two Clicks", buttonStyle))
            {
                BeginDrawShellWall();
            }

            if (GUILayout.Button("Add Bathroom Block", buttonStyle))
            {
                AddRoomShellPrimitive("Bathroom Block", "Cube", "#C7D7DF", new Vector3(0f, 0.08f, 0f), new Vector3(2.1f, 0.06f, 1.9f));
            }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();

            GUI.enabled = isDrawingShellWall;
            if (GUILayout.Button("Cancel Wall Draw", buttonStyle))
            {
                CancelDrawShellWall("Wall drawing cancelled.");
            }
            GUI.enabled = true;

            if (GUILayout.Button("Deselect Shell", buttonStyle))
            {
                selectedRoomPrimitiveIndex = -1;
                BuildRoomBuilderPreview();
            }
            GUILayout.EndHorizontal();
            if (isDrawingShellWall)
            {
                GUILayout.Label(hasShellWallStart
                    ? "Wall draw: move the mouse to preview, then left-click the endpoint. Esc/right-click cancels."
                    : "Wall draw: left-click the floor to choose the start point.",
                    mutedStyle);
            }
            if (isFloorPatchPlacementActive)
            {
                GUILayout.Label("Floor placement: walls are hidden temporarily. Drag the patch; the blue dashed shadow shows where it will snap. Release the mouse or press Finish to rebuild the shell view.", mutedStyle);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Finish Floor Placement", buttonStyle))
                {
                    FinishFloorPatchPlacement("Floor patch placed. Walls restored.");
                }

                if (GUILayout.Button("Cancel Floor Placement", buttonStyle))
                {
                    CancelFloorPatchPlacement();
                }
                GUILayout.EndHorizontal();
            }
            if (isWallSegmentPlacementActive)
            {
                GUILayout.Label("Wall placement: drag the wall segment near a floor edge. The blue dashed shadow shows the snapped wall before it is placed.", mutedStyle);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Finish Wall Placement", buttonStyle))
                {
                    FinishWallSegmentPlacement("Wall segment placed with edge snapping.");
                }

                if (GUILayout.Button("Cancel Wall Placement", buttonStyle))
                {
                    CancelWallSegmentPlacement();
                }
                GUILayout.EndHorizontal();
            }

            if (TryGetSelectedRoomPrimitive(out var selectedPrimitive))
            {
                GUILayout.Space(5);
                GUILayout.Label("Selected Shell Piece", smallTitleStyle);
                selectedPrimitive.label = DrawLabeledTextField("Label", selectedPrimitive.label);
                GUILayout.Label($"ID: {selectedPrimitive.id}", mutedStyle);
                GUILayout.Label($"Position: x {selectedPrimitive.position.x:F2}, y {selectedPrimitive.position.y:F2}, z {selectedPrimitive.position.z:F2}", mutedStyle);
                GUILayout.Label($"Scale: x {selectedPrimitive.scale.x:F2}, y {selectedPrimitive.scale.y:F2}, z {selectedPrimitive.scale.z:F2}", mutedStyle);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Rotate -15", buttonStyle)) RotateSelectedRoomPrimitive(-15f);
                if (GUILayout.Button("Rotate +15", buttonStyle)) RotateSelectedRoomPrimitive(15f);
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Smaller", buttonStyle)) ScaleSelectedRoomPrimitive(0.9f);
                if (GUILayout.Button("Bigger", buttonStyle)) ScaleSelectedRoomPrimitive(1.1f);
                GUILayout.EndHorizontal();
                if (GUILayout.Button("Delete Shell Piece", buttonStyle))
                {
                    DeleteSelectedRoomPrimitive();
                }
            }
            GUILayout.EndVertical();
            }

            GUILayout.BeginVertical(sectionStyle);
            GUILayout.Label("Finish", smallTitleStyle);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Use This Room", buttonStyle))
            {
                ClearStudyRoom();
                stage = ExperimentStage.Setup;
                selectedBuilderAnchorIndex = -1;
                MoveCameraToOverview();
                statusMessage = "Room builder changes are active for the next session.";
            }

            if (GUILayout.Button("Save Room JSON", buttonStyle))
            {
                SaveCurrentRoomSpec();
            }
            GUILayout.EndHorizontal();
            if (GUILayout.Button("Back To Setup", buttonStyle))
            {
                ClearStudyRoom();
                stage = ExperimentStage.Setup;
                selectedBuilderAnchorIndex = -1;
                MoveCameraToOverview();
            }
            GUILayout.Label(statusMessage, mutedStyle);
            GUILayout.EndVertical();

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawGuidedRoomBuilderWizard()
        {
            GUILayout.BeginVertical(sectionStyle);
            GUILayout.Label("Guided Build Flow", smallTitleStyle);
            GUILayout.Label("Teacher-demo flow: choose the remembered room shape, generate a furnished dollhouse-style room, confirm it from top-down, then enter from the door.", mutedStyle);
            builderWizardStep = (BuilderWizardStep)GUILayout.Toolbar((int)builderWizardStep, BuilderWizardStepLabels);
            GUILayout.Space(6);

            switch (builderWizardStep)
            {
                case BuilderWizardStep.Layout:
                    DrawGuidedLayoutStep();
                    break;
                case BuilderWizardStep.CoreFurniture:
                    DrawGuidedCoreFurnitureStep();
                    break;
                case BuilderWizardStep.CustomFurniture:
                    DrawGuidedCustomFurnitureStep();
                    break;
                case BuilderWizardStep.TopDownConfirm:
                    DrawGuidedTopDownConfirmStep();
                    break;
                case BuilderWizardStep.EntrancePreview:
                    DrawGuidedEntrancePreviewStep();
                    break;
            }

            GUILayout.EndVertical();
        }

        private void DrawGuidedLayoutStep()
        {
            GUILayout.Label("Step 1: ask the participant to imagine their own room, then choose the closest basic plan.", labelStyle);
            GUILayout.Label("Room Shape", mutedStyle);
            guidedRoomShapeIndex = GUILayout.SelectionGrid(
                Mathf.Clamp(guidedRoomShapeIndex, 0, GuidedRoomShapeOptions.Length - 1),
                GuidedRoomShapeOptions,
                3);
            guidedHasBathroom = GUILayout.Toggle(guidedHasBathroom, "Has bathroom area");
            GUI.enabled = guidedHasBathroom;
            guidedHasToilet = GUILayout.Toggle(guidedHasToilet, "Has toilet");
            GUI.enabled = true;
            if (!guidedHasBathroom)
            {
                guidedHasToilet = false;
            }
            else
            {
                GUILayout.Label("Bathroom Position", mutedStyle);
                guidedBathroomZoneIndex = GUILayout.SelectionGrid(
                    Mathf.Clamp(guidedBathroomZoneIndex, 0, GuidedBathroomZones.Length - 1),
                    GuidedBathroomZones,
                    2);
            }

            GUILayout.Label($"Whole Area Adjustment: width {guidedRoomWidthAdjustment:+0.0;-0.0;0.0}, depth {guidedRoomDepthAdjustment:+0.0;-0.0;0.0}", mutedStyle);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Narrower", buttonStyle)) guidedRoomWidthAdjustment = Mathf.Max(-2f, guidedRoomWidthAdjustment - 0.5f);
            if (GUILayout.Button("Wider", buttonStyle)) guidedRoomWidthAdjustment = Mathf.Min(2f, guidedRoomWidthAdjustment + 0.5f);
            if (GUILayout.Button("Shallower", buttonStyle)) guidedRoomDepthAdjustment = Mathf.Max(-2f, guidedRoomDepthAdjustment - 0.5f);
            if (GUILayout.Button("Deeper", buttonStyle)) guidedRoomDepthAdjustment = Mathf.Min(2f, guidedRoomDepthAdjustment + 0.5f);
            GUILayout.EndHorizontal();
            if (GUILayout.Button("Reset Area Size", buttonStyle))
            {
                guidedRoomWidthAdjustment = 0f;
                guidedRoomDepthAdjustment = 0f;
            }

            GUILayout.Label("For a quick teacher demo, generate the complete furnished preview. Use shell-only if you want to inspect the layout before furniture.", mutedStyle);
            if (GUILayout.Button("Generate Complete Dollhouse Preview", buttonStyle))
            {
                ApplyGuidedLayoutShell();
                GenerateGuidedMainFurniture();
                builderWizardStep = BuilderWizardStep.TopDownConfirm;
                MoveCameraToPlanView();
                statusMessage = "Generated a furnished open-roof room preview. If it matches the participant's room, continue to entrance view.";
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Generate Editable Floor Plan Shell", buttonStyle))
            {
                ApplyGuidedLayoutShell();
            }

            if (GUILayout.Button("Top-Down Preview", buttonStyle))
            {
                MoveCameraToPlanView();
            }
            GUILayout.EndHorizontal();

            if (GUILayout.Button("Layout Looks Correct -> Main Items", buttonStyle))
            {
                builderWizardStep = BuilderWizardStep.CoreFurniture;
                MoveCameraToPlanView();
                statusMessage = "Layout accepted. Choose which main objects exist and where they should be placed.";
            }
        }

        private void DrawGuidedCoreFurnitureStep()
        {
            GUILayout.Label("Step 2: confirm the major objects. Unchecked means 'not in this room'. Position is only a rough area; Ollama will choose exact plausible coordinates.", labelStyle);

            var previewChanged = false;
            for (int i = 0; i < GuidedFurnitureLabels.Length; i++)
            {
                var label = GuidedFurnitureLabels[i];
                var canUse = !string.Equals(label, "Toilet", StringComparison.OrdinalIgnoreCase) || (guidedHasBathroom && guidedHasToilet);
                var oldEnabled = GUI.enabled;
                GUI.enabled = oldEnabled && canUse;

                GUILayout.BeginVertical(sectionStyle);
                var previousIncluded = guidedFurnitureIncluded[i];
                guidedFurnitureIncluded[i] = GUILayout.Toggle(guidedFurnitureIncluded[i], label + (canUse ? string.Empty : " (disabled: no toilet area)"));
                previewChanged |= previousIncluded != guidedFurnitureIncluded[i];
                GUI.enabled = oldEnabled && canUse && guidedFurnitureIncluded[i];
                var previousZoneIndex = guidedFurnitureZoneIndexes[i];
                guidedFurnitureZoneIndexes[i] = GUILayout.Toolbar(
                    Mathf.Clamp(guidedFurnitureZoneIndexes[i], 0, GuidedFurnitureZones.Length - 1),
                    GuidedFurnitureZones);
                previewChanged |= previousZoneIndex != guidedFurnitureZoneIndexes[i];
                GUI.enabled = oldEnabled;
                GUILayout.EndVertical();

                if (!canUse)
                {
                    if (guidedFurnitureIncluded[i])
                    {
                        previewChanged = true;
                        guidedFurnitureIncluded[i] = false;
                    }
                }
            }

            if (previewChanged)
            {
                BuildRoomBuilderPreview();
                statusMessage = "Updated dashed placement previews for the selected rough furniture zones.";
            }

            GUILayout.BeginHorizontal();
            GUI.enabled = !isGeneratingGuidedFurnitureLayout;
            if (GUILayout.Button(isGeneratingGuidedFurnitureLayout ? "Ollama Is Placing Furniture..." : "Generate Smart Furniture Layout With Ollama", buttonStyle))
            {
                BeginGuidedMainFurnitureGeneration();
            }

            if (GUILayout.Button("Generate Local Fallback Layout", buttonStyle))
            {
                GenerateGuidedMainFurniture();
                builderWizardStep = BuilderWizardStep.CustomFurniture;
            }
            GUI.enabled = true;

            if (GUILayout.Button("Back To Layout", buttonStyle))
            {
                builderWizardStep = BuilderWizardStep.Layout;
            }
            GUILayout.EndHorizontal();
        }

        private void DrawGuidedCustomFurnitureStep()
        {
            GUILayout.Label("Step 3: add any extra furniture, then adjust the whole room with Q/W/E/R. The detailed editor below stays available for custom objects and backup controls.", labelStyle);
            GUILayout.Label("Tip: W = move, E = rotate, R = scale, Q = select. Drag the colored gizmo or drag the object itself.", mutedStyle);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Plan Check From Top View", buttonStyle))
            {
                builderWizardStep = BuilderWizardStep.TopDownConfirm;
                MoveCameraToPlanView();
            }

            if (GUILayout.Button("Clean And Rebuild Preview", buttonStyle))
            {
                CleanUpCurrentRoomLayout();
                MoveCameraToPlanView();
            }
            GUILayout.EndHorizontal();
        }

        private void DrawGuidedTopDownConfirmStep()
        {
            GUILayout.Label("Step 4: show the participant this floor-plan-like overview and ask whether the room matches their mental room closely enough.", labelStyle);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Looks Correct -> Entrance View", buttonStyle))
            {
                builderWizardStep = BuilderWizardStep.EntrancePreview;
                MoveCameraToEntrancePreview();
            }

            if (GUILayout.Button("Needs Editing -> Adjust", buttonStyle))
            {
                builderWizardStep = BuilderWizardStep.CustomFurniture;
                MoveCameraToPlanView();
            }
            GUILayout.EndHorizontal();

            if (GUILayout.Button("Refresh Top-Down View", buttonStyle))
            {
                MoveCameraToPlanView();
            }
        }

        private void DrawGuidedEntrancePreviewStep()
        {
            GUILayout.Label("Step 5: ask the participant to imagine the room one more time. The camera moves to the entrance so the next memory-palace phase starts from the door.", labelStyle);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Move To Entrance", buttonStyle))
            {
                MoveCameraToEntrancePreview();
            }

            if (GUILayout.Button(condition == ExperimentCondition.LlmGenerated ? "Start LLM Mnemonic Flow" : "Start Self Authoring", buttonStyle))
            {
                ClearStudyRoom();
                selectedBuilderAnchorIndex = -1;
                if (condition == ExperimentCondition.LlmGenerated)
                {
                    BeginLlmFlow();
                }
                else
                {
                    BeginSelfAuthoring();
                }
            }
            GUILayout.EndHorizontal();
        }

        private void ApplyGuidedLayoutShell()
        {
            var room = BuildGuidedRoomShell();
            RoomSpecCatalog.SetCurrentRoom(room);
            selectedBuilderAnchorIndex = -1;
            selectedRoomPrimitiveIndex = -1;
            roomGenerationError = string.Empty;
            ClearStudyRoom();
            BuildRoomBuilderPreview();
            MoveCameraToPlanView();
            statusMessage = $"Generated a clean dollhouse-style {GuidedRoomShapeOptions[Mathf.Clamp(guidedRoomShapeIndex, 0, GuidedRoomShapeOptions.Length - 1)]} room shell. Confirm it or add furniture.";
        }

        private RoomSpecDefinition BuildGuidedRoomShell()
        {
            var shape = GuidedRoomShapeOptions[Mathf.Clamp(guidedRoomShapeIndex, 0, GuidedRoomShapeOptions.Length - 1)];
            var isLShape = string.Equals(shape, "L-Shape", StringComparison.OrdinalIgnoreCase);
            var width = Mathf.Clamp((shape == "Square" ? 8.4f : isLShape ? 10.8f : 11.2f) + guidedRoomWidthAdjustment, 6.2f, 13.5f);
            var depth = Mathf.Clamp((shape == "Square" ? 8.4f : isLShape ? 9.4f : 7.2f) + guidedRoomDepthAdjustment, 5.8f, 12.5f);
            var halfWidth = width * 0.5f;
            var halfDepth = depth * 0.5f;
            var minX = -halfWidth;
            var maxX = halfWidth;
            var minZ = -halfDepth;
            var maxZ = halfDepth;
            var guidedLayoutDescription = BuildGuidedLayoutDescription(shape, width, depth);
            roomDescription = guidedLayoutDescription;

            var room = new RoomSpecDefinition
            {
                roomId = "guided_room_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"),
                roomName = "Guided " + shape + " Memory Room",
                generatedBy = "Guided local floor-plan builder",
                sourcePrompt = guidedLayoutDescription,
                summary = BuildGuidedRoomSummary(shape),
                overviewCamera = new CameraPoseDefinition
                {
                    position = new Vector3(width * 0.48f, 7.4f, -depth * 0.72f),
                    eulerAngles = new Vector3(58f, -34f, 0f)
                },
                studyCamera = new CameraPoseDefinition
                {
                    position = new Vector3(-halfWidth + 1.2f, 1.55f, -halfDepth + 1.1f),
                    eulerAngles = new Vector3(0f, 38f, 0f)
                }
            };

            var wallHeight = 1.35f;
            var wallY = wallHeight * 0.5f;
            var doorCenterX = 0f;
            var doorGap = 1.5f;

            if (isLShape)
            {
                GetGuidedLShapeParameters(width, depth, out var sideArmWidth, out var backArmDepth, out var cutX, out var cutZ);
                doorCenterX = minX + sideArmWidth * 0.5f;

                room.environmentPrimitives.Add(CreateGuidedRoomPrimitive("floor_side_arm", "L Floor Side Arm", "Cube", "#E7D9C1", new Vector3((minX + cutX) * 0.5f, 0f, 0f), new Vector3(sideArmWidth, 0.08f, depth), Vector3.zero));
                room.environmentPrimitives.Add(CreateGuidedRoomPrimitive("floor_back_arm", "L Floor Back Arm", "Cube", "#E1C8AA", new Vector3((cutX + maxX) * 0.5f, 0f, (cutZ + maxZ) * 0.5f), new Vector3(maxX - cutX, 0.08f, backArmDepth), Vector3.zero));

                AddHorizontalGuidedPrimitive(room, "floor_border_front", "Floor Border", "#B98A58", minX, cutX, minZ, 0.055f, 0.08f, 0.08f);
                AddHorizontalGuidedPrimitive(room, "floor_border_back", "Floor Border", "#B98A58", minX, maxX, maxZ, 0.055f, 0.08f, 0.08f);
                AddVerticalGuidedPrimitive(room, "floor_border_left", "Floor Border", "#B98A58", minX, minZ, maxZ, 0.055f, 0.08f, 0.08f);
                AddVerticalGuidedPrimitive(room, "floor_border_right_upper", "Floor Border", "#B98A58", maxX, cutZ, maxZ, 0.055f, 0.08f, 0.08f);
                AddHorizontalGuidedPrimitive(room, "floor_border_inner_horizontal", "L Inner Floor Border", "#B98A58", cutX, maxX, cutZ, 0.055f, 0.08f, 0.08f);
                AddVerticalGuidedPrimitive(room, "floor_border_inner_vertical", "L Inner Floor Border", "#B98A58", cutX, minZ, cutZ, 0.055f, 0.08f, 0.08f);

                AddHorizontalGuidedPrimitive(room, "wall_back", "Back Wall", "#F3F0E8", minX, maxX, maxZ + 0.06f, wallY, wallHeight, 0.18f);
                AddVerticalGuidedPrimitive(room, "wall_left", "Left Wall", "#F7F4EC", minX - 0.06f, minZ, maxZ, wallY, wallHeight, 0.18f);
                AddVerticalGuidedPrimitive(room, "wall_right_upper", "Right Wall", "#F7F4EC", maxX + 0.06f, cutZ, maxZ, wallY, wallHeight, 0.18f);
                AddHorizontalGuidedPrimitive(room, "wall_inner_horizontal", "L Shape Inner Wall", "#F7F4EC", cutX, maxX, cutZ - 0.06f, wallY, wallHeight, 0.18f);
                AddVerticalGuidedPrimitive(room, "wall_inner_vertical", "L Shape Inner Wall", "#F7F4EC", cutX + 0.06f, minZ, cutZ, wallY, wallHeight, 0.18f);

                AddFrontWallWithDoor(room, minX, cutX, minZ - 0.06f, doorCenterX, doorGap, wallY, wallHeight);
            }
            else
            {
                room.environmentPrimitives.Add(CreateGuidedRoomPrimitive("floor", "Warm Wood Floor", "Cube", "#E7D9C1", Vector3.zero, new Vector3(width, 0.08f, depth), Vector3.zero));
                room.environmentPrimitives.Add(CreateGuidedRoomPrimitive("floor_border_front", "Floor Border", "Cube", "#B98A58", new Vector3(0f, 0.055f, -halfDepth), new Vector3(width, 0.08f, 0.08f), Vector3.zero));
                room.environmentPrimitives.Add(CreateGuidedRoomPrimitive("floor_border_back", "Floor Border", "Cube", "#B98A58", new Vector3(0f, 0.055f, halfDepth), new Vector3(width, 0.08f, 0.08f), Vector3.zero));
                room.environmentPrimitives.Add(CreateGuidedRoomPrimitive("floor_border_left", "Floor Border", "Cube", "#B98A58", new Vector3(-halfWidth, 0.055f, 0f), new Vector3(0.08f, 0.08f, depth), Vector3.zero));
                room.environmentPrimitives.Add(CreateGuidedRoomPrimitive("floor_border_right", "Floor Border", "Cube", "#B98A58", new Vector3(halfWidth, 0.055f, 0f), new Vector3(0.08f, 0.08f, depth), Vector3.zero));

                room.environmentPrimitives.Add(CreateGuidedRoomPrimitive("wall_back", "Back Wall", "Cube", "#F3F0E8", new Vector3(0f, wallY, halfDepth + 0.06f), new Vector3(width, wallHeight, 0.18f), Vector3.zero));
                room.environmentPrimitives.Add(CreateGuidedRoomPrimitive("wall_left", "Left Wall", "Cube", "#F7F4EC", new Vector3(-halfWidth - 0.06f, wallY, 0f), new Vector3(0.18f, wallHeight, depth), Vector3.zero));
                room.environmentPrimitives.Add(CreateGuidedRoomPrimitive("wall_right", "Right Wall", "Cube", "#F7F4EC", new Vector3(halfWidth + 0.06f, wallY, 0f), new Vector3(0.18f, wallHeight, depth), Vector3.zero));
                AddFrontWallWithDoor(room, minX, maxX, minZ - 0.06f, doorCenterX, doorGap, wallY, wallHeight);
            }

            if (guidedHasBathroom)
            {
                var bathWidth = Mathf.Min(2.55f, isLShape ? width * 0.36f : width * 0.32f);
                var bathDepth = Mathf.Min(2.35f, depth * 0.34f);
                var bathCenter = GetGuidedBathroomCenter(width, depth, bathWidth, bathDepth);
                room.environmentPrimitives.Add(CreateGuidedRoomPrimitive("bath_tile_floor", "Bathroom Tile", "Cube", "#C7D7DF", bathCenter, new Vector3(bathWidth, 0.06f, bathDepth), Vector3.zero));
                room.environmentPrimitives.Add(CreateGuidedRoomPrimitive("bath_divider_back", "Bathroom Divider", "Cube", "#E8ECEF", new Vector3(bathCenter.x, wallY, bathCenter.z + bathDepth * 0.5f), new Vector3(bathWidth, wallHeight, 0.14f), Vector3.zero));
                room.environmentPrimitives.Add(CreateGuidedRoomPrimitive("bath_divider_left", "Bathroom Divider", "Cube", "#E8ECEF", new Vector3(bathCenter.x - bathWidth * 0.5f, wallY, bathCenter.z), new Vector3(0.14f, wallHeight, bathDepth), Vector3.zero));
            }

            AddDollhouseFloorZones(room, width, depth, isLShape);
            AddDollhouseInteriorPartitions(room, width, depth, isLShape, wallY, wallHeight);
            AddDollhouseWallCaps(room);
            AddDollhouseWindowsAndProps(room, width, depth, isLShape);

            var doorTemplate = BuildCustomFurnitureTemplate("Door");
            room.anchors.Add(CreateGuidedAnchor(room, doorTemplate, new Vector3(doorCenterX, doorTemplate.DefaultY, -halfDepth + 0.08f), Vector3.zero));
            return room;
        }

        private void AddDollhouseFloorZones(RoomSpecDefinition room, float width, float depth, bool isLShape)
        {
            var halfWidth = width * 0.5f;
            var halfDepth = depth * 0.5f;
            if (isLShape)
            {
                GetGuidedLShapeParameters(width, depth, out var sideArmWidth, out var backArmDepth, out var cutX, out var cutZ);
                AddDecorativePrimitive(room, "zone_entry", "Entry Zone", "#C7B49D", new Vector3(-halfWidth + sideArmWidth * 0.5f, 0.072f, -halfDepth + 1.2f), new Vector3(sideArmWidth - 0.55f, 0.018f, 2.1f), Vector3.zero);
                AddDecorativePrimitive(room, "zone_living", "Living Zone", "#B98A58", new Vector3((cutX + halfWidth) * 0.5f, 0.074f, cutZ + backArmDepth * 0.34f), new Vector3(Mathf.Max(2.4f, halfWidth - cutX - 0.6f), 0.018f, Mathf.Max(2.2f, backArmDepth * 0.48f)), Vector3.zero);
                AddDecorativePrimitive(room, "zone_sleep", "Sleeping Zone", "#D7C4A3", new Vector3(-halfWidth + sideArmWidth * 0.5f, 0.076f, halfDepth - 1.65f), new Vector3(sideArmWidth - 0.65f, 0.018f, 2.25f), Vector3.zero);
                AddDecorativePrimitive(room, "zone_study", "Study Zone", "#B7C7D6", new Vector3(cutX + 1.35f, 0.078f, halfDepth - 1.1f), new Vector3(2.05f, 0.018f, 1.25f), Vector3.zero);
                return;
            }

            AddDecorativePrimitive(room, "zone_living", "Living Zone", "#B98A58", new Vector3(-halfWidth * 0.30f, 0.074f, -halfDepth * 0.12f), new Vector3(width * 0.42f, 0.018f, depth * 0.46f), Vector3.zero);
            AddDecorativePrimitive(room, "zone_sleep", "Sleeping Zone", "#D7C4A3", new Vector3(halfWidth * 0.38f, 0.076f, halfDepth * 0.28f), new Vector3(width * 0.34f, 0.018f, depth * 0.38f), Vector3.zero);
            AddDecorativePrimitive(room, "zone_study", "Study Zone", "#B7C7D6", new Vector3(-halfWidth * 0.45f, 0.078f, halfDepth * 0.28f), new Vector3(width * 0.26f, 0.018f, depth * 0.32f), Vector3.zero);
            AddDecorativePrimitive(room, "zone_kitchen", "Kitchen Zone", "#C7B49D", new Vector3(halfWidth * 0.22f, 0.08f, -halfDepth + 1.0f), new Vector3(width * 0.42f, 0.018f, 1.25f), Vector3.zero);
        }

        private void AddDollhouseInteriorPartitions(RoomSpecDefinition room, float width, float depth, bool isLShape, float wallY, float wallHeight)
        {
            var halfWidth = width * 0.5f;
            var halfDepth = depth * 0.5f;
            var dividerHeight = wallHeight * 0.82f;
            var dividerY = dividerHeight * 0.5f;

            if (isLShape)
            {
                GetGuidedLShapeParameters(width, depth, out var sideArmWidth, out var backArmDepth, out var cutX, out var cutZ);
                AddVerticalGuidedPrimitive(room, "partition_sleep_entry", "Low Interior Partition", "#EFE7DA", -halfWidth + sideArmWidth - 0.12f, cutZ + 0.35f, halfDepth - 0.7f, dividerY, dividerHeight, 0.14f);
                AddHorizontalGuidedPrimitive(room, "partition_living_study", "Low Interior Partition", "#EFE7DA", cutX + 0.55f, halfWidth - 0.85f, cutZ + backArmDepth * 0.48f, dividerY, dividerHeight, 0.14f);
                return;
            }

            AddVerticalGuidedPrimitive(room, "partition_sleeping", "Low Interior Partition", "#EFE7DA", halfWidth * 0.18f, -halfDepth + 1.6f, halfDepth - 0.55f, dividerY, dividerHeight, 0.14f);
            AddHorizontalGuidedPrimitive(room, "partition_entry", "Low Interior Partition", "#EFE7DA", -halfWidth + 0.85f, halfWidth * 0.18f, -halfDepth + 1.75f, dividerY, dividerHeight, 0.14f);
        }

        private void AddDollhouseWallCaps(RoomSpecDefinition room)
        {
            var count = room.environmentPrimitives.Count;
            for (int i = 0; i < count; i++)
            {
                var primitive = room.environmentPrimitives[i];
                if (primitive == null || !IsWallLikeRoomPrimitive(primitive))
                {
                    continue;
                }

                var capScale = new Vector3(
                    Mathf.Max(primitive.scale.x + 0.08f, primitive.scale.x),
                    0.055f,
                    Mathf.Max(primitive.scale.z + 0.08f, primitive.scale.z));
                var capPosition = primitive.position + Vector3.up * (primitive.scale.y * 0.5f + 0.04f);
                AddDecorativePrimitive(room, "cap_" + primitive.id, "Wall Top Trim", "#A87552", capPosition, capScale, primitive.rotationEuler);
            }
        }

        private void AddDollhouseWindowsAndProps(RoomSpecDefinition room, float width, float depth, bool isLShape)
        {
            var halfWidth = width * 0.5f;
            var halfDepth = depth * 0.5f;
            AddDecorativePrimitive(room, "window_back_left", "Window Glass", "#93BBD0", new Vector3(-halfWidth * 0.42f, 1.0f, halfDepth + 0.01f), new Vector3(1.25f, 0.55f, 0.035f), Vector3.zero);
            AddDecorativePrimitive(room, "window_back_right", "Window Glass", "#93BBD0", new Vector3(halfWidth * 0.42f, 1.0f, halfDepth + 0.01f), new Vector3(1.25f, 0.55f, 0.035f), Vector3.zero);
            AddDecorativePrimitive(room, "wall_art", "Wall Art", "#D7A36A", new Vector3(-halfWidth - 0.02f, 0.92f, 0.35f), new Vector3(0.035f, 0.58f, 0.85f), Vector3.zero);
            AddDecorativePrimitive(room, "runner_rug", "Runner Rug", "#8A5B4D", new Vector3(0f, 0.086f, -halfDepth + 1.85f), new Vector3(width * 0.46f, 0.018f, 0.42f), Vector3.zero);

            if (isLShape)
            {
                GetGuidedLShapeParameters(width, depth, out _, out _, out var cutX, out var cutZ);
                AddDecorativePrimitive(room, "inner_corner_trim", "Inner Corner Trim", "#A87552", new Vector3(cutX + 0.02f, 0.76f, cutZ + 0.02f), new Vector3(0.08f, 1.2f, 0.42f), Vector3.zero);
                AddDecorativePrimitive(room, "window_side_arm", "Window Glass", "#93BBD0", new Vector3(-halfWidth - 0.01f, 1.0f, halfDepth * 0.25f), new Vector3(0.035f, 0.55f, 1.1f), Vector3.zero);
            }
        }

        private void AddDecorativePrimitive(RoomSpecDefinition room, string id, string label, string colorHex, Vector3 position, Vector3 scale, Vector3 rotationEuler)
        {
            room.environmentPrimitives.Add(CreateGuidedRoomPrimitive(id, label, "Cube", colorHex, position, scale, rotationEuler));
        }

        private bool IsWallLikeRoomPrimitive(RoomPrimitiveDefinition primitive)
        {
            var text = ((primitive.id ?? string.Empty) + " " + (primitive.label ?? string.Empty)).ToLowerInvariant();
            if (ContainsAny(text, "art", "glass", "window", "rug", "border", "floor", "zone", "trim", "cap"))
            {
                return false;
            }

            return ContainsAny(text, "wall", "divider", "partition")
                && primitive.scale.y > 0.45f
                && !ContainsAny(text, "cap", "trim");
        }

        private string BuildGuidedRoomSummary(string shape)
        {
            var bathroom = guidedHasBathroom
                ? guidedHasToilet ? "with a bathroom/toilet area" : "with a bathroom area but no toilet"
                : "without bathroom or toilet";
            return $"Guided {shape.ToLowerInvariant()} floor-plan shell {bathroom}; intended to be checked from a top-down apartment view before the memory-palace phase.";
        }

        private string BuildGuidedLayoutDescription(string shape, float width, float depth)
        {
            var bathroom = guidedHasBathroom
                ? guidedHasToilet ? "includes a compact bathroom with a toilet" : "includes a compact bathroom without a toilet"
                : "has no bathroom or toilet zone";
            var lShapeNote = string.Equals(shape, "L-Shape", StringComparison.OrdinalIgnoreCase)
                ? " The front-right corner is intentionally absent, producing a true editable L-shaped footprint."
                : string.Empty;
            return $"{shape} remembered-room shell, {width:F1}m wide by {depth:F1}m deep, {bathroom}.{lShapeNote}";
        }

        private void AddFrontWallWithDoor(
            RoomSpecDefinition room,
            float xMin,
            float xMax,
            float z,
            float doorCenterX,
            float doorGap,
            float wallY,
            float wallHeight)
        {
            var leftEnd = Mathf.Clamp(doorCenterX - doorGap * 0.5f, xMin, xMax);
            var rightStart = Mathf.Clamp(doorCenterX + doorGap * 0.5f, xMin, xMax);
            AddHorizontalGuidedPrimitive(room, "wall_front_left", "Front Wall", "#F7F4EC", xMin, leftEnd, z, wallY, wallHeight, 0.12f);
            AddHorizontalGuidedPrimitive(room, "wall_front_right", "Front Wall", "#F7F4EC", rightStart, xMax, z, wallY, wallHeight, 0.12f);
        }

        private void AddHorizontalGuidedPrimitive(
            RoomSpecDefinition room,
            string id,
            string label,
            string colorHex,
            float xMin,
            float xMax,
            float z,
            float y,
            float height,
            float thickness)
        {
            var start = Mathf.Min(xMin, xMax);
            var end = Mathf.Max(xMin, xMax);
            var length = end - start;
            if (length <= 0.08f)
            {
                return;
            }

            room.environmentPrimitives.Add(CreateGuidedRoomPrimitive(
                id,
                label,
                "Cube",
                colorHex,
                new Vector3((start + end) * 0.5f, y, z),
                new Vector3(length, height, thickness),
                Vector3.zero));
        }

        private void AddVerticalGuidedPrimitive(
            RoomSpecDefinition room,
            string id,
            string label,
            string colorHex,
            float x,
            float zMin,
            float zMax,
            float y,
            float height,
            float thickness)
        {
            var start = Mathf.Min(zMin, zMax);
            var end = Mathf.Max(zMin, zMax);
            var length = end - start;
            if (length <= 0.08f)
            {
                return;
            }

            room.environmentPrimitives.Add(CreateGuidedRoomPrimitive(
                id,
                label,
                "Cube",
                colorHex,
                new Vector3(x, y, (start + end) * 0.5f),
                new Vector3(thickness, height, length),
                Vector3.zero));
        }

        private static void GetGuidedLShapeParameters(
            float roomWidth,
            float roomDepth,
            out float sideArmWidth,
            out float backArmDepth,
            out float cutX,
            out float cutZ)
        {
            sideArmWidth = Mathf.Clamp(roomWidth * 0.46f, 3.0f, roomWidth - 2.7f);
            backArmDepth = Mathf.Clamp(roomDepth * 0.58f, 3.0f, roomDepth - 2.1f);
            cutX = -roomWidth * 0.5f + sideArmWidth;
            cutZ = roomDepth * 0.5f - backArmDepth;
        }

        private bool IsGuidedLShapeActive()
        {
            var selectedShape = GuidedRoomShapeOptions[Mathf.Clamp(guidedRoomShapeIndex, 0, GuidedRoomShapeOptions.Length - 1)];
            var roomName = RoomSpecCatalog.CurrentRoom?.roomName ?? string.Empty;
            return string.Equals(selectedShape, "L-Shape", StringComparison.OrdinalIgnoreCase)
                || roomName.IndexOf("L-Shape", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private Vector3 GetGuidedDoorPosition(float roomWidth, float roomDepth, FurnitureTemplate doorTemplate)
        {
            var doorX = 0f;
            if (IsGuidedLShapeActive())
            {
                GetGuidedLShapeParameters(roomWidth, roomDepth, out var sideArmWidth, out _, out _, out _);
                doorX = -roomWidth * 0.5f + sideArmWidth * 0.5f;
            }

            return new Vector3(doorX, doorTemplate.DefaultY, -roomDepth * 0.5f + 0.08f);
        }

        private Vector3 GetGuidedBathroomCenter(float roomWidth, float roomDepth, float bathWidth, float bathDepth)
        {
            var halfWidth = roomWidth * 0.5f;
            var halfDepth = roomDepth * 0.5f;
            var margin = 0.22f;
            var zone = Mathf.Clamp(guidedBathroomZoneIndex, 0, GuidedBathroomZones.Length - 1);
            var left = zone == 0 || zone == 2;
            var front = zone == 0 || zone == 1;
            var x = left
                ? -halfWidth + bathWidth * 0.5f + margin
                : halfWidth - bathWidth * 0.5f - margin;
            var z = front
                ? -halfDepth + bathDepth * 0.5f + margin
                : halfDepth - bathDepth * 0.5f - margin;

            var center = new Vector3(x, 0.08f, z);
            if (IsGuidedLShapeActive())
            {
                center = ClampPositionToGuidedFootprint(center, new Vector2(bathWidth * 0.5f, bathDepth * 0.5f), roomWidth, roomDepth);
            }

            return center;
        }

        private void BeginGuidedMainFurnitureGeneration()
        {
            var requests = BuildGuidedFurnitureRequests();
            if (requests.Count == 0)
            {
                statusMessage = "Select at least one main furniture item before asking Ollama to place it.";
                return;
            }

            isGeneratingGuidedFurnitureLayout = true;
            statusMessage = "Calling Ollama to place the selected furniture from the rough zones...";
            StartCoroutine(RunGuidedMainFurnitureGeneration(requests));
        }

        private IEnumerator RunGuidedMainFurnitureGeneration(List<string> requests)
        {
            var service = new OllamaLlmService();
            List<OllamaLlmService.GuidedFurniturePlacementSuggestion> suggestions = null;
            string error = null;
            GetCurrentRoomFootprint(out var roomWidth, out var roomDepth);

            yield return StartCoroutine(service.GenerateGuidedFurnitureLayout(
                ollamaBaseUrl,
                ollamaModel,
                GuidedRoomShapeOptions[Mathf.Clamp(guidedRoomShapeIndex, 0, GuidedRoomShapeOptions.Length - 1)],
                guidedHasBathroom,
                guidedHasToilet,
                roomDescription,
                roomWidth,
                roomDepth,
                requests,
                result => suggestions = result,
                err => error = err));

            isGeneratingGuidedFurnitureLayout = false;

            if (suggestions == null || suggestions.Count == 0)
            {
                GenerateGuidedMainFurniture();
                statusMessage = "Ollama furniture placement failed, so a local fallback layout was generated: " + error;
                yield break;
            }

            ApplyGuidedMainFurniture(suggestions, true);
        }

        private List<string> BuildGuidedFurnitureRequests()
        {
            var requests = new List<string>();
            for (int i = 0; i < GuidedFurnitureLabels.Length; i++)
            {
                if (!guidedFurnitureIncluded[i])
                {
                    continue;
                }

                var label = GuidedFurnitureLabels[i];
                if (string.Equals(label, "Toilet", StringComparison.OrdinalIgnoreCase) && (!guidedHasBathroom || !guidedHasToilet))
                {
                    continue;
                }

                var zone = GuidedFurnitureZones[Mathf.Clamp(guidedFurnitureZoneIndexes[i], 0, GuidedFurnitureZones.Length - 1)];
                requests.Add($"{label}: rough zone = {zone}");
            }

            return requests;
        }

        private void GenerateGuidedMainFurniture()
        {
            ApplyGuidedMainFurniture(null, false);
        }

        private void ApplyGuidedMainFurniture(List<OllamaLlmService.GuidedFurniturePlacementSuggestion> suggestions, bool usedOllama)
        {
            var room = RoomSpecCatalog.CurrentRoom;
            room.anchors.Clear();
            GetCurrentRoomFootprint(out var roomWidth, out var roomDepth);
            var doorTemplate = BuildCustomFurnitureTemplate("Door");
            room.anchors.Add(CreateGuidedAnchor(room, doorTemplate, GetGuidedDoorPosition(roomWidth, roomDepth, doorTemplate), Vector3.zero));

            var suggestionLookup = BuildGuidedSuggestionLookup(suggestions);
            var zoneCounts = new int[GuidedFurnitureZones.Length];
            for (int i = 0; i < GuidedFurnitureLabels.Length; i++)
            {
                if (!guidedFurnitureIncluded[i])
                {
                    continue;
                }

                var label = GuidedFurnitureLabels[i];
                if (string.Equals(label, "Toilet", StringComparison.OrdinalIgnoreCase) && (!guidedHasBathroom || !guidedHasToilet))
                {
                    continue;
                }

                var zoneIndex = Mathf.Clamp(guidedFurnitureZoneIndexes[i], 0, GuidedFurnitureZones.Length - 1);
                var ordinal = zoneCounts[zoneIndex]++;
                var template = BuildCustomFurnitureTemplate(label);
                Vector3 position;
                Vector3 rotationEuler;
                OllamaLlmService.GuidedFurniturePlacementSuggestion appliedSuggestion = null;
                if (suggestionLookup.TryGetValue(NormalizeGuidedLabel(label), out var suggestion))
                {
                    appliedSuggestion = suggestion;
                    position = suggestion.position;
                    rotationEuler = suggestion.rotationEuler;
                }
                else
                {
                    position = GetGuidedFurniturePlacement(label, zoneIndex, ordinal, roomWidth, roomDepth, template, out rotationEuler);
                }

                var anchor = CreateGuidedAnchor(room, template, position, rotationEuler);
                if (appliedSuggestion != null && appliedSuggestion.scale != default)
                {
                    anchor.scale = appliedSuggestion.scale;
                }

                room.anchors.Add(anchor);
            }

            for (int i = 0; i < room.anchors.Count; i++)
            {
                ConstrainAnchorPlacement(room.anchors[i], i, roomWidth, roomDepth);
            }

            ResolveCurrentAnchorOverlaps(roomWidth, roomDepth);
            for (int i = 0; i < room.anchors.Count; i++)
            {
                ConstrainAnchorPlacement(room.anchors[i], i, roomWidth, roomDepth);
            }

            selectedBuilderAnchorIndex = room.anchors.Count > 0 ? 0 : -1;
            var reassigned = ReassignCurrentItemsToAnchors();
            BuildRoomBuilderPreview();
            MoveCameraToPlanView();
            statusMessage = reassigned > 0
                ? $"Generated main furniture and redistributed {reassigned} word(s) to the updated anchors."
                : usedOllama
                    ? "Ollama placed the main furniture from the rough zones. Add custom objects or adjust positions next."
                    : "Generated a local fallback furniture layout from the guided answers. Add custom objects or adjust positions next.";
        }

        private Dictionary<string, OllamaLlmService.GuidedFurniturePlacementSuggestion> BuildGuidedSuggestionLookup(List<OllamaLlmService.GuidedFurniturePlacementSuggestion> suggestions)
        {
            var result = new Dictionary<string, OllamaLlmService.GuidedFurniturePlacementSuggestion>();
            if (suggestions == null)
            {
                return result;
            }

            for (int i = 0; i < suggestions.Count; i++)
            {
                var suggestion = suggestions[i];
                if (suggestion == null || string.IsNullOrWhiteSpace(suggestion.label))
                {
                    continue;
                }

                result[NormalizeGuidedLabel(suggestion.label)] = suggestion;
            }

            return result;
        }

        private string NormalizeGuidedLabel(string label)
        {
            return string.IsNullOrWhiteSpace(label)
                ? string.Empty
                : label.Trim().ToLowerInvariant().Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty);
        }

        private Vector3 GetGuidedFurniturePlacement(string label, int zoneIndex, int ordinal, float roomWidth, float roomDepth, FurnitureTemplate template, out Vector3 rotationEuler)
        {
            var halfWidth = roomWidth * 0.5f;
            var halfDepth = roomDepth * 0.5f;
            var lane = ordinal - 1;
            rotationEuler = Vector3.zero;
            var renderScale = template.Scale == default ? Vector3.one : template.Scale;
            var wallInsetX = Mathf.Max(0.75f, renderScale.x * 0.55f + 0.18f);
            var wallInsetZ = Mathf.Max(0.75f, renderScale.z * 0.55f + 0.18f);
            var y = template.DefaultY;

            if (string.Equals(label, "Window", StringComparison.OrdinalIgnoreCase)
                || string.Equals(label, "Air Conditioner", StringComparison.OrdinalIgnoreCase))
            {
                y = string.Equals(label, "Air Conditioner", StringComparison.OrdinalIgnoreCase) ? 2.25f : 1.75f;
            }

            switch (zoneIndex)
            {
                case 0:
                    rotationEuler = new Vector3(0f, 90f, 0f);
                    return new Vector3(-halfWidth + wallInsetX, y, Mathf.Clamp(-halfDepth + 1.5f + ordinal * 1.35f, -halfDepth + wallInsetZ, halfDepth - wallInsetZ));
                case 1:
                    rotationEuler = new Vector3(0f, 90f, 0f);
                    return new Vector3(halfWidth - wallInsetX, y, Mathf.Clamp(-halfDepth + 1.5f + ordinal * 1.35f, -halfDepth + wallInsetZ, halfDepth - wallInsetZ));
                case 2:
                    rotationEuler = Vector3.zero;
                    return new Vector3(Mathf.Clamp(-halfWidth + 1.6f + ordinal * 1.5f, -halfWidth + wallInsetX, halfWidth - wallInsetX), y, halfDepth - wallInsetZ);
                case 3:
                    rotationEuler = Vector3.zero;
                    return new Vector3(Mathf.Clamp(-halfWidth + 1.6f + ordinal * 1.5f, -halfWidth + wallInsetX, halfWidth - wallInsetX), y, -halfDepth + wallInsetZ);
                case 5:
                    rotationEuler = new Vector3(0f, -90f, 0f);
                    return new Vector3(halfWidth - 1.1f, y, -halfDepth + 1.2f + ordinal * 0.75f);
                default:
                    return new Vector3(Mathf.Clamp(lane * 1.15f, -halfWidth + wallInsetX, halfWidth - wallInsetX), y, Mathf.Clamp(ordinal % 2 == 0 ? 0.15f : -0.85f, -halfDepth + wallInsetZ, halfDepth - wallInsetZ));
            }
        }

        private RoomPrimitiveDefinition CreateGuidedRoomPrimitive(
            string id,
            string label,
            string shape,
            string colorHex,
            Vector3 position,
            Vector3 scale,
            Vector3 rotationEuler)
        {
            return new RoomPrimitiveDefinition
            {
                id = id,
                label = label,
                primitiveShape = shape,
                colorHex = colorHex,
                position = position,
                scale = scale,
                rotationEuler = rotationEuler,
                showLabel = false,
                labelHeight = 0.6f
            };
        }

        private AnchorDefinition CreateGuidedAnchor(RoomSpecDefinition room, FurnitureTemplate template, Vector3 position, Vector3 rotationEuler)
        {
            return new AnchorDefinition
            {
                id = BuildUniqueAnchorId(room, template.IdPrefix),
                label = template.Label,
                primitiveShape = template.Shape,
                colorHex = template.ColorHex,
                position = position,
                scale = template.Scale,
                rotationEuler = rotationEuler,
                mnemonicOffset = new Vector3(0f, template.MnemonicYOffset, 0f),
                labelHeight = template.LabelHeight,
                modelParts = CloneVisualObjectSpecs(template.ModelParts)
            };
        }

        private string BuildUniqueAnchorId(RoomSpecDefinition room, string prefix)
        {
            var baseId = string.IsNullOrWhiteSpace(prefix) ? "anchor" : SanitizeIdPrefix(prefix);
            var candidate = baseId;
            var index = 1;
            while (RoomHasAnchorId(room, candidate))
            {
                index++;
                candidate = $"{baseId}_{index}";
            }

            return candidate;
        }

        private static bool RoomHasAnchorId(RoomSpecDefinition room, string id)
        {
            if (room?.anchors == null)
            {
                return false;
            }

            for (int i = 0; i < room.anchors.Count; i++)
            {
                if (string.Equals(room.anchors[i].id, id, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private void BeginRoomGeneration()
        {
            roomGenerationError = string.Empty;
            statusMessage = $"Calling Ollama to generate a {RoomLayoutOptions[selectedRoomLayoutIndex]} room...";
            isGeneratingRoom = true;
            StopAllCoroutines();
            StartCoroutine(RunRoomGeneration());
        }

        private IEnumerator RunRoomGeneration()
        {
            yield return new WaitForSecondsRealtime(0.2f);

            var service = new OllamaLlmService();
            RoomSpecDefinition generatedRoom = null;
            string error = null;

            yield return StartCoroutine(service.GenerateRoomSpec(
                ollamaBaseUrl,
                ollamaModel,
                RoomLayoutOptions[Mathf.Clamp(selectedRoomLayoutIndex, 0, RoomLayoutOptions.Length - 1)],
                roomDescription,
                room => generatedRoom = room,
                err => error = err));

            isGeneratingRoom = false;

            if (!string.IsNullOrWhiteSpace(error))
            {
                roomGenerationError = error;
                statusMessage = "Room generation failed. You can still edit the current room in Room Builder.";
                yield break;
            }

            RoomSpecCatalog.SetCurrentRoom(generatedRoom);
            selectedBuilderAnchorIndex = RoomSpecCatalog.AnchorCount > 0 ? 0 : -1;
            roomGenerationError = string.Empty;
            statusMessage = $"Generated room ready: {RoomSpecCatalog.RoomName}. Open Room Builder to inspect it, or continue to mnemonic generation.";

            ClearStudyRoom();
            MoveCameraToOverview();

            if (stage == ExperimentStage.RoomBuilder)
            {
                BuildRoomBuilderPreview();
            }
        }

        private void OpenRoomBuilder()
        {
            ClearStudyRoom();
            selectedBuilderAnchorIndex = RoomSpecCatalog.AnchorCount > 0 ? 0 : -1;
            builderWizardStep = BuilderWizardStep.Layout;
            stage = ExperimentStage.RoomBuilder;
            BuildRoomBuilderPreview();
            MoveCameraToPlanView();
            statusMessage = "Room builder opened. Start with the guided layout step, or use Q/W/E/R to edit existing anchors.";
        }

        private void CancelRoomGeneration()
        {
            StopAllCoroutines();
            isGeneratingRoom = false;
            roomGenerationError = "Room generation was cancelled.";
            statusMessage = "Room generation cancelled. The current room is still available.";
        }

        private void CancelMnemonicGeneration()
        {
            StopAllCoroutines();
            isGenerating = false;
            generationError = "Mnemonic generation was cancelled.";
            statusMessage = "Mnemonic generation cancelled. You can return to setup or try again.";
        }

        private void DrawGenerationView()
        {
            var rect = new Rect(18f, ContentTop, Screen.width - 36f, ContentHeight);
            GUI.Box(rect, GUIContent.none, panelStyle);
            RegisterGuiRect(rect);

            GUILayout.BeginArea(rect);
            generationScroll = GUILayout.BeginScrollView(generationScroll);

            GUILayout.Label("Step 2 of 7 - Mnemonic Preview", titleStyle);
            GUILayout.Label("This page previews how each word has been mapped to a room anchor, a bilingual scene, and a bilingual memory link. The next step is to enter the room and inspect those cues in context.", mutedStyle);
            GUILayout.Space(8);

            GUILayout.BeginVertical(sectionStyle);
            GUILayout.Label("Current Session", smallTitleStyle);
            GUILayout.Label($"Condition: {GetConditionDisplayName()}", labelStyle);
            GUILayout.Label($"Mnemonic Source: {ResolveProviderLabel()}", labelStyle);
            GUILayout.Label($"Ollama Request Used: {(IsUsingLiveLlm() ? "Yes" : "No")}", labelStyle);
            GUILayout.Label($"Room: {RoomSpecCatalog.RoomName}", labelStyle);
            GUILayout.Label($"Mnemonic Items: {currentItems.Count}", labelStyle);
            GUILayout.Label(statusMessage, mutedStyle);
            GUILayout.EndVertical();

            if (isGenerating)
            {
                GUILayout.Space(8);
                GUILayout.Label("Generating mnemonic items...", labelStyle);
                if (GUILayout.Button("Cancel Mnemonic Generation", buttonStyle))
                {
                    CancelMnemonicGeneration();
                }
            }

            if (!string.IsNullOrWhiteSpace(generationError))
            {
                GUILayout.Space(8);
                GUILayout.Label(generationError, labelStyle);
            }

            GUILayout.Space(12);

            foreach (var item in currentItems)
            {
                GUILayout.BeginVertical(sectionStyle);
                GUILayout.Label($"{item.word}  -  {item.anchorLabel}", smallTitleStyle);
                GUILayout.Label(item.meaning + (string.IsNullOrWhiteSpace(item.meaningJa) ? string.Empty : $" ({item.meaningJa})"), mutedStyle);
                DrawBilingualSection("Overlay Cue Scene / Image Scene", item.visualCue, item.visualCueJa, smallTitleStyle, labelStyle);
                GUILayout.Space(4);
                DrawBilingualSection("Cue Story / Memory Link", item.mnemonic, item.mnemonicJa, smallTitleStyle, labelStyle);
                /*
                DrawBilingualSection("Scene To Imagine / 想像シーン", item.visualCue, item.visualCueJa, smallTitleStyle, labelStyle);
                GUILayout.Space(4);
                DrawBilingualSection("Word Connection / 単語とのつながり", item.mnemonic, item.mnemonicJa, smallTitleStyle, labelStyle);
                */
                GUILayout.EndVertical();
            }

            GUILayout.Space(12);
            GUI.enabled = !isGenerating && currentItems.Count > 0;
            if (GUILayout.Button("Next: Enter Study Room", buttonStyle))
            {
                EnterStudyRoom();
            }
            GUI.enabled = true;

            if (GUILayout.Button("Back to Setup", buttonStyle))
            {
                StopAllCoroutines();
                isGenerating = false;
                stage = ExperimentStage.Setup;
                statusMessage = "Returned to setup.";
                generationError = string.Empty;
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawSelfAuthoringView()
        {
            var rect = new Rect(18f, ContentTop, Screen.width - 36f, ContentHeight);
            GUI.Box(rect, GUIContent.none, panelStyle);
            RegisterGuiRect(rect);

            GUILayout.BeginArea(rect);
            authoringScroll = GUILayout.BeginScrollView(authoringScroll);

            GUILayout.Label("Step 2 of 7 - Self-Generated Mnemonic Authoring", titleStyle);
            GUILayout.Label("Assign each word to an anchor and write your own bilingual scene and memory link. For the demo, you can auto-fill a starter draft and then edit it before entering the room.", mutedStyle);
            GUILayout.Space(10);

            if (GUILayout.Button("Auto-Fill Starter Drafts", buttonStyle))
            {
                AutoFillSelfDrafts();
            }

            GUILayout.Space(10);

            for (int i = 0; i < currentItems.Count; i++)
            {
                var item = currentItems[i];
                GUILayout.BeginVertical(sectionStyle);
                GUILayout.Label($"{i + 1}. {item.word}", titleStyle);
                GUILayout.Label(item.meaning + (string.IsNullOrWhiteSpace(item.meaningJa) ? string.Empty : $" ({item.meaningJa})"), mutedStyle);

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("< Anchor", GUILayout.Width(100f), GUILayout.Height(28f)))
                {
                    CycleAnchor(item, -1);
                }

                GUILayout.Label($"Anchor: {item.anchorLabel}", labelStyle, GUILayout.Width(220f));

                if (GUILayout.Button("Anchor >", GUILayout.Width(100f), GUILayout.Height(28f)))
                {
                    CycleAnchor(item, 1);
                }
                GUILayout.EndHorizontal();

                GUILayout.Label("Overlay Cue Scene (EN)", mutedStyle);
                item.visualCue = GUILayout.TextArea(item.visualCue ?? string.Empty, textAreaStyle, GUILayout.MinHeight(54f));
                GUILayout.Label("Overlay Cue Scene (JA)", mutedStyle);
                item.visualCueJa = GUILayout.TextArea(item.visualCueJa ?? string.Empty, textAreaStyle, GUILayout.MinHeight(54f));
                GUILayout.Label("Cue Story (EN)", mutedStyle);
                item.mnemonic = GUILayout.TextArea(item.mnemonic ?? string.Empty, textAreaStyle, GUILayout.MinHeight(54f));
                GUILayout.Label("Cue Story (JA)", mutedStyle);
                item.mnemonicJa = GUILayout.TextArea(item.mnemonicJa ?? string.Empty, textAreaStyle, GUILayout.MinHeight(54f));

                GUILayout.EndVertical();
            }

            GUILayout.Space(12);
            if (GUILayout.Button("Next: Enter Study Room", buttonStyle))
            {
                FinalizeSelfDrafts();
                EnterStudyRoom();
            }

            if (GUILayout.Button("Back to Setup", buttonStyle))
            {
                StopAllCoroutines();
                isGenerating = false;
                stage = ExperimentStage.Setup;
                statusMessage = "Returned to setup.";
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawStudyView()
        {
            var topLeftHeight = Mathf.Min(282f, ContentHeight - 150f);
            var topLeft = new Rect(18f, ContentTop, 386f, topLeftHeight);
            GUI.Box(topLeft, GUIContent.none, panelStyle);
            RegisterGuiRect(topLeft);

            GUILayout.BeginArea(topLeft);
            studyInfoScroll = GUILayout.BeginScrollView(studyInfoScroll, false, true);
            GUILayout.Label("Step 3 of 7 - Study Room", titleStyle);
            GUILayout.Label("Explore the room, inspect a mnemonic marker, and press the snapshot button when you decide that memory has been encoded.", mutedStyle);
            GUILayout.Space(8);
            GUILayout.Label($"Participant: {participantId}", labelStyle);
            GUILayout.Label($"Condition: {GetConditionDisplayName()}", labelStyle);
            GUILayout.Label($"Mnemonic Source: {ResolveProviderLabel()}", labelStyle);
            GUILayout.Label($"Ollama Call: {(IsUsingLiveLlm() ? "Yes" : "No")}", labelStyle);
            GUILayout.Label(GetLlmStatusText(), mutedStyle);
            GUILayout.Label($"Word Set: {activeWordSet.displayName}", labelStyle);
            GUILayout.Label($"Room: {RoomSpecCatalog.RoomName}", labelStyle);
            GUILayout.Label($"Viewed Markers: {viewedWords.Count} / {currentItems.Count}", labelStyle);
            GUILayout.Label($"Snapshots Captured: {memorizedWords.Count} / {currentItems.Count}", labelStyle);
            GUILayout.Label($"Elapsed: {(Time.unscaledTime - studyStartTime):F1}s", labelStyle);
            GUILayout.Space(8);
            GUILayout.Label("Controls", smallTitleStyle);
            GUILayout.Label("Right mouse drag: look around\nWASD: move\nQ / E: move down / up\nLeft click: inspect mnemonic marker", guideStyle);
            GUILayout.EndScrollView();
            GUILayout.EndArea();

            var bottomLeft = new Rect(18f, topLeft.yMax + 12f, 386f, 108f);
            GUI.Box(bottomLeft, GUIContent.none, panelStyle);
            RegisterGuiRect(bottomLeft);
            GUILayout.BeginArea(bottomLeft);
            if (!midTestCompleted && memorizedWords.Count >= MidTestTriggerCount)
            {
                if (GUILayout.Button("Next: Start Mid Test", buttonStyle))
                {
                    BeginSnapshotTest(false);
                }
            }
            else if (midTestCompleted && !finalTestCompleted && memorizedWords.Count == currentItems.Count && currentItems.Count >= MidTestTriggerCount)
            {
                if (GUILayout.Button("Next: Start Final Test", buttonStyle))
                {
                    BeginSnapshotTest(true);
                }
            }
            else
            {
                GUILayout.Label(GetStudyProgressHint(), mutedStyle);
            }

            if (GUILayout.Button("Back to Setup", buttonStyle))
            {
                ClearStudyRoom();
                stage = ExperimentStage.Setup;
                statusMessage = "Returned to setup.";
                selectedStudyItem = null;
                MoveCameraToOverview();
            }
            GUILayout.EndArea();

            var rightPanelHeight = Mathf.Max(300f, ContentHeight - 10f);
            var rightPanelRect = new Rect(Screen.width - 448f, ContentTop, 430f, rightPanelHeight);
            GUI.Box(rightPanelRect, GUIContent.none, panelStyle);
            RegisterGuiRect(rightPanelRect);

            GUILayout.BeginArea(rightPanelRect);
            studyDetailScroll = GUILayout.BeginScrollView(studyDetailScroll, false, true);
            if (selectedStudyItem == null)
            {
                GUILayout.Label("How This Phase Works", smallTitleStyle);
                GUILayout.Label("1. Find the floating colored markers placed near key furniture anchors.\n2. Left-click one marker to inspect the word, meaning, cue, and mnemonic.\n3. When you feel the item is memorized, capture a snapshot.\n4. Mid and final tests unlock from those stored snapshots.", guideStyle);
                GUILayout.Space(10);
                GUILayout.Label("What You Are Seeing", smallTitleStyle);
                GUILayout.Label("Each marker has a stable room anchor plus an LLM-authored overlay cue: the anchor fixes the place, and the imagined cue carries the word meaning.", guideStyle);
                GUILayout.Space(10);
                GUILayout.Label("Why There Are Two Text Fields", smallTitleStyle);
                GUILayout.Label("Overlay Cue Scene is the anchor plus imagined cue. Cue Story is the short explanation of why that cue points back to the target word.", guideStyle);
                GUILayout.Space(10);
                GUILayout.Label("Tip", smallTitleStyle);
                GUILayout.Label("For teacher demos, capture three memories first, run the mid test once, then finish the rest and trigger the final test.", guideStyle);
            }
            else
            {
                if (ApplyMeaningFirstMnemonicGuardrails(selectedStudyItem))
                {
                    ClearGeneratedImageCueForWord(selectedStudyItem.word);
                }

                GUILayout.Label("Selected Mnemonic", smallTitleStyle);
                GUILayout.Label(selectedStudyItem.word, titleStyle);
                GUILayout.Label(selectedStudyItem.meaning + (string.IsNullOrWhiteSpace(selectedStudyItem.meaningJa) ? string.Empty : $" ({selectedStudyItem.meaningJa})"), mutedStyle);
                GUILayout.Label($"Anchor: {selectedStudyItem.anchorLabel}", mutedStyle);
                GUILayout.Space(10);
                DrawBilingualSection("Overlay Cue Scene / Image Scene", selectedStudyItem.visualCue, selectedStudyItem.visualCueJa, smallTitleStyle, guideStyle);
                GUILayout.Space(10);
                DrawBilingualSection("Cue Story / Memory Link", selectedStudyItem.mnemonic, selectedStudyItem.mnemonicJa, smallTitleStyle, guideStyle);
                /*
                DrawBilingualSection("Scene To Imagine / 想像シーン", selectedStudyItem.visualCue, selectedStudyItem.visualCueJa, smallTitleStyle, guideStyle);
                GUILayout.Space(10);
                DrawBilingualSection("Word Connection / 単語とのつながり", selectedStudyItem.mnemonic, selectedStudyItem.mnemonicJa, smallTitleStyle, guideStyle);
                */
                GUILayout.Space(8);
                if (selectedStudyItem.visualObjects != null && selectedStudyItem.visualObjects.Count > 0)
                {
                    GUILayout.Label(showAbstractMnemonicProps
                        ? $"3D proxy props visible: {selectedStudyItem.visualObjects.Count}"
                        : "3D proxy props are hidden; use the generated image cue as the main visual mnemonic.", mutedStyle);
                }
                GUILayout.Space(8);
                DrawImageCuePanel(selectedStudyItem);
                GUILayout.Label("Anchor = where it lives. Overlay cue = what you picture. Story = why that cue recovers the word. Image Cue = generated illustration of the overlay cue at the anchor.", mutedStyle);
                GUILayout.Space(12);

                var hasSnapshot = memorySnapshots.ContainsKey(selectedStudyItem.word);
                if (hasSnapshot)
                {
                    GUILayout.Label("Stored Snapshot", smallTitleStyle);
                    GUILayout.Box(memorySnapshots[selectedStudyItem.word], GUILayout.Width(220f), GUILayout.Height(130f));
                    GUILayout.Label("This word has already been captured for the recognition tests. You can replace it if you generated a better image cue.", mutedStyle);
                    if (!isCapturingSnapshot && GUILayout.Button("Replace Stored Snapshot With Current Cue", buttonStyle))
                    {
                        StartCoroutine(CaptureMemorySnapshotRoutine(selectedStudyItem));
                    }
                }
                else if (isCapturingSnapshot)
                {
                    GUILayout.Label("Capturing snapshot...", mutedStyle);
                }
                else
                {
                    if (GUILayout.Button("Capture Memory Snapshot", buttonStyle))
                    {
                        StartCoroutine(CaptureMemorySnapshotRoutine(selectedStudyItem));
                    }
                }
                if (GUILayout.Button("Close Detail Panel", buttonStyle))
                {
                    selectedStudyItem = null;
                }
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawRecallView()
        {
            var rect = new Rect(18f, ContentTop, Screen.width - 36f, ContentHeight);
            GUI.Box(rect, GUIContent.none, panelStyle);
            RegisterGuiRect(rect);

            GUILayout.BeginArea(rect);
            recallScroll = GUILayout.BeginScrollView(recallScroll);

            GUILayout.Label(isFinalRecognitionPhase ? "Step 5 of 7 - Final Image Test" : "Step 4 of 7 - Mid Image Test", titleStyle);
            GUILayout.Label(isFinalRecognitionPhase
                ? "Choose the correct snapshot again with new distractors. After each answer, the camera jumps back to the correct anchor."
                : "Choose the snapshot that matches the target word. One image is correct and two are distractors captured from other memorized words.",
                mutedStyle);

            if (recognitionQueue.Count == 0 || string.IsNullOrWhiteSpace(currentRecognitionTargetWord))
            {
                GUILayout.Space(12);
                GUILayout.BeginVertical(sectionStyle);
                GUILayout.Label("Recognition queue is empty.", smallTitleStyle);
                GUILayout.Label("Capture at least three memory snapshots in the study room before launching the image-choice test.", guideStyle);
                GUILayout.EndVertical();
                GUILayout.Space(12);
                if (GUILayout.Button("Back to Study Room", buttonStyle))
                {
                    stage = ExperimentStage.Study;
                    ResetCameraForStudy();
                    SetupVrStudyRuntime();
                }
            }
            else
            {
                GUILayout.Space(12);
                GUILayout.BeginVertical(sectionStyle);
                GUILayout.Label($"Question {recognitionIndex + 1} / {recognitionQueue.Count}", smallTitleStyle);
                GUILayout.Label($"Target Word: {currentRecognitionTargetWord}", titleStyle);
                GUILayout.EndVertical();

                GUILayout.Space(12);
                GUILayout.BeginHorizontal();
                for (int i = 0; i < recognitionOptions.Count; i++)
                {
                    var optionWord = recognitionOptions[i];
                    GUILayout.BeginVertical(sectionStyle, GUILayout.Width(260f));
                    GUILayout.Label($"Option {i + 1}", smallTitleStyle);
                    if (memorySnapshots.TryGetValue(optionWord, out var snapshotTexture) && snapshotTexture != null)
                    {
                        GUI.enabled = !awaitingRecognitionAdvance && !isCapturingSnapshot;
                        if (GUILayout.Button(snapshotTexture, GUILayout.Width(232f), GUILayout.Height(150f)))
                        {
                            SubmitRecognitionChoice(optionWord);
                        }
                        GUI.enabled = true;
                    }
                    else
                    {
                        GUILayout.Box("Snapshot missing", GUILayout.Width(232f), GUILayout.Height(150f));
                    }

                    GUILayout.Label(awaitingRecognitionAdvance ? optionWord : "Image choice", mutedStyle);
                    GUILayout.EndVertical();
                }
                GUILayout.EndHorizontal();

                if (!string.IsNullOrWhiteSpace(recognitionFeedback))
                {
                    GUILayout.Space(12);
                    GUILayout.BeginVertical(sectionStyle);
                    GUILayout.Label("Feedback", smallTitleStyle);
                    GUILayout.Label(recognitionFeedback, guideStyle);
                    var answeredItem = FindItemByWord(currentRecognitionTargetWord);
                    if (answeredItem != null)
                    {
                        GUILayout.Space(8);
                        GUILayout.Label("Correct Word / 正解語彙", smallTitleStyle);
                        GUILayout.Label(answeredItem.word, labelStyle);
                        GUILayout.Label(
                            answeredItem.meaning + (string.IsNullOrWhiteSpace(answeredItem.meaningJa) ? string.Empty : $" ({answeredItem.meaningJa})"),
                            mutedStyle);
                    }
                    GUILayout.EndVertical();
                }

                GUILayout.Space(12);
                if (awaitingRecognitionAdvance)
                {
                    if (GUILayout.Button(recognitionIndex < recognitionQueue.Count - 1 ? "Continue To Next Test" : (isFinalRecognitionPhase ? "Continue To Questionnaire" : "Return To Study Room"), buttonStyle))
                    {
                        AdvanceRecognitionFlow();
                    }
                }
                else if (GUILayout.Button("Back to Study Room", buttonStyle))
                {
                    stage = ExperimentStage.Study;
                    ResetCameraForStudy();
                    SetupVrStudyRuntime();
                }
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawQuestionnaireView()
        {
            var rect = new Rect(18f, ContentTop, Screen.width - 36f, ContentHeight);
            GUI.Box(rect, GUIContent.none, panelStyle);
            RegisterGuiRect(rect);

            GUILayout.BeginArea(rect);
            questionnaireScroll = GUILayout.BeginScrollView(questionnaireScroll);
            GUILayout.Label("Step 6 of 7 - Post-Session Questionnaire", titleStyle);
            GUILayout.Label("NASA-TLX style sliders plus subjective quality ratings. This is the post-study evaluation block before export.", mutedStyle);
            GUILayout.Space(10);

            questionnaire.mentalDemand = DrawSlider("Mental Demand", questionnaire.mentalDemand, 0, 20);
            questionnaire.physicalDemand = DrawSlider("Physical Demand", questionnaire.physicalDemand, 0, 20);
            questionnaire.temporalDemand = DrawSlider("Temporal Demand", questionnaire.temporalDemand, 0, 20);
            questionnaire.performance = DrawSlider("Performance", questionnaire.performance, 0, 20);
            questionnaire.effort = DrawSlider("Effort", questionnaire.effort, 0, 20);
            questionnaire.frustration = DrawSlider("Frustration", questionnaire.frustration, 0, 20);

            GUILayout.Space(10);
            GUILayout.Label("Mnemonic quality ratings (1-7)", titleStyle);
            questionnaire.vividness = DrawSevenPointScale("Vividness", questionnaire.vividness);
            questionnaire.helpfulness = DrawSevenPointScale("Helpfulness", questionnaire.helpfulness);
            questionnaire.trust = DrawSevenPointScale("Trust", questionnaire.trust);

            GUILayout.Space(10);
            GUILayout.Label("Notes", mutedStyle);
            questionnaire.notes = GUILayout.TextArea(questionnaire.notes ?? string.Empty, textAreaStyle, GUILayout.MinHeight(90f));

            GUILayout.Space(14);
            if (GUILayout.Button("Next: Finish Session and Export", buttonStyle))
            {
                FinalizeAndExport();
            }

            if (GUILayout.Button("Back to Study Room", buttonStyle))
            {
                stage = ExperimentStage.Study;
                ResetCameraForStudy();
                SetupVrStudyRuntime();
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawResultView()
        {
            var rect = new Rect(18f, ContentTop, Screen.width - 36f, ContentHeight);
            GUI.Box(rect, GUIContent.none, panelStyle);
            RegisterGuiRect(rect);

            GUILayout.BeginArea(rect);
            resultScroll = GUILayout.BeginScrollView(resultScroll);

            GUILayout.Label("Step 7 of 7 - Session Summary", titleStyle);
            GUILayout.Label(statusMessage, mutedStyle);
            GUILayout.Space(8);

            var midCorrect = CountSnapshotResponses("mid", true);
            var midTotal = CountSnapshotResponses("mid", null);
            var finalCorrect = CountSnapshotResponses("final", true);
            var finalTotal = CountSnapshotResponses("final", null);

            GUILayout.BeginVertical(sectionStyle);
            GUILayout.Label($"Participant: {participantId}", labelStyle);
            GUILayout.Label($"Condition: {GetConditionDisplayName()}", labelStyle);
            GUILayout.Label($"Mnemonic Source: {ResolveProviderLabel()}", labelStyle);
            GUILayout.Label($"Ollama Request Used: {(IsUsingLiveLlm() ? "Yes" : "No")}", labelStyle);
            GUILayout.Label($"Room: {RoomSpecCatalog.RoomName}", labelStyle);
            GUILayout.Label($"Room Source: {RoomSpecCatalog.CurrentRoom.generatedBy}", mutedStyle);
            GUILayout.Label($"Study Duration: {studyDurationSeconds:F1}s", labelStyle);
            GUILayout.Label($"Snapshots Captured: {memorizedWords.Count}/{currentItems.Count}", labelStyle);
            GUILayout.Label($"Mid Test Score: {midCorrect}/{midTotal}", labelStyle);
            GUILayout.Label($"Final Test Score: {finalCorrect}/{finalTotal}", labelStyle);
            GUILayout.Label($"Viewed Items: {viewedWords.Count}/{currentItems.Count}", labelStyle);
            GUILayout.EndVertical();

            GUILayout.BeginVertical(sectionStyle);
            GUILayout.Label("Export Paths", titleStyle);
            GUILayout.Label(lastJsonExportPath, labelStyle);
            GUILayout.Label(lastCsvExportPath, labelStyle);
            GUILayout.Label(exportMessage, mutedStyle);
            GUILayout.EndVertical();

            GUILayout.BeginVertical(sectionStyle);
            GUILayout.Label("Generated / Authored Items", titleStyle);
            foreach (var item in currentItems)
            {
                GUILayout.Label($"{item.word} @ {item.anchorLabel}", labelStyle);
                DrawBilingualSection("Overlay Cue Scene / Image Scene", item.visualCue, item.visualCueJa, smallTitleStyle, mutedStyle);
                GUILayout.Space(4);
                DrawBilingualSection("Cue Story / Memory Link", item.mnemonic, item.mnemonicJa, smallTitleStyle, mutedStyle);
                /*
                DrawBilingualSection("Scene To Imagine / 想像シーン", item.visualCue, item.visualCueJa, smallTitleStyle, mutedStyle);
                GUILayout.Space(4);
                DrawBilingualSection("Word Connection / 単語とのつながり", item.mnemonic, item.mnemonicJa, smallTitleStyle, mutedStyle);
                */
                GUILayout.Space(6);
            }
            GUILayout.EndVertical();

            GUILayout.Space(12);
            if (GUILayout.Button("Start New Session", buttonStyle))
            {
                ResetForNewSession();
            }

            if (GUILayout.Button("Return to Setup", buttonStyle))
            {
                stage = ExperimentStage.Setup;
                MoveCameraToOverview();
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void BeginLlmFlow()
        {
            if (!PrepareWordSetFromSetup())
            {
                return;
            }

            ResetSessionState();
            sessionId = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            stage = ExperimentStage.Generation;
            generationError = string.Empty;
            usedLiveLlmForCurrentSession = false;
            statusMessage = "Calling Ollama to generate mnemonic set...";

            StopAllCoroutines();
            isGenerating = true;
            StartCoroutine(RunLlmGeneration());
        }

        private IEnumerator RunLlmGeneration()
        {
            yield return new WaitForSecondsRealtime(0.4f);

            var service = new OllamaLlmService();
            List<MnemonicItemData> results = null;
            string error = null;

            yield return StartCoroutine(service.GenerateMnemonics(
                ollamaBaseUrl,
                ollamaModel,
                activeWordSet.words,
                items => results = items,
                err => error = err));

            if (!string.IsNullOrWhiteSpace(error))
            {
                generationError = error;
                currentItems = new List<MnemonicItemData>();
                statusMessage = error.StartsWith("Failed to parse Ollama", StringComparison.Ordinal)
                    ? "Ollama responded, but the mnemonic JSON was malformed. Try generating again."
                    : "Ollama request failed. Check that Ollama is running and the model name is available.";
                usedLiveLlmForCurrentSession = false;
            }
            else
            {
                currentItems = EnsureMnemonicDefaults(results);
                ReassignCurrentItemsToAnchors();
                statusMessage = $"Ollama returned {currentItems.Count} mnemonic items successfully.";
                usedLiveLlmForCurrentSession = true;
            }

            isGenerating = false;
        }

        private void BeginSelfAuthoring()
        {
            if (!PrepareWordSetFromSetup())
            {
                return;
            }

            ResetSessionState();
            sessionId = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            currentItems = BuildBlankSelfDrafts(activeWordSet.words);
            StopAllCoroutines();
            stage = ExperimentStage.SelfAuthoring;
            statusMessage = "Self-authoring workspace is ready.";
        }

        private void EnterStudyRoom()
        {
            BuildStudyRoom();
            stage = ExperimentStage.Study;
            studyStartTime = Time.unscaledTime;
            selectedStudyItem = null;
            viewedWords.Clear();
            ResetCameraForStudy();
            SetupVrStudyRuntime();
            statusMessage = $"Study phase started in {RoomSpecCatalog.RoomName}.";
        }

        private void BeginSnapshotTest(bool finalPhase)
        {
            if (memorySnapshots.Count < MidTestTriggerCount)
            {
                statusMessage = $"Capture at least {MidTestTriggerCount} memory snapshots before starting the image-choice test.";
                return;
            }

            studyDurationSeconds = Time.unscaledTime - studyStartTime;
            ClearVrStudyRuntime();
            stage = ExperimentStage.Recall;
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
            isFinalRecognitionPhase = finalPhase;
            awaitingRecognitionAdvance = false;
            recognitionFeedback = string.Empty;
            recognitionQueue.Clear();
            recognitionOptions.Clear();
            currentRecognitionTargetWord = string.Empty;
            recognitionIndex = 0;

            for (int i = 0; i < currentItems.Count; i++)
            {
                if (memorySnapshots.ContainsKey(currentItems[i].word))
                {
                    recognitionQueue.Add(currentItems[i].word);
                }
            }

            if (recognitionQueue.Count < MidTestTriggerCount)
            {
                statusMessage = "Not enough snapshots were available to build a three-image recognition question.";
                stage = ExperimentStage.Study;
                return;
            }

            PrepareRecognitionQuestion();
            statusMessage = finalPhase ? "Final image test started." : "Mid image test started.";
        }

        private void PrepareRecognitionQuestion()
        {
            if (recognitionIndex < 0 || recognitionIndex >= recognitionQueue.Count)
            {
                currentRecognitionTargetWord = string.Empty;
                recognitionOptions.Clear();
                return;
            }

            currentRecognitionTargetWord = recognitionQueue[recognitionIndex];
            recognitionOptions.Clear();
            recognitionOptions.Add(currentRecognitionTargetWord);

            var distractors = new List<string>();
            foreach (var entry in memorySnapshots)
            {
                if (entry.Key != currentRecognitionTargetWord)
                {
                    distractors.Add(entry.Key);
                }
            }

            ShuffleList(distractors);
            var distractorCount = Mathf.Min(2, distractors.Count);
            for (int i = 0; i < distractorCount; i++)
            {
                recognitionOptions.Add(distractors[i]);
            }

            ShuffleList(recognitionOptions);
            awaitingRecognitionAdvance = false;
            recognitionFeedback = string.Empty;
        }

        private void SubmitRecognitionChoice(string chosenWord)
        {
            if (awaitingRecognitionAdvance || string.IsNullOrWhiteSpace(currentRecognitionTargetWord))
            {
                return;
            }

            var targetItem = FindItemByWord(currentRecognitionTargetWord);
            var isCorrect = string.Equals(chosenWord, currentRecognitionTargetWord, StringComparison.Ordinal);

            snapshotTestResponses.Add(new SnapshotTestResponse
            {
                phase = isFinalRecognitionPhase ? "final" : "mid",
                targetWord = currentRecognitionTargetWord,
                targetAnchorId = targetItem != null ? targetItem.anchorId : string.Empty,
                optionWords = new List<string>(recognitionOptions),
                chosenWord = chosenWord,
                isCorrect = isCorrect
            });

            awaitingRecognitionAdvance = true;
            recognitionFeedback = isCorrect
                ? $"Correct. The snapshot matched {currentRecognitionTargetWord}."
                : $"Incorrect. The correct snapshot was {currentRecognitionTargetWord}.";

            LogInteraction(
                isFinalRecognitionPhase ? "final_image_choice" : "mid_image_choice",
                currentRecognitionTargetWord,
                targetItem != null ? targetItem.anchorId : string.Empty,
                $"Selected snapshot: {chosenWord}");

            if (isFinalRecognitionPhase)
            {
                StartCoroutine(TeleportToMnemonicRoutine(currentRecognitionTargetWord));
            }
        }

        private void AdvanceRecognitionFlow()
        {
            recognitionIndex++;

            if (recognitionIndex >= recognitionQueue.Count)
            {
                awaitingRecognitionAdvance = false;
                recognitionOptions.Clear();
                currentRecognitionTargetWord = string.Empty;

                if (isFinalRecognitionPhase)
                {
                    finalTestCompleted = true;
                    stage = ExperimentStage.Questionnaire;
                    statusMessage = "Final image test finished. Questionnaire is ready.";
                }
                else
                {
                    midTestCompleted = true;
                    stage = ExperimentStage.Study;
                    ResetCameraForStudy();
                    SetupVrStudyRuntime();
                    statusMessage = "Mid image test finished. Continue studying the remaining items.";
                }

                return;
            }

            PrepareRecognitionQuestion();
        }

        private IEnumerator CaptureMemorySnapshotRoutine(MnemonicItemData item)
        {
            if (item == null || runtimeCamera == null || isCapturingSnapshot)
            {
                yield break;
            }

            isCapturingSnapshot = true;
            flashOverlayAlpha = 0.95f;
            yield return new WaitForEndOfFrame();

            var usedGeneratedImage = mnemonicImageCues.TryGetValue(item.word, out var imageCueTexture) && imageCueTexture != null;
            var texture = usedGeneratedImage
                ? CloneTexture(imageCueTexture)
                : CaptureCurrentCameraSnapshot(512, 320);
            if (memorySnapshots.TryGetValue(item.word, out var previousTexture) && previousTexture != null)
            {
                Destroy(previousTexture);
            }

            memorySnapshots[item.word] = texture;
            memorizedWords.Add(item.word);
            selectedStudyItem = item;
            LogInteraction(
                "capture_memory_snapshot",
                item.word,
                item.anchorId,
                usedGeneratedImage
                    ? "Stored the generated image cue for later recognition tests."
                    : "Captured a study snapshot for later recognition tests.");

            for (var elapsed = 0f; elapsed < 0.3f; elapsed += Time.unscaledDeltaTime)
            {
                flashOverlayAlpha = Mathf.Lerp(0.95f, 0f, elapsed / 0.3f);
                yield return null;
            }

            flashOverlayAlpha = 0f;
            isCapturingSnapshot = false;

            if (!midTestCompleted && memorizedWords.Count >= MidTestTriggerCount)
            {
                statusMessage = $"Snapshot stored for {item.word}. Mid test is now unlocked.";
            }
            else if (midTestCompleted && !finalTestCompleted && memorizedWords.Count == currentItems.Count)
            {
                statusMessage = $"Snapshot stored for {item.word}. Final test is now unlocked.";
            }
            else
            {
                statusMessage = usedGeneratedImage
                    ? $"Generated image cue stored for {item.word}."
                    : $"Snapshot stored for {item.word}.";
            }
        }

        private void DrawImageCuePanel(MnemonicItemData item)
        {
            if (item == null)
            {
                return;
            }

            GUILayout.Label("Generated Image Cue", smallTitleStyle);
            if (mnemonicImageCues.TryGetValue(item.word, out var texture) && texture != null)
            {
                GUILayout.Box(texture, GUILayout.Width(220f), GUILayout.Height(220f));
                GUILayout.Label("This image will be used for the image-choice tests when you capture this memory.", mutedStyle);
                if (GUILayout.Button("Regenerate Image Cue", buttonStyle))
                {
                    StartCoroutine(GenerateMnemonicImageCueRoutine(item));
                }
                return;
            }

            var prompt = BuildMnemonicImagePrompt(item);
            GUILayout.Label("Prompt: " + prompt, mutedStyle);
            var isGeneratingCue = generatingImageCueWords.Contains(item.word);
            GUI.enabled = !isGeneratingCue;
            if (GUILayout.Button(isGeneratingCue ? "Generating Image Cue..." : "Generate Image Cue (Local SD)", buttonStyle))
            {
                StartCoroutine(GenerateMnemonicImageCueRoutine(item));
            }
            GUI.enabled = true;

            if (!string.IsNullOrWhiteSpace(imageGenerationStatus))
            {
                GUILayout.Label(imageGenerationStatus, mutedStyle);
            }
        }

        private IEnumerator GenerateMnemonicImageCueRoutine(MnemonicItemData item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.word))
            {
                yield break;
            }

            if (generatingImageCueWords.Contains(item.word))
            {
                yield break;
            }

            if (string.IsNullOrWhiteSpace(imageGenerationEndpoint))
            {
                imageGenerationStatus = "Image endpoint is empty. Use a local txt2img endpoint such as Stable Diffusion WebUI with --api.";
                yield break;
            }

            generatingImageCueWords.Add(item.word);
            imageGenerationStatus = $"Generating image cue for {item.word}...";

            var requestBody = new StableDiffusionTxt2ImgRequest
            {
                prompt = BuildMnemonicImagePrompt(item),
                negative_prompt = BuildMnemonicImageNegativePrompt(),
                width = 576,
                height = 576,
                steps = 30,
                cfg_scale = 8f,
                sampler_name = "DPM++ 2M Karras",
                override_settings = BuildImageOverrideSettings(),
                override_settings_restore_afterwards = true
            };
            var json = JsonUtility.ToJson(requestBody);

            using (var request = new UnityWebRequest(imageGenerationEndpoint.Trim(), UnityWebRequest.kHttpVerbPOST))
            {
                var bodyRaw = Encoding.UTF8.GetBytes(json);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = 180;
                request.SetRequestHeader("Content-Type", "application/json");

                yield return request.SendWebRequest();

                generatingImageCueWords.Remove(item.word);

                if (request.result != UnityWebRequest.Result.Success)
                {
                    imageGenerationStatus = $"Image generation failed: {request.error}. Check that Stable Diffusion WebUI is running with --api.";
                    yield break;
                }

                StableDiffusionTxt2ImgResponse response = null;
                try
                {
                    response = JsonUtility.FromJson<StableDiffusionTxt2ImgResponse>(request.downloadHandler.text);
                }
                catch (Exception ex)
                {
                    imageGenerationStatus = "Failed to parse image response: " + ex.Message;
                    yield break;
                }

                if (response == null || response.images == null || response.images.Length == 0 || string.IsNullOrWhiteSpace(response.images[0]))
                {
                    imageGenerationStatus = "Image response did not contain any images.";
                    yield break;
                }

                if (!TryLoadBase64Image(response.images[0], out var texture, out var error))
                {
                    imageGenerationStatus = error;
                    yield break;
                }

                if (mnemonicImageCues.TryGetValue(item.word, out var previousTexture) && previousTexture != null)
                {
                    Destroy(previousTexture);
                }

                mnemonicImageCues[item.word] = texture;
                item.imageCuePath = SaveMnemonicImageCue(item, texture);
                imageGenerationStatus = $"Generated image cue for {item.word}.";
                LogInteraction("generate_image_cue", item.word, item.anchorId, "Generated local txt2img cue: " + item.imageCuePath);
            }
        }

        private bool TryLoadBase64Image(string rawImage, out Texture2D texture, out string error)
        {
            texture = null;
            error = string.Empty;
            var base64 = rawImage.Trim();
            var commaIndex = base64.IndexOf(',');
            if (base64.StartsWith("data:image", StringComparison.OrdinalIgnoreCase) && commaIndex >= 0)
            {
                base64 = base64.Substring(commaIndex + 1);
            }

            try
            {
                var bytes = Convert.FromBase64String(base64);
                texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!texture.LoadImage(bytes))
                {
                    Destroy(texture);
                    texture = null;
                    error = "Image bytes could not be loaded as a texture.";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                error = "Failed to decode generated image: " + ex.Message;
                return false;
            }
        }

        private string SaveMnemonicImageCue(MnemonicItemData item, Texture2D texture)
        {
            if (texture == null)
            {
                return string.Empty;
            }

            var exportFolder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "ExperimentExports", "GeneratedMnemonicImages", string.IsNullOrWhiteSpace(sessionId) ? "unsaved_session" : sessionId));
            Directory.CreateDirectory(exportFolder);
            var fileName = SanitizeIdPrefix(item.word) + "_image_cue.png";
            var path = Path.Combine(exportFolder, fileName);
            File.WriteAllBytes(path, texture.EncodeToPNG());
            return path;
        }

        private Texture2D CloneTexture(Texture2D source)
        {
            var clone = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            clone.LoadImage(source.EncodeToPNG());
            return clone;
        }

        private string BuildMnemonicImagePrompt(MnemonicItemData item)
        {
            if (item == null)
            {
                return "mnemonic illustration, no text, no letters, no captions";
            }

            if (ApplyMeaningFirstMnemonicGuardrails(item))
            {
                item.imageCuePath = string.Empty;
            }
            return BuildAnchorGroundedImagePrompt(item, item.imagePrompt);
        }

        private void ApplyAnchorConsistency(MnemonicItemData item)
        {
            if (item == null)
            {
                return;
            }

            if (ApplyMeaningFirstMnemonicGuardrails(item))
            {
                item.imageCuePath = string.Empty;
            }
            item.visualCue = AnchorGroundSceneEnglish(item, item.visualCue);
            item.visualCueJa = AnchorGroundSceneJapanesePreserve(item, item.visualCueJa);
            item.mnemonic = AnchorGroundMnemonicEnglish(item, item.mnemonic);
            item.mnemonicJa = AnchorGroundMnemonicJapanesePreserve(item, item.mnemonicJa);
            item.imagePrompt = BuildAnchorGroundedImagePrompt(item, item.imagePrompt);
            item.imagePromptJa = BuildAnchorGroundedImagePrompt(item, item.imagePromptJa);
        }

        private string AnchorGroundSceneJapanesePreserve(MnemonicItemData item, string scene)
        {
            var anchor = GetAnchorDisplayName(item);
            var meaning = string.IsNullOrWhiteSpace(item.meaningJa) ? GetMeaningText(item) : item.meaningJa.Trim();
            if (string.IsNullOrWhiteSpace(scene))
            {
                return $"{anchor}を舞台に、{meaning}が直感的に浮かぶ印象的な場面。";
            }

            var trimmed = ReplaceConflictingRoomObjectTerms(scene.Trim(), anchor);
            if (TextMentionsAnchor(trimmed, anchor))
            {
                return trimmed;
            }

            return $"{anchor}を舞台に、{trimmed}";
        }

        private string AnchorGroundMnemonicJapanesePreserve(MnemonicItemData item, string mnemonic)
        {
            var anchor = GetAnchorDisplayName(item);
            var meaning = string.IsNullOrWhiteSpace(item.meaningJa) ? GetMeaningText(item) : item.meaningJa.Trim();
            if (string.IsNullOrWhiteSpace(mnemonic))
            {
                return $"{anchor}の具体的なイメージが、{item.word}と{meaning}を結びつける。";
            }

            var trimmed = RemoveJapaneseMemoryLocationTemplate(ReplaceConflictingRoomObjectTerms(mnemonic.Trim(), anchor), anchor);
            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                return trimmed;
            }

            return $"{anchor}を記憶場所として、{trimmed}";
        }

        private string AnchorGroundSceneEnglish(MnemonicItemData item, string scene)
        {
            var anchor = GetAnchorDisplayName(item);
            var sceneText = string.IsNullOrWhiteSpace(scene)
                ? BuildRecoveredSceneDetail(item)
                : ExtractPromptSceneDetail(scene);

            if (string.IsNullOrWhiteSpace(sceneText))
            {
                return $"At the {anchor}, a concrete event makes {GetMeaningText(item)} easy to picture.";
            }

            if (TextMentionsAnchor(sceneText, anchor))
            {
                return sceneText;
            }

            return EnsureEnglishAnchorLead(anchor, sceneText);
        }

        private string AnchorGroundSceneJapanese(MnemonicItemData item, string scene)
        {
            var anchor = GetAnchorDisplayName(item);
            var meaning = string.IsNullOrWhiteSpace(item.meaningJa) ? GetMeaningText(item) : item.meaningJa.Trim();
            if (string.IsNullOrWhiteSpace(scene))
            {
                return $"{anchor}を中心に、{meaning}をその家具の上または周囲で表す印象的な場面。";
            }

            var trimmed = scene.Trim();
            if (TextMentionsAnchor(trimmed, anchor))
            {
                return trimmed;
            }

            return $"{anchor}を中心に、{meaning}をその家具の上または周囲で表す場面。";
        }

        private string AnchorGroundMnemonicEnglish(MnemonicItemData item, string mnemonic)
        {
            var anchor = GetAnchorDisplayName(item);
            var meaning = GetMeaningText(item);
            if (string.IsNullOrWhiteSpace(mnemonic))
            {
                return $"{item.word} is cued by a concrete event at the {anchor}: {meaning}.";
            }

            var trimmed = ReplaceConflictingRoomObjectTerms(mnemonic.Trim(), anchor);
            return RemoveMemoryLocationTemplate(trimmed, anchor);
        }

        private string AnchorGroundMnemonicJapanese(MnemonicItemData item, string mnemonic)
        {
            var anchor = GetAnchorDisplayName(item);
            var meaning = string.IsNullOrWhiteSpace(item.meaningJa) ? GetMeaningText(item) : item.meaningJa.Trim();
            if (string.IsNullOrWhiteSpace(mnemonic))
            {
                return $"{anchor}の場面が、{item.word}と{meaning}を結びつける。";
            }

            var trimmed = mnemonic.Trim();
            if (TextMentionsAnchor(trimmed, anchor))
            {
                return trimmed;
            }

            return $"{anchor}を記憶場所として、{trimmed}";
        }

        private string BuildAnchorGroundedImagePrompt(MnemonicItemData item, string promptOverride)
        {
            var anchor = GetAnchorDisplayName(item);
            var backgroundScene = BuildImageBackgroundScene(item);
            var foregroundFocus = BuildImageForegroundFocus(item, promptOverride);

            if (string.IsNullOrWhiteSpace(backgroundScene))
            {
                backgroundScene = BuildRecoveredSceneDetail(item);
            }

            if (string.IsNullOrWhiteSpace(backgroundScene))
            {
                backgroundScene = $"a vivid mnemonic metaphor for {GetMeaningText(item)}";
            }

            if (IsAlreadyAnchorGroundedImagePrompt(promptOverride, anchor))
            {
                return promptOverride;
            }

            backgroundScene = ExtractPromptSceneDetail(backgroundScene);
            if (string.IsNullOrWhiteSpace(backgroundScene))
            {
                backgroundScene = $"one concrete mnemonic scene for {item.word}, meaning {GetMeaningText(item)}";
            }

            foregroundFocus = ExtractPromptSceneDetail(foregroundFocus);
            if (string.IsNullOrWhiteSpace(foregroundFocus))
            {
                foregroundFocus = backgroundScene;
            }

            return $"((single mnemonic subject)), ((close-up view)), ((foreground mnemonic detail dominates the frame)), ((no room overview)), simple indoor anchor-cue mnemonic illustration staged at the {anchor}. Use this Scene to Imagine only as background context: {backgroundScene}. Make this Cue Story the clear foreground focus of the image: {foregroundFocus}. Keep the {anchor} visible only as supporting spatial context in the background. The foreground object, gesture, or small action should occupy most of the frame and be more visually important than the room. Minimal background clutter. Avoid empty-room, window-only, furniture-only, smoke, haze, light-beam, architectural rendering, and interior-design compositions. Soft natural room lighting, centered composition, no disaster scene, no explosion, no fire, no smoke cloud, no industrial building, no text, no letters, no captions, no logos, no watermark.";
        }

        private string BuildMnemonicImageNegativePrompt()
        {
            return "text, letters, words, captions, logo, watermark, signature, blurry, low quality, distorted, extra limbs, empty room, bare room, furniture only, window only, shelf only, landscape view, room overview, full room, whole room, establishing shot, wide shot, long shot, distant subject, tiny subject, architectural rendering, interior design photo, background emphasis, smoke, fog, haze, light beam, abstract atmosphere, cluttered background";
        }

        private string BuildImageBackgroundScene(MnemonicItemData item)
        {
            if (item == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(item.visualCue))
            {
                return item.visualCue.Trim();
            }

            return BuildRecoveredSceneDetail(item);
        }

        private string BuildImageForegroundFocus(MnemonicItemData item, string promptOverride)
        {
            if (item == null)
            {
                return string.Empty;
            }

            var promptFocus = ExtractImageForegroundFocus(promptOverride);
            if (!string.IsNullOrWhiteSpace(promptFocus))
            {
                return $"foreground overlay cue/action: {promptFocus}";
            }

            if (!string.IsNullOrWhiteSpace(item.visualCue))
            {
                return $"foreground overlay cue/action from Scene to Imagine: {item.visualCue.Trim()}";
            }

            if (!string.IsNullOrWhiteSpace(item.mnemonic))
            {
                return $"visible mnemonic focus that makes this cue story obvious: {item.mnemonic.Trim()}";
            }

            if (!string.IsNullOrWhiteSpace(promptOverride))
            {
                return promptOverride.Trim();
            }

            return BuildRecoveredSceneDetail(item);
        }

        private static string ExtractImageForegroundFocus(string promptOverride)
        {
            var detail = ExtractPromptSceneDetail(promptOverride);
            if (string.IsNullOrWhiteSpace(detail))
            {
                return string.Empty;
            }

            var lower = detail.ToLowerInvariant();
            if (detail.Length > 140
                || ContainsAny(lower,
                    "scene to imagine only as background context",
                    "room overview",
                    "whole room",
                    "full room",
                    "empty room",
                    "interior design",
                    "architectural rendering",
                    "wide shot",
                    "long shot",
                    "establishing shot",
                    "no text",
                    "no letters",
                    "no captions",
                    "watermark"))
            {
                return string.Empty;
            }

            return detail;
        }

        private StableDiffusionOverrideSettings BuildImageOverrideSettings()
        {
            if (string.IsNullOrWhiteSpace(imageGenerationCheckpoint))
            {
                return null;
            }

            return new StableDiffusionOverrideSettings
            {
                sd_model_checkpoint = imageGenerationCheckpoint.Trim()
            };
        }

        private static bool IsAlreadyAnchorGroundedImagePrompt(string prompt, string anchor)
        {
            if (string.IsNullOrWhiteSpace(prompt) || string.IsNullOrWhiteSpace(anchor))
            {
                return false;
            }

            var normalized = prompt.Trim().ToLowerInvariant();
            var anchorLower = anchor.ToLowerInvariant();
            return (normalized.StartsWith("((single mnemonic subject))", StringComparison.Ordinal)
                    || (normalized.Contains("scene to imagine only as background context")
                        && normalized.Contains("clear foreground focus"))
                    || normalized.StartsWith($"simple indoor mnemonic illustration staged at the {anchorLower}", StringComparison.Ordinal)
                    || normalized.StartsWith($"mnemonic illustration staged at the {anchorLower}", StringComparison.Ordinal)
                    || normalized.StartsWith($"centered on the {anchorLower}", StringComparison.Ordinal))
                && normalized.Contains("no text");
        }

        private string GetAnchorDisplayName(MnemonicItemData item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.anchorLabel))
            {
                return "memory palace anchor";
            }

            return item.anchorLabel.Trim();
        }

        private string GetMeaningText(MnemonicItemData item)
        {
            if (item == null)
            {
                return "the target meaning";
            }

            if (!string.IsNullOrWhiteSpace(item.meaning))
            {
                return item.meaning.Trim();
            }

            return string.IsNullOrWhiteSpace(item.meaningJa) ? "the target meaning" : item.meaningJa.Trim();
        }

        private bool ApplyMeaningFirstMnemonicGuardrails(MnemonicItemData item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.word))
            {
                return false;
            }

            if (string.Equals(item.word.Trim(), "iconoclast", StringComparison.OrdinalIgnoreCase)
                && IsWeakIconoclastMnemonic(item))
            {
                var anchor = GetAnchorDisplayName(item);
                item.visualCue = $"At the {anchor}, a small figure chips a cherished ceramic idol with a tiny hammer.";
                item.visualCueJa = item.visualCue;
                item.mnemonic = "The cracked cherished idol cues a person attacking established beliefs, the core of iconoclast.";
                item.mnemonicJa = item.mnemonic;
                item.imagePrompt = $"Close-up indoor anchor-cue mnemonic at the {anchor}: a small figure uses a tiny hammer to chip a cherished ceramic idol, with cracked fragments visible in the foreground. Keep the {anchor} visible in the background, no text, no letters, no captions, no logos, no watermark.";
                item.imagePromptJa = item.imagePrompt;
                return true;
            }

            if (IsKnownWeakGeneratedMnemonic(item)
                && ApplyDistinctFallbackMnemonic(item))
            {
                return true;
            }

            return false;
        }

        private static bool IsKnownWeakGeneratedMnemonic(MnemonicItemData item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.word))
            {
                return false;
            }

            var word = item.word.Trim().ToLowerInvariant();
            switch (word)
            {
                case "intransigent":
                    return IsWeakIntransigentMnemonic(item);

                case "immutable":
                    return IsWeakImmutableMnemonic(item);

                case "capricious":
                    return IsWeakCapriciousMnemonic(item);

                default:
                    return false;
            }
        }

        private static bool IsWeakIconoclastMnemonic(MnemonicItemData item)
        {
            var sceneText = BuildMnemonicSearchText(item);
            var hasCherishedObject = ContainsAny(sceneText, "idol", "statue", "portrait", "tradition", "belief", "cherished");
            var hasBreakingAction = ContainsAny(sceneText, "crack", "break", "chip", "smash", "attack", "hammer");

            return !hasCherishedObject || !hasBreakingAction;
        }

        private static bool IsWeakIntransigentMnemonic(MnemonicItemData item)
        {
            var text = BuildMnemonicSearchText(item);
            return ContainsAny(text,
                "paper corner",
                "pinned flat",
                "fingers fail",
                "finger fails",
                "paper stays",
                "unmoving paper",
                "cannot move it",
                "fail to move it");
        }

        private static bool IsWeakImmutableMnemonic(MnemonicItemData item)
        {
            var text = BuildMnemonicSearchText(item);
            return ContainsAny(text,
                "paper corner",
                "pinned flat",
                "fingers fail",
                "finger fails",
                "paper stays",
                "unmoving paper",
                "refusing compromise",
                "cannot move it",
                "fail to move it");
        }

        private static bool IsWeakCapriciousMnemonic(MnemonicItemData item)
        {
            var text = BuildMnemonicSearchText(item);
            return ContainsAny(text,
                "red dot",
                "small dot",
                "tiny dot",
                "blinking dot",
                "flickering dot",
                "dot flickers",
                "flickers unpredictably");
        }

        private static string BuildMnemonicSearchText(MnemonicItemData item)
        {
            if (item == null)
            {
                return string.Empty;
            }

            return ((item.visualCue ?? string.Empty) + " "
                + (item.mnemonic ?? string.Empty) + " "
                + (item.imagePrompt ?? string.Empty) + " "
                + (item.visualCueJa ?? string.Empty) + " "
                + (item.mnemonicJa ?? string.Empty) + " "
                + (item.imagePromptJa ?? string.Empty)).ToLowerInvariant();
        }

        private string BuildRecoveredSceneDetail(MnemonicItemData item)
        {
            if (item == null)
            {
                return string.Empty;
            }

            var candidates = new[]
            {
                item.imagePrompt,
                item.visualCue,
                item.mnemonic
            };

            for (int i = 0; i < candidates.Length; i++)
            {
                var detail = ExtractPromptSceneDetail(candidates[i]);
                if (!string.IsNullOrWhiteSpace(detail))
                {
                    return detail;
                }
            }

            return string.Empty;
        }

        private static string ExtractPromptSceneDetail(string text)
        {
            var trimmed = string.IsNullOrWhiteSpace(text) ? string.Empty : text.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return string.Empty;
            }

            var colonIndex = trimmed.IndexOf(':');
            if (colonIndex >= 0)
            {
                var prefix = trimmed.Substring(0, colonIndex).Trim();
                if (prefix.StartsWith("Simple indoor mnemonic illustration", StringComparison.OrdinalIgnoreCase)
                    || prefix.StartsWith("Simple indoor anchor-cue mnemonic illustration", StringComparison.OrdinalIgnoreCase)
                    || prefix.StartsWith("Mnemonic illustration", StringComparison.OrdinalIgnoreCase)
                    || prefix.StartsWith("Close-up indoor anchor-cue mnemonic", StringComparison.OrdinalIgnoreCase)
                    || prefix.StartsWith("Close-up indoor mnemonic", StringComparison.OrdinalIgnoreCase))
                {
                    trimmed = trimmed.Substring(colonIndex + 1).Trim();
                }
            }

            trimmed = StripPromptTail(trimmed);
            trimmed = StripLeadingSceneLocationIntro(trimmed);
            return trimmed.Trim().TrimEnd('.', ';', ',');
        }

        private static string StripPromptTail(string text)
        {
            var trimmed = string.IsNullOrWhiteSpace(text) ? string.Empty : text.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return string.Empty;
            }

            var stopMarkers = new[]
            {
                ". Include the ",
                ". Keep the ",
                ". Show this specific mnemonic scene:",
                ". Soft natural room lighting",
                ", soft natural room lighting",
                ", close-up composition",
                ", one clear everyday mnemonic detail",
                ", no disaster scene",
                ", no text",
                ". no text"
            };

            var cutIndex = trimmed.Length;
            for (int i = 0; i < stopMarkers.Length; i++)
            {
                var markerIndex = trimmed.IndexOf(stopMarkers[i], StringComparison.OrdinalIgnoreCase);
                if (markerIndex >= 0 && markerIndex < cutIndex)
                {
                    cutIndex = markerIndex;
                }
            }

            return cutIndex < trimmed.Length
                ? trimmed.Substring(0, cutIndex).Trim()
                : trimmed;
        }

        private static string StripLeadingSceneLocationIntro(string text)
        {
            var trimmed = string.IsNullOrWhiteSpace(text) ? string.Empty : text.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return string.Empty;
            }

            var prefixes = new[]
            {
                "Centered on the ",
                "Centered on ",
                "At the ",
                "At ",
                "On the ",
                "On ",
                "Around the ",
                "Around ",
                "Near the ",
                "Near ",
                "Beside the ",
                "Beside "
            };

            for (int i = 0; i < prefixes.Length; i++)
            {
                if (!trimmed.StartsWith(prefixes[i], StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var commaIndex = trimmed.IndexOf(',');
                if (commaIndex > 0 && commaIndex < 96)
                {
                    return trimmed.Substring(commaIndex + 1).Trim();
                }
            }

            return trimmed;
        }

        private static string EnsureEnglishAnchorLead(string anchor, string text)
        {
            var trimmed = string.IsNullOrWhiteSpace(text) ? string.Empty : text.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return $"At the {anchor}, imagine a vivid mnemonic scene.";
            }

            var normalized = trimmed.TrimStart().ToLowerInvariant();
            var anchorLower = anchor.ToLowerInvariant();
            if (normalized.StartsWith($"at the {anchorLower}", StringComparison.Ordinal)
                || normalized.StartsWith($"at {anchorLower}", StringComparison.Ordinal)
                || normalized.StartsWith($"centered on the {anchorLower}", StringComparison.Ordinal))
            {
                return trimmed;
            }

            return $"At the {anchor}, {LowercaseFirst(trimmed)}";
        }

        private static string StripLeadingEnglishAnchorIntro(string anchor, string text)
        {
            var trimmed = string.IsNullOrWhiteSpace(text) ? string.Empty : text.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || string.IsNullOrWhiteSpace(anchor))
            {
                return trimmed;
            }

            var patterns = new[]
            {
                $"Centered on the {anchor},",
                $"Centered on {anchor},",
                $"At the {anchor},",
                $"At {anchor},",
                $"On the {anchor},",
                $"On {anchor},",
                $"Around the {anchor},",
                $"Around {anchor},"
            };

            for (int i = 0; i < patterns.Length; i++)
            {
                if (trimmed.StartsWith(patterns[i], StringComparison.OrdinalIgnoreCase))
                {
                    return trimmed.Substring(patterns[i].Length).Trim();
                }
            }

            return trimmed;
        }

        private static string RemoveMemoryLocationTemplate(string text, string anchor)
        {
            var trimmed = string.IsNullOrWhiteSpace(text) ? string.Empty : text.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return trimmed;
            }

            var prefixes = new[]
            {
                $"Using the {anchor} as the memory location,",
                $"Using {anchor} as the memory location,",
                $"Using the {anchor} as a memory location,",
                $"Using {anchor} as a memory location,",
                $"The {anchor} is the memory location,",
                $"The {anchor} is the memory location;",
                $"At the {anchor},"
            };

            for (int i = 0; i < prefixes.Length; i++)
            {
                if (trimmed.StartsWith(prefixes[i], StringComparison.OrdinalIgnoreCase))
                {
                    return UppercaseFirst(trimmed.Substring(prefixes[i].Length));
                }
            }

            return trimmed;
        }

        private static string RemoveJapaneseMemoryLocationTemplate(string text, string anchor)
        {
            var trimmed = string.IsNullOrWhiteSpace(text) ? string.Empty : text.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return trimmed;
            }

            var prefixes = new[]
            {
                $"{anchor}を記憶場所として、",
                $"{anchor}を記憶場所にして、",
                $"{anchor}を舞台として、",
                $"{anchor}を舞台に、"
            };

            for (int i = 0; i < prefixes.Length; i++)
            {
                if (trimmed.StartsWith(prefixes[i], StringComparison.OrdinalIgnoreCase))
                {
                    return trimmed.Substring(prefixes[i].Length).Trim();
                }
            }

            return trimmed;
        }

        private static string LowercaseFirst(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var trimmed = value.Trim();
            return trimmed.Length == 1
                ? trimmed.ToLowerInvariant()
                : char.ToLowerInvariant(trimmed[0]) + trimmed.Substring(1);
        }

        private static string UppercaseFirst(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var trimmed = value.Trim();
            return trimmed.Length == 1
                ? trimmed.ToUpperInvariant()
                : char.ToUpperInvariant(trimmed[0]) + trimmed.Substring(1);
        }

        private static bool TextMentionsAnchor(string text, string anchor)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(anchor))
            {
                return false;
            }

            var aliases = GetAnchorAliases(anchor);
            for (int i = 0; i < aliases.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(aliases[i])
                    && text.IndexOf(aliases[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static string ReplaceConflictingRoomObjectTerms(string text, string anchor)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            var result = text.Trim();
            var anchorAliases = GetAnchorAliases(anchor);
            var roomTerms = new[]
            {
                "balcony window", "kitchen counter", "bathroom sink", "ceiling lamp", "dining table",
                "refrigerator", "television", "computer", "bookshelf", "display shelf", "window", "fridge",
                "counter", "toilet", "door", "plant", "shelf", "table", "desk", "chair", "sofa", "couch",
                "bed", "lamp", "sink", "cabinet", "wardrobe", "closet"
            };

            for (int i = 0; i < roomTerms.Length; i++)
            {
                if (IsAnchorAlias(roomTerms[i], anchorAliases))
                {
                    continue;
                }

                result = ReplaceCaseInsensitive(result, roomTerms[i], anchor);
            }

            return result;
        }

        private static bool IsAnchorAlias(string term, string[] aliases)
        {
            for (int i = 0; i < aliases.Length; i++)
            {
                if (string.Equals(term, aliases[i], StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string[] GetAnchorAliases(string anchor)
        {
            var normalized = string.IsNullOrWhiteSpace(anchor) ? string.Empty : anchor.Trim().ToLowerInvariant();
            if (normalized.Contains("fridge") || normalized.Contains("refrigerator"))
            {
                return new[] { normalized, "fridge", "refrigerator" };
            }

            if (normalized.Contains("window") || normalized.Contains("balcony"))
            {
                return new[] { normalized, "window", "balcony window", "balcony" };
            }

            if (normalized.Contains("door"))
            {
                return new[] { normalized, "door", "entrance door" };
            }

            if (normalized.Contains("lamp") || normalized.Contains("light"))
            {
                return new[] { normalized, "lamp", "ceiling lamp", "light" };
            }

            if (normalized.Contains("shelf") || normalized.Contains("bookshelf"))
            {
                return new[] { normalized, "shelf", "bookshelf", "display shelf" };
            }

            if (normalized.Contains("counter"))
            {
                return new[] { normalized, "counter", "kitchen counter" };
            }

            if (normalized.Contains("sink"))
            {
                return new[] { normalized, "sink", "bathroom sink" };
            }

            if (normalized.Contains("table"))
            {
                return new[] { normalized, "table", "dining table" };
            }

            if (normalized.Contains("television") || normalized == "tv" || normalized.Contains("monitor"))
            {
                return new[] { normalized, "television", "tv", "monitor", "screen" };
            }

            if (normalized.Contains("computer") || normalized.Contains("laptop") || normalized.Contains("pc"))
            {
                return new[] { normalized, "computer", "laptop", "pc" };
            }

            if (normalized.Contains("sofa") || normalized.Contains("couch"))
            {
                return new[] { normalized, "sofa", "couch" };
            }

            if (normalized.Contains("plant"))
            {
                return new[] { normalized, "plant", "leaf", "leaves" };
            }

            if (normalized.Contains("bed"))
            {
                return new[] { normalized, "bed" };
            }

            if (normalized.Contains("chair"))
            {
                return new[] { normalized, "chair" };
            }

            if (normalized.Contains("toilet"))
            {
                return new[] { normalized, "toilet", "wc" };
            }

            return new[] { normalized };
        }

        private static string ReplaceCaseInsensitive(string source, string oldValue, string newValue)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(oldValue))
            {
                return source;
            }

            var result = new StringBuilder(source.Length);
            var searchIndex = 0;
            while (searchIndex < source.Length)
            {
                var matchIndex = source.IndexOf(oldValue, searchIndex, StringComparison.OrdinalIgnoreCase);
                if (matchIndex < 0)
                {
                    result.Append(source, searchIndex, source.Length - searchIndex);
                    break;
                }

                result.Append(source, searchIndex, matchIndex - searchIndex);
                result.Append(newValue);
                searchIndex = matchIndex + oldValue.Length;
            }

            return result.ToString();
        }

        private Texture2D CaptureCurrentCameraSnapshot(int width, int height)
        {
            var renderTexture = new RenderTexture(width, height, 24);
            var previousTarget = runtimeCamera.targetTexture;
            var previousActive = RenderTexture.active;
            var hiddenLabelRenderers = new List<Renderer>();

            if (roomRoot != null)
            {
                var textMeshes = roomRoot.GetComponentsInChildren<TextMesh>(true);
                for (int i = 0; i < textMeshes.Length; i++)
                {
                    var labelRenderer = textMeshes[i].GetComponent<Renderer>();
                    if (labelRenderer != null && labelRenderer.enabled)
                    {
                        labelRenderer.enabled = false;
                        hiddenLabelRenderers.Add(labelRenderer);
                    }
                }
            }

            runtimeCamera.targetTexture = renderTexture;
            runtimeCamera.Render();
            RenderTexture.active = renderTexture;

            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
            texture.Apply();

            runtimeCamera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            for (int i = 0; i < hiddenLabelRenderers.Count; i++)
            {
                if (hiddenLabelRenderers[i] != null)
                {
                    hiddenLabelRenderers[i].enabled = true;
                }
            }
            Destroy(renderTexture);
            return texture;
        }

        private IEnumerator TeleportToMnemonicRoutine(string word)
        {
            var item = FindItemByWord(word);
            if (item == null || runtimeCamera == null)
            {
                yield break;
            }

            for (var elapsed = 0f; elapsed < 0.18f; elapsed += Time.unscaledDeltaTime)
            {
                teleportOverlayAlpha = Mathf.Lerp(0f, 1f, elapsed / 0.18f);
                yield return null;
            }

            teleportOverlayAlpha = 1f;
            var anchor = RoomSpecCatalog.GetAnchor(item.anchorId);
            var direction = new Vector3(anchor.position.x, 0f, anchor.position.z);
            if (direction.sqrMagnitude < 0.001f)
            {
                direction = Vector3.back;
            }
            else
            {
                direction.Normalize();
            }

            runtimeCamera.transform.position = anchor.position - direction * 1.8f + Vector3.up * 1.1f;
            runtimeCamera.transform.LookAt(anchor.position + anchor.mnemonicOffset * 0.35f);
            cameraYaw = runtimeCamera.transform.eulerAngles.y;
            cameraPitch = runtimeCamera.transform.eulerAngles.x;
            selectedStudyItem = item;

            yield return new WaitForSecondsRealtime(0.08f);

            for (var elapsed = 0f; elapsed < 0.22f; elapsed += Time.unscaledDeltaTime)
            {
                teleportOverlayAlpha = Mathf.Lerp(1f, 0f, elapsed / 0.22f);
                yield return null;
            }

            teleportOverlayAlpha = 0f;
            recognitionFeedback += " The camera has returned to the correct anchor.";
        }

        private void FinalizeAndExport()
        {
            studyDurationSeconds = studyDurationSeconds <= 0f ? Time.unscaledTime - studyStartTime : studyDurationSeconds;
            var export = BuildExportPayload();
            WriteExportFiles(export);
            stage = ExperimentStage.Result;
            statusMessage = "Session finished and exported.";
        }

        private void ScoreRecall()
        {
            foreach (var response in recallResponses)
            {
                response.meaningCorrect = CompareAnswers(response.expectedMeaning, response.answerMeaning);
                response.wordCorrect = CompareAnswers(response.word, response.answerWord);
            }
        }

        private ExperimentSessionExport BuildExportPayload()
        {
            var midCorrect = CountSnapshotResponses("mid", true);
            var midTotal = CountSnapshotResponses("mid", null);
            var finalCorrect = CountSnapshotResponses("final", true);
            var finalTotal = CountSnapshotResponses("final", null);

            var export = new ExperimentSessionExport
            {
                participantId = participantId,
                sessionId = sessionId,
                createdAtUtc = DateTime.UtcNow.ToString("o"),
                wordSetId = activeWordSet.setId,
                wordSetName = activeWordSet.displayName,
                roomId = RoomSpecCatalog.CurrentRoom.roomId,
                roomName = RoomSpecCatalog.RoomName,
                roomGeneratedBy = RoomSpecCatalog.CurrentRoom.generatedBy,
                roomSourcePrompt = RoomSpecCatalog.CurrentRoom.sourcePrompt,
                llmProvider = ResolveProviderLabel(),
                llmModel = condition == ExperimentCondition.SelfGenerated ? "self-authored" : ollamaModel,
                llmStatus = GetLlmStatusText(),
                condition = condition,
                studyDurationSeconds = studyDurationSeconds,
                viewedCount = viewedWords.Count,
                memorizedCount = memorizedWords.Count,
                totalItems = currentItems.Count,
                correctMeaningCount = finalCorrect,
                correctWordCount = finalCorrect,
                midTestCorrectCount = midCorrect,
                midTestTotal = midTotal,
                finalTestCorrectCount = finalCorrect,
                finalTestTotal = finalTotal,
                questionnaire = questionnaire,
                recallResponses = new List<RecallResponse>(recallResponses),
                snapshotTestResponses = new List<SnapshotTestResponse>(snapshotTestResponses),
                interactionLogs = new List<InteractionLog>(interactionLogs)
            };

            foreach (var item in currentItems)
            {
                export.items.Add(new ExportWordEntry
                {
                    word = item.word,
                    meaning = item.meaning,
                    meaningJa = item.meaningJa,
                    anchorId = item.anchorId,
                    cue = item.visualCue,
                    cueJa = item.visualCueJa,
                    mnemonic = item.mnemonic,
                    mnemonicJa = item.mnemonicJa,
                    imagePrompt = item.imagePrompt,
                    imagePromptJa = item.imagePromptJa,
                    imageCuePath = item.imageCuePath,
                    visualObjects = item.visualObjects == null ? new List<VisualObjectSpec>() : new List<VisualObjectSpec>(item.visualObjects)
                });
            }

            return export;
        }

        private void WriteExportFiles(ExperimentSessionExport export)
        {
            var exportFolder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "ExperimentExports"));
            Directory.CreateDirectory(exportFolder);

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            lastJsonExportPath = Path.Combine(exportFolder, $"session_{participantId}_{timestamp}.json");
            lastCsvExportPath = Path.Combine(exportFolder, $"session_{participantId}_{timestamp}.csv");

            export.exportPath = lastJsonExportPath;
            File.WriteAllText(lastJsonExportPath, JsonUtility.ToJson(export, true), Encoding.UTF8);
            File.WriteAllText(lastCsvExportPath, BuildRecallCsv(export), Encoding.UTF8);

            exportMessage = "JSON and CSV were written successfully.";
        }

        private string BuildRecallCsv(ExperimentSessionExport export)
        {
            var builder = new StringBuilder();
            builder.AppendLine("participant_id,session_id,condition,provider,room_id,room_name,phase,target_word,target_anchor_id,option_words,chosen_word,is_correct");

            for (int i = 0; i < export.snapshotTestResponses.Count; i++)
            {
                var response = export.snapshotTestResponses[i];
                builder.Append(QuoteCsv(export.participantId)).Append(',')
                    .Append(QuoteCsv(export.sessionId)).Append(',')
                    .Append(QuoteCsv(export.condition.ToString())).Append(',')
                    .Append(QuoteCsv(export.llmProvider)).Append(',')
                    .Append(QuoteCsv(export.roomId)).Append(',')
                    .Append(QuoteCsv(export.roomName)).Append(',')
                    .Append(QuoteCsv(response.phase)).Append(',')
                    .Append(QuoteCsv(response.targetWord)).Append(',')
                    .Append(QuoteCsv(response.targetAnchorId)).Append(',')
                    .Append(QuoteCsv(string.Join(" | ", response.optionWords))).Append(',')
                    .Append(QuoteCsv(response.chosenWord)).Append(',')
                    .Append(response.isCorrect ? "1" : "0")
                    .AppendLine();
            }

            return builder.ToString();
        }

        private void ResetForNewSession()
        {
            ResetSessionState();
            stage = ExperimentStage.Setup;
            statusMessage = "Ready for a new session.";
            generationError = string.Empty;
            exportMessage = string.Empty;
            MoveCameraToOverview();
        }

        private void ResetSessionState()
        {
            StopAllCoroutines();
            currentItems = new List<MnemonicItemData>();
            recallResponses.Clear();
            snapshotTestResponses.Clear();
            interactionLogs.Clear();
            viewedWords.Clear();
            memorizedWords.Clear();
            recognitionQueue.Clear();
            recognitionOptions.Clear();
            selectedStudyItem = null;
            questionnaire = new QuestionnaireResponse();
            studyDurationSeconds = 0f;
            recognitionIndex = 0;
            isFinalRecognitionPhase = false;
            awaitingRecognitionAdvance = false;
            midTestCompleted = false;
            finalTestCompleted = false;
            isCapturingSnapshot = false;
            usedLiveLlmForCurrentSession = false;
            flashOverlayAlpha = 0f;
            teleportOverlayAlpha = 0f;
            currentRecognitionTargetWord = string.Empty;
            recognitionFeedback = string.Empty;
            imageGenerationStatus = string.Empty;
            lastJsonExportPath = string.Empty;
            lastCsvExportPath = string.Empty;
            generatingImageCueWords.Clear();

            foreach (var snapshot in memorySnapshots.Values)
            {
                if (snapshot != null)
                {
                    Destroy(snapshot);
                }
            }
            memorySnapshots.Clear();

            foreach (var imageCue in mnemonicImageCues.Values)
            {
                if (imageCue != null)
                {
                    Destroy(imageCue);
                }
            }
            mnemonicImageCues.Clear();

            if (roomRoot != null)
            {
                Destroy(roomRoot.gameObject);
                roomRoot = null;
            }
        }

        private void ClearStudyRoom()
        {
            ClearVrStudyRuntime();

            if (roomRoot == null)
            {
                return;
            }

            Destroy(roomRoot.gameObject);
            roomRoot = null;
        }

        private void ClearVrStudyRuntime()
        {
            if (runtimeCamera != null && vrRigRoot != null && runtimeCamera.transform.IsChildOf(vrRigRoot))
            {
                runtimeCamera.transform.SetParent(null, true);
            }

            if (vrRigRoot != null)
            {
                Destroy(vrRigRoot.gameObject);
                vrRigRoot = null;
            }

            if (vrWorldUiRoot != null)
            {
                Destroy(vrWorldUiRoot.gameObject);
                vrWorldUiRoot = null;
            }

            vrPointerLine = null;
            vrPointerReticle = null;
            vrProgressText = null;
            vrTitleText = null;
            vrMeaningText = null;
            vrAnchorText = null;
            vrCueText = null;
            vrStoryText = null;
            vrActionText = null;
            vrHeadTrackingActive = false;
        }

        private bool PrepareWordSetFromSetup()
        {
            if (useCustomCsv)
            {
                var parsedWords = ParseCsvWords(customCsvText);
                if (parsedWords.Count == 0)
                {
                    statusMessage = "The custom CSV could not be parsed. Please provide at least one valid word,meaning row.";
                    return false;
                }

                if (parsedWords.Count > RoomSpecCatalog.AnchorCount)
                {
                    parsedWords.RemoveRange(RoomSpecCatalog.AnchorCount, parsedWords.Count - RoomSpecCatalog.AnchorCount);
                    statusMessage = $"Custom CSV was trimmed to {RoomSpecCatalog.AnchorCount} words to fit the room anchors.";
                }

                activeWordSet = new WordSetDefinition
                {
                    setId = "custom",
                    displayName = "Custom CSV Set",
                    description = "User-supplied material",
                    words = parsedWords
                };
                return true;
            }

            if (usingRandomAdvancedWordSet && activeWordSet != null && activeWordSet.words.Count > 0)
            {
                activeWordSet = CloneWordSet(activeWordSet);
                return true;
            }

            var selectableSets = GetSelectableWordSets();
            if (selectableSets.Count == 0)
            {
                statusMessage = "No word sets were loaded.";
                return false;
            }

            SyncWordSetSelection();
            activeWordSet = CloneWordSet(selectableSets[selectedWordSetIndex]);
            return true;
        }

        private void SyncWordSetSelection()
        {
            var selectableSets = GetSelectableWordSets();
            if (selectableSets.Count == 0)
            {
                activeWordSet = new WordSetDefinition
                {
                    setId = "empty",
                    displayName = "No Word Sets",
                    description = "No word data loaded."
                };
                return;
            }

            selectedWordSetIndex = Mathf.Clamp(selectedWordSetIndex, 0, selectableSets.Count - 1);
            activeWordSet = CloneWordSet(selectableSets[selectedWordSetIndex]);
        }

        private List<WordSetDefinition> GetSelectableWordSets()
        {
            var selectableSets = new List<WordSetDefinition>();
            for (int i = 0; i < library.wordSets.Count; i++)
            {
                if (!string.Equals(library.wordSets[i].setId, AdvancedPoolSetId, StringComparison.OrdinalIgnoreCase))
                {
                    selectableSets.Add(library.wordSets[i]);
                }
            }

            return selectableSets;
        }

        private WordSetDefinition GetAdvancedWordPool()
        {
            for (int i = 0; i < library.wordSets.Count; i++)
            {
                if (string.Equals(library.wordSets[i].setId, AdvancedPoolSetId, StringComparison.OrdinalIgnoreCase))
                {
                    return library.wordSets[i];
                }
            }

            return null;
        }

        private void GenerateRandomAdvancedWordSet()
        {
            var pool = GetAdvancedWordPool();
            if (pool == null || pool.words.Count == 0)
            {
                statusMessage = "No Spanish noun pool was loaded.";
                return;
            }

            var shuffledPool = new List<WordEntry>();
            for (int i = 0; i < pool.words.Count; i++)
            {
                shuffledPool.Add(new WordEntry
                {
                    word = pool.words[i].word,
                    meaning = pool.words[i].meaning,
                    meaningJa = pool.words[i].meaningJa
                });
            }

            ShuffleList(shuffledPool);

            var sampleCount = Mathf.Min(RandomAdvancedWordCount, RoomSpecCatalog.AnchorCount, shuffledPool.Count);
            var sampledWords = new List<WordEntry>();
            for (int i = 0; i < sampleCount; i++)
            {
                sampledWords.Add(shuffledPool[i]);
            }

            activeWordSet = new WordSetDefinition
            {
                setId = "advanced_random",
                displayName = $"Random Spanish Nouns ({sampleCount})",
                description = "Randomly sampled Spanish nouns for testing how well room anchors and generated scenes support concrete mnemonic imagery.",
                words = sampledWords
            };

            usingRandomAdvancedWordSet = true;
            useCustomCsv = false;
            customCsvText = BuildCsvText(activeWordSet);
            statusMessage = "Random Spanish noun sample prepared.";
        }

        private WordSetDefinition CloneWordSet(WordSetDefinition source)
        {
            var clone = new WordSetDefinition
            {
                setId = source.setId,
                displayName = source.displayName,
                description = source.description
            };

            foreach (var word in source.words)
            {
                clone.words.Add(new WordEntry
                {
                    word = word.word,
                    meaning = word.meaning,
                    meaningJa = word.meaningJa
                });
            }

            return clone;
        }

        private List<WordEntry> ParseCsvWords(string csv)
        {
            var words = new List<WordEntry>();
            var lines = csv.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (line.StartsWith("word", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var parts = line.Split(',');
                if (parts.Length < 2)
                {
                    continue;
                }

                words.Add(new WordEntry
                {
                    word = parts[0].Trim(),
                    meaning = parts[1].Trim(),
                    meaningJa = parts.Length >= 3 ? parts[2].Trim() : string.Empty
                });
            }

            return words;
        }

        private string[] BuildWordSetNames()
        {
            var selectableSets = GetSelectableWordSets();
            if (selectableSets.Count == 0)
            {
                return Array.Empty<string>();
            }

            var names = new string[selectableSets.Count];
            for (int i = 0; i < selectableSets.Count; i++)
            {
                names[i] = selectableSets[i].displayName;
            }

            return names;
        }

        private string[] BuildFurnitureTemplateNames()
        {
            var names = new string[FurnitureTemplates.Length];
            for (int i = 0; i < FurnitureTemplates.Length; i++)
            {
                names[i] = FurnitureTemplates[i].Label;
            }

            return names;
        }

        private string GetDefaultRoomDescription(int layoutIndex)
        {
            var safeIndex = Mathf.Clamp(layoutIndex, 0, RoomLayoutDefaultDescriptions.Length - 1);
            return RoomLayoutDefaultDescriptions[safeIndex];
        }

        private string BuildCsvText(WordSetDefinition wordSet)
        {
            var builder = new StringBuilder();
            builder.AppendLine("word,meaning,meaning_ja");
            foreach (var word in wordSet.words)
            {
                builder.Append(word.word).Append(',')
                    .Append(word.meaning).Append(',')
                    .Append(word.meaningJa)
                    .AppendLine();
            }

            return builder.ToString();
        }

        private string BuildWordListSummary(List<WordEntry> words)
        {
            if (words == null || words.Count == 0)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            for (int i = 0; i < words.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(words[i].word);
            }

            return builder.ToString();
        }

        private List<MnemonicItemData> BuildBlankSelfDrafts(List<WordEntry> words)
        {
            var items = new List<MnemonicItemData>();
            for (int i = 0; i < words.Count; i++)
            {
                var anchor = RoomSpecCatalog.GetAssignmentAnchor(i, words.Count);
                items.Add(new MnemonicItemData
                {
                    word = words[i].word,
                    meaning = words[i].meaning,
                    meaningJa = words[i].meaningJa,
                    anchorId = anchor.id,
                    anchorLabel = anchor.label,
                    visualCue = string.Empty,
                    visualCueJa = string.Empty,
                    mnemonic = string.Empty,
                    mnemonicJa = string.Empty,
                    imagePrompt = string.Empty,
                    imagePromptJa = string.Empty,
                    objectShape = PickShape(i),
                    colorHex = PickColor(i),
                    visualObjects = BuildDefaultVisualObjects(i)
                });
            }

            return items;
        }

        private static List<VisualObjectSpec> BuildDefaultVisualObjects(int index)
        {
            return new List<VisualObjectSpec>
            {
                new()
                {
                    label = "memory cue",
                    primitiveShape = PickShape(index),
                    colorHex = PickColor(index),
                    localPosition = new Vector3(0f, 0.25f, 0f),
                    scale = Vector3.one * 0.26f,
                    effect = "glow"
                }
            };
        }

        private void AutoFillSelfDrafts()
        {
            var autoFilled = MockMnemonicGenerator.GenerateFallback(activeWordSet.words);
            for (int i = 0; i < currentItems.Count && i < autoFilled.Count; i++)
            {
                currentItems[i].visualCue = autoFilled[i].visualCue;
                currentItems[i].visualCueJa = autoFilled[i].visualCueJa;
                currentItems[i].mnemonic = autoFilled[i].mnemonic;
                currentItems[i].mnemonicJa = autoFilled[i].mnemonicJa;
            }

            statusMessage = "Starter drafts were added. You can now edit them.";
        }

        private void FinalizeSelfDrafts()
        {
            foreach (var item in currentItems)
            {
                if (string.IsNullOrWhiteSpace(item.visualCue))
                {
                    item.visualCue = $"At the {item.anchorLabel}, imagine a memorable scene that captures '{item.meaning}'.";
                }

                if (string.IsNullOrWhiteSpace(item.visualCueJa))
                {
                    item.visualCueJa = $"At {item.anchorLabel}, imagine a memorable scene for {(string.IsNullOrWhiteSpace(item.meaningJa) ? item.meaning : item.meaningJa)}.";
                    /*
                    item.visualCueJa = $"{item.anchorLabel} で、「{(string.IsNullOrWhiteSpace(item.meaningJa) ? item.meaning : item.meaningJa)}」を表す印象的な場面を想像する。";
                    */
                }

                if (string.IsNullOrWhiteSpace(item.mnemonic))
                {
                    item.mnemonic = $"Use the sound or meaning of '{item.word}' to connect it with the {item.anchorLabel}.";
                }

                if (string.IsNullOrWhiteSpace(item.mnemonicJa))
                {
                    item.mnemonicJa = $"Connect {item.word} with {item.anchorLabel} through sound or meaning.";
                    /*
                    item.mnemonicJa = $"「{item.word}」の音や意味を {item.anchorLabel} と結びつけて覚える。";
                    */
                }

                ApplyAnchorConsistency(item);
            }
        }

        private List<MnemonicItemData> EnsureMnemonicDefaults(List<MnemonicItemData> items)
        {
            for (int i = 0; i < items.Count; i++)
            {
                var anchor = RoomSpecCatalog.TryGetAnchor(items[i].anchorId, out var existingAnchor)
                    ? existingAnchor
                    : RoomSpecCatalog.GetAssignmentAnchor(i, items.Count);
                items[i].anchorId = anchor.id;
                items[i].anchorLabel = anchor.label;

                if (string.IsNullOrWhiteSpace(items[i].objectShape))
                {
                    items[i].objectShape = PickShape(i);
                }

                if (string.IsNullOrWhiteSpace(items[i].colorHex))
                {
                    items[i].colorHex = PickColor(i);
                }

                if (condition == ExperimentCondition.SelfGenerated
                    && (items[i].visualObjects == null || items[i].visualObjects.Count == 0))
                {
                    items[i].visualObjects = BuildDefaultVisualObjects(i);
                }

                if (string.IsNullOrWhiteSpace(items[i].visualCueJa))
                {
                    items[i].visualCueJa = items[i].visualCue;
                }

                if (string.IsNullOrWhiteSpace(items[i].mnemonicJa))
                {
                    items[i].mnemonicJa = items[i].mnemonic;
                }

                if (string.IsNullOrWhiteSpace(items[i].imagePrompt))
                {
                    items[i].imagePrompt = BuildMnemonicImagePrompt(items[i]);
                }

                if (string.IsNullOrWhiteSpace(items[i].imagePromptJa))
                {
                    items[i].imagePromptJa = items[i].imagePrompt;
                }

                ApplyAnchorConsistency(items[i]);
            }

            RepairDuplicateMnemonicCues(items);
            return items;
        }

        private void RepairDuplicateMnemonicCues(List<MnemonicItemData> items)
        {
            if (items == null || items.Count <= 1)
            {
                return;
            }

            var seenSignatures = new HashSet<string>();
            for (int i = 0; i < items.Count; i++)
            {
                var signature = BuildMnemonicCueSignature(items[i]);
                if (string.IsNullOrWhiteSpace(signature))
                {
                    continue;
                }

                if (seenSignatures.Contains(signature))
                {
                    if (ApplyDistinctFallbackMnemonic(items[i]))
                    {
                        ApplyAnchorConsistency(items[i]);
                        items[i].imageCuePath = string.Empty;
                        var repairedSignature = BuildMnemonicCueSignature(items[i]);
                        if (!string.IsNullOrWhiteSpace(repairedSignature))
                        {
                            seenSignatures.Add(repairedSignature);
                        }
                    }
                }
                else
                {
                    seenSignatures.Add(signature);
                }
            }
        }

        private string BuildMnemonicCueSignature(MnemonicItemData item)
        {
            if (item == null)
            {
                return string.Empty;
            }

            var text = ((item.visualCue ?? string.Empty) + " " + (item.mnemonic ?? string.Empty) + " " + (item.imagePrompt ?? string.Empty)).ToLowerInvariant();
            if (ContainsAny(text, "paper corner", "pinned paper", "unmoving paper", "paper stays pinned"))
            {
                return "paper-pinned";
            }

            if (ContainsAny(text, "red dot", "flicker", "flickers", "blinking dot"))
            {
                return "flickering-dot";
            }

            if (ContainsAny(text, "statue", "idol") && ContainsAny(text, "hammer", "chip", "crack", "smash"))
            {
                return "hammered-idol";
            }

            if (ContainsAny(text, "crystal", "sealed", "resin"))
            {
                return "sealed-object";
            }

            return string.Empty;
        }

        private bool ApplyDistinctFallbackMnemonic(MnemonicItemData item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.word))
            {
                return false;
            }

            var anchor = GetAnchorDisplayName(item);
            var word = item.word.Trim().ToLowerInvariant();
            switch (word)
            {
                case "immutable":
                    item.visualCue = $"At the {anchor}, a clear resin cube seals a metal gear that cannot turn or change.";
                    item.visualCueJa = item.visualCue;
                    item.mnemonic = "The sealed gear cannot move or alter shape, cueing something unchangeable.";
                    item.mnemonicJa = item.mnemonic;
                    item.imagePrompt = $"Close-up at the {anchor}: a transparent resin cube seals a small metal gear, with the gear visibly trapped and unable to turn. Keep the {anchor} only as background context, no text, no letters, no captions, no logos, no watermark.";
                    item.imagePromptJa = item.imagePrompt;
                    return true;

                case "intransigent":
                    item.visualCue = $"At the {anchor}, two puzzle pieces meet while a tiny steel wedge refuses to slide into place.";
                    item.visualCueJa = item.visualCue;
                    item.mnemonic = "The stuck wedge refuses to fit with the other piece, cueing refusal to compromise.";
                    item.mnemonicJa = item.mnemonic;
                    item.imagePrompt = $"Close-up at the {anchor}: two puzzle pieces almost connect, but a tiny steel wedge blocks the join and refuses to slide into place. Keep the {anchor} only as background context, no text, no letters, no captions, no logos, no watermark.";
                    item.imagePromptJa = item.imagePrompt;
                    return true;

                case "capricious":
                    item.visualCue = $"At the {anchor}, a small puppet face snaps from laughing to crying to angry without warning.";
                    item.visualCueJa = item.visualCue;
                    item.mnemonic = "The puppet's sudden emotional flips cue capricious changes in mood or behavior.";
                    item.mnemonicJa = item.mnemonic;
                    item.imagePrompt = $"Close-up at the {anchor}: a small puppet face flips from laughing to crying to angry, dominating the foreground. Keep the {anchor} only as background context, no text, no letters, no captions, no logos, no watermark.";
                    item.imagePromptJa = item.imagePrompt;
                    return true;

                default:
                    return false;
            }
        }

        private void CycleAnchor(MnemonicItemData item, int direction)
        {
            var currentIndex = 0;
            for (int i = 0; i < RoomSpecCatalog.AnchorCount; i++)
            {
                if (RoomSpecCatalog.Anchors[i].id == item.anchorId)
                {
                    currentIndex = i;
                    break;
                }
            }

            var nextIndex = (currentIndex + direction + RoomSpecCatalog.AnchorCount) % RoomSpecCatalog.AnchorCount;
            var nextAnchor = RoomSpecCatalog.Anchors[nextIndex];
            item.anchorId = nextAnchor.id;
            item.anchorLabel = nextAnchor.label;
            ApplyAnchorConsistency(item);
            ClearGeneratedImageCueForWord(item.word);
        }

        private int ReassignCurrentItemsToAnchors()
        {
            if (currentItems == null || currentItems.Count == 0 || RoomSpecCatalog.AnchorCount == 0)
            {
                return 0;
            }

            var changed = 0;
            for (int i = 0; i < currentItems.Count; i++)
            {
                var anchor = RoomSpecCatalog.GetAssignmentAnchor(i, currentItems.Count);
                if (!string.Equals(currentItems[i].anchorId, anchor.id, StringComparison.Ordinal)
                    || !string.Equals(currentItems[i].anchorLabel, anchor.label, StringComparison.Ordinal))
                {
                    changed++;
                }

                currentItems[i].anchorId = anchor.id;
                currentItems[i].anchorLabel = anchor.label;
                ApplyAnchorConsistency(currentItems[i]);
            }

            if (changed > 0)
            {
                ResetAnchorDependentProgress();
            }

            return changed;
        }

        private void ResetAnchorDependentProgress()
        {
            selectedStudyItem = null;
            viewedWords.Clear();
            memorizedWords.Clear();
            foreach (var snapshot in memorySnapshots.Values)
            {
                if (snapshot != null)
                {
                    Destroy(snapshot);
                }
            }
            memorySnapshots.Clear();
            foreach (var imageCue in mnemonicImageCues.Values)
            {
                if (imageCue != null)
                {
                    Destroy(imageCue);
                }
            }
            mnemonicImageCues.Clear();
            for (int i = 0; i < currentItems.Count; i++)
            {
                currentItems[i].imageCuePath = string.Empty;
            }
            recognitionQueue.Clear();
            recognitionOptions.Clear();
            currentRecognitionTargetWord = string.Empty;
            recognitionFeedback = string.Empty;
            recognitionIndex = 0;
            awaitingRecognitionAdvance = false;
            midTestCompleted = false;
            finalTestCompleted = false;
            isFinalRecognitionPhase = false;
        }

        private void ClearGeneratedImageCueForWord(string word)
        {
            if (string.IsNullOrWhiteSpace(word))
            {
                return;
            }

            if (mnemonicImageCues.TryGetValue(word, out var imageCue) && imageCue != null)
            {
                Destroy(imageCue);
            }
            mnemonicImageCues.Remove(word);

            var item = FindItemByWord(word);
            if (item != null)
            {
                item.imageCuePath = string.Empty;
            }
        }

        private void BuildRoomBuilderPreview()
        {
            if (roomRoot != null)
            {
                Destroy(roomRoot.gameObject);
            }

            roomRoot = new GameObject("RoomBuilderRuntime").transform;
            var roomSpec = RoomSpecCatalog.CurrentRoom;

            for (int i = 0; i < roomSpec.environmentPrimitives.Count; i++)
            {
                if (ShouldHidePrimitiveInBuilderPreview(roomSpec.environmentPrimitives[i])
                    || ShouldTemporarilyHidePrimitiveForFloorPlacement(roomSpec.environmentPrimitives[i], i))
                {
                    continue;
                }

                var primitiveObject = CreateEnvironmentPrimitive(roomSpec.environmentPrimitives[i], roomRoot);
                if (primitiveObject != null)
                {
                    var interactable = primitiveObject.AddComponent<RoomPrimitiveInteractable>();
                    interactable.Index = i;
                }
            }

            for (int i = 0; i < roomSpec.anchors.Count; i++)
            {
                CreateEditableAnchorPrimitive(roomSpec.anchors[i], i, roomRoot);
            }

            CreateSelectedRoomPrimitiveOverlay(roomRoot);
            CreateGuidedFurniturePlacementPreviews(roomRoot);
            CreateShellWallDrawPreview(roomRoot);
            CreateFloorPatchDropPreview(roomRoot);
            CreateWallSegmentDropPreview(roomRoot);
        }

        private bool ShouldHidePrimitiveInBuilderPreview(RoomPrimitiveDefinition primitive)
        {
            if (primitive == null)
            {
                return false;
            }

            var text = ((primitive.id ?? string.Empty) + " " + (primitive.label ?? string.Empty)).ToLowerInvariant();
            if (ContainsAny(text, "ceiling", "roof", "top cover", "lid"))
            {
                return true;
            }

            // Some generated room specs name the cover poorly, so also catch very wide,
            // thin slabs that sit above normal wall height.
            return primitive.position.y > 2.15f
                && primitive.scale.y <= 0.22f
                && primitive.scale.x >= 3.5f
                && primitive.scale.z >= 3.5f;
        }

        private bool ShouldTemporarilyHidePrimitiveForFloorPlacement(RoomPrimitiveDefinition primitive, int primitiveIndex)
        {
            if (!IsFloorPatchPlacementVisualMode() || primitive == null || primitiveIndex == activeFloorPatchIndex)
            {
                return false;
            }

            if (IsRoomPrimitiveFloorLike(primitiveIndex))
            {
                return false;
            }

            var text = ((primitive.id ?? string.Empty) + " " + (primitive.label ?? string.Empty)).ToLowerInvariant();
            return IsWallLikeRoomPrimitive(primitive)
                || ContainsAny(text, "wall", "partition", "divider", "baseboard", "trim", "cap", "window", "door", "bathroom");
        }

        private void CreateGuidedFurniturePlacementPreviews(Transform parent)
        {
            if (stage != ExperimentStage.RoomBuilder
                || builderWizardStep != BuilderWizardStep.CoreFurniture
                || isGeneratingGuidedFurnitureLayout
                || parent == null)
            {
                return;
            }

            GetCurrentRoomFootprint(out var roomWidth, out var roomDepth);
            var previewRoot = new GameObject("GuidedDashedPlacementPreviews").transform;
            previewRoot.SetParent(parent);

            var zoneCounts = new int[GuidedFurnitureZones.Length];
            for (int i = 0; i < GuidedFurnitureLabels.Length; i++)
            {
                if (!guidedFurnitureIncluded[i])
                {
                    continue;
                }

                var label = GuidedFurnitureLabels[i];
                if (string.Equals(label, "Toilet", StringComparison.OrdinalIgnoreCase) && (!guidedHasBathroom || !guidedHasToilet))
                {
                    continue;
                }

                var zoneIndex = Mathf.Clamp(guidedFurnitureZoneIndexes[i], 0, GuidedFurnitureZones.Length - 1);
                var ordinal = zoneCounts[zoneIndex]++;
                var template = BuildCustomFurnitureTemplate(label);
                var position = GetGuidedFurniturePlacement(label, zoneIndex, ordinal, roomWidth, roomDepth, template, out var rotationEuler);
                var previewAnchor = CreatePreviewAnchor(template, position, rotationEuler);
                ConstrainAnchorPlacement(previewAnchor, -100 - i, roomWidth, roomDepth);

                var ghostRoot = new GameObject($"DashedPreview_{previewAnchor.id}").transform;
                ghostRoot.SetParent(previewRoot);
                ghostRoot.position = previewAnchor.position;
                ghostRoot.rotation = Quaternion.Euler(previewAnchor.rotationEuler);

                CreatePlacementGhost(previewAnchor, ghostRoot, new Color(0.32f, 0.88f, 1.0f, 0.5f));
                CreateWorldLabel(
                    "Preview: " + previewAnchor.label,
                    previewAnchor.position + Vector3.up * Mathf.Max(0.85f, previewAnchor.labelHeight + 0.22f),
                    0.026f,
                    new Color(0.78f, 0.96f, 1.0f, 0.78f),
                    previewRoot);
            }
        }

        private AnchorDefinition CreatePreviewAnchor(FurnitureTemplate template, Vector3 position, Vector3 rotationEuler)
        {
            return new AnchorDefinition
            {
                id = "preview_" + SanitizeIdPrefix(template.IdPrefix),
                label = template.Label,
                primitiveShape = template.Shape,
                colorHex = template.ColorHex,
                position = position,
                scale = template.Scale,
                rotationEuler = rotationEuler,
                mnemonicOffset = new Vector3(0f, template.MnemonicYOffset, 0f),
                labelHeight = template.LabelHeight,
                modelParts = CloneVisualObjectSpecs(template.ModelParts)
            };
        }

        private void CreateSelectedRoomPrimitiveOverlay(Transform parent)
        {
            if (!TryGetSelectedRoomPrimitive(out var primitive) || parent == null)
            {
                return;
            }

            var overlayRoot = new GameObject($"ShellEditorOverlay_{primitive.id}").transform;
            overlayRoot.SetParent(parent);
            overlayRoot.position = primitive.position;
            overlayRoot.rotation = Quaternion.Euler(primitive.rotationEuler);

            var proxyAnchor = BuildRoomPrimitiveProxyAnchor(primitive);
            CreatePlacementGhost(proxyAnchor, overlayRoot, new Color(0.2f, 0.95f, 1.0f, 0.42f));
            CreateSelectionRing(proxyAnchor, overlayRoot);
            CreateBuilderGizmo(proxyAnchor, overlayRoot);
            CreateWorldLabel("Shell: " + primitive.label, primitive.position + Vector3.up * Mathf.Max(0.65f, primitive.scale.y + 0.35f), 0.027f, new Color(0.78f, 0.96f, 1f, 0.85f), parent);
        }

        private void CreateShellWallDrawPreview(Transform parent)
        {
            if (!isDrawingShellWall || !hasShellWallStart || parent == null)
            {
                return;
            }

            var delta = shellWallPreviewPoint - shellWallStartPoint;
            delta.y = 0f;
            if (delta.magnitude < 0.18f)
            {
                return;
            }

            var preview = BuildWallSegmentPrimitive("Wall Draw Preview", shellWallStartPoint, shellWallPreviewPoint, "#7EDBFF");
            var go = CreateEnvironmentPrimitive(preview, parent);
            if (go == null)
            {
                return;
            }

            var collider = go.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }

            var renderer = go.GetComponent<Renderer>();
            if (renderer != null)
            {
                ApplyPrimitiveMaterial(renderer, new Color(0.44f, 0.9f, 1f, 0.36f));
            }

            CreateWorldLabel("Wall preview", preview.position + Vector3.up * 1.1f, 0.026f, new Color(0.78f, 0.96f, 1f, 0.8f), parent);
        }

        private void CreateFloorPatchDropPreview(Transform parent)
        {
            if (!hasFloorPatchDropPreview
                || activeFloorPatchIndex < 0
                || parent == null
                || activeFloorPatchIndex >= RoomSpecCatalog.CurrentRoom.environmentPrimitives.Count)
            {
                return;
            }

            var patch = RoomSpecCatalog.CurrentRoom.environmentPrimitives[activeFloorPatchIndex];
            if (patch == null)
            {
                return;
            }

            var previewRoot = new GameObject($"FloorPatchSnapPreview_{patch.id}").transform;
            previewRoot.SetParent(parent);
            previewRoot.position = floorPatchDropPreviewPosition + Vector3.up * 0.045f;
            previewRoot.rotation = Quaternion.Euler(patch.rotationEuler);

            var color = new Color(0.28f, 0.9f, 1f, 0.46f);
            CreateFloorPatchGhost(previewRoot, patch.scale, color);
            CreateWorldLabel("Snap preview", floorPatchDropPreviewPosition + Vector3.up * 0.24f, 0.026f, new Color(0.7f, 0.96f, 1f, 0.82f), parent);
        }

        private void CreateWallSegmentDropPreview(Transform parent)
        {
            if (!hasWallSegmentDropPreview
                || activeWallSegmentIndex < 0
                || parent == null
                || activeWallSegmentIndex >= RoomSpecCatalog.CurrentRoom.environmentPrimitives.Count)
            {
                return;
            }

            var wall = RoomSpecCatalog.CurrentRoom.environmentPrimitives[activeWallSegmentIndex];
            if (wall == null)
            {
                return;
            }

            var preview = new RoomPrimitiveDefinition
            {
                id = "wall_snap_preview_" + wall.id,
                label = "Wall Snap Preview",
                primitiveShape = "Cube",
                colorHex = "#7EDBFF",
                position = wallSegmentDropPreviewPosition,
                rotationEuler = wallSegmentDropPreviewRotation,
                scale = wallSegmentDropPreviewScale,
                showLabel = false,
                labelHeight = 1.2f
            };

            var go = CreateEnvironmentPrimitive(preview, parent);
            if (go != null)
            {
                var collider = go.GetComponent<Collider>();
                if (collider != null)
                {
                    Destroy(collider);
                }

                var renderer = go.GetComponent<Renderer>();
                if (renderer != null)
                {
                    ApplyPrimitiveMaterial(renderer, new Color(0.28f, 0.9f, 1f, 0.28f));
                }
            }

            var ghostRoot = new GameObject($"WallSegmentSnapGhost_{wall.id}").transform;
            ghostRoot.SetParent(parent);
            ghostRoot.position = new Vector3(wallSegmentDropPreviewPosition.x, 0.055f, wallSegmentDropPreviewPosition.z);
            ghostRoot.rotation = Quaternion.Euler(wallSegmentDropPreviewRotation);
            CreateWallSegmentGhost(ghostRoot, wallSegmentDropPreviewScale, new Color(0.28f, 0.9f, 1f, 0.62f));
            CreateWorldLabel("Wall snap preview", wallSegmentDropPreviewPosition + Vector3.up * (wallSegmentDropPreviewScale.y + 0.18f), 0.026f, new Color(0.7f, 0.96f, 1f, 0.84f), parent);
        }

        private void CreateFloorPatchGhost(Transform parent, Vector3 patchScale, Color color)
        {
            var width = Mathf.Max(0.4f, patchScale.x);
            var depth = Mathf.Max(0.4f, patchScale.z);
            var line = 0.035f;
            var y = 0f;

            const int segmentsPerEdge = 5;
            for (int i = 0; i < segmentsPerEdge; i++)
            {
                if (i % 2 != 0)
                {
                    continue;
                }

                var t = (i + 0.5f) / segmentsPerEdge - 0.5f;
                CreateGhostPart(parent, "FloorPreviewFront", new Vector3(t * width, y, -depth * 0.5f), new Vector3(width / segmentsPerEdge * 0.72f, line, line), color);
                CreateGhostPart(parent, "FloorPreviewBack", new Vector3(t * width, y, depth * 0.5f), new Vector3(width / segmentsPerEdge * 0.72f, line, line), color);
                CreateGhostPart(parent, "FloorPreviewLeft", new Vector3(-width * 0.5f, y, t * depth), new Vector3(line, line, depth / segmentsPerEdge * 0.72f), color);
                CreateGhostPart(parent, "FloorPreviewRight", new Vector3(width * 0.5f, y, t * depth), new Vector3(line, line, depth / segmentsPerEdge * 0.72f), color);
            }

            var fill = CreatePrimitiveLocal(
                "FloorPreviewFill",
                PrimitiveType.Cube,
                new Vector3(0f, -0.02f, 0f),
                new Vector3(width, 0.018f, depth),
                new Color(color.r, color.g, color.b, 0.16f),
                parent);
            var collider = fill.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }
        }

        private void CreateWallSegmentGhost(Transform parent, Vector3 wallScale, Color color)
        {
            var width = Mathf.Max(0.12f, wallScale.x);
            var depth = Mathf.Max(0.12f, wallScale.z);
            var longAxisIsX = width >= depth;
            var length = Mathf.Max(width, depth);
            var thickness = Mathf.Max(0.06f, Mathf.Min(width, depth));
            var dashCount = Mathf.Clamp(Mathf.RoundToInt(length / 0.42f), 3, 12);

            for (int i = 0; i < dashCount; i++)
            {
                if (i % 2 != 0)
                {
                    continue;
                }

                var t = (i + 0.5f) / dashCount - 0.5f;
                var position = longAxisIsX
                    ? new Vector3(t * length, 0f, 0f)
                    : new Vector3(0f, 0f, t * length);
                var scale = longAxisIsX
                    ? new Vector3(length / dashCount * 0.7f, 0.04f, thickness + 0.06f)
                    : new Vector3(thickness + 0.06f, 0.04f, length / dashCount * 0.7f);
                CreateGhostPart(parent, "WallPreviewDash", position, scale, color);
            }
        }

        private AnchorDefinition BuildRoomPrimitiveProxyAnchor(RoomPrimitiveDefinition primitive)
        {
            return new AnchorDefinition
            {
                id = primitive.id,
                label = primitive.label,
                primitiveShape = primitive.primitiveShape,
                colorHex = primitive.colorHex,
                position = primitive.position,
                scale = primitive.scale,
                rotationEuler = primitive.rotationEuler,
                mnemonicOffset = Vector3.up,
                labelHeight = Mathf.Max(0.5f, primitive.scale.y + 0.25f)
            };
        }

        private void CreateEditableAnchorPrimitive(AnchorDefinition anchor, int index, Transform parent)
        {
            var root = CreateFurnitureModel(anchor, parent, index == selectedBuilderAnchorIndex, true, index);
            if (index == selectedBuilderAnchorIndex)
            {
                var overlayRoot = new GameObject($"EditorOverlay_{anchor.id}").transform;
                overlayRoot.SetParent(parent);
                overlayRoot.position = anchor.position;
                overlayRoot.rotation = Quaternion.Euler(anchor.rotationEuler);
                CreateWorldLabel(anchor.label, anchor.position + Vector3.up * (anchor.labelHeight + 0.2f), 0.035f, Color.white, parent);
                CreatePlacementGhost(anchor, overlayRoot);
                CreateSelectionRing(anchor, overlayRoot);
                CreateBuilderGizmo(anchor, overlayRoot);
                CreateBuilderFeedbackPulse(index, anchor, overlayRoot);
            }
        }

        private void AddFurnitureAnchor(FurnitureTemplate template)
        {
            var position = GetCameraPlacementPosition(template.DefaultY);
            var anchor = new AnchorDefinition
            {
                id = BuildUniqueAnchorId(template.IdPrefix),
                label = template.Label,
                primitiveShape = template.Shape,
                colorHex = template.ColorHex,
                position = position,
                scale = template.Scale,
                rotationEuler = Vector3.zero,
                mnemonicOffset = new Vector3(0f, template.MnemonicYOffset, 0f),
                labelHeight = template.LabelHeight,
                modelParts = CloneVisualObjectSpecs(template.ModelParts)
            };

            RoomSpecCatalog.CurrentRoom.anchors.Add(anchor);
            selectedBuilderAnchorIndex = RoomSpecCatalog.CurrentRoom.anchors.Count - 1;
            GetCurrentRoomFootprint(out var roomWidth, out var roomDepth);
            ConstrainAnchorPlacement(anchor, selectedBuilderAnchorIndex, roomWidth, roomDepth);
            ResolveCurrentAnchorOverlaps(roomWidth, roomDepth);
            var reassigned = ReassignCurrentItemsToAnchors();
            BuildRoomBuilderPreview();
            statusMessage = reassigned > 0
                ? $"Added {template.Label} as a new anchor and redistributed {reassigned} word(s). Regenerate mnemonics if you want the text rewritten for the new anchor layout."
                : $"Added {template.Label} as a new anchor. The next word assignment will include the updated anchor list.";
        }

        private void BeginCustomFurnitureGeneration()
        {
            var requestedName = string.IsNullOrWhiteSpace(customFurnitureName) ? "Custom Furniture" : customFurnitureName.Trim();
            isGeneratingFurniture = true;
            statusMessage = $"Calling Ollama to interpret custom furniture: {requestedName}...";
            StartCoroutine(RunCustomFurnitureGeneration(requestedName));
        }

        private IEnumerator RunCustomFurnitureGeneration(string requestedName)
        {
            var service = new OllamaLlmService();
            OllamaLlmService.FurnitureTemplateSuggestion suggestion = null;
            string error = null;

            yield return StartCoroutine(service.GenerateFurnitureTemplate(
                ollamaBaseUrl,
                ollamaModel,
                requestedName,
                result => suggestion = result,
                err => error = err));

            isGeneratingFurniture = false;

            if (suggestion != null && string.IsNullOrWhiteSpace(error))
            {
                AddFurnitureAnchor(BuildCustomFurnitureTemplateFromSuggestion(requestedName, suggestion));
                statusMessage += " LLM furniture interpretation was used.";
                yield break;
            }

            AddFurnitureAnchor(BuildCustomFurnitureTemplate(requestedName));
            statusMessage += $" Ollama furniture interpretation failed, so a local fallback was used: {error}";
        }

        private FurnitureTemplate BuildCustomFurnitureTemplateFromSuggestion(string requestedName, OllamaLlmService.FurnitureTemplateSuggestion suggestion)
        {
            if (suggestion == null)
            {
                return BuildCustomFurnitureTemplate(requestedName);
            }

            var category = string.IsNullOrWhiteSpace(suggestion.category) ? SanitizeIdPrefix(requestedName) : SanitizeIdPrefix(suggestion.category);
            var label = string.IsNullOrWhiteSpace(suggestion.label) ? requestedName : suggestion.label.Trim();

            if (!string.IsNullOrWhiteSpace(suggestion.category)
                && !ContainsAny(label, suggestion.category)
                && (ContainsAny(suggestion.category, "television", "computer", "toilet", "bathtub", "sink", "fridge", "cabinet", "bed", "sofa", "chair", "table", "desk", "counter", "shelf", "plant", "lamp")))
            {
                label = suggestion.label.Trim();
            }

            return new FurnitureTemplate(
                label,
                category,
                string.IsNullOrWhiteSpace(suggestion.primitiveShape) ? "Cube" : suggestion.primitiveShape,
                string.IsNullOrWhiteSpace(suggestion.colorHex) ? "#8B7A65" : suggestion.colorHex,
                suggestion.scale == default ? BuildCustomFurnitureTemplate(requestedName).Scale : suggestion.scale,
                suggestion.defaultY <= 0f ? Mathf.Max(0.15f, suggestion.scale.y * 0.5f) : suggestion.defaultY,
                suggestion.mnemonicYOffset <= 0f ? Mathf.Max(0.45f, suggestion.scale.y * 0.65f + 0.35f) : suggestion.mnemonicYOffset,
                suggestion.parts);
        }

        private FurnitureTemplate BuildCustomFurnitureTemplate(string rawName)
        {
            var label = string.IsNullOrWhiteSpace(rawName) ? "Custom Furniture" : rawName.Trim();
            var lower = label.ToLowerInvariant();

            if (ContainsAny(lower, "toilet", "wc", "\u9a6c\u6876", "\u99ac\u6876", "\u4fbf\u5668", "\u30c8\u30a4\u30ec"))
            {
                return new FurnitureTemplate(label, "toilet", "Cylinder", "#E9ECEF", new Vector3(0.85f, 0.75f, 0.95f), 0.42f, 0.78f);
            }

            if (ContainsAny(lower, "bath", "bathtub", "tub", "\u6d74\u69fd", "\u98a8\u5442"))
            {
                return new FurnitureTemplate(label, "bathtub", "Cube", "#DDE7EF", new Vector3(1.7f, 0.55f, 0.95f), 0.35f, 0.72f);
            }

            if (ContainsAny(lower, "sink", "\u6d17\u9762", "\u6d41\u3057"))
            {
                return new FurnitureTemplate(label, "sink", "Cube", "#C9D6E2", new Vector3(1.0f, 0.7f, 0.75f), 0.58f, 0.82f);
            }

            if (ContainsAny(lower, "fridge", "refrigerator", "\u51b7\u8535", "\u51b0\u7bb1"))
            {
                return new FurnitureTemplate(label, "fridge", "Cube", "#D4D7DD", new Vector3(0.95f, 2.0f, 0.8f), 1.0f, 1.12f);
            }

            if (ContainsAny(lower, "bookcase", "bookshelf", "book shelf"))
            {
                return new FurnitureTemplate(label, "bookshelf", "Cube", "#8B6A3A", new Vector3(1.05f, 2.15f, 0.42f), 1.08f, 1.25f);
            }

            if (ContainsAny(lower, "stove", "cooktop", "range", "hob"))
            {
                return new FurnitureTemplate(label, "stove", "Cube", "#4D525A", new Vector3(1.15f, 0.82f, 0.85f), 0.48f, 0.92f);
            }

            if (ContainsAny(lower, "air conditioner", "aircon", "air conditioning", "ac unit", "a/c"))
            {
                return new FurnitureTemplate(label, "air_conditioner", "Cube", "#E6ECEF", new Vector3(1.35f, 0.42f, 0.22f), 2.25f, 0.55f);
            }

            if (ContainsAny(lower, "television", "tv", "monitor", "screen", "\u30c6\u30ec\u30d3", "\u7535\u89c6", "\u96fb\u8996"))
            {
                return new FurnitureTemplate(label, "television", "Cube", "#252A32", new Vector3(1.45f, 0.9f, 0.22f), 0.75f, 1.02f);
            }

            if (ContainsAny(lower, "computer", "pc", "laptop", "desktop", "keyboard", "comput", "omputer", "macbook", "\u30b3\u30f3\u30d4\u30e5\u30fc\u30bf", "\u7535\u8111", "\u96fb\u8133"))
            {
                return new FurnitureTemplate(label, "computer", "Cube", "#303845", new Vector3(1.15f, 0.85f, 0.65f), 0.55f, 0.95f);
            }

            if (ContainsAny(lower, "door", "\u30c9\u30a2", "\u95e8", "\u9580"))
            {
                return new FurnitureTemplate(label, "door", "Cube", "#7B5032", new Vector3(1.15f, 2.35f, 0.12f), 1.2f, 0.2f);
            }

            if (ContainsAny(lower, "window", "balcony", "\u7a93", "\u7a97", "\u30d9\u30e9\u30f3\u30c0"))
            {
                return new FurnitureTemplate(label, "window", "Cube", "#779CCB", new Vector3(1.7f, 1.15f, 0.1f), 1.7f, 0.55f);
            }

            if (ContainsAny(lower, "wardrobe", "closet", "cabinet", "\u8863\u67dc", "\u30af\u30ed\u30fc\u30bc\u30c3\u30c8"))
            {
                return new FurnitureTemplate(label, "cabinet", "Cube", "#7E6B55", new Vector3(1.2f, 2.0f, 0.55f), 1.0f, 1.15f);
            }

            return new FurnitureTemplate(label, SanitizeIdPrefix(label), "Cube", "#8B7A65", new Vector3(1.0f, 0.85f, 0.8f), 0.55f, 0.82f);
        }

        private List<VisualObjectSpec> CloneVisualObjectSpecs(IEnumerable<VisualObjectSpec> source)
        {
            var result = new List<VisualObjectSpec>();
            if (source == null)
            {
                return result;
            }

            foreach (var item in source)
            {
                if (item == null)
                {
                    continue;
                }

                result.Add(new VisualObjectSpec
                {
                    label = item.label,
                    primitiveShape = item.primitiveShape,
                    colorHex = item.colorHex,
                    localPosition = item.localPosition,
                    scale = item.scale,
                    effect = item.effect
                });
            }

            return result;
        }

        private Vector3 GetCameraPlacementPosition(float defaultY)
        {
            if (runtimeCamera == null)
            {
                return new Vector3(0f, defaultY, 0f);
            }

            var forward = runtimeCamera.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f)
            {
                forward = Vector3.forward;
            }

            var position = runtimeCamera.transform.position + forward.normalized * 2.4f;
            position.x = Mathf.Clamp(position.x, -5.2f, 5.2f);
            position.y = defaultY;
            position.z = Mathf.Clamp(position.z, -5.2f, 5.2f);
            return position;
        }

        private string BuildUniqueAnchorId(string prefix)
        {
            var baseId = string.IsNullOrWhiteSpace(prefix) ? "anchor" : prefix.Trim().ToLowerInvariant();
            var candidate = baseId;
            var index = 1;

            while (AnchorIdExists(candidate))
            {
                index++;
                candidate = $"{baseId}_{index}";
            }

            return candidate;
        }

        private bool AnchorIdExists(string id)
        {
            var anchors = RoomSpecCatalog.CurrentRoom.anchors;
            for (int i = 0; i < anchors.Count; i++)
            {
                if (string.Equals(anchors[i].id, id, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private string BuildUniqueRoomPrimitiveId(string prefix)
        {
            var baseId = string.IsNullOrWhiteSpace(prefix) ? "shell" : SanitizeIdPrefix(prefix);
            var candidate = baseId;
            var index = 1;
            var primitives = RoomSpecCatalog.CurrentRoom.environmentPrimitives;
            while (true)
            {
                var exists = false;
                for (int i = 0; i < primitives.Count; i++)
                {
                    if (string.Equals(primitives[i].id, candidate, StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                {
                    return candidate;
                }

                index++;
                candidate = $"{baseId}_{index}";
            }
        }

        private bool TryGetSelectedRoomPrimitive(out RoomPrimitiveDefinition primitive)
        {
            var primitives = RoomSpecCatalog.CurrentRoom.environmentPrimitives;
            if (selectedRoomPrimitiveIndex < 0 || selectedRoomPrimitiveIndex >= primitives.Count)
            {
                primitive = null;
                return false;
            }

            primitive = primitives[selectedRoomPrimitiveIndex];
            return primitive != null;
        }

        private void AddFloorPatchPrimitive()
        {
            AddRoomShellPrimitive("Floor Patch", "Cube", "#E7D9C1", new Vector3(0f, 0f, 0f), new Vector3(2.2f, 0.08f, 2.2f));
            BeginFloorPatchPlacement(selectedRoomPrimitiveIndex, true);
        }

        private void AddWallSegmentPrimitive()
        {
            AddRoomShellPrimitive("Wall Segment", "Cube", "#F7F4EC", new Vector3(0f, 0.82f, 0f), new Vector3(2.2f, 1.65f, 0.12f));
            BeginWallSegmentPlacement(selectedRoomPrimitiveIndex, true);
        }

        private void AddRoomShellPrimitive(string label, string shape, string colorHex, Vector3 localDefaultPosition, Vector3 scale)
        {
            var position = GetBuilderShellPlacementPosition(localDefaultPosition.y);
            position.y = localDefaultPosition.y;
            var primitive = new RoomPrimitiveDefinition
            {
                id = BuildUniqueRoomPrimitiveId(label),
                label = label,
                primitiveShape = shape,
                colorHex = colorHex,
                position = position,
                scale = scale,
                rotationEuler = Vector3.zero,
                showLabel = false,
                labelHeight = 0.6f
            };

            RoomSpecCatalog.CurrentRoom.environmentPrimitives.Add(primitive);
            selectedRoomPrimitiveIndex = RoomSpecCatalog.CurrentRoom.environmentPrimitives.Count - 1;
            selectedBuilderAnchorIndex = -1;
            BuildRoomBuilderPreview();
            statusMessage = $"Added editable shell piece: {label}. Use W/E/R and drag it like sandbox building.";
        }

        private void BeginFloorPatchPlacement(int primitiveIndex, bool wasNew)
        {
            var primitives = RoomSpecCatalog.CurrentRoom.environmentPrimitives;
            if (primitiveIndex < 0 || primitiveIndex >= primitives.Count || !IsRoomPrimitiveFloorLike(primitiveIndex))
            {
                return;
            }

            activeFloorPatchIndex = primitiveIndex;
            activeFloorPatchWasNew = wasNew;
            isFloorPatchPlacementActive = true;
            selectedRoomPrimitiveIndex = primitiveIndex;
            selectedBuilderAnchorIndex = -1;
            SetBuilderTool(BuilderToolMode.Move);
            var primitive = primitives[primitiveIndex];
            primitive.position = GetSnappedFloorPatchPosition(primitive, primitiveIndex, primitive.position);
            floorPatchDropPreviewPosition = primitive.position;
            hasFloorPatchDropPreview = true;
            BuildRoomBuilderPreview();
            statusMessage = "Floor patch placement: walls are hidden. Drag the patch; the blue dashed shadow shows the snapped drop point.";
        }

        private void BeginWallSegmentPlacement(int primitiveIndex, bool wasNew)
        {
            var primitives = RoomSpecCatalog.CurrentRoom.environmentPrimitives;
            if (primitiveIndex < 0 || primitiveIndex >= primitives.Count || !IsRoomPrimitiveWallBuildPiece(primitiveIndex))
            {
                return;
            }

            activeWallSegmentIndex = primitiveIndex;
            activeWallSegmentWasNew = wasNew;
            isWallSegmentPlacementActive = true;
            selectedRoomPrimitiveIndex = primitiveIndex;
            selectedBuilderAnchorIndex = -1;
            SetBuilderTool(BuilderToolMode.Move);

            var primitive = primitives[primitiveIndex];
            ApplyWallSegmentSnapPreview(primitive, primitiveIndex, primitive.position);
            primitive.position = wallSegmentDropPreviewPosition;
            primitive.rotationEuler = wallSegmentDropPreviewRotation;
            primitive.scale = wallSegmentDropPreviewScale;
            BuildRoomBuilderPreview();
            statusMessage = "Wall placement: drag near an existing floor edge. The blue dashed preview shows where the wall will snap.";
        }

        private void FinishFloorPatchPlacement(string message)
        {
            if (activeFloorPatchIndex >= 0
                && activeFloorPatchIndex < RoomSpecCatalog.CurrentRoom.environmentPrimitives.Count
                && hasFloorPatchDropPreview)
            {
                var primitive = RoomSpecCatalog.CurrentRoom.environmentPrimitives[activeFloorPatchIndex];
                if (primitive != null)
                {
                    primitive.position = floorPatchDropPreviewPosition;
                    primitive.position.y = 0f;
                    primitive.scale = ClampRoomPrimitiveScale(primitive.scale);
                }
            }

            var selectedPrimitive = activeFloorPatchIndex >= 0 && activeFloorPatchIndex < RoomSpecCatalog.CurrentRoom.environmentPrimitives.Count
                ? RoomSpecCatalog.CurrentRoom.environmentPrimitives[activeFloorPatchIndex]
                : null;
            RebuildAutoWallsFromFloorSurfaces();
            if (selectedPrimitive != null)
            {
                selectedRoomPrimitiveIndex = RoomSpecCatalog.CurrentRoom.environmentPrimitives.IndexOf(selectedPrimitive);
            }

            isFloorPatchPlacementActive = false;
            activeFloorPatchIndex = -1;
            activeFloorPatchWasNew = false;
            hasFloorPatchDropPreview = false;
            statusMessage = message;
            BuildRoomBuilderPreview();
        }

        private void RebuildAutoWallsFromFloorSurfaces()
        {
            var room = RoomSpecCatalog.CurrentRoom;
            var primitives = room.environmentPrimitives;
            for (int i = primitives.Count - 1; i >= 0; i--)
            {
                if (ShouldRemoveForAutoWallRebuild(primitives[i]))
                {
                    primitives.RemoveAt(i);
                }
            }

            const float grid = 0.25f;
            var occupied = new HashSet<Vector2Int>();
            for (int i = 0; i < primitives.Count; i++)
            {
                if (!IsRoomPrimitiveFloorSurfaceLike(i))
                {
                    continue;
                }

                var floor = primitives[i];
                var yaw = floor.rotationEuler.y * Mathf.Deg2Rad;
                var cos = Mathf.Abs(Mathf.Cos(yaw));
                var sin = Mathf.Abs(Mathf.Sin(yaw));
                var halfX = (cos * floor.scale.x + sin * floor.scale.z) * 0.5f;
                var halfZ = (sin * floor.scale.x + cos * floor.scale.z) * 0.5f;
                var minX = Mathf.RoundToInt((floor.position.x - halfX) / grid);
                var maxX = Mathf.RoundToInt((floor.position.x + halfX) / grid) - 1;
                var minZ = Mathf.RoundToInt((floor.position.z - halfZ) / grid);
                var maxZ = Mathf.RoundToInt((floor.position.z + halfZ) / grid) - 1;

                if (maxX < minX) maxX = minX;
                if (maxZ < minZ) maxZ = minZ;

                for (int x = minX; x <= maxX; x++)
                {
                    for (int z = minZ; z <= maxZ; z++)
                    {
                        occupied.Add(new Vector2Int(x, z));
                    }
                }
            }

            if (occupied.Count == 0)
            {
                return;
            }

            var horizontalEdges = new Dictionary<string, List<int>>();
            var verticalEdges = new Dictionary<string, List<int>>();

            foreach (var cell in occupied)
            {
                if (!occupied.Contains(new Vector2Int(cell.x, cell.y - 1)))
                {
                    AddEdgeUnit(horizontalEdges, cell.y, -1, cell.x);
                }

                if (!occupied.Contains(new Vector2Int(cell.x, cell.y + 1)))
                {
                    AddEdgeUnit(horizontalEdges, cell.y + 1, 1, cell.x);
                }

                if (!occupied.Contains(new Vector2Int(cell.x - 1, cell.y)))
                {
                    AddEdgeUnit(verticalEdges, cell.x, -1, cell.y);
                }

                if (!occupied.Contains(new Vector2Int(cell.x + 1, cell.y)))
                {
                    AddEdgeUnit(verticalEdges, cell.x + 1, 1, cell.y);
                }
            }

            var wallIndex = 0;
            AddMergedAutoEdges(horizontalEdges, true, grid, ref wallIndex);
            AddMergedAutoEdges(verticalEdges, false, grid, ref wallIndex);
            AddDollhouseWallCaps(room);

            void AddEdgeUnit(Dictionary<string, List<int>> edges, int fixedCoord, int outwardSign, int startCoord)
            {
                var key = fixedCoord + "|" + outwardSign;
                if (!edges.TryGetValue(key, out var values))
                {
                    values = new List<int>();
                    edges[key] = values;
                }

                values.Add(startCoord);
            }

            void AddMergedAutoEdges(Dictionary<string, List<int>> edges, bool horizontal, float cellSize, ref int index)
            {
                foreach (var pair in edges)
                {
                    var keyParts = pair.Key.Split('|');
                    if (keyParts.Length != 2
                        || !int.TryParse(keyParts[0], out var fixedCoord)
                        || !int.TryParse(keyParts[1], out var outwardSign))
                    {
                        continue;
                    }

                    var values = pair.Value;
                    values.Sort();
                    if (values.Count == 0)
                    {
                        continue;
                    }

                    var runStart = values[0];
                    var previous = values[0];
                    for (int i = 1; i < values.Count; i++)
                    {
                        if (values[i] == previous + 1)
                        {
                            previous = values[i];
                            continue;
                        }

                        AddAutoShellRun(horizontal, fixedCoord, runStart, previous + 1, outwardSign, cellSize, ref index);
                        runStart = values[i];
                        previous = values[i];
                    }

                    AddAutoShellRun(horizontal, fixedCoord, runStart, previous + 1, outwardSign, cellSize, ref index);
                }
            }
        }

        private bool ShouldRemoveForAutoWallRebuild(RoomPrimitiveDefinition primitive)
        {
            if (primitive == null)
            {
                return false;
            }

            var id = (primitive.id ?? string.Empty).ToLowerInvariant();
            var label = (primitive.label ?? string.Empty).ToLowerInvariant();
            if (id.StartsWith("auto_wall_", StringComparison.Ordinal)
                || id.StartsWith("auto_floor_border_", StringComparison.Ordinal)
                || id.StartsWith("cap_", StringComparison.Ordinal)
                || id.StartsWith("floor_border_", StringComparison.Ordinal)
                || id.StartsWith("front_wall", StringComparison.Ordinal))
            {
                return true;
            }

            if (IsDefaultGeneratedOuterWallId(id))
            {
                return true;
            }

            if (ContainsAny(label, "wall segment", "drawn wall", "interior wall", "partition", "divider", "bathroom wall"))
            {
                return false;
            }

            return ContainsAny(label, "back wall", "left wall", "right wall", "entrance wall", "l shape inner wall");
        }

        private bool IsDefaultGeneratedOuterWallId(string id)
        {
            return id == "wall_back"
                || id == "wall_left"
                || id == "wall_right"
                || id == "wall_right_upper"
                || id == "wall_inner_horizontal"
                || id == "wall_inner_vertical"
                || id == "entrance_wall"
                || id == "feature_wall"
                || id == "left_wall"
                || id == "right_wall";
        }

        private void AddAutoShellRun(
            bool horizontal,
            int fixedCoord,
            int startCoord,
            int endCoord,
            int outwardSign,
            float grid,
            ref int wallIndex)
        {
            var start = startCoord * grid;
            var end = endCoord * grid;
            if (end - start < 0.24f)
            {
                return;
            }

            var fixedPosition = fixedCoord * grid;
            if (horizontal)
            {
                AddAutoHorizontalShellSegment(start, end, fixedPosition, outwardSign, wallIndex++);
            }
            else
            {
                AddAutoVerticalShellSegment(fixedPosition, start, end, outwardSign, wallIndex++);
            }
        }

        private List<Vector2> SplitSegmentAroundDoorGaps(bool horizontal, float fixedPosition, float start, float end)
        {
            var segments = new List<Vector2> { new(start, end) };
            var anchors = RoomSpecCatalog.CurrentRoom.anchors;
            for (int i = 0; i < anchors.Count; i++)
            {
                var anchor = anchors[i];
                if (anchor == null || !IsDoorAnchor(anchor))
                {
                    continue;
                }

                var doorAlong = horizontal ? anchor.position.x : anchor.position.z;
                var doorAcross = horizontal ? anchor.position.z : anchor.position.x;
                if (Mathf.Abs(doorAcross - fixedPosition) > 0.82f || doorAlong < start - 0.95f || doorAlong > end + 0.95f)
                {
                    continue;
                }

                var gapMin = doorAlong - 0.82f;
                var gapMax = doorAlong + 0.82f;
                for (int segmentIndex = segments.Count - 1; segmentIndex >= 0; segmentIndex--)
                {
                    var segment = segments[segmentIndex];
                    if (gapMax <= segment.x || gapMin >= segment.y)
                    {
                        continue;
                    }

                    segments.RemoveAt(segmentIndex);
                    if (gapMin - segment.x > 0.24f)
                    {
                        segments.Add(new Vector2(segment.x, Mathf.Clamp(gapMin, segment.x, segment.y)));
                    }

                    if (segment.y - gapMax > 0.24f)
                    {
                        segments.Add(new Vector2(Mathf.Clamp(gapMax, segment.x, segment.y), segment.y));
                    }
                }
            }

            return segments;
        }

        private void AddAutoHorizontalShellSegment(float xMin, float xMax, float z, int outwardSign, int index)
        {
            var room = RoomSpecCatalog.CurrentRoom;
            var length = xMax - xMin;
            var centerX = (xMin + xMax) * 0.5f;
            var wallHeight = 1.35f;
            var wallThickness = 0.18f;
            var offsetZ = z + outwardSign * 0.06f;

            room.environmentPrimitives.Add(CreateGuidedRoomPrimitive(
                $"auto_floor_border_h_{index}",
                "Auto Floor Border",
                "Cube",
                "#B98A58",
                new Vector3(centerX, 0.055f, z),
                new Vector3(length, 0.08f, 0.08f),
                Vector3.zero));
            room.environmentPrimitives.Add(CreateGuidedRoomPrimitive(
                $"auto_wall_h_{index}",
                "Auto Outer Wall",
                "Cube",
                "#F7F4EC",
                new Vector3(centerX, wallHeight * 0.5f, offsetZ),
                new Vector3(length, wallHeight, wallThickness),
                Vector3.zero));
        }

        private void AddAutoVerticalShellSegment(float x, float zMin, float zMax, int outwardSign, int index)
        {
            var room = RoomSpecCatalog.CurrentRoom;
            var length = zMax - zMin;
            var centerZ = (zMin + zMax) * 0.5f;
            var wallHeight = 1.35f;
            var wallThickness = 0.18f;
            var offsetX = x + outwardSign * 0.06f;

            room.environmentPrimitives.Add(CreateGuidedRoomPrimitive(
                $"auto_floor_border_v_{index}",
                "Auto Floor Border",
                "Cube",
                "#B98A58",
                new Vector3(x, 0.055f, centerZ),
                new Vector3(0.08f, 0.08f, length),
                Vector3.zero));
            room.environmentPrimitives.Add(CreateGuidedRoomPrimitive(
                $"auto_wall_v_{index}",
                "Auto Outer Wall",
                "Cube",
                "#F7F4EC",
                new Vector3(offsetX, wallHeight * 0.5f, centerZ),
                new Vector3(wallThickness, wallHeight, length),
                Vector3.zero));
        }

        private void CancelFloorPatchPlacement()
        {
            var primitives = RoomSpecCatalog.CurrentRoom.environmentPrimitives;
            if (activeFloorPatchWasNew && activeFloorPatchIndex >= 0 && activeFloorPatchIndex < primitives.Count)
            {
                primitives.RemoveAt(activeFloorPatchIndex);
            }

            selectedRoomPrimitiveIndex = -1;
            isFloorPatchPlacementActive = false;
            activeFloorPatchIndex = -1;
            activeFloorPatchWasNew = false;
            hasFloorPatchDropPreview = false;
            statusMessage = "Floor patch placement cancelled.";
            BuildRoomBuilderPreview();
        }

        private void FinishWallSegmentPlacement(string message)
        {
            if (activeWallSegmentIndex >= 0
                && activeWallSegmentIndex < RoomSpecCatalog.CurrentRoom.environmentPrimitives.Count
                && hasWallSegmentDropPreview)
            {
                var primitive = RoomSpecCatalog.CurrentRoom.environmentPrimitives[activeWallSegmentIndex];
                if (primitive != null)
                {
                    primitive.position = wallSegmentDropPreviewPosition;
                    primitive.rotationEuler = wallSegmentDropPreviewRotation;
                    primitive.scale = ClampRoomPrimitiveScale(wallSegmentDropPreviewScale);
                }
            }

            isWallSegmentPlacementActive = false;
            activeWallSegmentIndex = -1;
            activeWallSegmentWasNew = false;
            hasWallSegmentDropPreview = false;
            statusMessage = message;
            BuildRoomBuilderPreview();
        }

        private void CancelWallSegmentPlacement()
        {
            var primitives = RoomSpecCatalog.CurrentRoom.environmentPrimitives;
            if (activeWallSegmentWasNew && activeWallSegmentIndex >= 0 && activeWallSegmentIndex < primitives.Count)
            {
                primitives.RemoveAt(activeWallSegmentIndex);
            }

            selectedRoomPrimitiveIndex = -1;
            isWallSegmentPlacementActive = false;
            activeWallSegmentIndex = -1;
            activeWallSegmentWasNew = false;
            hasWallSegmentDropPreview = false;
            statusMessage = "Wall placement cancelled.";
            BuildRoomBuilderPreview();
        }

        private Vector3 GetBuilderShellPlacementPosition(float defaultY)
        {
            if (runtimeCamera != null)
            {
                var ray = runtimeCamera.ScreenPointToRay(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
                var plane = new Plane(Vector3.up, new Vector3(0f, defaultY, 0f));
                if (plane.Raycast(ray, out var enter))
                {
                    var position = SnapShellPointToGrid(ray.GetPoint(enter));
                    position.y = defaultY;
                    return position;
                }
            }

            return GetCameraPlacementPosition(defaultY);
        }

        private void BeginDrawShellWall()
        {
            isDrawingShellWall = true;
            hasShellWallStart = false;
            selectedRoomPrimitiveIndex = -1;
            selectedBuilderAnchorIndex = -1;
            SetBuilderTool(BuilderToolMode.Select);
            statusMessage = "Wall draw mode: left-click the floor for the start point, then left-click again for the endpoint.";
            BuildRoomBuilderPreview();
        }

        private void CancelDrawShellWall(string message)
        {
            isDrawingShellWall = false;
            hasShellWallStart = false;
            statusMessage = message;
            BuildRoomBuilderPreview();
        }

        private RoomPrimitiveDefinition BuildWallSegmentPrimitive(string label, Vector3 start, Vector3 end, string colorHex)
        {
            var delta = end - start;
            delta.y = 0f;
            var length = Mathf.Max(0.24f, delta.magnitude);
            var midpoint = (start + end) * 0.5f;
            midpoint.y = 0.82f;

            var yaw = delta.sqrMagnitude > 0.0001f
                ? Mathf.Atan2(-delta.normalized.z, delta.normalized.x) * Mathf.Rad2Deg
                : 0f;

            return new RoomPrimitiveDefinition
            {
                id = BuildUniqueRoomPrimitiveId(label),
                label = label,
                primitiveShape = "Cube",
                colorHex = colorHex,
                position = midpoint,
                scale = new Vector3(length, 1.65f, 0.12f),
                rotationEuler = new Vector3(0f, yaw, 0f),
                showLabel = false,
                labelHeight = 1.2f
            };
        }

        private void PlaceDrawnShellWall(Vector3 start, Vector3 end)
        {
            var primitive = BuildWallSegmentPrimitive("Drawn Wall", start, end, "#F7F4EC");
            RoomSpecCatalog.CurrentRoom.environmentPrimitives.Add(primitive);
            selectedRoomPrimitiveIndex = RoomSpecCatalog.CurrentRoom.environmentPrimitives.Count - 1;
            selectedBuilderAnchorIndex = -1;
            isDrawingShellWall = false;
            hasShellWallStart = false;
            BuildRoomBuilderPreview();
            statusMessage = "Placed drawn wall. Use W/E/R if you want to refine it.";
        }

        private Vector3 SnapShellPointToGrid(Vector3 point)
        {
            const float grid = 0.25f;
            point.x = Mathf.Round(point.x / grid) * grid;
            point.y = 0f;
            point.z = Mathf.Round(point.z / grid) * grid;
            return point;
        }

        private Vector3 GetSnappedFloorPatchPosition(RoomPrimitiveDefinition patch, int patchIndex, Vector3 candidate)
        {
            var snapped = SnapShellPointToGrid(candidate);
            snapped.y = 0f;

            if (patch == null)
            {
                return snapped;
            }

            var primitives = RoomSpecCatalog.CurrentRoom.environmentPrimitives;
            var patchHalfX = Mathf.Max(0.08f, patch.scale.x * 0.5f);
            var patchHalfZ = Mathf.Max(0.08f, patch.scale.z * 0.5f);
            var best = snapped;
            var bestDistance = float.PositiveInfinity;

            for (int i = 0; i < primitives.Count; i++)
            {
                if (i == patchIndex || !IsRoomPrimitiveFloorSurfaceLike(i) || primitives[i] == null)
                {
                    continue;
                }

                var floor = primitives[i];
                var floorHalfX = Mathf.Max(0.08f, floor.scale.x * 0.5f);
                var floorHalfZ = Mathf.Max(0.08f, floor.scale.z * 0.5f);

                ConsiderFloorSnapCandidate(new Vector3(
                    floor.position.x - floorHalfX - patchHalfX,
                    0f,
                    Mathf.Clamp(snapped.z, floor.position.z - floorHalfZ + patchHalfZ, floor.position.z + floorHalfZ - patchHalfZ)));
                ConsiderFloorSnapCandidate(new Vector3(
                    floor.position.x + floorHalfX + patchHalfX,
                    0f,
                    Mathf.Clamp(snapped.z, floor.position.z - floorHalfZ + patchHalfZ, floor.position.z + floorHalfZ - patchHalfZ)));
                ConsiderFloorSnapCandidate(new Vector3(
                    Mathf.Clamp(snapped.x, floor.position.x - floorHalfX + patchHalfX, floor.position.x + floorHalfX - patchHalfX),
                    0f,
                    floor.position.z - floorHalfZ - patchHalfZ));
                ConsiderFloorSnapCandidate(new Vector3(
                    Mathf.Clamp(snapped.x, floor.position.x - floorHalfX + patchHalfX, floor.position.x + floorHalfX - patchHalfX),
                    0f,
                    floor.position.z + floorHalfZ + patchHalfZ));

                void ConsiderFloorSnapCandidate(Vector3 candidatePosition)
                {
                    candidatePosition = SnapShellPointToGrid(candidatePosition);
                    var distance = new Vector2(candidatePosition.x - snapped.x, candidatePosition.z - snapped.z).sqrMagnitude;
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = candidatePosition;
                    }
                }
            }

            const float snapDistance = 3.25f;
            if (bestDistance <= snapDistance * snapDistance)
            {
                return best;
            }

            return snapped;
        }

        private void ApplyWallSegmentSnapPreview(RoomPrimitiveDefinition wall, int wallIndex, Vector3 candidate)
        {
            if (wall == null)
            {
                hasWallSegmentDropPreview = false;
                return;
            }

            var snapped = SnapShellPointToGrid(candidate);
            var wallHeight = Mathf.Clamp(Mathf.Max(wall.scale.y, 1.25f), 0.75f, 2.6f);
            var wallThickness = Mathf.Clamp(Mathf.Min(wall.scale.x, wall.scale.z, 0.18f), 0.08f, 0.22f);
            var defaultLength = Mathf.Clamp(Mathf.Max(wall.scale.x, wall.scale.z, 1.2f), 0.65f, 5.8f);
            var prefersVertical = Mathf.Abs(Mathf.DeltaAngle(wall.rotationEuler.y, 90f)) < 45f || wall.scale.z > wall.scale.x;
            var bestPosition = new Vector3(snapped.x, wallHeight * 0.5f, snapped.z);
            var bestRotation = Vector3.zero;
            var bestScale = prefersVertical
                ? new Vector3(wallThickness, wallHeight, defaultLength)
                : new Vector3(defaultLength, wallHeight, wallThickness);
            var fallbackPosition = bestPosition;
            var fallbackRotation = bestRotation;
            var fallbackScale = bestScale;
            var bestDistance = float.PositiveInfinity;

            var primitives = RoomSpecCatalog.CurrentRoom.environmentPrimitives;
            for (int i = 0; i < primitives.Count; i++)
            {
                if (i == wallIndex || !IsRoomPrimitiveFloorSurfaceLike(i) || primitives[i] == null)
                {
                    continue;
                }

                var floor = primitives[i];
                var halfX = Mathf.Max(0.08f, floor.scale.x * 0.5f);
                var halfZ = Mathf.Max(0.08f, floor.scale.z * 0.5f);
                ConsiderWallSnapCandidate(
                    new Vector3(Mathf.Clamp(snapped.x, floor.position.x - halfX, floor.position.x + halfX), wallHeight * 0.5f, floor.position.z - halfZ - 0.06f),
                    Vector3.zero,
                    new Vector3(Mathf.Clamp(defaultLength, 0.65f, floor.scale.x), wallHeight, wallThickness));
                ConsiderWallSnapCandidate(
                    new Vector3(Mathf.Clamp(snapped.x, floor.position.x - halfX, floor.position.x + halfX), wallHeight * 0.5f, floor.position.z + halfZ + 0.06f),
                    Vector3.zero,
                    new Vector3(Mathf.Clamp(defaultLength, 0.65f, floor.scale.x), wallHeight, wallThickness));
                ConsiderWallSnapCandidate(
                    new Vector3(floor.position.x - halfX - 0.06f, wallHeight * 0.5f, Mathf.Clamp(snapped.z, floor.position.z - halfZ, floor.position.z + halfZ)),
                    Vector3.zero,
                    new Vector3(wallThickness, wallHeight, Mathf.Clamp(defaultLength, 0.65f, floor.scale.z)));
                ConsiderWallSnapCandidate(
                    new Vector3(floor.position.x + halfX + 0.06f, wallHeight * 0.5f, Mathf.Clamp(snapped.z, floor.position.z - halfZ, floor.position.z + halfZ)),
                    Vector3.zero,
                    new Vector3(wallThickness, wallHeight, Mathf.Clamp(defaultLength, 0.65f, floor.scale.z)));
            }

            const float wallSnapDistance = 1.35f;
            wallSegmentDropPreviewPosition = bestDistance <= wallSnapDistance * wallSnapDistance ? bestPosition : fallbackPosition;
            wallSegmentDropPreviewRotation = bestDistance <= wallSnapDistance * wallSnapDistance ? bestRotation : fallbackRotation;
            wallSegmentDropPreviewScale = bestDistance <= wallSnapDistance * wallSnapDistance ? bestScale : fallbackScale;
            hasWallSegmentDropPreview = true;

            void ConsiderWallSnapCandidate(Vector3 position, Vector3 rotation, Vector3 scale)
            {
                position = SnapShellPointToGrid(position);
                position.y = wallHeight * 0.5f;
                var distance = new Vector2(position.x - snapped.x, position.z - snapped.z).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestPosition = position;
                    bestRotation = rotation;
                    bestScale = scale;
                }
            }
        }

        private void RotateSelectedRoomPrimitive(float degrees)
        {
            if (!TryGetSelectedRoomPrimitive(out var primitive))
            {
                return;
            }

            primitive.rotationEuler = new Vector3(primitive.rotationEuler.x, primitive.rotationEuler.y + degrees, primitive.rotationEuler.z);
            BuildRoomBuilderPreview();
        }

        private void ScaleSelectedRoomPrimitive(float multiplier)
        {
            if (!TryGetSelectedRoomPrimitive(out var primitive))
            {
                return;
            }

            primitive.scale = ClampRoomPrimitiveScale(primitive.scale * multiplier);
            BuildRoomBuilderPreview();
        }

        private void DeleteSelectedRoomPrimitive()
        {
            var primitives = RoomSpecCatalog.CurrentRoom.environmentPrimitives;
            if (selectedRoomPrimitiveIndex < 0 || selectedRoomPrimitiveIndex >= primitives.Count)
            {
                return;
            }

            var label = primitives[selectedRoomPrimitiveIndex].label;
            primitives.RemoveAt(selectedRoomPrimitiveIndex);
            selectedRoomPrimitiveIndex = -1;
            BuildRoomBuilderPreview();
            statusMessage = $"Deleted shell piece: {label}.";
        }

        private Vector3 ClampRoomPrimitiveScale(Vector3 scale)
        {
            return new Vector3(
                Mathf.Clamp(scale.x, 0.06f, 14f),
                Mathf.Clamp(scale.y, 0.03f, 3.8f),
                Mathf.Clamp(scale.z, 0.06f, 14f));
        }

        private bool TryGetSelectedBuilderAnchor(out AnchorDefinition anchor)
        {
            var anchors = RoomSpecCatalog.CurrentRoom.anchors;
            if (selectedBuilderAnchorIndex < 0 || selectedBuilderAnchorIndex >= anchors.Count)
            {
                anchor = null;
                return false;
            }

            anchor = anchors[selectedBuilderAnchorIndex];
            return true;
        }

        private void AdjustSelectedAnchorPosition(Vector3 delta)
        {
            if (!TryGetSelectedBuilderAnchor(out var anchor))
            {
                return;
            }

            anchor.position += delta;
            anchor.position = new Vector3(
                Mathf.Clamp(anchor.position.x, -5.6f, 5.6f),
                Mathf.Clamp(anchor.position.y, 0.05f, 3.8f),
                Mathf.Clamp(anchor.position.z, -5.6f, 5.6f));
            GetCurrentRoomFootprint(out var roomWidth, out var roomDepth);
            ConstrainAnchorPlacement(anchor, selectedBuilderAnchorIndex, roomWidth, roomDepth);
            BuildRoomBuilderPreview();
        }

        private void RotateSelectedAnchor(float degrees)
        {
            if (!TryGetSelectedBuilderAnchor(out var anchor))
            {
                return;
            }

            anchor.rotationEuler = new Vector3(anchor.rotationEuler.x, anchor.rotationEuler.y + degrees, anchor.rotationEuler.z);
            BuildRoomBuilderPreview();
        }

        private void ScaleSelectedAnchor(float multiplier)
        {
            if (!TryGetSelectedBuilderAnchor(out var anchor))
            {
                return;
            }

            anchor.scale = new Vector3(
                Mathf.Clamp(anchor.scale.x * multiplier, 0.08f, 4f),
                Mathf.Clamp(anchor.scale.y * multiplier, 0.08f, 4f),
                Mathf.Clamp(anchor.scale.z * multiplier, 0.08f, 4f));
            anchor.scale = ClampAnchorPlacementScale(anchor);
            GetCurrentRoomFootprint(out var roomWidth, out var roomDepth);
            ConstrainAnchorPlacement(anchor, selectedBuilderAnchorIndex, roomWidth, roomDepth);
            anchor.mnemonicOffset = new Vector3(anchor.mnemonicOffset.x, Mathf.Max(0.35f, anchor.scale.y * 0.65f + 0.35f), anchor.mnemonicOffset.z);
            BuildRoomBuilderPreview();
        }

        private void DeleteSelectedAnchor()
        {
            var anchors = RoomSpecCatalog.CurrentRoom.anchors;
            if (selectedBuilderAnchorIndex < 0 || selectedBuilderAnchorIndex >= anchors.Count)
            {
                return;
            }

            if (anchors.Count <= 1)
            {
                statusMessage = "Keep at least one furniture anchor for mnemonic placement.";
                return;
            }

            var label = anchors[selectedBuilderAnchorIndex].label;
            anchors.RemoveAt(selectedBuilderAnchorIndex);
            selectedBuilderAnchorIndex = anchors.Count > 0 ? Mathf.Clamp(selectedBuilderAnchorIndex, 0, anchors.Count - 1) : -1;
            var reassigned = ReassignCurrentItemsToAnchors();
            BuildRoomBuilderPreview();
            statusMessage = reassigned > 0
                ? $"Deleted {label} and redistributed {reassigned} word(s) across the remaining anchors."
                : $"Deleted {label}.";
        }

        private void CleanUpCurrentRoomLayout()
        {
            GetCurrentRoomFootprint(out var roomWidth, out var roomDepth);
            var anchors = RoomSpecCatalog.CurrentRoom.anchors;
            for (int i = 0; i < anchors.Count; i++)
            {
                ConstrainAnchorPlacement(anchors[i], i, roomWidth, roomDepth);
            }

            ApplyCurrentInteriorAffinities(roomWidth, roomDepth);
            ResolveCurrentAnchorOverlaps(roomWidth, roomDepth);
            ApplyCurrentInteriorAffinities(roomWidth, roomDepth);

            for (int i = 0; i < anchors.Count; i++)
            {
                ConstrainAnchorPlacement(anchors[i], i, roomWidth, roomDepth);
            }

            BuildRoomBuilderPreview();
            statusMessage = "Layout cleaned: large furniture was arranged near walls, small anchors stayed in usable room space, and overlaps were reduced.";
        }

        private void GetCurrentRoomFootprint(out float width, out float depth)
        {
            var minX = float.PositiveInfinity;
            var maxX = float.NegativeInfinity;
            var minZ = float.PositiveInfinity;
            var maxZ = float.NegativeInfinity;
            var room = RoomSpecCatalog.CurrentRoom;
            if (room?.environmentPrimitives == null)
            {
                width = 12f;
                depth = 12f;
                return;
            }

            for (int i = 0; i < room.environmentPrimitives.Count; i++)
            {
                var primitive = room.environmentPrimitives[i];
                var label = ((primitive.id ?? string.Empty) + " " + (primitive.label ?? string.Empty)).ToLowerInvariant();
                if (!ContainsAny(label, "floor", "rug") || primitive.scale.x < 2f || primitive.scale.z < 2f)
                {
                    continue;
                }

                minX = Mathf.Min(minX, primitive.position.x - primitive.scale.x * 0.5f);
                maxX = Mathf.Max(maxX, primitive.position.x + primitive.scale.x * 0.5f);
                minZ = Mathf.Min(minZ, primitive.position.z - primitive.scale.z * 0.5f);
                maxZ = Mathf.Max(maxZ, primitive.position.z + primitive.scale.z * 0.5f);
            }

            width = maxX - minX;
            depth = maxZ - minZ;
            if (width <= 0.01f || depth <= 0.01f || float.IsInfinity(width) || float.IsInfinity(depth))
            {
                width = 12f;
                depth = 12f;
            }
        }

        private void ConstrainAnchorPlacement(AnchorDefinition anchor, int index, float roomWidth, float roomDepth)
        {
            if (anchor == null)
            {
                return;
            }

            anchor.scale = ClampAnchorPlacementScale(anchor);

            if (IsWallMountedAnchor(anchor))
            {
                SnapAnchorToNearestWall(anchor, index, roomWidth, roomDepth);
            }
            else
            {
                ClampAnchorInsideRoom(anchor, roomWidth, roomDepth);
                var renderScale = GetFurnitureRenderScale(anchor);
                anchor.position = new Vector3(anchor.position.x, Mathf.Max(0.05f, renderScale.y * 0.5f), anchor.position.z);
                TrySnapAnchorOntoSupport(anchor, index);
            }

            anchor.mnemonicOffset = new Vector3(anchor.mnemonicOffset.x, Mathf.Max(0.35f, GetFurnitureRenderScale(anchor).y * 0.65f + 0.35f), anchor.mnemonicOffset.z);
            anchor.labelHeight = Mathf.Max(0.65f, GetFurnitureRenderScale(anchor).y * 0.75f + 0.45f);
        }

        private Vector3 ClampAnchorPlacementScale(AnchorDefinition anchor)
        {
            var label = BuildAnchorSearchText(anchor);
            var scale = anchor.scale == default ? Vector3.one : anchor.scale;

            if (ContainsAny(label, "door", "\u30c9\u30a2", "\u95e8", "\u9580"))
            {
                return new Vector3(Mathf.Clamp(scale.x, 0.95f, 1.35f), Mathf.Clamp(Mathf.Max(scale.y, 2.0f), 2.0f, 2.55f), Mathf.Clamp(scale.z, 0.08f, 0.18f));
            }

            if (ContainsAny(label, "window", "balcony", "\u7a93", "\u7a97", "\u30d9\u30e9\u30f3\u30c0"))
            {
                return new Vector3(Mathf.Clamp(Mathf.Max(scale.x, 1.3f), 1.2f, 2.4f), Mathf.Clamp(Mathf.Max(scale.y, 0.9f), 0.85f, 1.55f), Mathf.Clamp(scale.z, 0.08f, 0.18f));
            }

            if (ContainsAny(label, "fridge", "refrigerator", "\u51b7\u8535", "\u51b0\u7bb1"))
            {
                return new Vector3(Mathf.Clamp(Mathf.Max(scale.x, 0.8f), 0.7f, 1.25f), Mathf.Clamp(Mathf.Max(scale.y, 1.65f), 1.55f, 2.25f), Mathf.Clamp(Mathf.Max(scale.z, 0.65f), 0.55f, 1.0f));
            }

            if (ContainsAny(label, "cabinet", "wardrobe", "closet", "\u8863\u67dc", "\u30af\u30ed\u30fc\u30bc\u30c3\u30c8"))
            {
                return new Vector3(Mathf.Clamp(Mathf.Max(scale.x, 0.8f), 0.7f, 1.6f), Mathf.Clamp(Mathf.Max(scale.y, 1.2f), 1.0f, 2.2f), Mathf.Clamp(Mathf.Max(scale.z, 0.45f), 0.35f, 0.9f));
            }

            if (ContainsAny(label, "television", "tv", "monitor", "screen", "\u30c6\u30ec\u30d3", "\u7535\u89c6", "\u96fb\u8996"))
            {
                return new Vector3(Mathf.Clamp(Mathf.Max(scale.x, 1.2f), 1.0f, 1.9f), Mathf.Clamp(Mathf.Max(scale.y, 0.75f), 0.65f, 1.25f), Mathf.Clamp(scale.z, 0.12f, 0.45f));
            }

            if (ContainsAny(label, "computer", "pc", "laptop", "desktop", "keyboard", "comput", "omputer", "macbook", "\u30b3\u30f3\u30d4\u30e5\u30fc\u30bf", "\u7535\u8111", "\u96fb\u8133"))
            {
                return new Vector3(Mathf.Clamp(Mathf.Max(scale.x, 1.0f), 0.8f, 1.6f), Mathf.Clamp(Mathf.Max(scale.y, 0.7f), 0.6f, 1.2f), Mathf.Clamp(Mathf.Max(scale.z, 0.55f), 0.45f, 1.0f));
            }

            if (ContainsAny(label, "air conditioner", "aircon", "air conditioning", "ac unit", "a/c"))
            {
                return new Vector3(Mathf.Clamp(Mathf.Max(scale.x, 1.15f), 0.9f, 1.8f), Mathf.Clamp(Mathf.Max(scale.y, 0.35f), 0.28f, 0.65f), Mathf.Clamp(Mathf.Max(scale.z, 0.18f), 0.12f, 0.35f));
            }

            if (ContainsAny(label, "stove", "cooktop", "range", "hob"))
            {
                return new Vector3(Mathf.Clamp(Mathf.Max(scale.x, 0.95f), 0.8f, 1.45f), Mathf.Clamp(Mathf.Max(scale.y, 0.72f), 0.58f, 1.05f), Mathf.Clamp(Mathf.Max(scale.z, 0.72f), 0.55f, 1.1f));
            }

            if (ContainsAny(label, "bed", "\u30d9\u30c3\u30c9", "\u5e8a"))
            {
                return new Vector3(Mathf.Clamp(Mathf.Max(scale.x, 1.7f), 1.4f, 2.6f), Mathf.Clamp(Mathf.Max(scale.y, 0.45f), 0.35f, 0.8f), Mathf.Clamp(Mathf.Max(scale.z, 1.2f), 1.0f, 1.9f));
            }

            if (ContainsAny(label, "sofa", "couch", "\u30bd\u30d5\u30a1"))
            {
                return new Vector3(Mathf.Clamp(Mathf.Max(scale.x, 1.35f), 1.1f, 2.2f), Mathf.Clamp(Mathf.Max(scale.y, 0.65f), 0.55f, 1.1f), Mathf.Clamp(Mathf.Max(scale.z, 0.7f), 0.55f, 1.15f));
            }

            if (ContainsAny(label, "table", "desk", "counter", "\u673a", "\u30c6\u30fc\u30d6\u30eb", "\u30ab\u30a6\u30f3\u30bf\u30fc"))
            {
                return new Vector3(Mathf.Clamp(Mathf.Max(scale.x, 1.1f), 0.85f, 2.2f), Mathf.Clamp(Mathf.Max(scale.y, 0.65f), 0.55f, 1.05f), Mathf.Clamp(Mathf.Max(scale.z, 0.7f), 0.55f, 1.5f));
            }

            if (ContainsAny(label, "chair", "\u6905\u5b50", "\u30a4\u30b9"))
            {
                return new Vector3(Mathf.Clamp(Mathf.Max(scale.x, 0.55f), 0.45f, 0.95f), Mathf.Clamp(Mathf.Max(scale.y, 0.75f), 0.65f, 1.2f), Mathf.Clamp(Mathf.Max(scale.z, 0.55f), 0.45f, 0.95f));
            }

            if (ContainsAny(label, "toilet", "wc", "\u9a6c\u6876", "\u99ac\u6876", "\u4fbf\u5668", "\u30c8\u30a4\u30ec"))
            {
                return new Vector3(Mathf.Clamp(Mathf.Max(scale.x, 0.75f), 0.6f, 1.05f), Mathf.Clamp(Mathf.Max(scale.y, 0.65f), 0.55f, 0.95f), Mathf.Clamp(Mathf.Max(scale.z, 0.85f), 0.65f, 1.2f));
            }

            if (ContainsAny(label, "sink", "\u6d17\u9762", "\u6d41\u3057"))
            {
                return new Vector3(Mathf.Clamp(Mathf.Max(scale.x, 0.9f), 0.75f, 1.35f), Mathf.Clamp(Mathf.Max(scale.y, 0.7f), 0.6f, 1.1f), Mathf.Clamp(Mathf.Max(scale.z, 0.65f), 0.5f, 1.0f));
            }

            if (ContainsAny(label, "plant", "lamp", "light", "\u690d\u7269", "\u89b3\u8449", "\u7167\u660e", "\u30e9\u30a4\u30c8", "\u30e9\u30f3\u30d7"))
            {
                return new Vector3(Mathf.Clamp(Mathf.Max(scale.x, 0.35f), 0.3f, 0.8f), Mathf.Clamp(Mathf.Max(scale.y, 0.9f), 0.65f, 1.7f), Mathf.Clamp(Mathf.Max(scale.z, 0.35f), 0.3f, 0.8f));
            }

            return new Vector3(
                Mathf.Clamp(Mathf.Max(scale.x, 0.25f), 0.2f, 3.0f),
                Mathf.Clamp(Mathf.Max(scale.y, 0.25f), 0.2f, 2.6f),
                Mathf.Clamp(Mathf.Max(scale.z, 0.25f), 0.2f, 2.4f));
        }

        private void SnapAnchorToNearestWall(AnchorDefinition anchor, int index, float roomWidth, float roomDepth)
        {
            var renderScale = GetFurnitureRenderScale(anchor);
            var halfWidth = roomWidth * 0.5f;
            var halfDepth = roomDepth * 0.5f;
            var position = anchor.position;
            var text = BuildAnchorSearchText(anchor);
            var isWindow = IsWindowAnchor(anchor);
            var isHighWall = isWindow || IsHighWallAnchor(anchor);
            var forceEntranceWall = IsDoorAnchor(anchor) && (index == 0 || ContainsAny(text, "entrance", "front", "main door"));
            var forceBackWall = isWindow && ContainsAny(text, "balcony", "back", "feature");

            if (forceEntranceWall)
            {
                if (IsGuidedLShapeActive())
                {
                    GetGuidedLShapeParameters(roomWidth, roomDepth, out var sideArmWidth, out _, out _, out _);
                    position.x = Mathf.Clamp(position.x, -halfWidth + renderScale.x * 0.6f, -halfWidth + sideArmWidth - renderScale.x * 0.6f);
                }
                else
                {
                    position.x = Mathf.Clamp(position.x, -halfWidth + renderScale.x * 0.6f, halfWidth - renderScale.x * 0.6f);
                }
                position.z = -halfDepth + 0.11f;
                position.y = renderScale.y * 0.5f;
                anchor.rotationEuler = new Vector3(0f, 0f, 0f);
                anchor.position = position;
                return;
            }

            if (forceBackWall)
            {
                position.x = Mathf.Clamp(position.x, -halfWidth + renderScale.x * 0.6f, halfWidth - renderScale.x * 0.6f);
                position.z = halfDepth - 0.11f;
                position.y = Mathf.Clamp(position.y <= 0.2f ? 1.9f : position.y, 1.25f, 2.75f);
                anchor.rotationEuler = new Vector3(0f, 0f, 0f);
                anchor.position = position;
                return;
            }

            var distanceLeft = Mathf.Abs(position.x + halfWidth);
            var distanceRight = Mathf.Abs(halfWidth - position.x);
            var distanceFront = Mathf.Abs(position.z + halfDepth);
            var distanceBack = Mathf.Abs(halfDepth - position.z);
            var nearest = Mathf.Min(Mathf.Min(distanceLeft, distanceRight), Mathf.Min(distanceFront, distanceBack));
            var wallY = isHighWall
                ? Mathf.Clamp(position.y <= 0.2f ? 2.15f : position.y, 1.25f, 2.75f)
                : renderScale.y * 0.5f;

            if (Mathf.Approximately(nearest, distanceLeft))
            {
                position.x = -halfWidth + 0.11f;
                position.z = Mathf.Clamp(position.z, -halfDepth + renderScale.x * 0.55f, halfDepth - renderScale.x * 0.55f);
                anchor.rotationEuler = new Vector3(0f, 90f, 0f);
            }
            else if (Mathf.Approximately(nearest, distanceRight))
            {
                position.x = halfWidth - 0.11f;
                position.z = Mathf.Clamp(position.z, -halfDepth + renderScale.x * 0.55f, halfDepth - renderScale.x * 0.55f);
                anchor.rotationEuler = new Vector3(0f, 90f, 0f);
            }
            else if (Mathf.Approximately(nearest, distanceFront))
            {
                position.x = Mathf.Clamp(position.x, -halfWidth + renderScale.x * 0.55f, halfWidth - renderScale.x * 0.55f);
                position.z = -halfDepth + 0.11f;
                anchor.rotationEuler = new Vector3(0f, 0f, 0f);
            }
            else
            {
                position.x = Mathf.Clamp(position.x, -halfWidth + renderScale.x * 0.55f, halfWidth - renderScale.x * 0.55f);
                position.z = halfDepth - 0.11f;
                anchor.rotationEuler = new Vector3(0f, 0f, 0f);
            }

            position.y = wallY;
            position = ClampPositionToGuidedFootprint(position, new Vector2(renderScale.x * 0.5f, renderScale.z * 0.5f), roomWidth, roomDepth);
            anchor.position = position;
        }

        private void ResolveCurrentAnchorOverlaps(float roomWidth, float roomDepth)
        {
            var anchors = RoomSpecCatalog.CurrentRoom.anchors;
            for (int iteration = 0; iteration < 10; iteration++)
            {
                var changed = false;
                for (int i = 0; i < anchors.Count; i++)
                {
                    if (IsWallMountedAnchor(anchors[i]) || IsStackedOnSupportSurface(anchors[i], i))
                    {
                        continue;
                    }

                    for (int j = i + 1; j < anchors.Count; j++)
                    {
                        if (IsWallMountedAnchor(anchors[j]) || IsStackedOnSupportSurface(anchors[j], j))
                        {
                            continue;
                        }

                        var a = anchors[i];
                        var b = anchors[j];
                        var delta = new Vector2(a.position.x - b.position.x, a.position.z - b.position.z);
                        var distance = delta.magnitude;
                        var minimumDistance = GetAnchorFootprintRadius(a) + GetAnchorFootprintRadius(b);
                        if (distance >= minimumDistance)
                        {
                            continue;
                        }

                        var direction = distance > 0.001f
                            ? delta / distance
                            : new Vector2(Mathf.Cos((i + 1) * 1.37f), Mathf.Sin((j + 1) * 1.91f)).normalized;
                        var push = (minimumDistance - distance) * 0.55f;
                        a.position += new Vector3(direction.x * push, 0f, direction.y * push);
                        b.position -= new Vector3(direction.x * push, 0f, direction.y * push);
                        ClampAnchorInsideRoom(a, roomWidth, roomDepth);
                        ClampAnchorInsideRoom(b, roomWidth, roomDepth);
                        changed = true;
                    }
                }

                if (!changed)
                {
                    break;
                }
            }
        }

        private void ApplyCurrentInteriorAffinities(float roomWidth, float roomDepth)
        {
            var anchors = RoomSpecCatalog.CurrentRoom.anchors;
            var wallPlacedCount = 0;
            for (int i = 0; i < anchors.Count; i++)
            {
                var anchor = anchors[i];
                if (anchor == null || IsWallMountedAnchor(anchor) || !ShouldPreferWallPlacement(anchor))
                {
                    continue;
                }

                SnapFurnitureNearWall(anchor, ChooseFurnitureWall(anchor, wallPlacedCount, roomWidth, roomDepth), roomWidth, roomDepth);
                wallPlacedCount++;
            }
        }

        private int ChooseFurnitureWall(AnchorDefinition anchor, int wallPlacedCount, float roomWidth, float roomDepth)
        {
            var roomText = ((RoomSpecCatalog.CurrentRoom.roomName ?? string.Empty) + " " +
                            (RoomSpecCatalog.CurrentRoom.summary ?? string.Empty) + " " +
                            (RoomSpecCatalog.CurrentRoom.sourcePrompt ?? string.Empty)).ToLowerInvariant();
            if (ContainsAny(roomText, "gallery", "long", "corridor", "hallway", "linear", "route"))
            {
                return wallPlacedCount % 2 == 0 ? 0 : 1;
            }

            var halfWidth = roomWidth * 0.5f;
            var halfDepth = roomDepth * 0.5f;
            var distanceLeft = Mathf.Abs(anchor.position.x + halfWidth);
            var distanceRight = Mathf.Abs(halfWidth - anchor.position.x);
            var distanceBack = Mathf.Abs(halfDepth - anchor.position.z);
            var nearest = Mathf.Min(distanceLeft, Mathf.Min(distanceRight, distanceBack));

            if (nearest < Mathf.Min(roomWidth, roomDepth) * 0.28f)
            {
                if (Mathf.Approximately(nearest, distanceLeft))
                {
                    return 0;
                }

                if (Mathf.Approximately(nearest, distanceRight))
                {
                    return 1;
                }

                return 2;
            }

            var pattern = wallPlacedCount % 3;
            return pattern == 0 ? 2 : pattern == 1 ? 0 : 1;
        }

        private void SnapFurnitureNearWall(AnchorDefinition anchor, int wall, float roomWidth, float roomDepth)
        {
            var halfWidth = roomWidth * 0.5f;
            var halfDepth = roomDepth * 0.5f;
            var position = anchor.position;

            if (wall == 0)
            {
                anchor.rotationEuler = new Vector3(0f, 90f, 0f);
                var extents = GetAnchorFootprintExtents(anchor);
                position.x = -halfWidth + extents.x + 0.04f;
                position.z = Mathf.Clamp(position.z, -halfDepth + extents.y + 0.08f, halfDepth - extents.y - 0.08f);
            }
            else if (wall == 1)
            {
                anchor.rotationEuler = new Vector3(0f, 90f, 0f);
                var extents = GetAnchorFootprintExtents(anchor);
                position.x = halfWidth - extents.x - 0.04f;
                position.z = Mathf.Clamp(position.z, -halfDepth + extents.y + 0.08f, halfDepth - extents.y - 0.08f);
            }
            else
            {
                anchor.rotationEuler = new Vector3(0f, 0f, 0f);
                var extents = GetAnchorFootprintExtents(anchor);
                position.x = Mathf.Clamp(position.x, -halfWidth + extents.x + 0.08f, halfWidth - extents.x - 0.08f);
                position.z = halfDepth - extents.y - 0.04f;
            }

            var renderScale = GetFurnitureRenderScale(anchor);
            anchor.position = new Vector3(position.x, Mathf.Max(0.05f, renderScale.y * 0.5f), position.z);
        }

        private bool ShouldPreferWallPlacement(AnchorDefinition anchor)
        {
            var label = BuildAnchorSearchText(anchor);
            if (CanPlaceOnSupportSurface(anchor))
            {
                return false;
            }

            if (ContainsAny(label, "chair", "dining table", "coffee table", "plant", "lamp", "\u6905\u5b50", "\u30a4\u30b9"))
            {
                return false;
            }

            return ContainsAny(
                label,
                "shelf", "book", "display", "cabinet", "wardrobe", "closet",
                "sofa", "bed", "desk", "counter", "fridge", "refrigerator",
                "sink", "toilet", "bath", "bathtub", "television", "tv", "monitor", "computer",
                "stove", "cooktop", "range", "air conditioner", "aircon",
                "\u68da", "\u672c\u68da", "\u8863\u67dc", "\u30bd\u30d5\u30a1", "\u30d9\u30c3\u30c9", "\u5e8a", "\u673a");
        }

        private void ClampAnchorInsideRoom(AnchorDefinition anchor, float roomWidth, float roomDepth)
        {
            var extents = GetAnchorFootprintExtents(anchor);
            var marginX = Mathf.Clamp(extents.x + 0.045f, 0.12f, Mathf.Max(0.13f, roomWidth * 0.48f));
            var marginZ = Mathf.Clamp(extents.y + 0.045f, 0.12f, Mathf.Max(0.13f, roomDepth * 0.48f));
            anchor.position = new Vector3(
                Mathf.Clamp(anchor.position.x, -roomWidth * 0.5f + marginX, roomWidth * 0.5f - marginX),
                anchor.position.y,
                Mathf.Clamp(anchor.position.z, -roomDepth * 0.5f + marginZ, roomDepth * 0.5f - marginZ));
            anchor.position = ClampPositionToGuidedFootprint(anchor.position, extents, roomWidth, roomDepth);
        }

        private Vector3 ClampPositionToGuidedFootprint(Vector3 position, Vector2 extents, float roomWidth, float roomDepth)
        {
            if (!IsGuidedLShapeActive())
            {
                return position;
            }

            GetGuidedLShapeParameters(roomWidth, roomDepth, out _, out _, out var cutX, out var cutZ);
            var overlapsMissingCorner = position.x + extents.x > cutX && position.z - extents.y < cutZ;
            if (!overlapsMissingCorner)
            {
                return position;
            }

            var safeSideX = cutX - extents.x - 0.08f;
            var safeBackZ = cutZ + extents.y + 0.08f;
            var moveToSide = Mathf.Abs(position.x - safeSideX);
            var moveToBack = Mathf.Abs(position.z - safeBackZ);
            if (moveToSide <= moveToBack)
            {
                position.x = Mathf.Clamp(safeSideX, -roomWidth * 0.5f + extents.x, cutX - extents.x);
            }
            else
            {
                position.z = Mathf.Clamp(safeBackZ, cutZ + extents.y, roomDepth * 0.5f - extents.y);
            }

            return position;
        }

        private bool TrySnapAnchorOntoSupport(AnchorDefinition anchor, int index)
        {
            if (!TryFindSupportSurface(anchor, index, out var support, out var surfaceY))
            {
                return false;
            }

            var renderScale = GetFurnitureRenderScale(anchor);
            anchor.position = new Vector3(
                anchor.position.x,
                surfaceY + GetStackedItemCenterOffset(anchor, renderScale) + 0.02f,
                anchor.position.z);
            return support != null;
        }

        private bool IsStackedOnSupportSurface(AnchorDefinition anchor, int index)
        {
            if (!TryFindSupportSurface(anchor, index, out _, out var surfaceY))
            {
                return false;
            }

            var expectedY = surfaceY + GetStackedItemCenterOffset(anchor, GetFurnitureRenderScale(anchor)) + 0.02f;
            return Mathf.Abs(anchor.position.y - expectedY) <= 0.28f;
        }

        private bool TryFindSupportSurface(AnchorDefinition anchor, int index, out AnchorDefinition support, out float surfaceY)
        {
            support = null;
            surfaceY = 0f;
            if (!CanPlaceOnSupportSurface(anchor))
            {
                return false;
            }

            var anchors = RoomSpecCatalog.CurrentRoom.anchors;
            var itemExtents = GetAnchorFootprintExtents(anchor);
            var bestY = float.NegativeInfinity;
            AnchorDefinition bestSupport = null;

            for (int i = 0; i < anchors.Count; i++)
            {
                if (i == index || anchors[i] == null || !IsSupportSurfaceAnchor(anchors[i]))
                {
                    continue;
                }

                var candidate = anchors[i];
                var supportExtents = GetAnchorFootprintExtents(candidate);
                var dx = Mathf.Abs(anchor.position.x - candidate.position.x);
                var dz = Mathf.Abs(anchor.position.z - candidate.position.z);
                var toleranceX = Mathf.Max(0.18f, Mathf.Min(itemExtents.x * 0.5f, 0.35f));
                var toleranceZ = Mathf.Max(0.18f, Mathf.Min(itemExtents.y * 0.5f, 0.35f));
                if (dx > supportExtents.x + toleranceX || dz > supportExtents.y + toleranceZ)
                {
                    continue;
                }

                var candidateSurfaceY = GetSupportSurfaceHeight(candidate);
                if (candidateSurfaceY <= bestY)
                {
                    continue;
                }

                bestY = candidateSurfaceY;
                bestSupport = candidate;
            }

            if (bestSupport == null)
            {
                return false;
            }

            support = bestSupport;
            surfaceY = bestY;
            return true;
        }

        private bool CanPlaceOnSupportSurface(AnchorDefinition anchor)
        {
            var label = BuildAnchorSearchText(anchor);
            if (IsWallMountedAnchor(anchor))
            {
                return false;
            }

            if (HasSupportSurfaceKeyword(label))
            {
                return false;
            }

            if (ContainsAny(label, "computer", "pc", "laptop", "keyboard", "mouse", "tablet", "phone", "monitor", "screen",
                    "television", "tv", "book", "notebook", "cup", "mug", "vase", "speaker", "clock"))
            {
                return true;
            }

            if (ContainsAny(label, "plant", "lamp", "light"))
            {
                var extents = GetAnchorFootprintExtents(anchor);
                return extents.x <= 0.55f && extents.y <= 0.55f;
            }

            var renderScale = GetFurnitureRenderScale(anchor);
            return renderScale.x <= 1.25f && renderScale.z <= 1.0f && renderScale.y <= 1.25f;
        }

        private bool IsSupportSurfaceAnchor(AnchorDefinition anchor)
        {
            var label = BuildAnchorSearchText(anchor);
            if (IsWallMountedAnchor(anchor) || CanPlaceOnSupportSurface(anchor))
            {
                return false;
            }

            return HasSupportSurfaceKeyword(label);
        }

        private bool HasSupportSurfaceKeyword(string label)
        {
            return ContainsAny(label,
                "desk", "table", "counter", "stove", "cooktop", "shelf", "bookcase",
                "cabinet", "dresser", "nightstand", "tv stand", "stand", "bed", "sofa");
        }

        private float GetSupportSurfaceHeight(AnchorDefinition support)
        {
            var label = BuildAnchorSearchText(support);
            var renderScale = GetFurnitureRenderScale(support);
            if (ContainsAny(label, "desk", "table", "counter", "stove", "cooktop", "nightstand", "tv stand", "stand"))
            {
                return support.position.y + renderScale.y * 0.32f;
            }

            if (ContainsAny(label, "bed", "sofa", "couch"))
            {
                return support.position.y + renderScale.y * 0.20f;
            }

            return support.position.y + renderScale.y * 0.50f;
        }

        private float GetStackedItemCenterOffset(AnchorDefinition anchor, Vector3 renderScale)
        {
            var label = BuildAnchorSearchText(anchor);
            if (ContainsAny(label, "computer", "pc", "laptop", "keyboard", "mouse", "monitor", "screen", "television", "tv"))
            {
                return renderScale.y * 0.34f;
            }

            if (ContainsAny(label, "book", "notebook", "tablet", "phone", "cup", "mug"))
            {
                return renderScale.y * 0.24f;
            }

            return renderScale.y * 0.42f;
        }

        private float GetAnchorFootprintRadius(AnchorDefinition anchor)
        {
            var extents = GetAnchorFootprintExtents(anchor);
            return Mathf.Clamp(Mathf.Max(extents.x, extents.y) + 0.14f, 0.28f, 1.45f);
        }

        private Vector2 GetAnchorFootprintExtents(AnchorDefinition anchor)
        {
            var renderScale = GetFurnitureRenderScale(anchor);
            var yaw = (anchor?.rotationEuler.y ?? 0f) * Mathf.Deg2Rad;
            var cos = Mathf.Abs(Mathf.Cos(yaw));
            var sin = Mathf.Abs(Mathf.Sin(yaw));
            var halfX = renderScale.x * 0.5f;
            var halfZ = renderScale.z * 0.5f;
            return new Vector2(cos * halfX + sin * halfZ, sin * halfX + cos * halfZ);
        }

        private bool IsWallMountedAnchor(AnchorDefinition anchor)
        {
            return IsDoorAnchor(anchor) || IsWindowAnchor(anchor) || IsHighWallAnchor(anchor);
        }

        private bool IsDoorAnchor(AnchorDefinition anchor)
        {
            return ContainsAny(BuildAnchorSearchText(anchor), "door", "\u30c9\u30a2", "\u95e8", "\u9580");
        }

        private bool IsWindowAnchor(AnchorDefinition anchor)
        {
            return ContainsAny(BuildAnchorSearchText(anchor), "window", "balcony", "\u7a93", "\u7a97", "\u30d9\u30e9\u30f3\u30c0");
        }

        private bool IsHighWallAnchor(AnchorDefinition anchor)
        {
            return ContainsAny(BuildAnchorSearchText(anchor), "air conditioner", "aircon", "air conditioning", "ac unit", "a/c");
        }

        private string BuildAnchorSearchText(AnchorDefinition anchor)
        {
            return ((anchor?.label ?? string.Empty) + " " + (anchor?.id ?? string.Empty)).ToLowerInvariant();
        }

        private void FocusCameraOnAnchor(AnchorDefinition anchor)
        {
            if (runtimeCamera == null || anchor == null)
            {
                return;
            }

            runtimeCamera.orthographic = false;
            var target = anchor.position + Vector3.up * 0.7f;
            runtimeCamera.transform.position = target + new Vector3(0f, 1.0f, -3.2f);
            runtimeCamera.transform.LookAt(target);
            var euler = runtimeCamera.transform.rotation.eulerAngles;
            cameraYaw = euler.y;
            cameraPitch = euler.x > 180f ? euler.x - 360f : euler.x;
        }

        private void SaveCurrentRoomSpec()
        {
            var exportFolder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "GeneratedRooms"));
            Directory.CreateDirectory(exportFolder);
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var path = Path.Combine(exportFolder, $"room_{timestamp}.json");
            File.WriteAllText(path, JsonUtility.ToJson(RoomSpecCatalog.CurrentRoom, true), Encoding.UTF8);
            statusMessage = $"Room JSON saved: {path}";
        }

        /*
        private GameObject CreateFurnitureModel(AnchorDefinition anchor, Transform parent, bool selected, bool editable, int editableIndex)
        {
            var root = new GameObject($"Furniture_{anchor.id}");
            root.transform.SetParent(parent);
            root.transform.position = anchor.position;
            root.transform.rotation = Quaternion.Euler(anchor.rotationEuler);
            root.transform.localScale = GetFurnitureRenderScale(anchor);

            var label = ((anchor.label ?? string.Empty) + " " + (anchor.id ?? string.Empty)).ToLowerInvariant();
            var color = selected
                ? Color.Lerp(RoomSpecCatalog.Hex(anchor.colorHex), Color.white, 0.28f)
                : RoomSpecCatalog.Hex(anchor.colorHex);

            if (ContainsAny(label, "toilet", "马桶", "馬桶", "便器", "トイレ") && !ContainsAny(label, "door", "ドア", "门", "門"))
            {
                AddFurniturePart(root.transform, "Tank", PrimitiveType.Cube, new Vector3(0f, 0.20f, 0.28f), new Vector3(0.62f, 0.38f, 0.18f), color, editable, editableIndex);
                AddFurniturePart(root.transform, "Bowl", PrimitiveType.Sphere, new Vector3(0f, -0.05f, -0.04f), new Vector3(0.58f, 0.34f, 0.68f), Color.Lerp(color, Color.white, 0.2f), editable, editableIndex);
                AddFurniturePart(root.transform, "Base", PrimitiveType.Cylinder, new Vector3(0f, -0.32f, -0.04f), new Vector3(0.34f, 0.16f, 0.34f), color, editable, editableIndex);
            }
            else if (ContainsAny(label, "bath", "bathtub", "tub", "浴槽", "風呂"))
            {
                AddFurniturePart(root.transform, "Tub", PrimitiveType.Cube, Vector3.zero, new Vector3(1.0f, 0.34f, 0.58f), color, editable, editableIndex);
                AddFurniturePart(root.transform, "Water", PrimitiveType.Cube, new Vector3(0f, 0.20f, 0f), new Vector3(0.82f, 0.04f, 0.42f), new Color(0.45f, 0.68f, 0.88f), editable, editableIndex);
            }
            else if (ContainsAny(label, "sink", "洗面", "流し"))
            {
                AddFurniturePart(root.transform, "Cabinet", PrimitiveType.Cube, new Vector3(0f, -0.15f, 0f), new Vector3(0.78f, 0.55f, 0.58f), color, editable, editableIndex);
                AddFurniturePart(root.transform, "Basin", PrimitiveType.Cube, new Vector3(0f, 0.18f, 0f), new Vector3(0.82f, 0.14f, 0.62f), Color.Lerp(color, Color.white, 0.35f), editable, editableIndex);
                AddFurniturePart(root.transform, "Faucet", PrimitiveType.Cylinder, new Vector3(0f, 0.42f, 0.16f), new Vector3(0.08f, 0.18f, 0.08f), new Color(0.65f, 0.70f, 0.72f), editable, editableIndex);
            }
            else if (ContainsAny(label, "bed", "ベッド", "床"))
            {
                AddFurniturePart(root.transform, "Base", PrimitiveType.Cube, new Vector3(0f, -0.12f, 0f), new Vector3(1.0f, 0.28f, 1.0f), color, editable, editableIndex);
                AddFurniturePart(root.transform, "Pillow", PrimitiveType.Cube, new Vector3(0f, 0.10f, 0.34f), new Vector3(0.62f, 0.16f, 0.22f), new Color(0.92f, 0.89f, 0.80f), editable, editableIndex);
            }
            else if (ContainsAny(label, "sofa", "couch", "ソファ"))
            {
                AddFurniturePart(root.transform, "Seat", PrimitiveType.Cube, new Vector3(0f, -0.10f, 0f), new Vector3(1.0f, 0.36f, 0.72f), color, editable, editableIndex);
                AddFurniturePart(root.transform, "Back", PrimitiveType.Cube, new Vector3(0f, 0.20f, 0.34f), new Vector3(1.0f, 0.62f, 0.18f), Color.Lerp(color, Color.black, 0.08f), editable, editableIndex);
                AddFurniturePart(root.transform, "LeftArm", PrimitiveType.Cube, new Vector3(-0.56f, 0.05f, 0f), new Vector3(0.14f, 0.48f, 0.72f), color, editable, editableIndex);
                AddFurniturePart(root.transform, "RightArm", PrimitiveType.Cube, new Vector3(0.56f, 0.05f, 0f), new Vector3(0.14f, 0.48f, 0.72f), color, editable, editableIndex);
            }
            else if (ContainsAny(label, "chair", "椅子", "イス"))
            {
                AddFurniturePart(root.transform, "Seat", PrimitiveType.Cube, new Vector3(0f, 0f, 0f), new Vector3(0.78f, 0.18f, 0.78f), color, editable, editableIndex);
                AddFurniturePart(root.transform, "Back", PrimitiveType.Cube, new Vector3(0f, 0.46f, 0.32f), new Vector3(0.78f, 0.74f, 0.16f), Color.Lerp(color, Color.black, 0.08f), editable, editableIndex);
                AddFurniturePart(root.transform, "Legs", PrimitiveType.Cube, new Vector3(0f, -0.34f, 0f), new Vector3(0.54f, 0.58f, 0.54f), Color.Lerp(color, Color.black, 0.18f), editable, editableIndex);
            }
            else if (ContainsAny(label, "table", "desk", "counter", "机", "テーブル", "カウンター"))
            {
                AddFurniturePart(root.transform, "Top", PrimitiveType.Cube, new Vector3(0f, 0.18f, 0f), new Vector3(1.0f, 0.16f, 1.0f), color, editable, editableIndex);
                AddFurniturePart(root.transform, "LegCenter", PrimitiveType.Cube, new Vector3(0f, -0.30f, 0f), new Vector3(0.18f, 0.74f, 0.18f), Color.Lerp(color, Color.black, 0.2f), editable, editableIndex);
            }
            else if (ContainsAny(label, "shelf", "book", "cabinet", "wardrobe", "closet", "棚", "本棚", "衣柜", "クローゼット"))
            {
                AddFurniturePart(root.transform, "Body", PrimitiveType.Cube, Vector3.zero, new Vector3(1.0f, 1.0f, 0.42f), color, editable, editableIndex);
                AddFurniturePart(root.transform, "ShelfLine1", PrimitiveType.Cube, new Vector3(0f, 0.22f, -0.24f), new Vector3(0.9f, 0.05f, 0.08f), Color.Lerp(color, Color.white, 0.2f), editable, editableIndex);
                AddFurniturePart(root.transform, "ShelfLine2", PrimitiveType.Cube, new Vector3(0f, -0.22f, -0.24f), new Vector3(0.9f, 0.05f, 0.08f), Color.Lerp(color, Color.white, 0.2f), editable, editableIndex);
            }
            else if (ContainsAny(label, "plant", "植物", "観葉"))
            {
                AddFurniturePart(root.transform, "Pot", PrimitiveType.Cylinder, new Vector3(0f, -0.30f, 0f), new Vector3(0.5f, 0.35f, 0.5f), new Color(0.48f, 0.30f, 0.20f), editable, editableIndex);
                AddFurniturePart(root.transform, "Leaf", PrimitiveType.Sphere, new Vector3(0f, 0.28f, 0f), new Vector3(0.88f, 0.78f, 0.88f), color, editable, editableIndex);
            }
            else if (ContainsAny(label, "lamp", "light", "照明", "ライト"))
            {
                AddFurniturePart(root.transform, "Pole", PrimitiveType.Cylinder, new Vector3(0f, -0.12f, 0f), new Vector3(0.12f, 0.78f, 0.12f), new Color(0.55f, 0.55f, 0.58f), editable, editableIndex);
                AddFurniturePart(root.transform, "Shade", PrimitiveType.Sphere, new Vector3(0f, 0.48f, 0f), new Vector3(0.72f, 0.42f, 0.72f), color, editable, editableIndex);
            }
            else if (ContainsAny(label, "fridge", "refrigerator", "冷蔵", "冰箱"))
            {
                AddFurniturePart(root.transform, "Body", PrimitiveType.Cube, Vector3.zero, new Vector3(1.0f, 1.0f, 1.0f), color, editable, editableIndex);
                AddFurniturePart(root.transform, "Handle", PrimitiveType.Cube, new Vector3(0.42f, 0.05f, -0.52f), new Vector3(0.06f, 0.62f, 0.06f), new Color(0.65f, 0.68f, 0.70f), editable, editableIndex);
            }
            else if (ContainsAny(label, "door", "ドア", "门", "門"))
            {
                AddFurniturePart(root.transform, "Panel", PrimitiveType.Cube, Vector3.zero, new Vector3(1.0f, 1.0f, 1.0f), color, editable, editableIndex);
                AddFurniturePart(root.transform, "Knob", PrimitiveType.Sphere, new Vector3(0.38f, 0f, -0.55f), new Vector3(0.12f, 0.12f, 0.12f), new Color(0.86f, 0.70f, 0.38f), editable, editableIndex);
            }
            else if (ContainsAny(label, "window", "balcony", "窓", "窗", "ベランダ"))
            {
                AddFurniturePart(root.transform, "Glass", PrimitiveType.Cube, Vector3.zero, new Vector3(1.0f, 1.0f, 1.0f), color, editable, editableIndex);
                AddFurniturePart(root.transform, "FrameH", PrimitiveType.Cube, new Vector3(0f, 0f, -0.06f), new Vector3(1.0f, 0.08f, 0.08f), Color.white, editable, editableIndex);
                AddFurniturePart(root.transform, "FrameV", PrimitiveType.Cube, new Vector3(0f, 0f, -0.07f), new Vector3(0.08f, 1.0f, 0.08f), Color.white, editable, editableIndex);
            }
            else
            {
                AddFurniturePart(root.transform, "Generic", RoomSpecCatalog.ParsePrimitiveType(anchor.primitiveShape), Vector3.zero, Vector3.one, color, editable, editableIndex);
            }

            return root;
        }

        */

        private GameObject CreateFurnitureModel(AnchorDefinition anchor, Transform parent, bool selected, bool editable, int editableIndex)
        {
            var root = new GameObject($"Furniture_{anchor.id}");
            root.transform.SetParent(parent);
            root.transform.position = anchor.position;
            root.transform.rotation = Quaternion.Euler(anchor.rotationEuler);

            var label = ((anchor.label ?? string.Empty) + " " + (anchor.id ?? string.Empty)).ToLowerInvariant();
            var color = selected
                ? Color.Lerp(RoomSpecCatalog.Hex(anchor.colorHex), Color.white, 0.28f)
                : RoomSpecCatalog.Hex(anchor.colorHex);

            if (anchor.modelParts != null && anchor.modelParts.Count > 0)
            {
                CreateFurnitureModelFromParts(root.transform, anchor.modelParts, color, editable, editableIndex);
            }
            else if (ContainsAny(label, "toilet", "wc", "\u9a6c\u6876", "\u99ac\u6876", "\u4fbf\u5668", "\u30c8\u30a4\u30ec")
                && !ContainsAny(label, "door", "\u30c9\u30a2", "\u95e8", "\u9580"))
            {
                AddFurniturePart(root.transform, "Tank", PrimitiveType.Cube, new Vector3(0f, 0.20f, 0.28f), new Vector3(0.62f, 0.38f, 0.18f), color, editable, editableIndex);
                AddFurniturePart(root.transform, "Bowl", PrimitiveType.Sphere, new Vector3(0f, -0.05f, -0.04f), new Vector3(0.58f, 0.34f, 0.68f), Color.Lerp(color, Color.white, 0.2f), editable, editableIndex);
                AddFurniturePart(root.transform, "Base", PrimitiveType.Cylinder, new Vector3(0f, -0.32f, -0.04f), new Vector3(0.34f, 0.16f, 0.34f), color, editable, editableIndex);
            }
            else if (ContainsAny(label, "sink", "\u6d17\u9762", "\u6d41\u3057"))
            {
                AddFurniturePart(root.transform, "Cabinet", PrimitiveType.Cube, new Vector3(0f, -0.20f, 0f), new Vector3(0.82f, 0.56f, 0.62f), color, editable, editableIndex);
                AddFurniturePart(root.transform, "CounterTop", PrimitiveType.Cube, new Vector3(0f, 0.14f, 0f), new Vector3(0.92f, 0.10f, 0.70f), Color.Lerp(color, Color.white, 0.25f), editable, editableIndex);
                AddFurniturePart(root.transform, "Basin", PrimitiveType.Cube, new Vector3(0f, 0.23f, -0.04f), new Vector3(0.56f, 0.10f, 0.42f), new Color(0.88f, 0.92f, 0.95f), editable, editableIndex);
                AddFurniturePart(root.transform, "FaucetStem", PrimitiveType.Cylinder, new Vector3(0f, 0.42f, 0.18f), new Vector3(0.07f, 0.22f, 0.07f), new Color(0.65f, 0.70f, 0.72f), editable, editableIndex);
                AddFurniturePart(root.transform, "FaucetHead", PrimitiveType.Cube, new Vector3(0f, 0.52f, 0.04f), new Vector3(0.24f, 0.05f, 0.08f), new Color(0.65f, 0.70f, 0.72f), editable, editableIndex);
            }
            else if (ContainsAny(label, "bath", "bathtub", "tub", "\u6d74\u69fd", "\u98a8\u5442"))
            {
                AddFurniturePart(root.transform, "Tub", PrimitiveType.Cube, Vector3.zero, new Vector3(1.0f, 0.34f, 0.58f), color, editable, editableIndex);
                AddFurniturePart(root.transform, "Water", PrimitiveType.Cube, new Vector3(0f, 0.20f, 0f), new Vector3(0.82f, 0.04f, 0.42f), new Color(0.45f, 0.68f, 0.88f), editable, editableIndex);
            }
            else if (ContainsAny(label, "television", "tv", "monitor", "screen", "\u30c6\u30ec\u30d3", "\u7535\u89c6", "\u96fb\u8996"))
            {
                AddFurniturePart(root.transform, "Screen", PrimitiveType.Cube, new Vector3(0f, 0.18f, 0f), new Vector3(1.0f, 0.62f, 0.08f), new Color(0.05f, 0.06f, 0.08f), editable, editableIndex);
                AddFurniturePart(root.transform, "ScreenGlow", PrimitiveType.Cube, new Vector3(0f, 0.18f, -0.05f), new Vector3(0.86f, 0.48f, 0.03f), new Color(0.18f, 0.30f, 0.42f), editable, editableIndex);
                AddFurniturePart(root.transform, "StandNeck", PrimitiveType.Cube, new Vector3(0f, -0.20f, 0.02f), new Vector3(0.10f, 0.32f, 0.10f), Color.Lerp(color, Color.black, 0.1f), editable, editableIndex);
                AddFurniturePart(root.transform, "StandBase", PrimitiveType.Cube, new Vector3(0f, -0.38f, 0.02f), new Vector3(0.46f, 0.08f, 0.28f), Color.Lerp(color, Color.black, 0.1f), editable, editableIndex);
            }
            else if (ContainsAny(label, "computer", "pc", "laptop", "desktop", "keyboard", "comput", "omputer", "macbook", "\u30b3\u30f3\u30d4\u30e5\u30fc\u30bf", "\u7535\u8111", "\u96fb\u8133"))
            {
                AddFurniturePart(root.transform, "MonitorFrame", PrimitiveType.Cube, new Vector3(0f, 0.24f, 0.03f), new Vector3(0.76f, 0.52f, 0.08f), new Color(0.06f, 0.07f, 0.09f), editable, editableIndex);
                AddFurniturePart(root.transform, "MonitorGlow", PrimitiveType.Cube, new Vector3(0f, 0.24f, -0.02f), new Vector3(0.62f, 0.38f, 0.035f), new Color(0.18f, 0.45f, 0.70f), editable, editableIndex);
                AddFurniturePart(root.transform, "MonitorStand", PrimitiveType.Cube, new Vector3(0f, -0.08f, 0.04f), new Vector3(0.10f, 0.28f, 0.10f), Color.Lerp(color, Color.black, 0.12f), editable, editableIndex);
                AddFurniturePart(root.transform, "Keyboard", PrimitiveType.Cube, new Vector3(0f, -0.28f, -0.26f), new Vector3(0.72f, 0.06f, 0.22f), new Color(0.12f, 0.13f, 0.15f), editable, editableIndex);
                AddFurniturePart(root.transform, "Mouse", PrimitiveType.Sphere, new Vector3(0.42f, -0.26f, -0.24f), new Vector3(0.18f, 0.08f, 0.24f), new Color(0.16f, 0.17f, 0.19f), editable, editableIndex);
            }
            else if (ContainsAny(label, "air conditioner", "aircon", "air conditioning", "ac unit", "a/c"))
            {
                AddFurniturePart(root.transform, "UnitBody", PrimitiveType.Cube, Vector3.zero, new Vector3(1.0f, 0.72f, 0.9f), color, editable, editableIndex);
                AddFurniturePart(root.transform, "VentLineA", PrimitiveType.Cube, new Vector3(0f, -0.20f, -0.48f), new Vector3(0.86f, 0.04f, 0.05f), new Color(0.45f, 0.52f, 0.56f), editable, editableIndex);
                AddFurniturePart(root.transform, "VentLineB", PrimitiveType.Cube, new Vector3(0f, -0.08f, -0.48f), new Vector3(0.86f, 0.035f, 0.05f), new Color(0.60f, 0.66f, 0.70f), editable, editableIndex);
            }
            else if (ContainsAny(label, "stove", "cooktop", "range", "hob"))
            {
                AddFurniturePart(root.transform, "StoveBody", PrimitiveType.Cube, new Vector3(0f, -0.10f, 0f), new Vector3(1.0f, 0.76f, 0.92f), color, editable, editableIndex);
                AddFurniturePart(root.transform, "Cooktop", PrimitiveType.Cube, new Vector3(0f, 0.32f, -0.02f), new Vector3(0.92f, 0.08f, 0.82f), new Color(0.12f, 0.13f, 0.14f), editable, editableIndex);
                AddFurniturePart(root.transform, "BurnerA", PrimitiveType.Cylinder, new Vector3(-0.25f, 0.39f, -0.18f), new Vector3(0.22f, 0.03f, 0.22f), new Color(0.72f, 0.72f, 0.68f), editable, editableIndex);
                AddFurniturePart(root.transform, "BurnerB", PrimitiveType.Cylinder, new Vector3(0.25f, 0.39f, 0.16f), new Vector3(0.22f, 0.03f, 0.22f), new Color(0.72f, 0.72f, 0.68f), editable, editableIndex);
                AddFurniturePart(root.transform, "OvenDoor", PrimitiveType.Cube, new Vector3(0f, -0.20f, -0.48f), new Vector3(0.72f, 0.36f, 0.04f), new Color(0.16f, 0.18f, 0.20f), editable, editableIndex);
            }
            else if (ContainsAny(label, "bed", "\u30d9\u30c3\u30c9", "\u5e8a"))
            {
                AddFurniturePart(root.transform, "Frame", PrimitiveType.Cube, new Vector3(0f, -0.20f, 0f), new Vector3(1.0f, 0.20f, 1.0f), Color.Lerp(color, Color.black, 0.16f), editable, editableIndex);
                AddFurniturePart(root.transform, "Mattress", PrimitiveType.Cube, new Vector3(0f, 0.02f, 0f), new Vector3(0.92f, 0.22f, 0.92f), color, editable, editableIndex);
                AddFurniturePart(root.transform, "Blanket", PrimitiveType.Cube, new Vector3(0f, 0.17f, -0.10f), new Vector3(0.88f, 0.08f, 0.58f), new Color(0.64f, 0.48f, 0.36f), editable, editableIndex);
                AddFurniturePart(root.transform, "Pillow", PrimitiveType.Cube, new Vector3(0f, 0.20f, 0.34f), new Vector3(0.62f, 0.13f, 0.22f), new Color(0.92f, 0.89f, 0.80f), editable, editableIndex);
            }
            else if (ContainsAny(label, "sofa", "couch", "\u30bd\u30d5\u30a1"))
            {
                AddFurniturePart(root.transform, "Seat", PrimitiveType.Cube, new Vector3(0f, -0.10f, 0f), new Vector3(1.0f, 0.36f, 0.72f), color, editable, editableIndex);
                AddFurniturePart(root.transform, "Back", PrimitiveType.Cube, new Vector3(0f, 0.20f, 0.34f), new Vector3(1.0f, 0.62f, 0.18f), Color.Lerp(color, Color.black, 0.08f), editable, editableIndex);
                AddFurniturePart(root.transform, "LeftArm", PrimitiveType.Cube, new Vector3(-0.56f, 0.05f, 0f), new Vector3(0.14f, 0.48f, 0.72f), color, editable, editableIndex);
                AddFurniturePart(root.transform, "RightArm", PrimitiveType.Cube, new Vector3(0.56f, 0.05f, 0f), new Vector3(0.14f, 0.48f, 0.72f), color, editable, editableIndex);
            }
            else if (ContainsAny(label, "chair", "\u6905\u5b50", "\u30a4\u30b9"))
            {
                AddFurniturePart(root.transform, "Seat", PrimitiveType.Cube, new Vector3(0f, 0f, 0f), new Vector3(0.78f, 0.18f, 0.78f), color, editable, editableIndex);
                AddFurniturePart(root.transform, "Back", PrimitiveType.Cube, new Vector3(0f, 0.46f, 0.32f), new Vector3(0.78f, 0.74f, 0.16f), Color.Lerp(color, Color.black, 0.08f), editable, editableIndex);
                AddFurniturePart(root.transform, "LegFL", PrimitiveType.Cube, new Vector3(-0.25f, -0.34f, -0.25f), new Vector3(0.08f, 0.58f, 0.08f), Color.Lerp(color, Color.black, 0.18f), editable, editableIndex);
                AddFurniturePart(root.transform, "LegFR", PrimitiveType.Cube, new Vector3(0.25f, -0.34f, -0.25f), new Vector3(0.08f, 0.58f, 0.08f), Color.Lerp(color, Color.black, 0.18f), editable, editableIndex);
                AddFurniturePart(root.transform, "LegBL", PrimitiveType.Cube, new Vector3(-0.25f, -0.34f, 0.25f), new Vector3(0.08f, 0.58f, 0.08f), Color.Lerp(color, Color.black, 0.18f), editable, editableIndex);
                AddFurniturePart(root.transform, "LegBR", PrimitiveType.Cube, new Vector3(0.25f, -0.34f, 0.25f), new Vector3(0.08f, 0.58f, 0.08f), Color.Lerp(color, Color.black, 0.18f), editable, editableIndex);
            }
            else if (ContainsAny(label, "table", "desk", "counter", "\u673a", "\u30c6\u30fc\u30d6\u30eb", "\u30ab\u30a6\u30f3\u30bf\u30fc"))
            {
                AddFurniturePart(root.transform, "Top", PrimitiveType.Cube, new Vector3(0f, 0.22f, 0f), new Vector3(1.0f, 0.14f, 1.0f), color, editable, editableIndex);
                AddFurniturePart(root.transform, "LegFL", PrimitiveType.Cube, new Vector3(-0.38f, -0.25f, -0.34f), new Vector3(0.08f, 0.78f, 0.08f), Color.Lerp(color, Color.black, 0.2f), editable, editableIndex);
                AddFurniturePart(root.transform, "LegFR", PrimitiveType.Cube, new Vector3(0.38f, -0.25f, -0.34f), new Vector3(0.08f, 0.78f, 0.08f), Color.Lerp(color, Color.black, 0.2f), editable, editableIndex);
                AddFurniturePart(root.transform, "LegBL", PrimitiveType.Cube, new Vector3(-0.38f, -0.25f, 0.34f), new Vector3(0.08f, 0.78f, 0.08f), Color.Lerp(color, Color.black, 0.2f), editable, editableIndex);
                AddFurniturePart(root.transform, "LegBR", PrimitiveType.Cube, new Vector3(0.38f, -0.25f, 0.34f), new Vector3(0.08f, 0.78f, 0.08f), Color.Lerp(color, Color.black, 0.2f), editable, editableIndex);
                if (ContainsAny(label, "desk", "counter", "\u30ab\u30a6\u30f3\u30bf\u30fc"))
                {
                    AddFurniturePart(root.transform, "Drawer", PrimitiveType.Cube, new Vector3(0.23f, -0.05f, -0.42f), new Vector3(0.34f, 0.22f, 0.08f), Color.Lerp(color, Color.black, 0.08f), editable, editableIndex);
                }
            }
            else if (ContainsAny(label, "shelf", "book", "cabinet", "wardrobe", "closet", "\u68da", "\u672c\u68da", "\u8863\u67dc", "\u30af\u30ed\u30fc\u30bc\u30c3\u30c8"))
            {
                AddFurniturePart(root.transform, "Frame", PrimitiveType.Cube, Vector3.zero, new Vector3(1.0f, 1.0f, 0.42f), color, editable, editableIndex);
                AddFurniturePart(root.transform, "OpenFace", PrimitiveType.Cube, new Vector3(0f, 0f, -0.24f), new Vector3(0.82f, 0.86f, 0.05f), Color.Lerp(color, Color.white, 0.20f), editable, editableIndex);
                AddFurniturePart(root.transform, "ShelfLine1", PrimitiveType.Cube, new Vector3(0f, 0.22f, -0.29f), new Vector3(0.9f, 0.04f, 0.08f), Color.Lerp(color, Color.black, 0.12f), editable, editableIndex);
                AddFurniturePart(root.transform, "ShelfLine2", PrimitiveType.Cube, new Vector3(0f, -0.20f, -0.29f), new Vector3(0.9f, 0.04f, 0.08f), Color.Lerp(color, Color.black, 0.12f), editable, editableIndex);
                AddFurniturePart(root.transform, "BookA", PrimitiveType.Cube, new Vector3(-0.25f, 0.42f, -0.34f), new Vector3(0.10f, 0.28f, 0.10f), new Color(0.65f, 0.22f, 0.18f), editable, editableIndex);
                AddFurniturePart(root.transform, "BookB", PrimitiveType.Cube, new Vector3(-0.12f, 0.40f, -0.34f), new Vector3(0.09f, 0.24f, 0.10f), new Color(0.20f, 0.38f, 0.65f), editable, editableIndex);
                AddFurniturePart(root.transform, "BookC", PrimitiveType.Cube, new Vector3(0.03f, -0.02f, -0.34f), new Vector3(0.12f, 0.30f, 0.10f), new Color(0.75f, 0.62f, 0.24f), editable, editableIndex);
            }
            else if (ContainsAny(label, "plant", "\u690d\u7269", "\u89b3\u8449"))
            {
                AddFurniturePart(root.transform, "Pot", PrimitiveType.Cylinder, new Vector3(0f, -0.30f, 0f), new Vector3(0.5f, 0.35f, 0.5f), new Color(0.48f, 0.30f, 0.20f), editable, editableIndex);
                AddFurniturePart(root.transform, "Stem", PrimitiveType.Cylinder, new Vector3(0f, 0.08f, 0f), new Vector3(0.08f, 0.62f, 0.08f), new Color(0.35f, 0.55f, 0.28f), editable, editableIndex);
                AddFurniturePart(root.transform, "LeafA", PrimitiveType.Sphere, new Vector3(-0.18f, 0.34f, 0f), new Vector3(0.46f, 0.25f, 0.30f), color, editable, editableIndex);
                AddFurniturePart(root.transform, "LeafB", PrimitiveType.Sphere, new Vector3(0.18f, 0.48f, 0.02f), new Vector3(0.46f, 0.25f, 0.30f), color, editable, editableIndex);
                AddFurniturePart(root.transform, "LeafC", PrimitiveType.Sphere, new Vector3(0f, 0.62f, -0.12f), new Vector3(0.40f, 0.24f, 0.28f), Color.Lerp(color, Color.white, 0.1f), editable, editableIndex);
            }
            else if (ContainsAny(label, "lamp", "light", "\u7167\u660e", "\u30e9\u30a4\u30c8", "\u30e9\u30f3\u30d7"))
            {
                AddFurniturePart(root.transform, "Pole", PrimitiveType.Cylinder, new Vector3(0f, -0.12f, 0f), new Vector3(0.12f, 0.78f, 0.12f), new Color(0.55f, 0.55f, 0.58f), editable, editableIndex);
                AddFurniturePart(root.transform, "Shade", PrimitiveType.Sphere, new Vector3(0f, 0.48f, 0f), new Vector3(0.72f, 0.42f, 0.72f), color, editable, editableIndex);
            }
            else if (ContainsAny(label, "fridge", "refrigerator", "\u51b7\u8535", "\u51b0\u7bb1"))
            {
                AddFurniturePart(root.transform, "Body", PrimitiveType.Cube, Vector3.zero, new Vector3(1.0f, 1.0f, 1.0f), color, editable, editableIndex);
                AddFurniturePart(root.transform, "FreezerDoor", PrimitiveType.Cube, new Vector3(0f, 0.24f, -0.52f), new Vector3(0.94f, 0.42f, 0.05f), Color.Lerp(color, Color.white, 0.12f), editable, editableIndex);
                AddFurniturePart(root.transform, "FridgeDoor", PrimitiveType.Cube, new Vector3(0f, -0.25f, -0.52f), new Vector3(0.94f, 0.50f, 0.05f), Color.Lerp(color, Color.white, 0.06f), editable, editableIndex);
                AddFurniturePart(root.transform, "Handle", PrimitiveType.Cube, new Vector3(0.42f, 0.05f, -0.52f), new Vector3(0.06f, 0.62f, 0.06f), new Color(0.65f, 0.68f, 0.70f), editable, editableIndex);
            }
            else if (ContainsAny(label, "door", "\u30c9\u30a2", "\u95e8", "\u9580"))
            {
                AddFurniturePart(root.transform, "Panel", PrimitiveType.Cube, Vector3.zero, new Vector3(1.0f, 1.0f, 1.0f), color, editable, editableIndex);
                AddFurniturePart(root.transform, "Knob", PrimitiveType.Sphere, new Vector3(0.38f, 0f, -0.55f), new Vector3(0.12f, 0.12f, 0.12f), new Color(0.86f, 0.70f, 0.38f), editable, editableIndex);
            }
            else if (ContainsAny(label, "window", "balcony", "\u7a93", "\u7a97", "\u30d9\u30e9\u30f3\u30c0"))
            {
                AddFurniturePart(root.transform, "Glass", PrimitiveType.Cube, Vector3.zero, new Vector3(1.0f, 1.0f, 1.0f), color, editable, editableIndex);
                AddFurniturePart(root.transform, "FrameH", PrimitiveType.Cube, new Vector3(0f, 0f, -0.06f), new Vector3(1.0f, 0.08f, 0.08f), Color.white, editable, editableIndex);
                AddFurniturePart(root.transform, "FrameV", PrimitiveType.Cube, new Vector3(0f, 0f, -0.07f), new Vector3(0.08f, 1.0f, 0.08f), Color.white, editable, editableIndex);
                AddFurniturePart(root.transform, "Sill", PrimitiveType.Cube, new Vector3(0f, -0.56f, -0.06f), new Vector3(1.08f, 0.08f, 0.22f), new Color(0.72f, 0.68f, 0.58f), editable, editableIndex);
            }
            else
            {
                AddFurniturePart(root.transform, "Generic", RoomSpecCatalog.ParsePrimitiveType(anchor.primitiveShape), Vector3.zero, Vector3.one, color, editable, editableIndex);
            }

            return root;
        }

        private void CreateFurnitureModelFromParts(Transform root, List<VisualObjectSpec> parts, Color fallbackColor, bool editable, int editableIndex)
        {
            var minLocalBottom = float.PositiveInfinity;
            for (int i = 0; i < parts.Count && i < 12; i++)
            {
                var part = parts[i];
                if (part == null)
                {
                    continue;
                }

                var localScaleFactor = part.scale == default ? Vector3.one * 0.25f : ClampVisualPartScale(part.scale);
                minLocalBottom = Mathf.Min(minLocalBottom, part.localPosition.y - localScaleFactor.y * 0.5f);
            }

            var yOffset = float.IsPositiveInfinity(minLocalBottom)
                ? 0f
                : -0.5f - minLocalBottom;

            for (int i = 0; i < parts.Count && i < 12; i++)
            {
                var part = parts[i];
                if (part == null)
                {
                    continue;
                }

                var shape = ResolveShape(part.primitiveShape);
                var color = string.IsNullOrWhiteSpace(part.colorHex)
                    ? fallbackColor
                    : RoomSpecCatalog.Hex(part.colorHex);
                var scale = part.scale == default ? Vector3.one * 0.25f : ClampVisualPartScale(part.scale);
                var name = string.IsNullOrWhiteSpace(part.label) ? $"Part_{i + 1}" : part.label;
                var localPosition = part.localPosition + Vector3.up * yOffset;
                AddFurniturePart(root, name, shape, localPosition, scale, color, editable, editableIndex);
            }
        }

        private Vector3 ClampVisualPartScale(Vector3 scale)
        {
            return new Vector3(
                Mathf.Clamp(scale.x, 0.04f, 1.2f),
                Mathf.Clamp(scale.y, 0.04f, 1.2f),
                Mathf.Clamp(scale.z, 0.04f, 1.2f));
        }

        private void AddFurniturePart(Transform root, string name, PrimitiveType type, Vector3 localPosition, Vector3 localScaleFactor, Color color, bool editable, int editableIndex)
        {
            var part = CreatePrimitiveLocal(
                name,
                type,
                localPosition,
                localScaleFactor,
                color,
                root);
            if (editable)
            {
                var interactable = part.AddComponent<RoomAnchorInteractable>();
                interactable.Index = editableIndex;
            }
        }

        private Vector3 GetAnchorScaleFromRoot(Transform root)
        {
            var id = root.name.StartsWith("Furniture_", StringComparison.Ordinal)
                ? root.name.Substring("Furniture_".Length)
                : string.Empty;
            var anchors = RoomSpecCatalog.CurrentRoom.anchors;
            for (int i = 0; i < anchors.Count; i++)
            {
                if (string.Equals(anchors[i].id, id, StringComparison.Ordinal))
                {
                    return GetFurnitureRenderScale(anchors[i]);
                }
            }

            return Vector3.one;
        }

        private Vector3 GetFurnitureRenderScale(AnchorDefinition anchor)
        {
            if (anchor == null)
            {
                return Vector3.one;
            }

            var label = ((anchor.label ?? string.Empty) + " " + (anchor.id ?? string.Empty)).ToLowerInvariant();
            var scale = anchor.scale;

            if (ContainsAny(label, "door", "\u30c9\u30a2", "\u95e8", "\u9580"))
            {
                return new Vector3(Mathf.Max(scale.x, 0.95f), Mathf.Max(scale.y, 2.0f), Mathf.Max(scale.z, 0.08f));
            }

            if (ContainsAny(label, "window", "balcony", "\u7a93", "\u7a97", "\u30d9\u30e9\u30f3\u30c0"))
            {
                return new Vector3(Mathf.Max(scale.x, 1.35f), Mathf.Max(scale.y, 0.95f), Mathf.Max(scale.z, 0.08f));
            }

            if (ContainsAny(label, "toilet", "wc", "\u9a6c\u6876", "\u99ac\u6876", "\u4fbf\u5668", "\u30c8\u30a4\u30ec"))
            {
                return new Vector3(Mathf.Max(scale.x, 0.8f), Mathf.Max(scale.y, 0.75f), Mathf.Max(scale.z, 0.9f));
            }

            if (ContainsAny(label, "sink", "\u6d17\u9762", "\u6d41\u3057"))
            {
                return new Vector3(Mathf.Max(scale.x, 1.0f), Mathf.Max(scale.y, 0.85f), Mathf.Max(scale.z, 0.7f));
            }

            if (ContainsAny(label, "television", "tv", "monitor", "screen", "\u30c6\u30ec\u30d3", "\u7535\u89c6", "\u96fb\u8996"))
            {
                return new Vector3(Mathf.Max(scale.x, 1.25f), Mathf.Max(scale.y, 0.85f), Mathf.Max(scale.z, 0.22f));
            }

            if (ContainsAny(label, "computer", "pc", "laptop", "desktop", "keyboard", "comput", "omputer", "macbook", "\u30b3\u30f3\u30d4\u30e5\u30fc\u30bf", "\u7535\u8111", "\u96fb\u8133"))
            {
                return new Vector3(Mathf.Max(scale.x, 1.05f), Mathf.Max(scale.y, 0.75f), Mathf.Max(scale.z, 0.60f));
            }

            if (ContainsAny(label, "air conditioner", "aircon", "air conditioning", "ac unit", "a/c"))
            {
                return new Vector3(Mathf.Max(scale.x, 1.15f), Mathf.Max(scale.y, 0.35f), Mathf.Max(scale.z, 0.18f));
            }

            if (ContainsAny(label, "stove", "cooktop", "range", "hob"))
            {
                return new Vector3(Mathf.Max(scale.x, 0.95f), Mathf.Max(scale.y, 0.72f), Mathf.Max(scale.z, 0.72f));
            }

            if (ContainsAny(label, "bath", "bathtub", "tub", "\u6d74\u69fd", "\u98a8\u5442"))
            {
                return new Vector3(Mathf.Max(scale.x, 1.65f), Mathf.Max(scale.y, 0.55f), Mathf.Max(scale.z, 0.9f));
            }

            if (ContainsAny(label, "bed", "\u30d9\u30c3\u30c9", "\u5e8a"))
            {
                return new Vector3(Mathf.Max(scale.x, 1.9f), Mathf.Max(scale.y, 0.55f), Mathf.Max(scale.z, 1.35f));
            }

            if (ContainsAny(label, "sofa", "couch", "\u30bd\u30d5\u30a1"))
            {
                return new Vector3(Mathf.Max(scale.x, 1.55f), Mathf.Max(scale.y, 0.75f), Mathf.Max(scale.z, 0.8f));
            }

            if (ContainsAny(label, "table", "desk", "counter", "\u673a", "\u30c6\u30fc\u30d6\u30eb", "\u30ab\u30a6\u30f3\u30bf\u30fc"))
            {
                return new Vector3(Mathf.Max(scale.x, 1.25f), Mathf.Max(scale.y, 0.75f), Mathf.Max(scale.z, 0.75f));
            }

            if (ContainsAny(label, "chair", "\u6905\u5b50", "\u30a4\u30b9"))
            {
                return new Vector3(Mathf.Max(scale.x, 0.65f), Mathf.Max(scale.y, 0.85f), Mathf.Max(scale.z, 0.65f));
            }

            return new Vector3(Mathf.Max(scale.x, 0.55f), Mathf.Max(scale.y, 0.55f), Mathf.Max(scale.z, 0.55f));
        }

        private void CreatePlacementGhost(AnchorDefinition anchor, Transform parent)
        {
            var color = isBuilderDragging
                ? new Color(1.0f, 0.78f, 0.22f, 0.45f)
                : new Color(0.35f, 0.85f, 1.0f, 0.28f);
            CreatePlacementGhost(anchor, parent, color);
        }

        private void CreatePlacementGhost(AnchorDefinition anchor, Transform parent, Color color)
        {
            var renderScale = GetFurnitureRenderScale(anchor);
            var width = Mathf.Max(0.55f, renderScale.x * 1.18f);
            var depth = Mathf.Max(0.55f, renderScale.z * 1.18f);
            var y = -renderScale.y * 0.5f + 0.035f;
            var line = 0.035f;

            CreateGhostPart(parent, "GhostFront", new Vector3(0f, y, -depth * 0.5f), new Vector3(width * 0.32f, line, line), color);
            CreateGhostPart(parent, "GhostBack", new Vector3(0f, y, depth * 0.5f), new Vector3(width * 0.32f, line, line), color);
            CreateGhostPart(parent, "GhostLeft", new Vector3(-width * 0.5f, y, 0f), new Vector3(line, line, depth * 0.32f), color);
            CreateGhostPart(parent, "GhostRight", new Vector3(width * 0.5f, y, 0f), new Vector3(line, line, depth * 0.32f), color);

            var cornerScale = Vector3.one * 0.085f;
            CreateGhostPart(parent, "GhostCornerA", new Vector3(-width * 0.5f, y, -depth * 0.5f), cornerScale, color);
            CreateGhostPart(parent, "GhostCornerB", new Vector3(width * 0.5f, y, -depth * 0.5f), cornerScale, color);
            CreateGhostPart(parent, "GhostCornerC", new Vector3(-width * 0.5f, y, depth * 0.5f), cornerScale, color);
            CreateGhostPart(parent, "GhostCornerD", new Vector3(width * 0.5f, y, depth * 0.5f), cornerScale, color);
        }

        private void CreateGhostPart(Transform parent, string name, Vector3 localPosition, Vector3 localScale, Color color)
        {
            var go = CreatePrimitiveLocal(name, PrimitiveType.Cube, localPosition, localScale, color, parent);
            var collider = go.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }
        }

        private void CreateBuilderFeedbackPulse(int index, AnchorDefinition anchor, Transform parent)
        {
            if (builderFeedbackAnchorIndex != index || Time.unscaledTime > builderFeedbackUntil)
            {
                return;
            }

            var renderScale = GetFurnitureRenderScale(anchor);
            var width = Mathf.Max(0.42f, renderScale.x * 1.12f);
            var depth = Mathf.Max(0.42f, renderScale.z * 1.12f);
            var pulse = CreatePrimitiveLocal(
                "DropImpactPulse",
                PrimitiveType.Cylinder,
                new Vector3(0f, -renderScale.y * 0.5f + 0.055f, 0f),
                new Vector3(width, 0.018f, depth),
                new Color(1f, 0.72f, 0.18f, 0.42f),
                parent);
            var collider = pulse.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }
        }

        private void CreateSelectionRing(AnchorDefinition anchor, Transform parent)
        {
            var renderScale = GetFurnitureRenderScale(anchor);
            var width = Mathf.Max(0.42f, renderScale.x * 1.08f);
            var depth = Mathf.Max(0.42f, renderScale.z * 1.08f);
            var ring = CreatePrimitiveLocal(
                "SelectionRing",
                PrimitiveType.Cylinder,
                new Vector3(0f, -renderScale.y * 0.5f + 0.04f, 0f),
                new Vector3(width, 0.025f, depth),
                new Color(0.95f, 0.82f, 0.36f, 0.9f),
                parent);
            var collider = ring.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }
        }

        private void CreateBuilderGizmo(AnchorDefinition anchor, Transform parent)
        {
            var height = Mathf.Max(0.6f, GetFurnitureRenderScale(anchor).y * 0.7f);
            var center = new Vector3(0f, height, 0f);

            if (builderToolMode == BuilderToolMode.Move)
            {
                AddBuilderGizmoPart(parent, "MoveX", PrimitiveType.Cylinder, center + new Vector3(0.75f, 0f, 0f), new Vector3(0.035f, 0.75f, 0.035f), new Color(0.95f, 0.18f, 0.16f), new Vector3(0f, 0f, 90f), Vector3.right);
                AddBuilderGizmoPart(parent, "MoveY", PrimitiveType.Cylinder, center + new Vector3(0f, 0.75f, 0f), new Vector3(0.035f, 0.75f, 0.035f), new Color(0.25f, 0.9f, 0.25f), Vector3.zero, Vector3.up);
                AddBuilderGizmoPart(parent, "MoveZ", PrimitiveType.Cylinder, center + new Vector3(0f, 0f, 0.75f), new Vector3(0.035f, 0.75f, 0.035f), new Color(0.2f, 0.45f, 1f), new Vector3(90f, 0f, 0f), Vector3.forward);
                AddBuilderGizmoPart(parent, "MovePlane", PrimitiveType.Cube, center, Vector3.one * 0.18f, new Color(0.95f, 0.82f, 0.25f), Vector3.zero, Vector3.zero);
            }
            else if (builderToolMode == BuilderToolMode.Rotate)
            {
                AddBuilderGizmoPart(parent, "RotateY", PrimitiveType.Cylinder, center, new Vector3(1.45f, 0.018f, 1.45f), new Color(0.95f, 0.72f, 0.18f), Vector3.zero, Vector3.up);
                AddBuilderGizmoPart(parent, "RotateHandle", PrimitiveType.Sphere, center + new Vector3(0.72f, 0f, 0f), Vector3.one * 0.16f, new Color(0.95f, 0.72f, 0.18f), Vector3.zero, Vector3.up);
            }
            else if (builderToolMode == BuilderToolMode.Scale)
            {
                AddBuilderGizmoPart(parent, "ScaleUniform", PrimitiveType.Cube, center + new Vector3(0.0f, 0.55f, 0.0f), Vector3.one * 0.22f, new Color(0.95f, 0.82f, 0.25f), Vector3.zero, Vector3.zero);
                AddBuilderGizmoPart(parent, "ScaleX", PrimitiveType.Cube, center + new Vector3(0.65f, 0f, 0f), Vector3.one * 0.16f, new Color(0.95f, 0.18f, 0.16f), Vector3.zero, Vector3.right);
                AddBuilderGizmoPart(parent, "ScaleY", PrimitiveType.Cube, center + new Vector3(0f, 0.85f, 0f), Vector3.one * 0.16f, new Color(0.25f, 0.9f, 0.25f), Vector3.zero, Vector3.up);
                AddBuilderGizmoPart(parent, "ScaleZ", PrimitiveType.Cube, center + new Vector3(0f, 0f, 0.65f), Vector3.one * 0.16f, new Color(0.2f, 0.45f, 1f), Vector3.zero, Vector3.forward);
            }
        }

        private void AddBuilderGizmoPart(Transform parent, string name, PrimitiveType shape, Vector3 localPosition, Vector3 localScale, Color color, Vector3 localEuler, Vector3 axis)
        {
            var go = CreatePrimitiveLocal(name, shape, localPosition, localScale, color, parent);
            go.transform.localRotation = Quaternion.Euler(localEuler);
            var handle = go.AddComponent<BuilderGizmoHandle>();
            handle.AnchorIndex = selectedBuilderAnchorIndex;
            handle.PrimitiveIndex = selectedRoomPrimitiveIndex;
            handle.Axis = axis;
            handle.ToolName = builderToolMode.ToString();
        }

        private void BuildStudyRoom()
        {
            if (roomRoot != null)
            {
                Destroy(roomRoot.gameObject);
            }

            roomRoot = new GameObject("StudyRoomRuntime").transform;
            var roomSpec = RoomSpecCatalog.CurrentRoom;

            for (int i = 0; i < roomSpec.environmentPrimitives.Count; i++)
            {
                CreateEnvironmentPrimitive(roomSpec.environmentPrimitives[i], roomRoot);
            }

            for (int i = 0; i < RoomSpecCatalog.AnchorCount; i++)
            {
                CreateAnchorPrimitive(RoomSpecCatalog.Anchors[i], roomRoot);
            }

            for (int i = 0; i < currentItems.Count; i++)
            {
                CreateMnemonicObject(currentItems[i], i);
            }
        }

        private void CreateMnemonicObject(MnemonicItemData item, int index)
        {
            var anchor = RoomSpecCatalog.GetAnchor(item.anchorId);
            var position = anchor.position + anchor.mnemonicOffset + new Vector3(0f, (index % 2) * 0.08f, 0f);
            var objectColor = RoomSpecCatalog.Hex(item.colorHex);
            var go = CreatePrimitive($"MnemonicMarker_{item.word}", PrimitiveType.Sphere, position + new Vector3(0f, 0.04f, 0f), Vector3.one * 0.12f, objectColor, roomRoot);

            var interactable = go.AddComponent<StudyInteractable>();
            interactable.Data = item;

            var renderer = go.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material.EnableKeyword("_EMISSION");
                renderer.material.SetColor("_EmissionColor", objectColor * 0.55f);
            }

            var hoverSpin = go.AddComponent<HoverSpinAnimation>();
            hoverSpin.basePosition = go.transform.position;
            hoverSpin.rotationSpeed = 55f;
            hoverSpin.bobAmplitude = 0.035f;
            hoverSpin.bobFrequency = 1.3f + index * 0.05f;

            if (!showAbstractMnemonicProps)
            {
                return;
            }

            if (item.visualObjects == null || item.visualObjects.Count == 0)
            {
                return;
            }

            var visualObjects = item.visualObjects;
            CreateMnemonicSceneFrame(item, position, objectColor, Mathf.Min(visualObjects.Count, 5));
            for (int i = 0; i < visualObjects.Count; i++)
            {
                CreateMnemonicVisualObject(item, visualObjects[i], position, index + i);
            }
        }

        private void CreateMnemonicSceneFrame(MnemonicItemData item, Vector3 origin, Color color, int visualCount)
        {
            var root = new GameObject($"MnemonicSceneFrame_{item.word}");
            root.transform.SetParent(roomRoot);
            root.transform.position = origin + new Vector3(0f, -0.18f, 0f);
            var platformColor = Color.Lerp(color, new Color(0.08f, 0.10f, 0.14f), 0.55f);
            platformColor.a = 0.36f;
            AddMnemonicPropPart(root.transform, "SceneBase", PrimitiveType.Cylinder, Vector3.zero, new Vector3(1.65f, 0.035f, 1.05f), platformColor, item);
            AddMnemonicPropPart(root.transform, "CueBeam", PrimitiveType.Cylinder, new Vector3(0f, 0.35f, 0f), new Vector3(0.035f, 0.72f, 0.035f), Color.Lerp(color, Color.white, 0.35f), item, true);

            if (visualCount >= 3)
            {
                AddMnemonicPropPart(root.transform, "SceneBackplate", PrimitiveType.Cube, new Vector3(0f, 0.46f, 0.46f), new Vector3(1.35f, 0.035f, 0.08f), platformColor, item);
            }
        }

        private void CreateMnemonicVisualObject(MnemonicItemData item, VisualObjectSpec spec, Vector3 origin, int visualIndex)
        {
            var shape = ResolveShape(spec.primitiveShape);
            var color = RoomSpecCatalog.Hex(string.IsNullOrWhiteSpace(spec.colorHex) ? item.colorHex : spec.colorHex);
            var scale = spec.scale == default ? Vector3.one * 0.22f : spec.scale;
            var position = origin + spec.localPosition + new Vector3((visualIndex % 2) * 0.08f, 0f, 0f);
            var root = new GameObject($"VisualCue_{item.word}_{visualIndex}");
            root.transform.SetParent(roomRoot);
            root.transform.position = position;

            if (!TryCreateSemanticMnemonicProp(item, spec, root.transform, color, scale))
            {
                AddMnemonicPropPart(root.transform, "GenericProp", shape, Vector3.zero, Vector3.one, color, item);
            }

            if (ShouldAnimateVisualObject(spec.effect))
            {
                var hover = root.AddComponent<HoverSpinAnimation>();
                hover.basePosition = position;
                hover.rotationSpeed = string.Equals(spec.effect, "locked", StringComparison.OrdinalIgnoreCase) ? 0f : 24f;
                hover.bobAmplitude = string.Equals(spec.effect, "float", StringComparison.OrdinalIgnoreCase) ? 0.10f : 0.025f;
                hover.bobFrequency = 1.0f + visualIndex * 0.07f;
            }
        }

        private bool TryCreateSemanticMnemonicProp(MnemonicItemData item, VisualObjectSpec spec, Transform root, Color color, Vector3 baseScale)
        {
            var text = BuildMnemonicVisualSearchText(item, spec);
            var scale = NormalizePropScale(baseScale);
            root.localScale = scale;

            if (ContainsAny(text, "thread", "string", "wire", "rope", "line", "thin", "tenuous", "\u7cf8", "\u7dda", "\u7d30"))
            {
                AddMnemonicPropPart(root, "Thread", PrimitiveType.Cylinder, new Vector3(0f, 0.20f, 0f), new Vector3(0.10f, 2.3f, 0.10f), new Color(0.92f, 0.90f, 0.78f), item);
                AddMnemonicPropPart(root, "TinyWeight", PrimitiveType.Sphere, new Vector3(0f, -0.28f, 0f), new Vector3(0.42f, 0.42f, 0.42f), color, item);
                return true;
            }

            if (ContainsAny(text, "fire", "flame", "burn", "spark", "ignite", "blaze", "\u706b", "\u708e"))
            {
                AddMnemonicPropPart(root, "FlameCore", PrimitiveType.Capsule, new Vector3(0f, 0.12f, 0f), new Vector3(0.42f, 1.0f, 0.42f), new Color(1.0f, 0.42f, 0.10f), item, true);
                AddMnemonicPropPart(root, "FlameGlow", PrimitiveType.Sphere, new Vector3(0f, 0.02f, 0f), new Vector3(0.72f, 0.42f, 0.72f), new Color(1.0f, 0.82f, 0.18f), item, true);
                AddMnemonicPropPart(root, "Ember", PrimitiveType.Sphere, new Vector3(0.28f, -0.20f, -0.05f), new Vector3(0.18f, 0.18f, 0.18f), new Color(1.0f, 0.26f, 0.06f), item, true);
                return true;
            }

            if (ContainsAny(text, "lantern", "lamp", "light", "glow", "luminous", "\u30e9\u30f3\u30bf\u30f3", "\u30e9\u30a4\u30c8", "\u5149"))
            {
                AddMnemonicPropPart(root, "LanternGlass", PrimitiveType.Sphere, new Vector3(0f, 0.10f, 0f), new Vector3(1.0f, 0.85f, 1.0f), new Color(1.0f, 0.82f, 0.28f), item, true);
                AddMnemonicPropPart(root, "LanternTop", PrimitiveType.Cube, new Vector3(0f, 0.40f, 0f), new Vector3(0.72f, 0.16f, 0.72f), new Color(0.42f, 0.28f, 0.18f), item);
                AddMnemonicPropPart(root, "LanternBase", PrimitiveType.Cube, new Vector3(0f, -0.22f, 0f), new Vector3(0.72f, 0.12f, 0.72f), new Color(0.42f, 0.28f, 0.18f), item);
                return true;
            }

            if (ContainsAny(text, "smoke", "fume", "poison", "toxic", "cloud", "mist", "\u7159", "\u6bd2", "\u96f2"))
            {
                AddMnemonicPropPart(root, "SmokeA", PrimitiveType.Sphere, new Vector3(-0.16f, 0.02f, 0f), new Vector3(0.72f, 0.52f, 0.72f), new Color(0.42f, 0.78f, 0.50f), item, true);
                AddMnemonicPropPart(root, "SmokeB", PrimitiveType.Sphere, new Vector3(0.12f, 0.25f, 0.03f), new Vector3(0.62f, 0.48f, 0.62f), new Color(0.54f, 0.86f, 0.62f), item, true);
                AddMnemonicPropPart(root, "SmokeC", PrimitiveType.Sphere, new Vector3(0.24f, 0.48f, -0.03f), new Vector3(0.48f, 0.36f, 0.48f), new Color(0.74f, 0.90f, 0.70f), item, true);
                return true;
            }

            if (ContainsAny(text, "jar", "bottle", "spice", "vial", "potion", "\u74f6", "\u7f50", "\u30b8\u30e3\u30fc"))
            {
                AddMnemonicPropPart(root, "BottleBody", PrimitiveType.Cylinder, new Vector3(0f, 0.02f, 0f), new Vector3(0.62f, 1.15f, 0.62f), color, item);
                AddMnemonicPropPart(root, "BottleCap", PrimitiveType.Cylinder, new Vector3(0f, 0.42f, 0f), new Vector3(0.46f, 0.18f, 0.46f), new Color(0.25f, 0.25f, 0.28f), item);
                return true;
            }

            if (ContainsAny(text, "plate", "plain", "dish", "austere", "\u76bf", "\u30d7\u30ec\u30fc\u30c8"))
            {
                AddMnemonicPropPart(root, "Plate", PrimitiveType.Cylinder, new Vector3(0f, -0.06f, 0f), new Vector3(1.15f, 0.10f, 1.15f), new Color(0.92f, 0.92f, 0.86f), item);
                AddMnemonicPropPart(root, "HardLight", PrimitiveType.Cube, new Vector3(0f, 0.22f, 0f), new Vector3(0.62f, 0.05f, 0.62f), new Color(1.0f, 0.96f, 0.72f), item, true);
                return true;
            }

            if (ContainsAny(text, "person", "guest", "people", "crowd", "friend", "social", "gregarious", "\u4eba", "\u53cb", "\u5ba2"))
            {
                AddMnemonicPropPart(root, "BodyA", PrimitiveType.Capsule, new Vector3(-0.16f, 0f, 0f), new Vector3(0.34f, 0.95f, 0.34f), color, item);
                AddMnemonicPropPart(root, "HeadA", PrimitiveType.Sphere, new Vector3(-0.16f, 0.58f, 0f), new Vector3(0.28f, 0.28f, 0.28f), new Color(0.94f, 0.70f, 0.52f), item);
                AddMnemonicPropPart(root, "BodyB", PrimitiveType.Capsule, new Vector3(0.22f, -0.04f, 0.08f), new Vector3(0.30f, 0.82f, 0.30f), Color.Lerp(color, Color.white, 0.22f), item);
                AddMnemonicPropPart(root, "HeadB", PrimitiveType.Sphere, new Vector3(0.22f, 0.48f, 0.08f), new Vector3(0.24f, 0.24f, 0.24f), new Color(0.94f, 0.70f, 0.52f), item);
                return true;
            }

            if (ContainsAny(text, "book", "paper", "note", "word", "study", "\u672c", "\u7d19", "\u30ce\u30fc\u30c8"))
            {
                AddMnemonicPropPart(root, "OpenBookLeft", PrimitiveType.Cube, new Vector3(-0.12f, 0f, 0f), new Vector3(0.52f, 0.08f, 0.72f), new Color(0.92f, 0.88f, 0.76f), item);
                AddMnemonicPropPart(root, "OpenBookRight", PrimitiveType.Cube, new Vector3(0.12f, 0f, 0f), new Vector3(0.52f, 0.08f, 0.72f), new Color(0.96f, 0.92f, 0.80f), item);
                AddMnemonicPropPart(root, "Spine", PrimitiveType.Cube, new Vector3(0f, 0.04f, 0f), new Vector3(0.06f, 0.10f, 0.75f), new Color(0.36f, 0.20f, 0.14f), item);
                return true;
            }

            if (ContainsAny(text, "crack", "broken", "shatter", "fragment", "fragile", "\u5272", "\u58ca", "\u7834"))
            {
                AddMnemonicPropPart(root, "ShardA", PrimitiveType.Cube, new Vector3(-0.12f, 0.08f, 0f), new Vector3(0.08f, 0.78f, 0.08f), color, item, false, new Vector3(0f, 0f, 28f));
                AddMnemonicPropPart(root, "ShardB", PrimitiveType.Cube, new Vector3(0.12f, 0.02f, 0f), new Vector3(0.08f, 0.68f, 0.08f), Color.Lerp(color, Color.white, 0.2f), item, false, new Vector3(0f, 0f, -22f));
                return true;
            }

            if (ContainsAny(text, "water", "drip", "rain", "melt", "ephemeral", "\u6c34", "\u96e8", "\u6ef4"))
            {
                AddMnemonicPropPart(root, "DropA", PrimitiveType.Sphere, new Vector3(-0.12f, 0.28f, 0f), new Vector3(0.32f, 0.42f, 0.32f), new Color(0.35f, 0.72f, 1f), item, true);
                AddMnemonicPropPart(root, "DropB", PrimitiveType.Sphere, new Vector3(0.15f, 0.02f, 0.05f), new Vector3(0.26f, 0.34f, 0.26f), new Color(0.45f, 0.82f, 1f), item, true);
                AddMnemonicPropPart(root, "Puddle", PrimitiveType.Cylinder, new Vector3(0f, -0.22f, 0f), new Vector3(0.88f, 0.06f, 0.52f), new Color(0.32f, 0.62f, 0.82f), item);
                return true;
            }

            if (ContainsAny(text, "ice", "frost", "freeze", "snow", "cold", "\u6c37", "\u96ea", "\u51b7"))
            {
                AddMnemonicPropPart(root, "IceBlock", PrimitiveType.Cube, new Vector3(0f, 0.04f, 0f), new Vector3(0.82f, 0.62f, 0.82f), new Color(0.62f, 0.88f, 1.0f), item, true, new Vector3(0f, 18f, 0f));
                AddMnemonicPropPart(root, "FrostEdgeA", PrimitiveType.Cube, new Vector3(-0.34f, 0.36f, 0.02f), new Vector3(0.08f, 0.22f, 0.72f), new Color(0.86f, 0.96f, 1.0f), item, true, new Vector3(0f, 0f, 12f));
                AddMnemonicPropPart(root, "FrostEdgeB", PrimitiveType.Cube, new Vector3(0.22f, -0.18f, -0.22f), new Vector3(0.55f, 0.06f, 0.08f), new Color(0.86f, 0.96f, 1.0f), item, true);
                return true;
            }

            if (ContainsAny(text, "chain", "lock", "locked", "shackle", "bind", "\u9396", "\u30ed\u30c3\u30af"))
            {
                AddMnemonicPropPart(root, "ChainA", PrimitiveType.Capsule, new Vector3(-0.22f, 0.18f, 0f), new Vector3(0.16f, 0.58f, 0.16f), new Color(0.60f, 0.62f, 0.66f), item, false, new Vector3(0f, 0f, 50f));
                AddMnemonicPropPart(root, "ChainB", PrimitiveType.Capsule, new Vector3(0.18f, 0.18f, 0f), new Vector3(0.16f, 0.58f, 0.16f), new Color(0.66f, 0.68f, 0.72f), item, false, new Vector3(0f, 0f, -50f));
                AddMnemonicPropPart(root, "LockBody", PrimitiveType.Cube, new Vector3(0f, -0.22f, 0f), new Vector3(0.50f, 0.36f, 0.18f), new Color(0.95f, 0.68f, 0.20f), item);
                return true;
            }

            if (ContainsAny(text, "mirror", "reflect", "reflection", "glass", "\u93e1", "\u53cd\u5c04"))
            {
                AddMnemonicPropPart(root, "MirrorGlass", PrimitiveType.Cube, new Vector3(0f, 0.06f, 0f), new Vector3(0.76f, 0.92f, 0.06f), new Color(0.62f, 0.82f, 0.95f), item, true);
                AddMnemonicPropPart(root, "MirrorFrameH", PrimitiveType.Cube, new Vector3(0f, 0.54f, -0.03f), new Vector3(0.88f, 0.08f, 0.10f), new Color(0.72f, 0.55f, 0.34f), item);
                AddMnemonicPropPart(root, "MirrorFrameV", PrimitiveType.Cube, new Vector3(-0.44f, 0.06f, -0.03f), new Vector3(0.08f, 0.98f, 0.10f), new Color(0.72f, 0.55f, 0.34f), item);
                return true;
            }

            if (ContainsAny(text, "clock", "time", "timer", "hour", "temporal", "\u6642", "\u6642\u9593", "\u6642\u8a08"))
            {
                AddMnemonicPropPart(root, "ClockFace", PrimitiveType.Cylinder, new Vector3(0f, 0.08f, 0f), new Vector3(0.82f, 0.10f, 0.82f), new Color(0.92f, 0.88f, 0.74f), item, false, new Vector3(90f, 0f, 0f));
                AddMnemonicPropPart(root, "HourHand", PrimitiveType.Cube, new Vector3(0.08f, 0.10f, -0.04f), new Vector3(0.08f, 0.38f, 0.04f), new Color(0.12f, 0.12f, 0.14f), item, false, new Vector3(0f, 0f, -35f));
                AddMnemonicPropPart(root, "MinuteHand", PrimitiveType.Cube, new Vector3(-0.10f, 0.11f, -0.04f), new Vector3(0.06f, 0.52f, 0.04f), new Color(0.12f, 0.12f, 0.14f), item, false, new Vector3(0f, 0f, 55f));
                return true;
            }

            if (ContainsAny(text, "feather", "lightweight", "soft", "gentle", "\u7fbd", "\u8efd"))
            {
                AddMnemonicPropPart(root, "FeatherSpine", PrimitiveType.Capsule, new Vector3(0f, 0.04f, 0f), new Vector3(0.08f, 0.92f, 0.08f), new Color(0.92f, 0.88f, 0.72f), item, false, new Vector3(0f, 0f, -25f));
                AddMnemonicPropPart(root, "FeatherLeft", PrimitiveType.Cube, new Vector3(-0.18f, 0.08f, 0f), new Vector3(0.34f, 0.08f, 0.04f), new Color(0.78f, 0.86f, 0.92f), item, false, new Vector3(0f, 0f, -18f));
                AddMnemonicPropPart(root, "FeatherRight", PrimitiveType.Cube, new Vector3(0.18f, 0.18f, 0f), new Vector3(0.34f, 0.08f, 0.04f), new Color(0.86f, 0.92f, 0.96f), item, false, new Vector3(0f, 0f, 18f));
                return true;
            }

            if (ContainsAny(text, "stone", "rock", "weight", "heavy", "burden", "\u77f3", "\u91cd"))
            {
                AddMnemonicPropPart(root, "StoneBody", PrimitiveType.Sphere, new Vector3(0f, -0.04f, 0f), new Vector3(0.84f, 0.62f, 0.76f), new Color(0.45f, 0.46f, 0.45f), item);
                AddMnemonicPropPart(root, "StoneFacet", PrimitiveType.Cube, new Vector3(0.20f, 0.20f, -0.06f), new Vector3(0.32f, 0.08f, 0.34f), new Color(0.62f, 0.62f, 0.58f), item, false, new Vector3(0f, 0f, -24f));
                return true;
            }

            if (ContainsAny(text, "arrow", "path", "point", "direction", "route", "\u77e2", "\u65b9\u5411"))
            {
                AddMnemonicPropPart(root, "ArrowShaft", PrimitiveType.Cube, new Vector3(-0.10f, 0.02f, 0f), new Vector3(0.78f, 0.08f, 0.08f), color, item);
                AddMnemonicPropPart(root, "ArrowHeadA", PrimitiveType.Cube, new Vector3(0.34f, 0.14f, 0f), new Vector3(0.34f, 0.08f, 0.08f), color, item, false, new Vector3(0f, 0f, 38f));
                AddMnemonicPropPart(root, "ArrowHeadB", PrimitiveType.Cube, new Vector3(0.34f, -0.10f, 0f), new Vector3(0.34f, 0.08f, 0.08f), color, item, false, new Vector3(0f, 0f, -38f));
                return true;
            }

            if (ContainsAny(text, "eye", "watch", "observe", "look", "visible", "\u76ee", "\u898b"))
            {
                AddMnemonicPropPart(root, "EyeWhite", PrimitiveType.Sphere, new Vector3(0f, 0.06f, 0f), new Vector3(0.86f, 0.42f, 0.18f), new Color(0.96f, 0.96f, 0.90f), item);
                AddMnemonicPropPart(root, "Iris", PrimitiveType.Sphere, new Vector3(0f, 0.06f, -0.08f), new Vector3(0.26f, 0.26f, 0.08f), color, item, true);
                return true;
            }

            if (ContainsAny(text, "hand", "grab", "hold", "touch", "grasp", "\u624b", "\u63b4"))
            {
                AddMnemonicPropPart(root, "Palm", PrimitiveType.Sphere, new Vector3(0f, -0.06f, 0f), new Vector3(0.48f, 0.36f, 0.20f), new Color(0.94f, 0.70f, 0.52f), item);
                AddMnemonicPropPart(root, "FingerA", PrimitiveType.Capsule, new Vector3(-0.18f, 0.22f, 0f), new Vector3(0.10f, 0.46f, 0.10f), new Color(0.94f, 0.70f, 0.52f), item);
                AddMnemonicPropPart(root, "FingerB", PrimitiveType.Capsule, new Vector3(0.00f, 0.26f, 0f), new Vector3(0.10f, 0.54f, 0.10f), new Color(0.94f, 0.70f, 0.52f), item);
                AddMnemonicPropPart(root, "FingerC", PrimitiveType.Capsule, new Vector3(0.18f, 0.22f, 0f), new Vector3(0.10f, 0.46f, 0.10f), new Color(0.94f, 0.70f, 0.52f), item);
                return true;
            }

            if (ContainsAny(text, "web", "net", "mesh", "trap", "network", "\u7db2"))
            {
                AddMnemonicPropPart(root, "WebA", PrimitiveType.Cube, new Vector3(0f, 0.10f, 0f), new Vector3(0.92f, 0.035f, 0.035f), new Color(0.84f, 0.88f, 0.92f), item, false, new Vector3(0f, 0f, 0f));
                AddMnemonicPropPart(root, "WebB", PrimitiveType.Cube, new Vector3(0f, 0.10f, 0f), new Vector3(0.92f, 0.035f, 0.035f), new Color(0.84f, 0.88f, 0.92f), item, false, new Vector3(0f, 0f, 60f));
                AddMnemonicPropPart(root, "WebC", PrimitiveType.Cube, new Vector3(0f, 0.10f, 0f), new Vector3(0.92f, 0.035f, 0.035f), new Color(0.84f, 0.88f, 0.92f), item, false, new Vector3(0f, 0f, -60f));
                return true;
            }

            if (ContainsAny(text, "balance", "scale", "weigh", "justice", "equal", "\u79e4", "\u5929\u79e4"))
            {
                AddMnemonicPropPart(root, "ScaleStand", PrimitiveType.Cylinder, new Vector3(0f, -0.06f, 0f), new Vector3(0.08f, 0.70f, 0.08f), new Color(0.64f, 0.55f, 0.36f), item);
                AddMnemonicPropPart(root, "ScaleBeam", PrimitiveType.Cube, new Vector3(0f, 0.34f, 0f), new Vector3(1.05f, 0.06f, 0.06f), new Color(0.74f, 0.63f, 0.38f), item);
                AddMnemonicPropPart(root, "PanLeft", PrimitiveType.Cylinder, new Vector3(-0.45f, -0.08f, 0f), new Vector3(0.34f, 0.04f, 0.34f), new Color(0.72f, 0.65f, 0.50f), item);
                AddMnemonicPropPart(root, "PanRight", PrimitiveType.Cylinder, new Vector3(0.45f, -0.08f, 0f), new Vector3(0.34f, 0.04f, 0.34f), new Color(0.72f, 0.65f, 0.50f), item);
                return true;
            }

            if (ContainsAny(text, "coin", "money", "gold", "wealth", "price", "\u91d1", "\u30b3\u30a4\u30f3"))
            {
                AddMnemonicPropPart(root, "CoinA", PrimitiveType.Cylinder, new Vector3(-0.18f, 0.00f, 0f), new Vector3(0.36f, 0.08f, 0.36f), new Color(1.0f, 0.76f, 0.20f), item, true, new Vector3(90f, 0f, 0f));
                AddMnemonicPropPart(root, "CoinB", PrimitiveType.Cylinder, new Vector3(0.18f, 0.16f, 0f), new Vector3(0.32f, 0.08f, 0.32f), new Color(0.95f, 0.66f, 0.18f), item, true, new Vector3(90f, 0f, 0f));
                return true;
            }

            if (ContainsAny(text, "flower", "vine", "leaf", "grow", "bloom", "\u82b1", "\u8449", "\u8513"))
            {
                AddMnemonicPropPart(root, "Stem", PrimitiveType.Cylinder, new Vector3(0f, 0.00f, 0f), new Vector3(0.07f, 0.72f, 0.07f), new Color(0.32f, 0.56f, 0.28f), item);
                AddMnemonicPropPart(root, "PetalA", PrimitiveType.Sphere, new Vector3(-0.18f, 0.38f, 0f), new Vector3(0.28f, 0.18f, 0.22f), color, item, true);
                AddMnemonicPropPart(root, "PetalB", PrimitiveType.Sphere, new Vector3(0.18f, 0.38f, 0f), new Vector3(0.28f, 0.18f, 0.22f), Color.Lerp(color, Color.white, 0.18f), item, true);
                AddMnemonicPropPart(root, "Center", PrimitiveType.Sphere, new Vector3(0f, 0.34f, -0.02f), new Vector3(0.20f, 0.20f, 0.20f), new Color(1.0f, 0.78f, 0.22f), item, true);
                return true;
            }

            if (ContainsAny(text, "crown", "king", "royal", "queen", "\u738b", "\u51a0"))
            {
                AddMnemonicPropPart(root, "CrownBand", PrimitiveType.Cube, new Vector3(0f, -0.10f, 0f), new Vector3(0.76f, 0.16f, 0.30f), new Color(1.0f, 0.74f, 0.18f), item, true);
                AddMnemonicPropPart(root, "CrownPointA", PrimitiveType.Capsule, new Vector3(-0.28f, 0.16f, 0f), new Vector3(0.14f, 0.44f, 0.14f), new Color(1.0f, 0.78f, 0.24f), item, true);
                AddMnemonicPropPart(root, "CrownPointB", PrimitiveType.Capsule, new Vector3(0f, 0.24f, 0f), new Vector3(0.16f, 0.56f, 0.16f), new Color(1.0f, 0.82f, 0.28f), item, true);
                AddMnemonicPropPart(root, "CrownPointC", PrimitiveType.Capsule, new Vector3(0.28f, 0.16f, 0f), new Vector3(0.14f, 0.44f, 0.14f), new Color(1.0f, 0.78f, 0.24f), item, true);
                return true;
            }

            if (ContainsAny(text, "heart", "love", "care", "warm", "\u5fc3", "\u611b"))
            {
                AddMnemonicPropPart(root, "HeartLeft", PrimitiveType.Sphere, new Vector3(-0.16f, 0.12f, 0f), new Vector3(0.36f, 0.36f, 0.24f), new Color(0.95f, 0.16f, 0.22f), item, true);
                AddMnemonicPropPart(root, "HeartRight", PrimitiveType.Sphere, new Vector3(0.16f, 0.12f, 0f), new Vector3(0.36f, 0.36f, 0.24f), new Color(0.95f, 0.16f, 0.22f), item, true);
                AddMnemonicPropPart(root, "HeartPoint", PrimitiveType.Cube, new Vector3(0f, -0.12f, 0f), new Vector3(0.36f, 0.36f, 0.22f), new Color(0.85f, 0.08f, 0.16f), item, true, new Vector3(0f, 0f, 45f));
                return true;
            }

            return false;
        }

        private void AddMnemonicPropPart(
            Transform root,
            string name,
            PrimitiveType shape,
            Vector3 localPosition,
            Vector3 localScale,
            Color color,
            MnemonicItemData item,
            bool emissive = false,
            Vector3 localEuler = default)
        {
            var part = CreatePrimitiveLocal(name, shape, localPosition, localScale, color, root);
            part.transform.localRotation = Quaternion.Euler(localEuler);

            var interactable = part.AddComponent<StudyInteractable>();
            interactable.Data = item;

            if (emissive)
            {
                var renderer = part.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.material.EnableKeyword("_EMISSION");
                    renderer.material.SetColor("_EmissionColor", color * 0.45f);
                }
            }
        }

        private Vector3 NormalizePropScale(Vector3 scale)
        {
            if (scale == default)
            {
                scale = Vector3.one * 0.32f;
            }

            return new Vector3(
                Mathf.Clamp(scale.x, 0.18f, 0.72f),
                Mathf.Clamp(scale.y, 0.18f, 0.72f),
                Mathf.Clamp(scale.z, 0.18f, 0.72f));
        }

        private string BuildMnemonicVisualSearchText(MnemonicItemData item, VisualObjectSpec spec)
        {
            var builder = new StringBuilder();
            builder.Append(spec?.label).Append(' ');
            builder.Append(spec?.effect).Append(' ');
            builder.Append(spec?.primitiveShape).Append(' ');
            builder.Append(item?.word).Append(' ');
            builder.Append(item?.meaning).Append(' ');
            builder.Append(item?.visualCue).Append(' ');
            builder.Append(item?.mnemonic).Append(' ');
            builder.Append(item?.visualCueJa).Append(' ');
            builder.Append(item?.mnemonicJa);

            return builder.ToString().ToLowerInvariant();
        }

        private bool IsGenericVisualLabel(string label)
        {
            if (string.IsNullOrWhiteSpace(label))
            {
                return true;
            }

            var lower = label.ToLowerInvariant();
            return lower == "prop"
                || lower == "object"
                || lower == "visual"
                || lower == "memory cue"
                || lower == "small prop"
                || lower == "cue"
                || lower.Contains("generic");
        }

        private GameObject CreateEnvironmentPrimitive(RoomPrimitiveDefinition primitive, Transform parent)
        {
            var go = CreatePrimitive(
                primitive.label,
                RoomSpecCatalog.ParsePrimitiveType(primitive.primitiveShape),
                primitive.position,
                primitive.scale,
                RoomSpecCatalog.Hex(primitive.colorHex),
                parent);
            go.name = primitive.id;
            go.transform.rotation = Quaternion.Euler(primitive.rotationEuler);
            return go;
        }

        private void CreateAnchorPrimitive(AnchorDefinition anchor, Transform parent)
        {
            CreateFurnitureModel(anchor, parent, false, false, -1);
        }

        private GameObject CreatePrimitive(string name, PrimitiveType primitiveType, Vector3 position, Vector3 scale, Color color, Transform parent)
        {
            var go = GameObject.CreatePrimitive(primitiveType);
            go.name = name;
            go.transform.SetParent(parent);
            go.transform.position = position;
            go.transform.localScale = scale;

            var renderer = go.GetComponent<Renderer>();
            if (renderer != null)
            {
                ApplyPrimitiveMaterial(renderer, color);
            }

            return go;
        }

        private GameObject CreatePrimitiveLocal(string name, PrimitiveType primitiveType, Vector3 localPosition, Vector3 localScale, Color color, Transform parent)
        {
            var go = GameObject.CreatePrimitive(primitiveType);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = localScale;

            var renderer = go.GetComponent<Renderer>();
            if (renderer != null)
            {
                ApplyPrimitiveMaterial(renderer, color);
            }

            return go;
        }

        private void ApplyPrimitiveMaterial(Renderer renderer, Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader);
            material.color = color;

            if (color.a < 0.99f)
            {
                material.SetFloat("_Surface", 1f);
                material.SetFloat("_Mode", 3f);
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0f);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.EnableKeyword("_ALPHABLEND_ON");
                material.renderQueue = (int)RenderQueue.Transparent;
            }

            renderer.material = material;
        }

        private void CreateWorldLabel(string text, Vector3 position, float characterSize, Color color, Transform parent)
        {
            var label = new GameObject($"Label_{text}");
            label.transform.SetParent(parent);
            label.transform.position = position;

            var textMesh = label.AddComponent<TextMesh>();
            textMesh.text = text;
            textMesh.characterSize = characterSize;
            textMesh.fontSize = 48;
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = TextAlignment.Center;
            textMesh.color = color;
            textMesh.font = GetLabelFont();

            var renderer = label.GetComponent<MeshRenderer>();
            if (renderer != null && textMesh.font != null)
            {
                renderer.material = textMesh.font.material;
            }

            label.AddComponent<BillboardToMainCamera>();
        }

        private void SetupVrStudyRuntime()
        {
            ClearVrStudyRuntime();
            if (!enableVrStudyMode || runtimeCamera == null)
            {
                return;
            }

            vrHeadTrackingActive = TryGetXrNodePose(XRNode.Head, out var headLocalPosition, out var headLocalRotation);
            if (vrHeadTrackingActive)
            {
                var cameraPose = runtimeCamera.transform;
                vrRigRoot = new GameObject("VRStudyRig").transform;
                vrRigRoot.position = new Vector3(cameraPose.position.x, 0f, cameraPose.position.z);
                vrRigRoot.rotation = Quaternion.Euler(0f, cameraPose.rotation.eulerAngles.y, 0f);
                runtimeCamera.transform.SetParent(vrRigRoot, false);
                runtimeCamera.transform.localPosition = NormalizeHeadLocalPosition(headLocalPosition);
                runtimeCamera.transform.localRotation = headLocalRotation;
            }

            BuildVrStudyPanel();
            BuildVrPointer();
            UpdateVrStudyPanelText();
            statusMessage = vrHeadTrackingActive
                ? "VR study runtime ready. Use controller ray to inspect markers."
                : "VR study mode is enabled, but no XR headset was detected. Desktop study controls remain active.";
        }

        private void HandleVrStudyRuntime()
        {
            if (vrWorldUiRoot == null)
            {
                return;
            }

            UpdateVrHeadPose();
            HandleVrLocomotion();
            HandleVrPointer();
            UpdateVrStudyPanelPose();
            UpdateVrStudyPanelText();
        }

        private void UpdateVrHeadPose()
        {
            if (vrRigRoot == null || runtimeCamera == null)
            {
                return;
            }

            if (TryGetXrNodePose(XRNode.Head, out var headLocalPosition, out var headLocalRotation))
            {
                runtimeCamera.transform.localPosition = NormalizeHeadLocalPosition(headLocalPosition);
                runtimeCamera.transform.localRotation = headLocalRotation;
                vrHeadTrackingActive = true;
            }
        }

        private void HandleVrLocomotion()
        {
            if (vrRigRoot == null || runtimeCamera == null)
            {
                return;
            }

            var leftHand = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            if (!leftHand.isValid || !leftHand.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis, out var axis))
            {
                return;
            }

            if (axis.sqrMagnitude < 0.04f)
            {
                return;
            }

            var forward = runtimeCamera.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f)
            {
                forward = vrRigRoot.forward;
                forward.y = 0f;
            }

            var right = runtimeCamera.transform.right;
            right.y = 0f;
            var move = forward.normalized * axis.y + right.normalized * axis.x;
            vrRigRoot.position += move * (StudyMoveSpeed * 0.65f * Time.unscaledDeltaTime);
        }

        private void HandleVrPointer()
        {
            if (runtimeCamera == null)
            {
                return;
            }

            var ray = BuildVrPointerRay(out var hasControllerRay);
            var hitPoint = ray.origin + ray.direction * VrPointerDistance;
            StudyInteractable pointedInteractable = null;

            if (Physics.Raycast(ray, out var hit, VrPointerDistance))
            {
                hitPoint = hit.point;
                pointedInteractable = hit.collider.GetComponent<StudyInteractable>();
            }

            UpdateVrPointerVisual(ray.origin, hitPoint, pointedInteractable != null);

            var rightHand = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            var selectPressed = GetXrButton(rightHand, UnityEngine.XR.CommonUsages.triggerButton);
            var capturePressed = GetXrButton(rightHand, UnityEngine.XR.CommonUsages.primaryButton) || GetXrButton(rightHand, UnityEngine.XR.CommonUsages.gripButton);
            if (!hasControllerRay)
            {
                selectPressed = false;
                capturePressed = false;
            }

            if (Time.unscaledTime < nextVrActionTime)
            {
                return;
            }

            if (selectPressed && pointedInteractable != null)
            {
                SelectStudyItem(pointedInteractable.Data, "VR controller ray selected mnemonic marker.");
                nextVrActionTime = Time.unscaledTime + VrActionCooldownSeconds;
                return;
            }

            if (capturePressed && selectedStudyItem != null && !isCapturingSnapshot)
            {
                StartCoroutine(CaptureMemorySnapshotRoutine(selectedStudyItem));
                nextVrActionTime = Time.unscaledTime + VrActionCooldownSeconds;
            }
        }

        private Ray BuildVrPointerRay(out bool hasControllerRay)
        {
            if (TryGetXrNodePose(XRNode.RightHand, out var controllerPosition, out var controllerRotation))
            {
                hasControllerRay = true;
                var origin = vrRigRoot != null ? vrRigRoot.TransformPoint(controllerPosition) : controllerPosition;
                var direction = (vrRigRoot != null ? vrRigRoot.rotation * controllerRotation : controllerRotation) * Vector3.forward;
                return new Ray(origin, direction.normalized);
            }

            hasControllerRay = false;
            return new Ray(runtimeCamera.transform.position, runtimeCamera.transform.forward);
        }

        private void BuildVrPointer()
        {
            var pointerRoot = new GameObject("VRStudyPointer");
            pointerRoot.transform.SetParent(vrWorldUiRoot);

            vrPointerLine = pointerRoot.AddComponent<LineRenderer>();
            vrPointerLine.positionCount = 2;
            vrPointerLine.startWidth = 0.012f;
            vrPointerLine.endWidth = 0.004f;
            vrPointerLine.material = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default"));
            vrPointerLine.startColor = new Color(0.42f, 0.86f, 1f, 0.85f);
            vrPointerLine.endColor = new Color(0.42f, 0.86f, 1f, 0.2f);

            vrPointerReticle = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            vrPointerReticle.name = "VRStudyPointerReticle";
            vrPointerReticle.transform.SetParent(vrWorldUiRoot);
            vrPointerReticle.transform.localScale = Vector3.one * 0.045f;
            var reticleRenderer = vrPointerReticle.GetComponent<Renderer>();
            if (reticleRenderer != null)
            {
                ApplyPrimitiveMaterial(reticleRenderer, new Color(0.42f, 0.86f, 1f, 0.9f));
            }
            var collider = vrPointerReticle.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }
        }

        private void UpdateVrPointerVisual(Vector3 origin, Vector3 hitPoint, bool hasTarget)
        {
            if (vrPointerLine != null)
            {
                vrPointerLine.SetPosition(0, origin);
                vrPointerLine.SetPosition(1, hitPoint);
                vrPointerLine.startColor = hasTarget ? new Color(0.95f, 0.86f, 0.38f, 0.95f) : new Color(0.42f, 0.86f, 1f, 0.75f);
                vrPointerLine.endColor = hasTarget ? new Color(0.95f, 0.86f, 0.38f, 0.35f) : new Color(0.42f, 0.86f, 1f, 0.2f);
            }

            if (vrPointerReticle != null)
            {
                vrPointerReticle.transform.position = hitPoint;
                vrPointerReticle.SetActive(true);
            }
        }

        private void BuildVrStudyPanel()
        {
            vrWorldUiRoot = new GameObject("VRStudyWorldUI").transform;
            var panel = new GameObject("VRMnemonicPanel");
            panel.transform.SetParent(vrWorldUiRoot);

            var canvas = panel.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = runtimeCamera;
            canvas.sortingOrder = 5;

            var rect = panel.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(760f, 520f);
            panel.transform.localScale = Vector3.one * 0.0019f;

            var image = panel.AddComponent<Image>();
            image.color = new Color(0.05f, 0.07f, 0.10f, 0.88f);

            vrProgressText = CreateVrPanelText(panel.transform, "Progress", 16, new Rect(26f, -34f, 708f, 44f), new Color(0.76f, 0.84f, 0.94f));
            vrTitleText = CreateVrPanelText(panel.transform, "Title", 34, new Rect(26f, -96f, 708f, 58f), Color.white);
            vrMeaningText = CreateVrPanelText(panel.transform, "Meaning", 20, new Rect(26f, -152f, 708f, 44f), new Color(0.90f, 0.94f, 1f));
            vrAnchorText = CreateVrPanelText(panel.transform, "Anchor", 18, new Rect(26f, -198f, 708f, 36f), new Color(0.70f, 0.78f, 0.90f));
            vrCueText = CreateVrPanelText(panel.transform, "Cue", 18, new Rect(26f, -282f, 708f, 82f), new Color(0.94f, 0.96f, 1f));
            vrStoryText = CreateVrPanelText(panel.transform, "Story", 18, new Rect(26f, -392f, 708f, 100f), new Color(0.94f, 0.96f, 1f));
            vrActionText = CreateVrPanelText(panel.transform, "Action", 16, new Rect(26f, -486f, 708f, 60f), new Color(0.95f, 0.86f, 0.48f));

            UpdateVrStudyPanelPose();
        }

        private Text CreateVrPanelText(Transform parent, string name, int fontSize, Rect rect, Color color)
        {
            var textObject = new GameObject(name);
            textObject.transform.SetParent(parent, false);

            var text = textObject.AddComponent<Text>();
            text.font = GetLabelFont();
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;

            var textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0f, 1f);
            textRect.anchorMax = new Vector2(0f, 1f);
            textRect.pivot = new Vector2(0f, 1f);
            textRect.anchoredPosition = new Vector2(rect.x, rect.y);
            textRect.sizeDelta = new Vector2(rect.width, rect.height);
            return text;
        }

        private void UpdateVrStudyPanelPose()
        {
            if (vrWorldUiRoot == null || runtimeCamera == null)
            {
                return;
            }

            var forward = runtimeCamera.transform.forward;
            if (forward.sqrMagnitude < 0.001f)
            {
                forward = Vector3.forward;
            }

            vrWorldUiRoot.position = runtimeCamera.transform.position + forward.normalized * 1.75f - Vector3.up * 0.04f;
            vrWorldUiRoot.rotation = Quaternion.LookRotation(vrWorldUiRoot.position - runtimeCamera.transform.position, Vector3.up);
        }

        private void UpdateVrStudyPanelText()
        {
            if (vrProgressText == null)
            {
                return;
            }

            vrProgressText.text = $"Viewed {viewedWords.Count}/{currentItems.Count}  Snapshots {memorizedWords.Count}/{currentItems.Count}  Elapsed {(Time.unscaledTime - studyStartTime):F1}s";

            if (selectedStudyItem == null)
            {
                vrTitleText.text = "Find a memory marker";
                vrMeaningText.text = "Aim the controller ray at a floating marker and press trigger.";
                vrAnchorText.text = "Desktop controls remain available in the Game view.";
                vrCueText.text = "Scene to Imagine appears here after selection.";
                vrStoryText.text = "Cue Story appears here after selection.";
                vrActionText.text = vrHeadTrackingActive
                    ? "Trigger: inspect marker    A / Grip: capture selected memory"
                    : "No XR headset detected. Use desktop mouse and keyboard for now.";
                return;
            }

            vrTitleText.text = selectedStudyItem.word;
            vrMeaningText.text = selectedStudyItem.meaning + (string.IsNullOrWhiteSpace(selectedStudyItem.meaningJa) ? string.Empty : $" ({selectedStudyItem.meaningJa})");
            vrAnchorText.text = "Anchor: " + selectedStudyItem.anchorLabel;
            vrCueText.text = "Scene: " + (selectedStudyItem.visualCue ?? string.Empty);
            vrStoryText.text = "Story: " + (selectedStudyItem.mnemonic ?? string.Empty);

            var hasSnapshot = memorySnapshots.ContainsKey(selectedStudyItem.word);
            vrActionText.text = hasSnapshot
                ? "Snapshot stored. Press A / Grip to replace it with the current cue."
                : "Press A / Grip to capture this memory for the image-choice tests.";
        }

        private static bool TryGetXrNodePose(XRNode node, out Vector3 localPosition, out Quaternion localRotation)
        {
            localPosition = Vector3.zero;
            localRotation = Quaternion.identity;

            var device = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(node);
            if (!device.isValid)
            {
                return false;
            }

            var hasPosition = device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.devicePosition, out localPosition);
            var hasRotation = device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.deviceRotation, out localRotation);
            return hasPosition || hasRotation;
        }

        private static bool GetXrButton(UnityEngine.XR.InputDevice device, UnityEngine.XR.InputFeatureUsage<bool> usage)
        {
            return device.isValid && device.TryGetFeatureValue(usage, out var pressed) && pressed;
        }

        private static Vector3 NormalizeHeadLocalPosition(Vector3 localPosition)
        {
            if (localPosition.y < 0.35f)
            {
                localPosition.y = VrDefaultHeadHeight;
            }

            return localPosition;
        }

        private void SelectStudyItem(MnemonicItemData item, string details)
        {
            if (item == null)
            {
                return;
            }

            selectedStudyItem = item;
            viewedWords.Add(selectedStudyItem.word);
            LogInteraction("inspect", selectedStudyItem.word, selectedStudyItem.anchorId, details);
        }

        private void ResetCameraForStudy()
        {
            var studyCamera = RoomSpecCatalog.CurrentRoom.studyCamera;
            runtimeCamera.orthographic = false;
            runtimeCamera.transform.position = studyCamera.position;
            cameraYaw = studyCamera.eulerAngles.y;
            cameraPitch = studyCamera.eulerAngles.x;
            runtimeCamera.transform.rotation = Quaternion.Euler(studyCamera.eulerAngles);
        }

        private void MoveCameraToOverview()
        {
            if (runtimeCamera == null)
            {
                return;
            }

            var overviewCamera = RoomSpecCatalog.CurrentRoom.overviewCamera;
            runtimeCamera.orthographic = false;
            runtimeCamera.transform.position = overviewCamera.position;
            runtimeCamera.transform.rotation = Quaternion.Euler(overviewCamera.eulerAngles);
        }

        private void MoveCameraToPlanView()
        {
            if (runtimeCamera == null)
            {
                return;
            }

            GetCurrentRoomFootprint(out var roomWidth, out var roomDepth);
            runtimeCamera.orthographic = true;
            runtimeCamera.orthographicSize = Mathf.Clamp(Mathf.Max(roomWidth, roomDepth) * 0.58f, 4.8f, 8.4f);
            runtimeCamera.transform.position = new Vector3(roomWidth * 0.46f, Mathf.Max(7.2f, roomDepth * 0.9f), -roomDepth * 0.68f);
            runtimeCamera.transform.rotation = Quaternion.Euler(58f, -35f, 0f);
            cameraYaw = runtimeCamera.transform.rotation.eulerAngles.y;
            cameraPitch = 58f;
        }

        private void MoveCameraToEntrancePreview()
        {
            if (runtimeCamera == null)
            {
                return;
            }

            GetCurrentRoomFootprint(out var roomWidth, out var roomDepth);
            var entrance = RoomSpecCatalog.CurrentRoom.anchors.Count > 0 ? RoomSpecCatalog.CurrentRoom.anchors[0] : null;
            for (int i = 0; i < RoomSpecCatalog.CurrentRoom.anchors.Count; i++)
            {
                if (IsDoorAnchor(RoomSpecCatalog.CurrentRoom.anchors[i]))
                {
                    entrance = RoomSpecCatalog.CurrentRoom.anchors[i];
                    break;
                }
            }

            var doorPosition = entrance?.position ?? new Vector3(0f, 1.0f, -roomDepth * 0.5f + 0.12f);
            var inward = new Vector3(-doorPosition.x, 0f, -doorPosition.z);
            if (inward.sqrMagnitude < 0.001f)
            {
                inward = Vector3.forward;
            }

            var cameraPosition = doorPosition + inward.normalized * 1.15f;
            cameraPosition.y = 1.55f;
            var lookTarget = new Vector3(0f, 1.3f, Mathf.Clamp(roomDepth * 0.12f, -roomDepth * 0.25f, roomDepth * 0.35f));

            runtimeCamera.orthographic = false;
            runtimeCamera.fieldOfView = 60f;
            runtimeCamera.transform.position = cameraPosition;
            runtimeCamera.transform.LookAt(lookTarget);

            var euler = runtimeCamera.transform.rotation.eulerAngles;
            cameraYaw = euler.y;
            cameraPitch = euler.x > 180f ? euler.x - 360f : euler.x;
            RoomSpecCatalog.CurrentRoom.studyCamera.position = runtimeCamera.transform.position;
            RoomSpecCatalog.CurrentRoom.studyCamera.eulerAngles = runtimeCamera.transform.rotation.eulerAngles;
            statusMessage = "Entrance preview ready. The study phase will start from this door viewpoint.";
        }

        private void HandleStudyControls()
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (keyboard == null || mouse == null || runtimeCamera == null)
            {
                return;
            }

            var deltaTime = Time.unscaledDeltaTime;
            var moveSpeed = StudyMoveSpeed * (keyboard.leftShiftKey.isPressed ? 1.8f : 1f);

            var move = Vector3.zero;
            if (keyboard.wKey.isPressed) move += runtimeCamera.transform.forward;
            if (keyboard.sKey.isPressed) move -= runtimeCamera.transform.forward;
            if (keyboard.dKey.isPressed) move += runtimeCamera.transform.right;
            if (keyboard.aKey.isPressed) move -= runtimeCamera.transform.right;
            if (keyboard.eKey.isPressed) move += Vector3.up;
            if (keyboard.qKey.isPressed) move -= Vector3.up;

            move.y = keyboard.eKey.isPressed || keyboard.qKey.isPressed ? move.y : 0f;
            runtimeCamera.transform.position += move.normalized * moveSpeed * deltaTime;

            if (mouse.rightButton.isPressed && !IsPointerOverGui())
            {
                var delta = mouse.delta.ReadValue();
                cameraYaw += delta.x * StudyLookSpeed;
                cameraPitch = Mathf.Clamp(cameraPitch - delta.y * StudyLookSpeed, -70f, 70f);
                runtimeCamera.transform.rotation = Quaternion.Euler(cameraPitch, cameraYaw, 0f);
                Cursor.visible = false;
                Cursor.lockState = CursorLockMode.Locked;
            }
            else
            {
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
            }

            if (mouse.leftButton.wasPressedThisFrame && !IsPointerOverGui())
            {
                var ray = runtimeCamera.ScreenPointToRay(mouse.position.ReadValue());
                if (Physics.Raycast(ray, out var hit, 100f))
                {
                    var interactable = hit.collider.GetComponent<StudyInteractable>();
                    if (interactable != null)
                    {
                        SelectStudyItem(interactable.Data, "Clicked mnemonic object.");
                    }
                }
            }
        }

        private void HandleRoomBuilderControls()
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (keyboard == null || mouse == null || runtimeCamera == null)
            {
                return;
            }

            if (!IsPointerOverGui())
            {
                if (keyboard.qKey.wasPressedThisFrame) SetBuilderTool(BuilderToolMode.Select);
                if (keyboard.wKey.wasPressedThisFrame) SetBuilderTool(BuilderToolMode.Move);
                if (keyboard.eKey.wasPressedThisFrame) SetBuilderTool(BuilderToolMode.Rotate);
                if (keyboard.rKey.wasPressedThisFrame) SetBuilderTool(BuilderToolMode.Scale);
            }

            var deltaTime = Time.unscaledDeltaTime;
            var moveSpeed = StudyMoveSpeed * (keyboard.leftShiftKey.isPressed ? 1.8f : 1f);
            var move = Vector3.zero;

            if (mouse.rightButton.isPressed && !IsPointerOverGui())
            {
                if (keyboard.wKey.isPressed) move += runtimeCamera.transform.forward;
                if (keyboard.sKey.isPressed) move -= runtimeCamera.transform.forward;
                if (keyboard.dKey.isPressed) move += runtimeCamera.transform.right;
                if (keyboard.aKey.isPressed) move -= runtimeCamera.transform.right;
                if (keyboard.eKey.isPressed) move += Vector3.up;
                if (keyboard.qKey.isPressed) move -= Vector3.up;

                runtimeCamera.transform.position += move.normalized * moveSpeed * deltaTime;

                var delta = mouse.delta.ReadValue();
                cameraYaw += delta.x * StudyLookSpeed;
                cameraPitch = Mathf.Clamp(cameraPitch - delta.y * StudyLookSpeed, -70f, 70f);
                runtimeCamera.transform.rotation = Quaternion.Euler(cameraPitch, cameraYaw, 0f);
                Cursor.visible = false;
                Cursor.lockState = CursorLockMode.Locked;
            }
            else
            {
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
            }

            if (isDrawingShellWall)
            {
                HandleShellWallDrawing(mouse, keyboard);
                return;
            }

            if (isBuilderDragging)
            {
                if (mouse.leftButton.isPressed)
                {
                    UpdateBuilderDrag(mouse.position.ReadValue());
                }
                else
                {
                    EndBuilderDrag();
                }

                return;
            }

            if (mouse.leftButton.wasPressedThisFrame && !IsPointerOverGui())
            {
                if (TryGetBuilderRaycastHit(mouse.position.ReadValue(), out var hit))
                {
                    var handle = hit.collider.GetComponent<BuilderGizmoHandle>();
                    if (handle != null && handle.PrimitiveIndex >= 0)
                    {
                        selectedRoomPrimitiveIndex = handle.PrimitiveIndex;
                        selectedBuilderAnchorIndex = -1;
                        SetBuilderTool(ParseBuilderTool(handle.ToolName));
                        StartBuilderDrag(handle.Axis, mouse.position.ReadValue());
                        return;
                    }

                    if (handle != null && handle.AnchorIndex >= 0)
                    {
                        selectedBuilderAnchorIndex = handle.AnchorIndex;
                        selectedRoomPrimitiveIndex = -1;
                        SetBuilderTool(ParseBuilderTool(handle.ToolName));
                        StartBuilderDrag(handle.Axis, mouse.position.ReadValue());
                        return;
                    }

                    var interactable = hit.collider.GetComponent<RoomAnchorInteractable>();
                    if (interactable != null)
                    {
                        selectedBuilderAnchorIndex = interactable.Index;
                        selectedRoomPrimitiveIndex = -1;
                        if (builderToolMode == BuilderToolMode.Select)
                        {
                            BuildRoomBuilderPreview();
                        }
                        else
                        {
                            StartBuilderDrag(Vector3.zero, mouse.position.ReadValue());
                        }
                        return;
                    }

                    var shellInteractable = hit.collider.GetComponent<RoomPrimitiveInteractable>();
                    if (shellInteractable != null)
                    {
                        selectedRoomPrimitiveIndex = shellInteractable.Index;
                        selectedBuilderAnchorIndex = -1;
                        if (builderToolMode == BuilderToolMode.Select)
                        {
                            BuildRoomBuilderPreview();
                        }
                        else
                        {
                            StartBuilderDrag(Vector3.zero, mouse.position.ReadValue());
                        }
                    }
                }
            }
        }

        private void HandleShellWallDrawing(Mouse mouse, Keyboard keyboard)
        {
            if (keyboard.escapeKey.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame)
            {
                CancelDrawShellWall("Wall drawing cancelled.");
                return;
            }

            if (IsPointerOverGui())
            {
                return;
            }

            if (TryGetMousePlanePoint(mouse.position.ReadValue(), 0f, out var planePoint))
            {
                var snappedPoint = SnapShellPointToGrid(planePoint);
                if ((snappedPoint - shellWallPreviewPoint).sqrMagnitude > 0.01f)
                {
                    shellWallPreviewPoint = snappedPoint;
                    if (hasShellWallStart)
                    {
                        BuildRoomBuilderPreview();
                    }
                }
            }

            if (!mouse.leftButton.wasPressedThisFrame)
            {
                return;
            }

            if (!TryGetMousePlanePoint(mouse.position.ReadValue(), 0f, out var clickPoint))
            {
                return;
            }

            clickPoint = SnapShellPointToGrid(clickPoint);
            if (!hasShellWallStart)
            {
                shellWallStartPoint = clickPoint;
                shellWallPreviewPoint = clickPoint;
                hasShellWallStart = true;
                statusMessage = "Wall start set. Move the mouse to preview the wall, then click the endpoint.";
                BuildRoomBuilderPreview();
                return;
            }

            if ((clickPoint - shellWallStartPoint).sqrMagnitude < 0.12f)
            {
                statusMessage = "Wall endpoint is too close to the start point. Move farther before placing.";
                return;
            }

            PlaceDrawnShellWall(shellWallStartPoint, clickPoint);
        }

        private bool TryGetBuilderRaycastHit(Vector2 mousePosition, out RaycastHit bestHit)
        {
            var ray = runtimeCamera.ScreenPointToRay(mousePosition);
            var hits = Physics.RaycastAll(ray, 100f);
            bestHit = default;
            if (hits == null || hits.Length == 0)
            {
                return false;
            }

            var bestPriority = -1;
            var bestDistance = float.MaxValue;
            for (int i = 0; i < hits.Length; i++)
            {
                var hit = hits[i];
                var priority = GetBuilderHitPriority(hit.collider);
                if (priority < 0)
                {
                    continue;
                }

                if (priority > bestPriority || (priority == bestPriority && hit.distance < bestDistance))
                {
                    bestHit = hit;
                    bestPriority = priority;
                    bestDistance = hit.distance;
                }
            }

            return bestPriority >= 0;
        }

        private int GetBuilderHitPriority(Collider collider)
        {
            if (collider == null)
            {
                return -1;
            }

            if (collider.GetComponent<BuilderGizmoHandle>() != null)
            {
                return 400;
            }

            if (collider.GetComponent<RoomAnchorInteractable>() != null)
            {
                return 300;
            }

            var primitive = collider.GetComponent<RoomPrimitiveInteractable>();
            if (primitive != null)
            {
                return IsRoomPrimitiveFloorLike(primitive.Index) ? 120 : 240;
            }

            return -1;
        }

        private bool IsRoomPrimitiveFloorLike(int primitiveIndex)
        {
            var primitives = RoomSpecCatalog.CurrentRoom.environmentPrimitives;
            if (primitiveIndex < 0 || primitiveIndex >= primitives.Count || primitives[primitiveIndex] == null)
            {
                return false;
            }

            var text = ((primitives[primitiveIndex].id ?? string.Empty) + " " + (primitives[primitiveIndex].label ?? string.Empty)).ToLowerInvariant();
            return ContainsAny(text, "floor", "tile", "rug", "carpet", "border");
        }

        private bool IsRoomPrimitiveFloorSurfaceLike(int primitiveIndex)
        {
            var primitives = RoomSpecCatalog.CurrentRoom.environmentPrimitives;
            if (primitiveIndex < 0 || primitiveIndex >= primitives.Count || primitives[primitiveIndex] == null)
            {
                return false;
            }

            var primitive = primitives[primitiveIndex];
            var text = ((primitive.id ?? string.Empty) + " " + (primitive.label ?? string.Empty)).ToLowerInvariant();
            return ContainsAny(text, "floor", "tile", "patch", "slab")
                && !ContainsAny(text, "rug", "carpet", "border", "inlay", "trim");
        }

        private bool IsRoomPrimitiveWallBuildPiece(int primitiveIndex)
        {
            var primitives = RoomSpecCatalog.CurrentRoom.environmentPrimitives;
            if (primitiveIndex < 0 || primitiveIndex >= primitives.Count || primitives[primitiveIndex] == null)
            {
                return false;
            }

            var primitive = primitives[primitiveIndex];
            var text = ((primitive.id ?? string.Empty) + " " + (primitive.label ?? string.Empty)).ToLowerInvariant();
            if (ContainsAny(text, "auto_wall", "auto floor border", "floor_border", "cap_", "ceiling", "roof", "window", "door", "art", "floor", "rug", "carpet", "trim", "baseboard"))
            {
                return false;
            }

            return ContainsAny(text, "wall segment", "drawn wall", "interior wall", "partition", "divider")
                || IsWallLikeRoomPrimitive(primitive);
        }

        private bool IsFloorPatchPlacementVisualMode()
        {
            return isFloorPatchPlacementActive
                || (isBuilderDragging && builderDragPrimitiveIndex >= 0 && IsRoomPrimitiveFloorLike(builderDragPrimitiveIndex));
        }

        private void SetBuilderTool(BuilderToolMode mode)
        {
            if (builderToolMode == mode)
            {
                return;
            }

            builderToolMode = mode;
            if (stage == ExperimentStage.RoomBuilder)
            {
                BuildRoomBuilderPreview();
            }
        }

        private BuilderToolMode ParseBuilderTool(string toolName)
        {
            return toolName switch
            {
                nameof(BuilderToolMode.Move) => BuilderToolMode.Move,
                nameof(BuilderToolMode.Rotate) => BuilderToolMode.Rotate,
                nameof(BuilderToolMode.Scale) => BuilderToolMode.Scale,
                _ => BuilderToolMode.Select
            };
        }

        private void StartBuilderDrag(Vector3 axis, Vector2 mousePosition)
        {
            builderDragAnchorIndex = -1;
            builderDragPrimitiveIndex = -1;
            builderDragAxis = axis.sqrMagnitude > 0.001f ? axis.normalized : Vector3.zero;
            builderDragStartMouse = mousePosition;

            if (TryGetSelectedBuilderAnchor(out var anchor))
            {
                builderDragAnchorIndex = selectedBuilderAnchorIndex;
                builderDragStartPosition = anchor.position;
                builderDragStartRotation = anchor.rotationEuler;
                builderDragStartScale = anchor.scale;
                isBuilderDragging = true;
                TriggerBuilderFeedback(selectedBuilderAnchorIndex, 0.16f);
                statusMessage = $"Picked up {anchor.label}. Release the mouse to place it.";

                if (builderToolMode == BuilderToolMode.Move && builderDragAxis == Vector3.zero && TryGetMousePlanePoint(mousePosition, anchor.position.y, out var anchorPlanePoint))
                {
                    builderDragStartPlanePoint = anchorPlanePoint;
                    builderDragAnchorOffset = anchor.position - anchorPlanePoint;
                }

                return;
            }

            if (TryGetSelectedRoomPrimitive(out var primitive))
            {
                builderDragPrimitiveIndex = selectedRoomPrimitiveIndex;
                builderDragStartPosition = primitive.position;
                builderDragStartRotation = primitive.rotationEuler;
                builderDragStartScale = primitive.scale;
                isBuilderDragging = true;
                if (IsRoomPrimitiveFloorLike(builderDragPrimitiveIndex))
                {
                    if (!isFloorPatchPlacementActive || activeFloorPatchIndex != builderDragPrimitiveIndex)
                    {
                        activeFloorPatchWasNew = false;
                    }

                    activeFloorPatchIndex = builderDragPrimitiveIndex;
                    isFloorPatchPlacementActive = true;
                    floorPatchDropPreviewPosition = GetSnappedFloorPatchPosition(primitive, builderDragPrimitiveIndex, primitive.position);
                    hasFloorPatchDropPreview = true;
                }
                else if (IsRoomPrimitiveWallBuildPiece(builderDragPrimitiveIndex))
                {
                    if (!isWallSegmentPlacementActive || activeWallSegmentIndex != builderDragPrimitiveIndex)
                    {
                        activeWallSegmentWasNew = false;
                    }

                    activeWallSegmentIndex = builderDragPrimitiveIndex;
                    isWallSegmentPlacementActive = true;
                    ApplyWallSegmentSnapPreview(primitive, builderDragPrimitiveIndex, primitive.position);
                }

                statusMessage = $"Picked up shell piece {primitive.label}. Release the mouse to place it.";

                if (builderToolMode == BuilderToolMode.Move && builderDragAxis == Vector3.zero && TryGetMousePlanePoint(mousePosition, primitive.position.y, out var primitivePlanePoint))
                {
                    builderDragStartPlanePoint = primitivePlanePoint;
                    builderDragAnchorOffset = primitive.position - primitivePlanePoint;
                }
            }
        }

        private void UpdateBuilderDrag(Vector2 mousePosition)
        {
            var mouseDelta = mousePosition - builderDragStartMouse;
            var draggingAnchor = builderDragAnchorIndex >= 0;
            var draggingPrimitive = builderDragPrimitiveIndex >= 0;
            if (!draggingAnchor && !draggingPrimitive)
            {
                EndBuilderDrag();
                return;
            }

            if (draggingAnchor)
            {
                var anchors = RoomSpecCatalog.CurrentRoom.anchors;
                if (builderDragAnchorIndex < 0 || builderDragAnchorIndex >= anchors.Count)
                {
                    EndBuilderDrag();
                    return;
                }

                var anchor = anchors[builderDragAnchorIndex];
                if (builderToolMode == BuilderToolMode.Move)
                {
                    if (builderDragAxis == Vector3.zero)
                    {
                        if (TryGetMousePlanePoint(mousePosition, builderDragStartPosition.y, out var planePoint))
                        {
                            anchor.position = planePoint + builderDragAnchorOffset;
                        }
                    }
                    else
                    {
                        var scalar = (mouseDelta.x - mouseDelta.y) * BuilderAxisDragScale;
                        anchor.position = builderDragStartPosition + builderDragAxis * scalar;
                    }
                }
                else if (builderToolMode == BuilderToolMode.Rotate)
                {
                    anchor.rotationEuler = builderDragStartRotation + Vector3.up * (mouseDelta.x * BuilderRotateDragScale);
                }
                else if (builderToolMode == BuilderToolMode.Scale)
                {
                    var scalar = Mathf.Clamp(1f + (mouseDelta.x - mouseDelta.y) * BuilderScaleDragScale, 0.2f, 4f);
                    if (builderDragAxis == Vector3.zero)
                    {
                        anchor.scale = ClampAnchorScale(builderDragStartScale * scalar);
                    }
                    else
                    {
                        var nextScale = builderDragStartScale;
                        if (Mathf.Abs(builderDragAxis.x) > 0.5f) nextScale.x = builderDragStartScale.x * scalar;
                        if (Mathf.Abs(builderDragAxis.y) > 0.5f) nextScale.y = builderDragStartScale.y * scalar;
                        if (Mathf.Abs(builderDragAxis.z) > 0.5f) nextScale.z = builderDragStartScale.z * scalar;
                        anchor.scale = ClampAnchorScale(nextScale);
                    }
                }

                selectedBuilderAnchorIndex = builderDragAnchorIndex;
                selectedRoomPrimitiveIndex = -1;
            }
            else
            {
                var primitives = RoomSpecCatalog.CurrentRoom.environmentPrimitives;
                if (builderDragPrimitiveIndex < 0 || builderDragPrimitiveIndex >= primitives.Count)
                {
                    EndBuilderDrag();
                    return;
                }

                var primitive = primitives[builderDragPrimitiveIndex];
                if (builderToolMode == BuilderToolMode.Move)
                {
                    if (builderDragAxis == Vector3.zero)
                    {
                        if (TryGetMousePlanePoint(mousePosition, builderDragStartPosition.y, out var planePoint))
                        {
                            primitive.position = planePoint + builderDragAnchorOffset;
                            if (IsRoomPrimitiveFloorLike(builderDragPrimitiveIndex))
                            {
                                primitive.position = SnapShellPointToGrid(primitive.position);
                                primitive.position.y = 0f;
                                floorPatchDropPreviewPosition = GetSnappedFloorPatchPosition(primitive, builderDragPrimitiveIndex, primitive.position);
                                hasFloorPatchDropPreview = true;
                            }
                            else if (IsRoomPrimitiveWallBuildPiece(builderDragPrimitiveIndex))
                            {
                                primitive.position = SnapShellPointToGrid(primitive.position);
                                primitive.position.y = Mathf.Max(0.38f, primitive.scale.y * 0.5f);
                                ApplyWallSegmentSnapPreview(primitive, builderDragPrimitiveIndex, primitive.position);
                            }
                        }
                    }
                    else
                    {
                        var scalar = (mouseDelta.x - mouseDelta.y) * BuilderAxisDragScale;
                        primitive.position = builderDragStartPosition + builderDragAxis * scalar;
                        if (IsRoomPrimitiveFloorLike(builderDragPrimitiveIndex))
                        {
                            primitive.position = SnapShellPointToGrid(primitive.position);
                            primitive.position.y = 0f;
                            floorPatchDropPreviewPosition = GetSnappedFloorPatchPosition(primitive, builderDragPrimitiveIndex, primitive.position);
                            hasFloorPatchDropPreview = true;
                        }
                        else if (IsRoomPrimitiveWallBuildPiece(builderDragPrimitiveIndex))
                        {
                            primitive.position = SnapShellPointToGrid(primitive.position);
                            primitive.position.y = Mathf.Max(0.38f, primitive.scale.y * 0.5f);
                            ApplyWallSegmentSnapPreview(primitive, builderDragPrimitiveIndex, primitive.position);
                        }
                    }
                }
                else if (builderToolMode == BuilderToolMode.Rotate)
                {
                    primitive.rotationEuler = builderDragStartRotation + Vector3.up * (mouseDelta.x * BuilderRotateDragScale);
                    if (IsRoomPrimitiveWallBuildPiece(builderDragPrimitiveIndex))
                    {
                        ApplyWallSegmentSnapPreview(primitive, builderDragPrimitiveIndex, primitive.position);
                    }
                }
                else if (builderToolMode == BuilderToolMode.Scale)
                {
                    var scalar = Mathf.Clamp(1f + (mouseDelta.x - mouseDelta.y) * BuilderScaleDragScale, 0.2f, 4f);
                    if (builderDragAxis == Vector3.zero)
                    {
                        primitive.scale = ClampRoomPrimitiveScale(builderDragStartScale * scalar);
                    }
                    else
                    {
                        var nextScale = builderDragStartScale;
                        if (Mathf.Abs(builderDragAxis.x) > 0.5f) nextScale.x = builderDragStartScale.x * scalar;
                        if (Mathf.Abs(builderDragAxis.y) > 0.5f) nextScale.y = builderDragStartScale.y * scalar;
                        if (Mathf.Abs(builderDragAxis.z) > 0.5f) nextScale.z = builderDragStartScale.z * scalar;
                        primitive.scale = ClampRoomPrimitiveScale(nextScale);
                    }

                    if (IsRoomPrimitiveFloorLike(builderDragPrimitiveIndex))
                    {
                        floorPatchDropPreviewPosition = GetSnappedFloorPatchPosition(primitive, builderDragPrimitiveIndex, primitive.position);
                        hasFloorPatchDropPreview = true;
                    }
                    else if (IsRoomPrimitiveWallBuildPiece(builderDragPrimitiveIndex))
                    {
                        ApplyWallSegmentSnapPreview(primitive, builderDragPrimitiveIndex, primitive.position);
                    }
                }

                selectedRoomPrimitiveIndex = builderDragPrimitiveIndex;
                selectedBuilderAnchorIndex = -1;
            }

            BuildRoomBuilderPreview();
        }

        private Vector3 ClampAnchorScale(Vector3 scale)
        {
            return new Vector3(
                Mathf.Clamp(scale.x, 0.15f, 5f),
                Mathf.Clamp(scale.y, 0.15f, 5f),
                Mathf.Clamp(scale.z, 0.15f, 5f));
        }

        private void EndBuilderDrag()
        {
            var placedAnchorIndex = builderDragAnchorIndex;
            var placedPrimitiveIndex = builderDragPrimitiveIndex;
            isBuilderDragging = false;
            builderDragAnchorIndex = -1;
            builderDragPrimitiveIndex = -1;
            builderDragAxis = Vector3.zero;

            if (placedAnchorIndex >= 0 && placedAnchorIndex < RoomSpecCatalog.CurrentRoom.anchors.Count)
            {
                GetCurrentRoomFootprint(out var roomWidth, out var roomDepth);
                ConstrainAnchorPlacement(RoomSpecCatalog.CurrentRoom.anchors[placedAnchorIndex], placedAnchorIndex, roomWidth, roomDepth);
                ResolveCurrentAnchorOverlaps(roomWidth, roomDepth);
                TriggerBuilderFeedback(placedAnchorIndex, 0.28f);
                statusMessage = $"Placed {RoomSpecCatalog.CurrentRoom.anchors[placedAnchorIndex].label}.";
                BuildRoomBuilderPreview();
                return;
            }

            if (placedPrimitiveIndex >= 0 && placedPrimitiveIndex < RoomSpecCatalog.CurrentRoom.environmentPrimitives.Count)
            {
                selectedRoomPrimitiveIndex = placedPrimitiveIndex;
                selectedBuilderAnchorIndex = -1;
                var primitive = RoomSpecCatalog.CurrentRoom.environmentPrimitives[placedPrimitiveIndex];
                var wasFloorPatchPlacement = IsRoomPrimitiveFloorLike(placedPrimitiveIndex) && (isFloorPatchPlacementActive || hasFloorPatchDropPreview);
                var wasWallSegmentPlacement = IsRoomPrimitiveWallBuildPiece(placedPrimitiveIndex) && (isWallSegmentPlacementActive || hasWallSegmentDropPreview);
                if (wasFloorPatchPlacement && hasFloorPatchDropPreview)
                {
                    primitive.position = floorPatchDropPreviewPosition;
                    primitive.position.y = 0f;
                }
                else if (wasWallSegmentPlacement && hasWallSegmentDropPreview)
                {
                    primitive.position = wallSegmentDropPreviewPosition;
                    primitive.rotationEuler = wallSegmentDropPreviewRotation;
                    primitive.scale = wallSegmentDropPreviewScale;
                }

                primitive.scale = ClampRoomPrimitiveScale(primitive.scale);
                if (wasFloorPatchPlacement)
                {
                    var selectedPrimitive = primitive;
                    RebuildAutoWallsFromFloorSurfaces();
                    selectedRoomPrimitiveIndex = RoomSpecCatalog.CurrentRoom.environmentPrimitives.IndexOf(selectedPrimitive);
                }

                isFloorPatchPlacementActive = false;
                activeFloorPatchIndex = -1;
                activeFloorPatchWasNew = false;
                hasFloorPatchDropPreview = false;
                isWallSegmentPlacementActive = false;
                activeWallSegmentIndex = -1;
                activeWallSegmentWasNew = false;
                hasWallSegmentDropPreview = false;
                statusMessage = wasFloorPatchPlacement
                    ? $"Placed floor patch {primitive.label}. Outer walls rebuilt from the new floor outline."
                    : wasWallSegmentPlacement
                        ? $"Placed wall segment {primitive.label} with snap preview."
                    : $"Placed shell piece {primitive.label}.";
                BuildRoomBuilderPreview();
            }
        }

        private void TriggerBuilderFeedback(int anchorIndex, float duration)
        {
            builderFeedbackAnchorIndex = anchorIndex;
            builderFeedbackUntil = Time.unscaledTime + duration;
            if (stage == ExperimentStage.RoomBuilder)
            {
                StartCoroutine(ClearBuilderFeedbackAfterDelay(duration + 0.05f));
            }
        }

        private IEnumerator ClearBuilderFeedbackAfterDelay(float delaySeconds)
        {
            yield return new WaitForSecondsRealtime(delaySeconds);
            if (Time.unscaledTime >= builderFeedbackUntil)
            {
                builderFeedbackAnchorIndex = -1;
                if (stage == ExperimentStage.RoomBuilder)
                {
                    BuildRoomBuilderPreview();
                }
            }
        }

        private bool TryGetMousePlanePoint(Vector2 mousePosition, float planeY, out Vector3 point)
        {
            var ray = runtimeCamera.ScreenPointToRay(mousePosition);
            var plane = new Plane(Vector3.up, new Vector3(0f, planeY, 0f));
            if (plane.Raycast(ray, out var enter))
            {
                point = ray.GetPoint(enter);
                return true;
            }

            point = Vector3.zero;
            return false;
        }

        private bool IsPointerOverGui()
        {
            var mouse = Mouse.current;
            if (mouse == null)
            {
                return false;
            }

            var position = mouse.position.ReadValue();
            var guiPoint = new Vector2(position.x, Screen.height - position.y);

            foreach (var rect in guiBlockRects)
            {
                if (rect.Contains(guiPoint))
                {
                    return true;
                }
            }

            return false;
        }

        private void RegisterGuiRect(Rect rect)
        {
            guiBlockRects.Add(rect);
        }

        private void LogInteraction(string type, string word, string anchorId, string details)
        {
            interactionLogs.Add(new InteractionLog
            {
                timestampUtc = DateTime.UtcNow.ToString("o"),
                type = type,
                word = word,
                anchorId = anchorId,
                details = details
            });
        }

        private int CountSnapshotResponses(string phase, bool? requireCorrect)
        {
            var count = 0;
            for (int i = 0; i < snapshotTestResponses.Count; i++)
            {
                if (!string.Equals(snapshotTestResponses[i].phase, phase, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (requireCorrect.HasValue && snapshotTestResponses[i].isCorrect != requireCorrect.Value)
                {
                    continue;
                }

                count++;
            }

            return count;
        }

        private MnemonicItemData FindItemByWord(string word)
        {
            for (int i = 0; i < currentItems.Count; i++)
            {
                if (string.Equals(currentItems[i].word, word, StringComparison.Ordinal))
                {
                    return currentItems[i];
                }
            }

            return null;
        }

        private void ShuffleList<T>(List<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                var swapIndex = UnityEngine.Random.Range(0, i + 1);
                var temp = list[i];
                list[i] = list[swapIndex];
                list[swapIndex] = temp;
            }
        }

        private string ResolveProviderLabel()
        {
            if (condition == ExperimentCondition.SelfGenerated)
            {
                return "Self Authored";
            }

            return "Ollama Local";
        }

        private bool IsUsingLiveLlm()
        {
            return condition == ExperimentCondition.LlmGenerated && usedLiveLlmForCurrentSession;
        }

        private string GetLlmStatusText()
        {
            if (condition == ExperimentCondition.SelfGenerated)
            {
                return "This run uses participant-authored scenes and connections.";
            }

            if (IsUsingLiveLlm())
            {
                return "This run is using a direct local Ollama response.";
            }

            if (!string.IsNullOrWhiteSpace(generationError))
            {
                return "Ollama generation failed. No local fallback is enabled for this condition.";
            }

            return "This condition is configured for direct Ollama generation only.";
        }

        private static bool CompareAnswers(string expected, string answer)
        {
            var normalizedExpected = NormalizeText(expected);
            var normalizedAnswer = NormalizeText(answer);

            if (string.IsNullOrWhiteSpace(normalizedExpected) || string.IsNullOrWhiteSpace(normalizedAnswer))
            {
                return false;
            }

            return normalizedExpected == normalizedAnswer
                || normalizedExpected.IndexOf(normalizedAnswer, StringComparison.Ordinal) >= 0
                || normalizedAnswer.IndexOf(normalizedExpected, StringComparison.Ordinal) >= 0;
        }

        private static string NormalizeText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length);
            foreach (var ch in value.Trim().ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(ch))
                {
                    builder.Append(ch);
                }
            }

            return builder.ToString();
        }

        private static Texture2D MakeTexture(Color color)
        {
            var texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }

        private static float GetMnemonicLabelSize(string word)
        {
            if (string.IsNullOrWhiteSpace(word))
            {
                return 0.08f;
            }

            if (word.Length >= 10)
            {
                return 0.07f;
            }

            if (word.Length >= 8)
            {
                return 0.075f;
            }

            return 0.085f;
        }

        private Font GetLabelFont()
        {
            if (labelFont != null)
            {
                return labelFont;
            }

            try
            {
                labelFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
            catch (ArgumentException)
            {
                try
                {
                    labelFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
                }
                catch (ArgumentException)
                {
                    Debug.LogWarning("No built-in runtime font was available for TextMesh labels.");
                }
            }

            return labelFont;
        }

        private static string QuoteCsv(string value)
        {
            value ??= string.Empty;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private static PrimitiveType ResolveShape(string shapeName)
        {
            return (shapeName ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "cube" => PrimitiveType.Cube,
                "capsule" => PrimitiveType.Capsule,
                "cylinder" => PrimitiveType.Cylinder,
                _ => PrimitiveType.Sphere
            };
        }

        private static bool ShouldAnimateVisualObject(string effect)
        {
            if (string.IsNullOrWhiteSpace(effect))
            {
                return true;
            }

            return !string.Equals(effect, "none", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(effect, "dim", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ContainsAny(string value, params string[] terms)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            for (int i = 0; i < terms.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(terms[i]) && value.IndexOf(terms[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static string SanitizeIdPrefix(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "custom_furniture";
            }

            var builder = new StringBuilder(value.Length);
            foreach (var ch in value.Trim().ToLowerInvariant())
            {
                if (ch <= 127 && char.IsLetterOrDigit(ch))
                {
                    builder.Append(ch);
                }
                else if (builder.Length > 0 && builder[builder.Length - 1] != '_')
                {
                    builder.Append('_');
                }
            }

            var result = builder.ToString().Trim('_');
            return string.IsNullOrWhiteSpace(result) ? "custom_furniture" : result;
        }

        private static string PickShape(int index)
        {
            var shapes = new[] { "Sphere", "Cube", "Capsule", "Cylinder" };
            return shapes[index % shapes.Length];
        }

        private static string PickColor(int index)
        {
            var colors = new[]
            {
                "#7EC8E3", "#F4A261", "#E76F51", "#A8DADC", "#2A9D8F",
                "#FFD166", "#E9C46A", "#CCD5AE", "#457B9D", "#9B5DE5"
            };
            return colors[index % colors.Length];
        }

        private string DrawLabeledTextField(string label, string value, bool password = false)
        {
            GUILayout.Label(label, mutedStyle);
            return password ? GUILayout.PasswordField(value ?? string.Empty, '*') : GUILayout.TextField(value ?? string.Empty);
        }

        private void DrawBilingualSection(string heading, string englishText, string japaneseText, GUIStyle headingStyle, GUIStyle bodyStyle)
        {
            GUILayout.Label(heading, headingStyle);

            if (!string.IsNullOrWhiteSpace(englishText))
            {
                GUILayout.Label("EN: " + englishText, bodyStyle);
            }

            if (!string.IsNullOrWhiteSpace(japaneseText))
            {
                GUILayout.Label("JA: " + japaneseText, bodyStyle);
            }
        }

        private int DrawSlider(string label, int value, int min, int max)
        {
            GUILayout.BeginHorizontal(sectionStyle);
            GUILayout.Label($"{label}: {value}", labelStyle, GUILayout.Width(240f));
            value = Mathf.RoundToInt(GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(240f)));
            GUILayout.EndHorizontal();
            return value;
        }

        private int DrawSevenPointScale(string label, int value)
        {
            GUILayout.BeginHorizontal(sectionStyle);
            GUILayout.Label(label, labelStyle, GUILayout.Width(140f));
            value = GUILayout.Toolbar(Mathf.Clamp(value - 1, 0, 6), new[] { "1", "2", "3", "4", "5", "6", "7" }) + 1;
            GUILayout.EndHorizontal();
            return value;
        }

        private sealed class FurnitureTemplate
        {
            public FurnitureTemplate(
                string label,
                string idPrefix,
                string shape,
                string colorHex,
                Vector3 scale,
                float defaultY,
                float mnemonicYOffset,
                IEnumerable<VisualObjectSpec> modelParts = null)
            {
                Label = label;
                IdPrefix = idPrefix;
                Shape = shape;
                ColorHex = colorHex;
                Scale = scale;
                DefaultY = defaultY;
                MnemonicYOffset = mnemonicYOffset;
                LabelHeight = Mathf.Max(0.65f, scale.y * 0.75f + 0.45f);
                ModelParts = modelParts == null ? new List<VisualObjectSpec>() : new List<VisualObjectSpec>(modelParts);
            }

            public string Label { get; }
            public string IdPrefix { get; }
            public string Shape { get; }
            public string ColorHex { get; }
            public Vector3 Scale { get; }
            public float DefaultY { get; }
            public float MnemonicYOffset { get; }
            public float LabelHeight { get; }
            public List<VisualObjectSpec> ModelParts { get; }
        }

        [Serializable]
        private sealed class StableDiffusionOverrideSettings
        {
            public string sd_model_checkpoint;
        }

        [Serializable]
        private sealed class StableDiffusionTxt2ImgRequest
        {
            public string prompt;
            public string negative_prompt;
            public int width;
            public int height;
            public int steps;
            public float cfg_scale;
            public string sampler_name;
            public StableDiffusionOverrideSettings override_settings;
            public bool override_settings_restore_afterwards;
        }

        [Serializable]
        private sealed class StableDiffusionTxt2ImgResponse
        {
            public string[] images;
        }
    }

    public sealed class RoomAnchorInteractable : MonoBehaviour
    {
        public int Index;
    }

    public sealed class RoomPrimitiveInteractable : MonoBehaviour
    {
        public int Index;
    }

    public sealed class BuilderGizmoHandle : MonoBehaviour
    {
        public int AnchorIndex;
        public int PrimitiveIndex = -1;
        public string ToolName;
        public Vector3 Axis;
    }

    public sealed class StudyInteractable : MonoBehaviour
    {
        public MnemonicItemData Data;
    }

    public sealed class HoverSpinAnimation : MonoBehaviour
    {
        public Vector3 basePosition;
        public float rotationSpeed = 60f;
        public float bobAmplitude = 0.08f;
        public float bobFrequency = 1.25f;

        private void Update()
        {
            transform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime, Space.World);
            transform.position = basePosition + Vector3.up * (Mathf.Sin(Time.time * bobFrequency) * bobAmplitude);
        }
    }

    public sealed class BillboardToMainCamera : MonoBehaviour
    {
        private void LateUpdate()
        {
            if (Camera.main == null)
            {
                return;
            }

            transform.forward = Camera.main.transform.forward;
        }
    }
}
