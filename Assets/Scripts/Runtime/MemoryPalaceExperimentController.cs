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
using XRCommonUsages = UnityEngine.XR.CommonUsages;
using XRInputDevice = UnityEngine.XR.InputDevice;

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
        private const string FormalWordPoolSetId = "formal_12_pool";
        private const float BuilderAxisDragScale = 0.012f;
        private const float BuilderRotateDragScale = 0.45f;
        private const float BuilderScaleDragScale = 0.01f;
        private const float VrDefaultHeadHeight = 1.20f;
        private const float VrPointerDistance = 8f;
        private const float VrActionCooldownSeconds = 0.35f;
        private const float StudyDetailMaxDistance = 2.85f;
        private const float StudyDetailFacingDotThreshold = 0.5f;
        private const int RequiredImagePromptCandidateCount = 4;
        private const int BufferedImageCueResultCount = 4;

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

        private enum MnemonicReviewExportScope
        {
            MissingOnly,
            MissingOrUnreviewed,
            Full
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
            new("Desk", "desk", "Cube", "#7A5A3B", new Vector3(0.84f, 0.15f, 0.48f), 0.48f, 0.64f),
            new("Chair", "chair", "Cube", "#5C6A7A", new Vector3(0.31f, 0.54f, 0.31f), 0.30f, 0.48f),
            new("Shelf", "shelf", "Cube", "#8B6A3A", new Vector3(0.60f, 1.48f, 0.22f), 0.74f, 0.92f),
            new("Table", "table", "Cube", "#8B6A3A", new Vector3(0.88f, 0.15f, 0.60f), 0.46f, 0.58f),
            new("Sofa", "sofa", "Cube", "#6A5C72", new Vector3(1.08f, 0.60f, 0.50f), 0.38f, 0.62f),
            new("Bed", "bed", "Cube", "#D8D6D0", new Vector3(1.30f, 0.30f, 0.90f), 0.28f, 0.54f),
            new("Plant", "plant", "Cylinder", "#6F8F5D", new Vector3(0.30f, 0.82f, 0.30f), 0.46f, 0.66f),
            new("Lamp", "lamp", "Sphere", "#FFD98A", new Vector3(0.23f, 0.23f, 0.23f), 0.86f, 0.0f)
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
        private Text vrPreviewHeaderText;
        private RawImage vrPreviewImage;
        private Text vrPreviewInfoText;
        private VrPanelButtonInteractable vrGenerateButton;
        private VrPanelButtonInteractable vrPreviousImageButton;
        private VrPanelButtonInteractable vrNextImageButton;
        private VrPanelButtonInteractable vrCaptureButton;
        private VrPanelButtonInteractable vrAdvanceButton;
        private Font labelFont;

        private readonly List<Rect> guiBlockRects = new();
        private readonly HashSet<string> viewedWords = new();
        private readonly HashSet<string> memorizedWords = new();
        private readonly List<InteractionLog> interactionLogs = new();
        private readonly List<RecallResponse> recallResponses = new();
        private readonly List<SnapshotTestResponse> snapshotTestResponses = new();
        private readonly Dictionary<string, Texture2D> memorySnapshots = new();
        private readonly Dictionary<string, Texture2D> mnemonicImageCues = new();
        private readonly Dictionary<string, List<ImageCueCandidateResult>> imageCueCandidateResults = new();
        private readonly Dictionary<string, int> displayedImageCueCandidateIndexes = new();
        private readonly Dictionary<string, string> imageCueValidationFailures = new();
        private readonly Dictionary<string, Transform> studyItemTargets = new();
        private readonly HashSet<string> generatingImageCueWords = new();
        private readonly HashSet<string> regeneratingMnemonicWords = new();
        private readonly List<string> recognitionQueue = new();
        private readonly List<string> recognitionOptions = new();
        private readonly System.Random randomAdvancedWordSampler = new(Guid.NewGuid().GetHashCode());
        private readonly System.Random mnemonicAnchorRandom = new(Guid.NewGuid().GetHashCode());
        private readonly List<string> previousRandomAdvancedSampleWords = new();

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
        private string geminiModel = "gemini-2.5-flash";
        private string geminiApiKey = string.Empty;
        private string imageGenerationEndpoint = "http://127.0.0.1:7860/sdapi/v1/txt2img";
        private string imageGenerationCheckpoint = string.Empty;
        private string imageCueValidationModel = "gemma3:12b";
        private bool enableVrStudyMode = true;
        private bool showAbstractMnemonicProps = false;
        private bool preferPreGeneratedMnemonics = true;
        private bool allowLiveLlmForMissingPreGenerated = false;
        private bool useLocalFallbackForMissingPreGenerated = true;
        private bool usePreGeneratedImageCueCatalog = true;
        private bool allowRuntimeImageCueGenerationForMissing = false;
        private bool randomizeMnemonicAnchors = false;
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
        private string preStudyImageCueStatus = string.Empty;
        private string geminiProviderStatus = string.Empty;
        private string preGeneratedCatalogStatus = string.Empty;
        private string preGeneratedImageCueCatalogStatus = string.Empty;
        private string mnemonicReviewBuilderStatus = string.Empty;
        private string mnemonicReviewJsonText = string.Empty;
        private string exportMessage = string.Empty;
        private string customCsvText = string.Empty;
        private bool useCustomCsv;
        private bool isGenerating;
        private bool isGeneratingRoom;
        private bool isGeneratingFurniture;
        private bool isGeneratingGuidedFurnitureLayout;
        private bool isTestingGeminiProvider;
        private bool isPreparingImageCuesBeforeStudy;
        private bool isBuildingImageCueCatalog;
        private bool showImageCueCatalogBuilder;
        private bool showMnemonicCatalogReviewBuilder;
        private int preStudyImageCueFailureCount;
        private int imageCueCatalogBuilderCompletedCount;
        private int imageCueCatalogBuilderTargetCount;
        private Coroutine preStudyImageCueCoroutine;
        private Coroutine imageCueCatalogBuilderCoroutine;
        private bool usedLiveLlmForCurrentSession;
        private bool usedPreGeneratedForCurrentSession;
        private bool usedLocalFallbackForCurrentSession;
        private int preGeneratedMnemonicHitCount;
        private int liveGeneratedMnemonicCount;
        private int localFallbackMnemonicCount;
        private string liveMnemonicProviderLabelForCurrentSession = string.Empty;
        private string liveMnemonicModelForCurrentSession = string.Empty;
        private string liveMnemonicSourceForCurrentSession = string.Empty;
        private string imageCueCatalogBuilderStatus = string.Empty;
        private string imageCueCatalogTargetWord = "aeropuerto";
        private string imageCueCatalogTargetAnchorType = "bed";
        private bool imageCueCatalogSkipVisionScoring = true;
        private bool usingRandomAdvancedWordSet;
        private readonly List<string> preGeneratedCatalogDetails = new();
        private readonly List<string> preGeneratedImageCueCatalogDetails = new();

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
                UpdateSelectedStudyItemVisibility();

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
                providerMode = (LlmProviderMode)GUILayout.Toolbar((int)providerMode, new[] { "Ollama", "Gemini" });
                if (providerMode == LlmProviderMode.GeminiOnline)
                {
                    geminiModel = DrawLabeledTextField("Gemini Mnemonic Model", geminiModel);
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("Use Flash Free", buttonStyle))
                    {
                        geminiModel = "gemini-2.5-flash";
                    }

                    if (GUILayout.Button("Use Flash-Lite Free", buttonStyle))
                    {
                        geminiModel = "gemini-2.5-flash-lite";
                    }

                    if (GUILayout.Button("Use Pro Paid", buttonStyle))
                    {
                        geminiModel = "gemini-2.5-pro";
                    }
                    GUILayout.EndHorizontal();
                    geminiApiKey = DrawLabeledTextField("Gemini API Key", geminiApiKey, true);
                    GUILayout.Label("Free keys should use Flash or Flash-Lite; Pro usually requires billing. If the key field is blank, the app uses GEMINI_API_KEY or GOOGLE_API_KEY from the environment.", mutedStyle);
                    GUI.enabled = !isTestingGeminiProvider;
                    if (GUILayout.Button(isTestingGeminiProvider ? "Testing Gemini..." : "Test Gemini Connection", buttonStyle))
                    {
                        StartCoroutine(TestGeminiProviderRoutine());
                    }
                    GUI.enabled = true;
                    if (!string.IsNullOrWhiteSpace(geminiProviderStatus))
                    {
                        GUILayout.Label(geminiProviderStatus, mutedStyle);
                    }
                }
                else
                {
                    ollamaBaseUrl = DrawLabeledTextField("Ollama Endpoint", ollamaBaseUrl);
                    ollamaModel = DrawLabeledTextField("Mnemonic Text Model", ollamaModel);
                }

                imageGenerationEndpoint = DrawLabeledTextField("Image Endpoint", imageGenerationEndpoint);
                imageGenerationCheckpoint = DrawLabeledTextField("Image Checkpoint", imageGenerationCheckpoint);
                imageCueValidationModel = DrawLabeledTextField("Image Cue Validation Model", imageCueValidationModel);
                usePreGeneratedImageCueCatalog = GUILayout.Toggle(usePreGeneratedImageCueCatalog, $"Use pre-generated image cue catalog ({PreGeneratedImageCueCatalog.EntryCount} word x furniture entries)");
                allowRuntimeImageCueGenerationForMissing = GUILayout.Toggle(allowRuntimeImageCueGenerationForMissing, "Researcher mode: allow slow local SD generation for missing image cues");
                GUILayout.Label("Formal sessions should load pre-generated image cues. Runtime SD generation is slow and should be used only while preparing the catalog.", mutedStyle);
                preferPreGeneratedMnemonics = GUILayout.Toggle(preferPreGeneratedMnemonics, $"Prefer pre-generated word x furniture mnemonics ({PreGeneratedMnemonicCatalog.EntryCount} loaded)");
                GUI.enabled = preferPreGeneratedMnemonics;
                randomizeMnemonicAnchors = GUILayout.Toggle(randomizeMnemonicAnchors, "Randomize furniture anchors before mnemonic lookup");
                useLocalFallbackForMissingPreGenerated = GUILayout.Toggle(useLocalFallbackForMissingPreGenerated, "Use local story-only fallback for missing catalog combinations");
                allowLiveLlmForMissingPreGenerated = GUILayout.Toggle(allowLiveLlmForMissingPreGenerated, $"Try {GetSelectedLiveMnemonicProviderLabel()} to improve missing combinations when available");
                GUI.enabled = true;
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Check Catalog Coverage", buttonStyle))
                {
                    RefreshPreGeneratedCatalogCoverage();
                }

                if (GUILayout.Button("Check 12 x 10 Mnemonic Matrix", buttonStyle))
                {
                    RefreshFullMnemonicMatrixCoverage();
                }

                if (GUILayout.Button("Validate Catalog", buttonStyle))
                {
                    ValidatePreGeneratedCatalog();
                }
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Check Image Catalog Coverage", buttonStyle))
                {
                    RefreshPreGeneratedImageCueCatalogCoverage();
                }

                if (GUILayout.Button("Check 12 x 10 Image Matrix", buttonStyle))
                {
                    RefreshFullImageCueMatrixCoverage();
                }

                if (GUILayout.Button("Validate Image Catalog", buttonStyle))
                {
                    ValidatePreGeneratedImageCueCatalog();
                }
                GUILayout.EndHorizontal();
                if (!string.IsNullOrWhiteSpace(preGeneratedCatalogStatus))
                {
                    GUILayout.Label(preGeneratedCatalogStatus, mutedStyle);
                    for (int i = 0; i < preGeneratedCatalogDetails.Count; i++)
                    {
                        GUILayout.Label(preGeneratedCatalogDetails[i], mutedStyle);
                    }
                }

                if (!string.IsNullOrWhiteSpace(preGeneratedImageCueCatalogStatus))
                {
                    GUILayout.Label(preGeneratedImageCueCatalogStatus, mutedStyle);
                    for (int i = 0; i < preGeneratedImageCueCatalogDetails.Count; i++)
                    {
                        GUILayout.Label(preGeneratedImageCueCatalogDetails[i], mutedStyle);
                    }
                }

                DrawMnemonicCatalogReviewBuilderPanel();
                DrawImageCueCatalogBuilderPanel();

                showAbstractMnemonicProps = GUILayout.Toggle(showAbstractMnemonicProps, "Show experimental 3D proxy props in the room");
                GUILayout.Label("Mnemonic text uses pre-generated catalog hits first. Missing text uses local story-only fallback by default. Cue images should be pre-generated and loaded from catalog for formal sessions.", mutedStyle);
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
            if (GUILayout.Button($"Sample {Mathf.Min(RandomAdvancedWordCount, RoomSpecCatalog.AnchorCount)} Words", buttonStyle))
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
                GUILayout.Label("Format: word,meaning", mutedStyle);
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
            GUILayout.Label("Create a clean covered room preview first. Advanced gizmo editing is available only if you expand the manual editor.", mutedStyle);
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
                statusMessage = "Generated a furnished room preview with raised walls and a ceiling. If it matches the participant's room, continue to entrance view.";
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
            var width = Mathf.Clamp((shape == "Square" ? 5.6f : isLShape ? 7.0f : 7.4f) + guidedRoomWidthAdjustment, 4.4f, 9.2f);
            var depth = Mathf.Clamp((shape == "Square" ? 5.6f : isLShape ? 6.1f : 5.1f) + guidedRoomDepthAdjustment, 4.2f, 8.8f);
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
                    position = new Vector3(width * 0.52f, 5.3f, -depth * 0.82f),
                    eulerAngles = new Vector3(58f, -34f, 0f)
                },
                studyCamera = new CameraPoseDefinition
                {
                    position = new Vector3(-halfWidth + 0.95f, 1.55f, -halfDepth + 0.95f),
                    eulerAngles = new Vector3(0f, 38f, 0f)
                }
            };

            var wallHeight = RoomSpecCatalog.DefaultShellWallHeight;
            var wallY = wallHeight * 0.5f;
            var doorCenterX = 0f;
            var doorGap = 1.1f;

            if (isLShape)
            {
                GetGuidedLShapeParameters(width, depth, out var sideArmWidth, out var backArmDepth, out var cutX, out var cutZ);
                doorCenterX = minX + sideArmWidth * 0.5f;

                room.environmentPrimitives.Add(CreateGuidedRoomPrimitive("floor_side_arm", "L Floor Side Arm", "Cube", "#E7D9C1", new Vector3((minX + cutX) * 0.5f, 0f, 0f), new Vector3(sideArmWidth, 0.06f, depth), Vector3.zero));
                room.environmentPrimitives.Add(CreateGuidedRoomPrimitive("floor_back_arm", "L Floor Back Arm", "Cube", "#E1C8AA", new Vector3((cutX + maxX) * 0.5f, 0f, (cutZ + maxZ) * 0.5f), new Vector3(maxX - cutX, 0.06f, backArmDepth), Vector3.zero));

                AddHorizontalGuidedPrimitive(room, "floor_border_front", "Floor Border", "#B98A58", minX, cutX, minZ, 0.042f, 0.06f, 0.06f);
                AddHorizontalGuidedPrimitive(room, "floor_border_back", "Floor Border", "#B98A58", minX, maxX, maxZ, 0.042f, 0.06f, 0.06f);
                AddVerticalGuidedPrimitive(room, "floor_border_left", "Floor Border", "#B98A58", minX, minZ, maxZ, 0.042f, 0.06f, 0.06f);
                AddVerticalGuidedPrimitive(room, "floor_border_right_upper", "Floor Border", "#B98A58", maxX, cutZ, maxZ, 0.042f, 0.06f, 0.06f);
                AddHorizontalGuidedPrimitive(room, "floor_border_inner_horizontal", "L Inner Floor Border", "#B98A58", cutX, maxX, cutZ, 0.042f, 0.06f, 0.06f);
                AddVerticalGuidedPrimitive(room, "floor_border_inner_vertical", "L Inner Floor Border", "#B98A58", cutX, minZ, cutZ, 0.042f, 0.06f, 0.06f);

                AddHorizontalGuidedPrimitive(room, "wall_back", "Back Wall", "#F3F0E8", minX, maxX, maxZ + 0.04f, wallY, wallHeight, 0.12f);
                AddVerticalGuidedPrimitive(room, "wall_left", "Left Wall", "#F7F4EC", minX - 0.04f, minZ, maxZ, wallY, wallHeight, 0.12f);
                AddVerticalGuidedPrimitive(room, "wall_right_upper", "Right Wall", "#F7F4EC", maxX + 0.04f, cutZ, maxZ, wallY, wallHeight, 0.12f);
                AddHorizontalGuidedPrimitive(room, "wall_inner_horizontal", "L Shape Inner Wall", "#F7F4EC", cutX, maxX, cutZ - 0.04f, wallY, wallHeight, 0.12f);
                AddVerticalGuidedPrimitive(room, "wall_inner_vertical", "L Shape Inner Wall", "#F7F4EC", cutX + 0.04f, minZ, cutZ, wallY, wallHeight, 0.12f);

                AddFrontWallWithDoor(room, minX, cutX, minZ - 0.04f, doorCenterX, doorGap, wallY, wallHeight);
            }
            else
            {
                room.environmentPrimitives.Add(CreateGuidedRoomPrimitive("floor", "Warm Wood Floor", "Cube", "#E7D9C1", Vector3.zero, new Vector3(width, 0.06f, depth), Vector3.zero));
                room.environmentPrimitives.Add(CreateGuidedRoomPrimitive("floor_border_front", "Floor Border", "Cube", "#B98A58", new Vector3(0f, 0.042f, -halfDepth), new Vector3(width, 0.06f, 0.06f), Vector3.zero));
                room.environmentPrimitives.Add(CreateGuidedRoomPrimitive("floor_border_back", "Floor Border", "Cube", "#B98A58", new Vector3(0f, 0.042f, halfDepth), new Vector3(width, 0.06f, 0.06f), Vector3.zero));
                room.environmentPrimitives.Add(CreateGuidedRoomPrimitive("floor_border_left", "Floor Border", "Cube", "#B98A58", new Vector3(-halfWidth, 0.042f, 0f), new Vector3(0.06f, 0.06f, depth), Vector3.zero));
                room.environmentPrimitives.Add(CreateGuidedRoomPrimitive("floor_border_right", "Floor Border", "Cube", "#B98A58", new Vector3(halfWidth, 0.042f, 0f), new Vector3(0.06f, 0.06f, depth), Vector3.zero));

                room.environmentPrimitives.Add(CreateGuidedRoomPrimitive("wall_back", "Back Wall", "Cube", "#F3F0E8", new Vector3(0f, wallY, halfDepth + 0.04f), new Vector3(width, wallHeight, 0.12f), Vector3.zero));
                room.environmentPrimitives.Add(CreateGuidedRoomPrimitive("wall_left", "Left Wall", "Cube", "#F7F4EC", new Vector3(-halfWidth - 0.04f, wallY, 0f), new Vector3(0.12f, wallHeight, depth), Vector3.zero));
                room.environmentPrimitives.Add(CreateGuidedRoomPrimitive("wall_right", "Right Wall", "Cube", "#F7F4EC", new Vector3(halfWidth + 0.04f, wallY, 0f), new Vector3(0.12f, wallHeight, depth), Vector3.zero));
                AddFrontWallWithDoor(room, minX, maxX, minZ - 0.04f, doorCenterX, doorGap, wallY, wallHeight);
            }

            if (guidedHasBathroom)
            {
                var bathWidth = Mathf.Min(1.85f, isLShape ? width * 0.34f : width * 0.30f);
                var bathDepth = Mathf.Min(1.65f, depth * 0.30f);
                var bathCenter = GetGuidedBathroomCenter(width, depth, bathWidth, bathDepth);
                room.environmentPrimitives.Add(CreateGuidedRoomPrimitive("bath_tile_floor", "Bathroom Tile", "Cube", "#C7D7DF", bathCenter, new Vector3(bathWidth, 0.05f, bathDepth), Vector3.zero));
                room.environmentPrimitives.Add(CreateGuidedRoomPrimitive("bath_divider_back", "Bathroom Divider", "Cube", "#E8ECEF", new Vector3(bathCenter.x, wallY, bathCenter.z + bathDepth * 0.5f), new Vector3(bathWidth, wallHeight, 0.10f), Vector3.zero));
                room.environmentPrimitives.Add(CreateGuidedRoomPrimitive("bath_divider_left", "Bathroom Divider", "Cube", "#E8ECEF", new Vector3(bathCenter.x - bathWidth * 0.5f, wallY, bathCenter.z), new Vector3(0.10f, wallHeight, bathDepth), Vector3.zero));
            }

            AddDollhouseFloorZones(room, width, depth, isLShape);
            AddDollhouseInteriorPartitions(room, width, depth, isLShape, wallY, wallHeight);
            AddDollhouseWallCaps(room);
            AddDollhouseWindowsAndProps(room, width, depth, isLShape);

            var doorTemplate = BuildCustomFurnitureTemplate("Door");
            room.anchors.Add(CreateGuidedAnchor(room, doorTemplate, new Vector3(doorCenterX, doorTemplate.DefaultY, -halfDepth + 0.06f), Vector3.zero));
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
            if (length <= 0.06f)
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
            if (length <= 0.06f)
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
            sideArmWidth = Mathf.Clamp(roomWidth * 0.48f, 2.1f, roomWidth - 2.0f);
            backArmDepth = Mathf.Clamp(roomDepth * 0.60f, 2.2f, roomDepth - 1.8f);
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

            return new Vector3(doorX, doorTemplate.DefaultY, -roomDepth * 0.5f + 0.06f);
        }

        private Vector3 GetGuidedBathroomCenter(float roomWidth, float roomDepth, float bathWidth, float bathDepth)
        {
            var halfWidth = roomWidth * 0.5f;
            var halfDepth = roomDepth * 0.5f;
            var margin = 0.14f;
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
            isPreparingImageCuesBeforeStudy = false;
            preStudyImageCueCoroutine = null;
            generatingImageCueWords.Clear();
            regeneratingMnemonicWords.Clear();
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
            GUILayout.Label("This page previews each word-anchor mnemonic and prepares the generated image cues before the study room begins.", mutedStyle);
            GUILayout.Space(8);

            GUILayout.BeginVertical(sectionStyle);
            GUILayout.Label("Current Session", smallTitleStyle);
            GUILayout.Label($"Condition: {GetConditionDisplayName()}", labelStyle);
            GUILayout.Label($"Mnemonic Source: {ResolveProviderLabel()}", labelStyle);
            GUILayout.Label($"Live LLM Request Used: {(isGenerating ? "In progress" : (IsUsingLiveLlm() ? "Yes" : "No"))}", labelStyle);
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
                GUILayout.Label(GetDisplayMeaningText(item), mutedStyle);
                DrawTextSection("Association Image Cue", item.associationPrompt, smallTitleStyle, labelStyle);
                GUILayout.Space(4);
                DrawTextSection("Mnemonic", item.mnemonic, smallTitleStyle, labelStyle);
                GUILayout.Space(6);
                var previousEnabled = GUI.enabled;
                GUI.enabled = previousEnabled && !isPreparingImageCuesBeforeStudy;
                DrawRegenerateMnemonicButton(item);
                GUI.enabled = previousEnabled;
                GUILayout.EndVertical();
            }

            GUILayout.Space(12);
            DrawPreStudyImageCuePreparationPanel(false);

            GUILayout.Space(12);
            var imageCuesReady = AreAllCurrentImageCuesReady();
            GUI.enabled = !isGenerating
                          && !isPreparingImageCuesBeforeStudy
                          && currentItems.Count > 0
                          && (imageCuesReady || usePreGeneratedImageCueCatalog);
            if (GUILayout.Button("Next: Enter Study Room", buttonStyle))
            {
                EnterStudyRoom();
            }
            GUI.enabled = true;
            if (currentItems.Count > 0 && !imageCuesReady)
            {
                GUILayout.Label("Generate all image cues before entering the study room.", mutedStyle);
            }

            if (GUILayout.Button("Back to Setup", buttonStyle))
            {
                StopAllCoroutines();
                isGenerating = false;
                isPreparingImageCuesBeforeStudy = false;
                preStudyImageCueCoroutine = null;
                generatingImageCueWords.Clear();
                regeneratingMnemonicWords.Clear();
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
            GUILayout.Label("Assign each word to an anchor, write the cue text, then prepare the generated image cues before the study room begins.", mutedStyle);
            GUILayout.Space(10);

            GUI.enabled = !isPreparingImageCuesBeforeStudy;
            if (GUILayout.Button("Auto-Fill Starter Drafts", buttonStyle))
            {
                AutoFillSelfDrafts();
            }
            GUI.enabled = true;
            if (isPreparingImageCuesBeforeStudy)
            {
                GUILayout.Label("Draft editing is locked while image cues are being prepared.", mutedStyle);
            }

            GUILayout.Space(10);

            for (int i = 0; i < currentItems.Count; i++)
            {
                var item = currentItems[i];
                GUILayout.BeginVertical(sectionStyle);
                GUILayout.Label($"{i + 1}. {item.word}", titleStyle);
                GUILayout.Label(GetDisplayMeaningText(item), mutedStyle);

                GUILayout.BeginHorizontal();
                var previousEnabled = GUI.enabled;
                GUI.enabled = previousEnabled && !isPreparingImageCuesBeforeStudy;
                if (GUILayout.Button("< Anchor", GUILayout.Width(100f), GUILayout.Height(28f)))
                {
                    CycleAnchor(item, -1);
                }

                GUI.enabled = previousEnabled;
                GUILayout.Label($"Anchor: {item.anchorLabel}", labelStyle, GUILayout.Width(220f));

                GUI.enabled = previousEnabled && !isPreparingImageCuesBeforeStudy;
                if (GUILayout.Button("Anchor >", GUILayout.Width(100f), GUILayout.Height(28f)))
                {
                    CycleAnchor(item, 1);
                }
                GUI.enabled = previousEnabled;
                GUILayout.EndHorizontal();

                GUILayout.Label("Association Image Cue (EN)", mutedStyle);
                var previousAssociationPrompt = item.associationPrompt ?? string.Empty;
                GUI.enabled = previousEnabled && !isPreparingImageCuesBeforeStudy;
                item.associationPrompt = GUILayout.TextArea(item.associationPrompt ?? string.Empty, textAreaStyle, GUILayout.MinHeight(42f));
                GUI.enabled = previousEnabled;
                GUILayout.Label("Mnemonic (EN)", mutedStyle);
                var previousMnemonic = item.mnemonic ?? string.Empty;
                GUI.enabled = previousEnabled && !isPreparingImageCuesBeforeStudy;
                item.mnemonic = GUILayout.TextArea(item.mnemonic ?? string.Empty, textAreaStyle, GUILayout.MinHeight(84f));
                GUI.enabled = previousEnabled;
                if (!string.Equals(previousAssociationPrompt, item.associationPrompt ?? string.Empty, StringComparison.Ordinal)
                    || !string.Equals(previousMnemonic, item.mnemonic ?? string.Empty, StringComparison.Ordinal))
                {
                    ClearGeneratedImageCueForWord(item.word);
                }

                GUILayout.EndVertical();
            }

            GUILayout.Space(12);
            DrawPreStudyImageCuePreparationPanel(true);

            GUILayout.Space(12);
            var selfImageCuesReady = AreAllCurrentImageCuesReady();
            GUI.enabled = !isPreparingImageCuesBeforeStudy
                          && currentItems.Count > 0
                          && (selfImageCuesReady || usePreGeneratedImageCueCatalog);
            if (GUILayout.Button("Next: Enter Study Room", buttonStyle))
            {
                FinalizeSelfDrafts();
                EnterStudyRoom();
            }
            GUI.enabled = true;
            if (currentItems.Count > 0 && !selfImageCuesReady)
            {
                GUILayout.Label("Finalize drafts and generate all image cues before entering the study room.", mutedStyle);
            }

            if (GUILayout.Button("Back to Setup", buttonStyle))
            {
                StopAllCoroutines();
                isGenerating = false;
                isPreparingImageCuesBeforeStudy = false;
                preStudyImageCueCoroutine = null;
                stage = ExperimentStage.Setup;
                statusMessage = "Returned to setup.";
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawPreStudyImageCuePreparationPanel(bool finalizeSelfDraftsBeforeGeneration)
        {
            if (currentItems == null || currentItems.Count == 0)
            {
                return;
            }

            GUILayout.BeginVertical(sectionStyle);
            GUILayout.Label("Image Cue Preparation", smallTitleStyle);

            var readyCount = CountReadyImageCueItems();
            var totalCount = currentItems.Count;
            GUILayout.Label($"Prepared Image Cues: {readyCount}/{totalCount}", labelStyle);

            var missingWords = BuildMissingImageCueWordSummary();
            if (!string.IsNullOrWhiteSpace(missingWords))
            {
                GUILayout.Label("Missing: " + missingWords, mutedStyle);
            }

            if (!string.IsNullOrWhiteSpace(preStudyImageCueStatus))
            {
                GUILayout.Label(preStudyImageCueStatus, mutedStyle);
            }

            if (!string.IsNullOrWhiteSpace(imageGenerationStatus))
            {
                GUILayout.Label(imageGenerationStatus, mutedStyle);
            }

            GUILayout.BeginHorizontal();
            if (isPreparingImageCuesBeforeStudy)
            {
                if (GUILayout.Button("Cancel Image Cue Preparation", buttonStyle))
                {
                    CancelPreStudyImageCuePreparation();
                }
            }
            else
            {
                GUI.enabled = readyCount < totalCount;
                var loadMissingLabel = usePreGeneratedImageCueCatalog
                    ? "Load Missing Image Cues From Catalog"
                    : "Generate Missing Image Cues With Local SD";
                if (GUILayout.Button(loadMissingLabel, buttonStyle))
                {
                    BeginPreStudyImageCuePreparation(false, finalizeSelfDraftsBeforeGeneration);
                }

                GUI.enabled = totalCount > 0;
                var rebuildAllLabel = usePreGeneratedImageCueCatalog
                    ? "Reload All Image Cues From Catalog"
                    : "Regenerate All Image Cues With Local SD";
                if (GUILayout.Button(rebuildAllLabel, buttonStyle))
                {
                    BeginPreStudyImageCuePreparation(true, finalizeSelfDraftsBeforeGeneration);
                }

                GUI.enabled = true;
            }
            GUILayout.EndHorizontal();

            GUILayout.Label(
                usePreGeneratedImageCueCatalog
                    ? "Formal sessions load pre-generated image cues here so Study Room time is spent studying, not waiting."
                    : "Runtime image generation is enabled; this is slow and intended for catalog preparation only.",
                mutedStyle);
            GUILayout.EndVertical();
        }

        private void BeginPreStudyImageCuePreparation(bool regenerateAll, bool finalizeSelfDraftsBeforeGeneration)
        {
            if (isPreparingImageCuesBeforeStudy || currentItems == null || currentItems.Count == 0)
            {
                return;
            }

            if (finalizeSelfDraftsBeforeGeneration)
            {
                FinalizeSelfDrafts();
            }

            if (regenerateAll)
            {
                ClearAllGeneratedImageCueTextures();
                for (int i = 0; i < currentItems.Count; i++)
                {
                    if (currentItems[i] != null)
                    {
                        currentItems[i].imageCuePath = string.Empty;
                        currentItems[i].selectedImagePrompt = string.Empty;
                        currentItems[i].selectedImageCandidateIndex = -1;
                        currentItems[i].imageSelectionReason = string.Empty;
                    }
                }
            }

            preStudyImageCueCoroutine = StartCoroutine(PrepareImageCuesBeforeStudyRoutine());
        }

        private void CancelPreStudyImageCuePreparation()
        {
            if (preStudyImageCueCoroutine != null)
            {
                StopCoroutine(preStudyImageCueCoroutine);
                preStudyImageCueCoroutine = null;
            }

            isPreparingImageCuesBeforeStudy = false;
            generatingImageCueWords.Clear();
            preStudyImageCueStatus = "Image cue preparation was cancelled. Already prepared image cues are kept.";
            statusMessage = preStudyImageCueStatus;
        }

        private IEnumerator PrepareImageCuesBeforeStudyRoutine()
        {
            isPreparingImageCuesBeforeStudy = true;
            preStudyImageCueFailureCount = 0;

            if (currentItems == null || currentItems.Count == 0)
            {
                preStudyImageCueStatus = "No mnemonic items are available for image cue preparation.";
                isPreparingImageCuesBeforeStudy = false;
                preStudyImageCueCoroutine = null;
                yield break;
            }

            var totalCount = currentItems.Count;
            for (int i = 0; i < totalCount; i++)
            {
                var item = currentItems[i];
                if (item == null)
                {
                    continue;
                }

                if (HasReadyImageCue(item))
                {
                    preStudyImageCueStatus = $"Image cue {i + 1}/{totalCount} already prepared for {item.word}.";
                    continue;
                }

                if (usePreGeneratedImageCueCatalog)
                {
                    preStudyImageCueStatus = $"Loading image cue {i + 1}/{totalCount} from catalog: {item.word}.";
                    if (TryLoadPreGeneratedImageCueSet(item, out var catalogError))
                    {
                        preStudyImageCueStatus = $"Loaded image cue {i + 1}/{totalCount} from catalog: {item.word}.";
                        continue;
                    }

                    if (!allowRuntimeImageCueGenerationForMissing)
                    {
                        preStudyImageCueFailureCount++;
                        preStudyImageCueStatus = $"Missing pre-generated image cue for {item.word} x {item.anchorType}: {catalogError}";
                        LogInteraction("prestudy_image_catalog_missing", item.word, item.anchorId, preStudyImageCueStatus);
                        continue;
                    }

                    preStudyImageCueStatus = $"Catalog missing for {item.word}; using slow local SD fallback because researcher mode is enabled.";
                }

                if (!allowRuntimeImageCueGenerationForMissing)
                {
                    preStudyImageCueFailureCount++;
                    preStudyImageCueStatus = $"No pre-generated image cue is ready for {item.word}, and runtime image generation is disabled.";
                    LogInteraction("prestudy_image_cue_missing", item.word, item.anchorId, preStudyImageCueStatus);
                    break;
                }

                preStudyImageCueStatus = $"Preparing image cue {i + 1}/{totalCount} with slow local SD: {item.word}.";
                yield return GenerateMnemonicImageCueRoutine(item);

                if (!HasReadyImageCue(item))
                {
                    preStudyImageCueFailureCount++;
                    preStudyImageCueStatus = $"Image cue preparation failed for {item.word}. Fix the image/vision endpoints and generate missing cues again.";
                    LogInteraction("prestudy_image_cue_failed", item.word, item.anchorId, imageGenerationStatus);
                    break;
                }

                preStudyImageCueStatus = $"Prepared image cue {i + 1}/{totalCount}: {item.word}.";
            }

            isPreparingImageCuesBeforeStudy = false;
            preStudyImageCueCoroutine = null;

            var readyCount = CountReadyImageCueItems();
            if (readyCount >= totalCount)
            {
                preStudyImageCueStatus = $"All {readyCount}/{totalCount} image cues are ready. You can enter the study room.";
                statusMessage = "Image cue preparation complete.";
            }
            else if (preStudyImageCueFailureCount > 0)
            {
                preStudyImageCueStatus = $"Prepared {readyCount}/{totalCount} image cues; {preStudyImageCueFailureCount} item(s) failed. Generate missing cues again after fixing the endpoint/model.";
                statusMessage = preStudyImageCueStatus;
            }
            else
            {
                preStudyImageCueStatus = $"Prepared {readyCount}/{totalCount} image cues.";
            }
        }

        private bool TryLoadPreGeneratedImageCueSet(MnemonicItemData item, out string error)
        {
            error = string.Empty;
            if (item == null)
            {
                error = "Mnemonic item is null.";
                return false;
            }

            ClearGeneratedImageCueForWord(item.word);
            if (!PreGeneratedImageCueCatalog.TryLoadImageCueSet(item, out var loadedSet, out error))
            {
                return false;
            }

            for (int i = 0; i < loadedSet.variants.Count; i++)
            {
                var variant = loadedSet.variants[i];
                if (variant?.texture == null)
                {
                    continue;
                }

                var result = new ImageCueCandidateResult
                {
                    candidateIndex = variant.index,
                    label = variant.label,
                    innerLabel = "pre-generated catalog",
                    rawPrompt = string.IsNullOrWhiteSpace(variant.rawPrompt) ? item.associationPrompt : variant.rawPrompt,
                    fullPrompt = string.IsNullOrWhiteSpace(variant.fullPrompt) ? item.imagePrompt : variant.fullPrompt,
                    texture = variant.texture,
                    validation = null,
                    score = variant.score,
                    pass = variant.pass,
                    validationComplete = true,
                    reason = variant.reason,
                    innerCandidates = new List<ImageCueInnerCandidateResult>
                    {
                        new()
                        {
                            index = variant.index,
                            label = variant.label,
                            rawPrompt = string.IsNullOrWhiteSpace(variant.rawPrompt) ? item.associationPrompt : variant.rawPrompt,
                            fullPrompt = string.IsNullOrWhiteSpace(variant.fullPrompt) ? item.imagePrompt : variant.fullPrompt,
                            imagePath = variant.imagePath,
                            validation = null,
                            score = variant.score,
                            pass = variant.pass,
                            validationComplete = true,
                            reason = variant.reason
                        }
                    }
                };
                AddImageCueCandidateResult(item, result);
            }

            var primaryIndex = Mathf.Clamp(loadedSet.primaryIndex, 0, loadedSet.variants.Count - 1);
            TryDisplayImageCueCandidate(item, primaryIndex, false);
            imageGenerationStatus = $"Loaded {loadedSet.variants.Count} pre-generated image cue choice(s) for {item.word}.";
            LogInteraction("load_pregenerated_image_cue_set", item.word, item.anchorId, imageGenerationStatus);
            return HasReadyImageCue(item);
        }

        private bool TryLoadCurrentImageCuesFromCatalog(bool reloadAll, out int loadedCount, out int failedCount, out string firstError)
        {
            loadedCount = 0;
            failedCount = 0;
            firstError = string.Empty;
            if (currentItems == null || currentItems.Count == 0 || !usePreGeneratedImageCueCatalog)
            {
                return AreAllCurrentImageCuesReady();
            }

            PreGeneratedImageCueCatalog.Reload();
            for (int i = 0; i < currentItems.Count; i++)
            {
                var item = currentItems[i];
                if (item == null)
                {
                    continue;
                }

                if (!reloadAll && HasReadyImageCue(item))
                {
                    continue;
                }

                if (TryLoadPreGeneratedImageCueSet(item, out var error))
                {
                    loadedCount++;
                    continue;
                }

                failedCount++;
                if (string.IsNullOrWhiteSpace(firstError))
                {
                    firstError = $"{item.word} x {item.anchorType}: {error}";
                }
            }

            var readyCount = CountReadyImageCueItems();
            if (readyCount >= currentItems.Count)
            {
                preStudyImageCueStatus = $"All {readyCount}/{currentItems.Count} image cues are ready. You can enter the study room.";
            }
            else if (failedCount > 0)
            {
                preStudyImageCueStatus = $"Loaded {loadedCount} image cue set(s) from catalog; {failedCount} missing/failed. First issue: {firstError}";
            }
            else if (loadedCount > 0)
            {
                preStudyImageCueStatus = $"Loaded {loadedCount} image cue set(s) from catalog.";
            }

            return readyCount >= currentItems.Count;
        }

        private bool AreAllCurrentImageCuesReady()
        {
            return currentItems != null
                   && currentItems.Count > 0
                   && CountReadyImageCueItems() >= currentItems.Count;
        }

        private int CountReadyImageCueItems()
        {
            if (currentItems == null)
            {
                return 0;
            }

            var count = 0;
            for (int i = 0; i < currentItems.Count; i++)
            {
                if (HasReadyImageCue(currentItems[i]))
                {
                    count++;
                }
            }

            return count;
        }

        private bool HasReadyImageCue(MnemonicItemData item)
        {
            return item != null
                   && !string.IsNullOrWhiteSpace(item.word)
                   && mnemonicImageCues.TryGetValue(item.word, out var texture)
                   && texture != null
                   && imageCueCandidateResults.TryGetValue(item.word, out var results)
                   && results != null
                   && results.Count > 0;
        }

        private string BuildMissingImageCueWordSummary()
        {
            if (currentItems == null || currentItems.Count == 0)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            for (int i = 0; i < currentItems.Count; i++)
            {
                var item = currentItems[i];
                if (HasReadyImageCue(item))
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(string.IsNullOrWhiteSpace(item?.word) ? $"item {i + 1}" : item.word);
            }

            return builder.ToString();
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
            GUILayout.Label($"Live LLM Call: {(IsUsingLiveLlm() ? "Yes" : "No")}", labelStyle);
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

            if (selectedStudyItem != null)
            {
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
                GUILayout.Label("Why There Are Separate Text Fields", smallTitleStyle);
                GUILayout.Label("Association Image Cue is only for generated images. Mnemonic is the learner-facing study text: either a strong hook plus story, or a story-only mnemonic when the hook would be weak.", guideStyle);
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
                GUILayout.Label(GetDisplayMeaningText(selectedStudyItem), mutedStyle);
                GUILayout.Label($"Anchor: {selectedStudyItem.anchorLabel}", mutedStyle);
                GUILayout.Space(10);
                DrawTextSection("Association Image Cue", selectedStudyItem.associationPrompt, smallTitleStyle, guideStyle);
                GUILayout.Space(10);
                DrawTextSection("Mnemonic", selectedStudyItem.mnemonic, smallTitleStyle, guideStyle);
                GUILayout.Space(8);
                DrawRegenerateMnemonicButton(selectedStudyItem);
                GUILayout.Space(8);
                if (selectedStudyItem.visualObjects != null && selectedStudyItem.visualObjects.Count > 0)
                {
                    GUILayout.Label(showAbstractMnemonicProps
                        ? $"3D proxy props visible: {selectedStudyItem.visualObjects.Count}"
                        : "3D proxy props are hidden; use the generated image cue as the main visual mnemonic.", mutedStyle);
                }
                GUILayout.Space(8);
                DrawImageCuePanel(selectedStudyItem);
                GUILayout.Label("Anchor = where it lives. Association Image Cue = what the image model draws. Mnemonic = what the learner studies.", mutedStyle);
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
            }
                GUILayout.EndScrollView();
                GUILayout.EndArea();
            }
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
                            GetDisplayMeaningText(answeredItem),
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
            GUILayout.Label($"Live LLM Request Used: {(IsUsingLiveLlm() ? "Yes" : "No")}", labelStyle);
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
                DrawTextSection("Association Image Cue", item.associationPrompt, smallTitleStyle, mutedStyle);
                GUILayout.Space(4);
                DrawTextSection("Mnemonic", item.mnemonic, smallTitleStyle, mutedStyle);
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
            usedPreGeneratedForCurrentSession = false;
            usedLocalFallbackForCurrentSession = false;
            preGeneratedMnemonicHitCount = 0;
            liveGeneratedMnemonicCount = 0;
            localFallbackMnemonicCount = 0;
            liveMnemonicProviderLabelForCurrentSession = string.Empty;
            liveMnemonicModelForCurrentSession = string.Empty;
            liveMnemonicSourceForCurrentSession = string.Empty;
            statusMessage = preferPreGeneratedMnemonics
                ? "Checking pre-generated mnemonic catalog..."
                : $"Calling {GetSelectedLiveMnemonicProviderLabel()} to generate mnemonic set...";

            StopAllCoroutines();
            isGenerating = true;
            StartCoroutine(RunLlmGeneration());
        }

        private IEnumerator RunLlmGeneration()
        {
            yield return new WaitForSecondsRealtime(0.4f);

            var words = activeWordSet?.words ?? new List<WordEntry>();
            var assignedAnchors = BuildMnemonicAnchorAssignments(words.Count);
            var mergedResults = new List<MnemonicItemData>();
            var missingWords = new List<WordEntry>();
            var missingAnchors = new List<AnchorDefinition>();
            var missingIndexes = new List<int>();

            preGeneratedMnemonicHitCount = 0;
            liveGeneratedMnemonicCount = 0;
            localFallbackMnemonicCount = 0;
            usedPreGeneratedForCurrentSession = false;
            usedLiveLlmForCurrentSession = false;
            usedLocalFallbackForCurrentSession = false;
            liveMnemonicProviderLabelForCurrentSession = string.Empty;
            liveMnemonicModelForCurrentSession = string.Empty;
            liveMnemonicSourceForCurrentSession = string.Empty;

            for (int i = 0; i < words.Count; i++)
            {
                mergedResults.Add(null);
                var anchor = i < assignedAnchors.Count ? assignedAnchors[i] : RoomSpecCatalog.GetAssignmentAnchor(i, words.Count);
                if (preferPreGeneratedMnemonics
                    && PreGeneratedMnemonicCatalog.TryCreateItem(words[i], anchor, i, out var preGeneratedItem))
                {
                    mergedResults[i] = preGeneratedItem;
                    preGeneratedMnemonicHitCount++;
                    continue;
                }

                missingWords.Add(words[i]);
                missingAnchors.Add(anchor);
                missingIndexes.Add(i);
            }

            usedPreGeneratedForCurrentSession = preGeneratedMnemonicHitCount > 0;

            if (missingWords.Count > 0)
            {
                var liveProviderLabel = GetSelectedLiveMnemonicProviderLabel();
                var liveSourceTag = GetSelectedLiveMnemonicSourceTag();
                var liveModelLabel = GetSelectedLiveMnemonicModelLabel();
                var service = new OllamaLlmService();
                List<MnemonicItemData> liveResults = null;
                string error = null;
                string liveFailure = null;
                var liveSucceeded = false;
                var shouldTryLiveProvider = !preferPreGeneratedMnemonics || allowLiveLlmForMissingPreGenerated;

                if (shouldTryLiveProvider)
                {
                    statusMessage = preGeneratedMnemonicHitCount > 0
                        ? $"Loaded {preGeneratedMnemonicHitCount} pre-generated items. Trying {liveProviderLabel} for {missingWords.Count} missing combinations..."
                        : $"Trying {liveProviderLabel} to generate mnemonic set...";

                    if (providerMode == LlmProviderMode.GeminiOnline)
                    {
                        var geminiKey = ResolveGeminiApiKey();
                        if (string.IsNullOrWhiteSpace(geminiKey) || string.IsNullOrWhiteSpace(geminiModel))
                        {
                            error = "Gemini API key or model is empty. Paste a key in setup or set GEMINI_API_KEY / GOOGLE_API_KEY.";
                        }
                        else
                        {
                            yield return StartCoroutine(service.GenerateGeminiMnemonics(
                                geminiKey,
                                geminiModel,
                                missingWords,
                                items => liveResults = items,
                                err => error = err,
                                missingAnchors));
                        }
                    }
                    else
                    {
                        yield return StartCoroutine(service.GenerateMnemonics(
                            ollamaBaseUrl,
                            ollamaModel,
                            missingWords,
                            items => liveResults = items,
                            err => error = err,
                            missingAnchors));
                    }

                    if (!string.IsNullOrWhiteSpace(error))
                    {
                        liveFailure = error;
                    }
                    else if (liveResults == null || liveResults.Count < missingWords.Count)
                    {
                        liveFailure = $"{liveProviderLabel} returned {liveResults?.Count ?? 0} live items for {missingWords.Count} missing combinations.";
                    }
                    else
                    {
                        liveSucceeded = true;
                    }
                }

                if (liveSucceeded)
                {
                    if (providerMode == LlmProviderMode.GeminiOnline
                        && !string.IsNullOrWhiteSpace(service.GeminiModelsUsedSummary))
                    {
                        liveModelLabel = service.GeminiModelsUsedSummary;
                    }

                    for (int i = 0; i < missingIndexes.Count; i++)
                    {
                        var item = liveResults[i];
                        if (item == null)
                        {
                            liveSucceeded = false;
                            liveFailure = $"{liveProviderLabel} returned an empty live item for {missingWords[i].word}.";
                            break;
                        }

                        item.mnemonicSource = string.IsNullOrWhiteSpace(item.mnemonicSource) ? liveSourceTag : item.mnemonicSource;
                        mergedResults[missingIndexes[i]] = item;
                    }

                    if (liveSucceeded)
                    {
                        liveGeneratedMnemonicCount = missingIndexes.Count;
                        usedLiveLlmForCurrentSession = liveGeneratedMnemonicCount > 0;
                        liveMnemonicProviderLabelForCurrentSession = liveProviderLabel;
                        liveMnemonicModelForCurrentSession = liveModelLabel;
                        liveMnemonicSourceForCurrentSession = liveSourceTag;
                    }
                }

                if (!liveSucceeded)
                {
                    if (!useLocalFallbackForMissingPreGenerated)
                    {
                        currentItems = new List<MnemonicItemData>();
                        generationError = string.IsNullOrWhiteSpace(liveFailure)
                            ? $"Pre-generated catalog is missing {missingWords.Count}/{words.Count} word-anchor combinations and local fallback is disabled."
                            : liveFailure;
                        statusMessage = string.IsNullOrWhiteSpace(liveFailure)
                            ? "Pre-generated mnemonic lookup was incomplete."
                            : $"{liveProviderLabel} request failed and local fallback is disabled.";
                        isGenerating = false;
                        yield break;
                    }

                    ApplyLocalFallbackForMissingMnemonics(missingWords, missingAnchors, missingIndexes, mergedResults);
                    statusMessage = string.IsNullOrWhiteSpace(liveFailure)
                        ? $"Loaded {preGeneratedMnemonicHitCount} pre-generated item(s) and filled {localFallbackMnemonicCount} missing item(s) with local story-only fallback."
                        : $"{liveProviderLabel} was unavailable, so {localFallbackMnemonicCount} missing item(s) were filled with local story-only fallback. Last provider error: {BuildShortPreview(liveFailure)}";
                }
            }

            currentItems = EnsureMnemonicDefaults(mergedResults);
            statusMessage = BuildMnemonicGenerationStatus();
            isGenerating = false;
        }

        private void ApplyLocalFallbackForMissingMnemonics(
            List<WordEntry> missingWords,
            List<AnchorDefinition> missingAnchors,
            List<int> missingIndexes,
            List<MnemonicItemData> mergedResults)
        {
            localFallbackMnemonicCount = 0;
            usedLocalFallbackForCurrentSession = false;
            if (missingWords == null || missingIndexes == null || mergedResults == null)
            {
                return;
            }

            for (int i = 0; i < missingWords.Count && i < missingIndexes.Count; i++)
            {
                var targetIndex = missingIndexes[i];
                if (targetIndex < 0 || targetIndex >= mergedResults.Count)
                {
                    continue;
                }

                var anchor = missingAnchors != null && i < missingAnchors.Count && missingAnchors[i] != null
                    ? missingAnchors[i]
                    : RoomSpecCatalog.GetAssignmentAnchor(targetIndex, Mathf.Max(1, mergedResults.Count));
                var item = BuildLocalFallbackMnemonicItem(missingWords[i], anchor, targetIndex);
                mergedResults[targetIndex] = item;
                localFallbackMnemonicCount++;
            }

            usedLocalFallbackForCurrentSession = localFallbackMnemonicCount > 0;
        }

        private MnemonicItemData BuildLocalFallbackMnemonicItem(WordEntry word, AnchorDefinition anchor, int itemIndex)
        {
            var safeWord = string.IsNullOrWhiteSpace(word?.word) ? "the Spanish word" : word.word.Trim();
            var meaning = string.IsNullOrWhiteSpace(word?.meaning) ? "the target meaning" : word.meaning.Trim();
            var anchorLabel = string.IsNullOrWhiteSpace(anchor?.label) ? "memory palace anchor" : anchor.label.Trim();
            var anchorType = PreGeneratedMnemonicCatalog.NormalizeAnchorType(anchor);
            var cueObject = BuildLocalFallbackCueObject(word, meaning);
            var relation = BuildLocalFallbackAnchorRelation(anchorLabel, anchorType, cueObject);
            var association = cueObject + " " + relation;
            var visualCue = $"At the {anchorLabel}, {association}.";
            var mnemonic = $"At the {anchorLabel}, {association}, making a clear {meaning} moment. That image gives {safeWord} a stable meaning.";

            var item = new MnemonicItemData
            {
                word = safeWord,
                meaning = meaning,
                anchorId = anchor?.id,
                anchorLabel = anchorLabel,
                anchorType = anchorType,
                mnemonicSource = "local_story_fallback",
                visualCue = visualCue,
                mainCueObject = cueObject,
                associationPrompt = association,
                mnemonic = mnemonic,
                mnemonicMode = "STORY_ONLY",
                hookAccepted = false,
                hookScore = 0,
                hookReason = "Local fallback uses a story-only meaning cue when the pre-generated catalog is missing and live LLM generation is unavailable or disabled.",
                mnemonicHook = string.Empty,
                storyCue = string.Empty,
                imagePrompt = association,
                imagePromptCandidates = BuildLocalFallbackImagePromptCandidates(cueObject, relation, anchorLabel),
                selectedImageCandidateIndex = -1,
                objectShape = PickShape(itemIndex),
                colorHex = PickColor(itemIndex),
                visualObjects = BuildLocalFallbackVisualObjects(cueObject, itemIndex),
                cueBlueprint = new CueBlueprintData
                {
                    targetMeaning = meaning,
                    visualSceneCore = association,
                    mainObject = cueObject,
                    anchorRelation = relation,
                    relativeSize = "large foreground cue",
                    mainActionOrState = relation,
                    visibleObjects = new List<string> { cueObject },
                    mnemonicHookNote = string.Empty,
                    mnemonicMode = "STORY_ONLY"
                }
            };

            ApplyAnchorConsistency(item);
            return item;
        }

        private static string BuildLocalFallbackCueObject(WordEntry word, string meaning)
        {
            var text = ((word?.word ?? string.Empty) + " " + (meaning ?? string.Empty)).ToLowerInvariant();
            if (ContainsAny(text, "curtain", "cortina"))
            {
                return "heavy curtain fabric";
            }

            if (ContainsAny(text, "bottle", "botella"))
            {
                return "clear bottle";
            }

            if (ContainsAny(text, "stairs", "stair", "escalera", "escaleras"))
            {
                return "small stair blocks";
            }

            if (ContainsAny(text, "hammer", "martillo"))
            {
                return "small hammer";
            }

            if (ContainsAny(text, "wardrobe", "closet", "armario"))
            {
                return "tiny wardrobe cabinet";
            }

            if (ContainsAny(text, "blanket", "manta"))
            {
                return "soft blanket";
            }

            if (ContainsAny(text, "wallet", "cartera"))
            {
                return "open wallet";
            }

            if (ContainsAny(text, "airport", "aeropuerto"))
            {
                return "boarding pass and tiny suitcase";
            }

            if (ContainsAny(text, "poster", "cartel"))
            {
                return "bright poster sheet";
            }

            if (ContainsAny(text, "mirror", "espejo"))
            {
                return "small mirror";
            }

            if (ContainsAny(text, "door", "puerta"))
            {
                return "small door model";
            }

            return string.IsNullOrWhiteSpace(meaning) ? "concrete meaning prop" : meaning.Trim() + " cue prop";
        }

        private static string BuildLocalFallbackAnchorRelation(string anchorLabel, string anchorType, string cueObject)
        {
            var anchorText = ((anchorLabel ?? string.Empty) + " " + (anchorType ?? string.Empty)).ToLowerInvariant();
            if (ContainsAny(anchorText, "air_conditioner", "air conditioner", "aircon", "ac unit", "a/c"))
            {
                return "touching the front vent flap of the wall-mounted air conditioner";
            }

            if (ContainsAny(anchorText, "chair"))
            {
                return "draped across the chair seat and backrest";
            }

            if (ContainsAny(anchorText, "bed"))
            {
                return "resting clearly on the bed pillow and blanket";
            }

            if (ContainsAny(anchorText, "desk"))
            {
                return "sitting on the desk surface beside the keyboard area";
            }

            if (ContainsAny(anchorText, "table"))
            {
                return "placed in the center of the table surface";
            }

            if (ContainsAny(anchorText, "sofa", "couch"))
            {
                return "leaning against the sofa cushion";
            }

            if (ContainsAny(anchorText, "door"))
            {
                return "hanging from the door handle";
            }

            if (ContainsAny(anchorText, "wardrobe", "closet"))
            {
                return "spilling from the open wardrobe shelf";
            }

            if (ContainsAny(anchorText, "bookshelf", "shelf"))
            {
                return "wedged visibly between the bookshelf shelves";
            }

            if (ContainsAny(anchorText, "television", "tv"))
            {
                return "touching the television screen frame without covering the whole screen";
            }

            return "touching the assigned anchor in the foreground";
        }

        private static List<string> BuildLocalFallbackImagePromptCandidates(string cueObject, string relation, string anchorLabel)
        {
            var basePrompt = cueObject + " " + relation;
            return new List<string>
            {
                basePrompt + ", clear two-subject close-up",
                cueObject + " physically contacting the " + anchorLabel,
                "recognizable " + anchorLabel + " with " + cueObject + " in the foreground",
                basePrompt + ", simple realistic composition"
            };
        }

        private static List<VisualObjectSpec> BuildLocalFallbackVisualObjects(string cueObject, int itemIndex)
        {
            return new List<VisualObjectSpec>
            {
                new()
                {
                    label = cueObject,
                    primitiveShape = PickShape(itemIndex),
                    colorHex = PickColor(itemIndex),
                    localPosition = new Vector3(0f, 0.26f, 0f),
                    scale = new Vector3(0.34f, 0.22f, 0.22f),
                    effect = "meaning cue"
                }
            };
        }

        private List<AnchorDefinition> BuildMnemonicAnchorAssignments(int itemCount)
        {
            return BuildMnemonicAnchorAssignments(itemCount, mnemonicAnchorRandom);
        }

        private List<AnchorDefinition> BuildMnemonicAnchorAssignments(int itemCount, System.Random rng)
        {
            var assignments = new List<AnchorDefinition>();
            if (itemCount <= 0)
            {
                return assignments;
            }

            var availableAnchors = BuildAvailableMnemonicAnchors();
            if (availableAnchors.Count == 0)
            {
                for (int i = 0; i < RoomSpecCatalog.AnchorCount; i++)
                {
                    availableAnchors.Add(RoomSpecCatalog.Anchors[i]);
                }
            }

            if (randomizeMnemonicAnchors && availableAnchors.Count > 0)
            {
                var anchors = new List<AnchorDefinition>(availableAnchors);
                ShuffleList(anchors, rng);
                for (int i = 0; i < itemCount; i++)
                {
                    assignments.Add(anchors[i % anchors.Count]);
                }

                return assignments;
            }

            for (int i = 0; i < itemCount; i++)
            {
                assignments.Add(availableAnchors.Count > 0
                    ? availableAnchors[i % availableAnchors.Count]
                    : RoomSpecCatalog.GetAssignmentAnchor(i, itemCount));
            }

            return assignments;
        }

        private List<AnchorDefinition> BuildAvailableMnemonicAnchors()
        {
            var anchors = new List<AnchorDefinition>();
            if (!usePreGeneratedImageCueCatalog)
            {
                for (int i = 0; i < RoomSpecCatalog.AnchorCount; i++)
                {
                    anchors.Add(RoomSpecCatalog.Anchors[i]);
                }

                return anchors;
            }

            for (int i = 0; i < RoomSpecCatalog.AnchorCount; i++)
            {
                var anchor = RoomSpecCatalog.Anchors[i];
                if (IsFormalImageCueAnchor(anchor))
                {
                    anchors.Add(anchor);
                }
            }

            return anchors;
        }

        private static bool IsFormalImageCueAnchor(AnchorDefinition anchor)
        {
            var anchorType = PreGeneratedMnemonicCatalog.NormalizeAnchorType(anchor);
            return IsFormalImageCueAnchorType(anchorType);
        }

        private static bool IsFormalImageCueAnchorType(string anchorType)
        {
            for (int i = 0; i < PreGeneratedImageCueCatalog.FormalAnchorTypes.Length; i++)
            {
                if (string.Equals(anchorType, PreGeneratedImageCueCatalog.FormalAnchorTypes[i], StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private string BuildMnemonicGenerationStatus()
        {
            if (currentItems == null || currentItems.Count == 0)
            {
                return "No mnemonic items were generated.";
            }

            if (preGeneratedMnemonicHitCount > 0 && liveGeneratedMnemonicCount > 0 && localFallbackMnemonicCount > 0)
            {
                return $"Loaded {preGeneratedMnemonicHitCount} pre-generated item(s), generated {liveGeneratedMnemonicCount} item(s) with {GetCurrentLiveMnemonicProviderLabel()}, and filled {localFallbackMnemonicCount} item(s) with local story-only fallback.";
            }

            if (preGeneratedMnemonicHitCount > 0 && liveGeneratedMnemonicCount > 0)
            {
                return $"Loaded {preGeneratedMnemonicHitCount} pre-generated item(s) and generated {liveGeneratedMnemonicCount} missing item(s) with {GetCurrentLiveMnemonicProviderLabel()}.";
            }

            if (preGeneratedMnemonicHitCount > 0 && localFallbackMnemonicCount > 0)
            {
                return $"Loaded {preGeneratedMnemonicHitCount} pre-generated item(s) and filled {localFallbackMnemonicCount} missing item(s) with local story-only fallback.";
            }

            if (preGeneratedMnemonicHitCount > 0)
            {
                return $"Loaded {preGeneratedMnemonicHitCount} pre-generated mnemonic item(s) successfully.";
            }

            if (liveGeneratedMnemonicCount > 0 && localFallbackMnemonicCount > 0)
            {
                return $"{GetCurrentLiveMnemonicProviderLabel()} generated {liveGeneratedMnemonicCount} item(s), and local story-only fallback filled {localFallbackMnemonicCount} item(s).";
            }

            if (liveGeneratedMnemonicCount > 0)
            {
                return $"{GetCurrentLiveMnemonicProviderLabel()} returned {liveGeneratedMnemonicCount} mnemonic item(s) successfully.";
            }

            if (localFallbackMnemonicCount > 0)
            {
                return $"Filled {localFallbackMnemonicCount} mnemonic item(s) with local story-only fallback.";
            }

            return $"{GetCurrentLiveMnemonicProviderLabel()} returned {currentItems.Count} mnemonic items successfully.";
        }

        private IEnumerator TestGeminiProviderRoutine()
        {
            if (isTestingGeminiProvider)
            {
                yield break;
            }

            var geminiKey = ResolveGeminiApiKey();
            if (string.IsNullOrWhiteSpace(geminiKey) || string.IsNullOrWhiteSpace(geminiModel))
            {
                geminiProviderStatus = "Gemini API key or model is empty.";
                yield break;
            }

            isTestingGeminiProvider = true;
            geminiProviderStatus = "Testing Gemini connection...";

            var service = new OllamaLlmService();
            string success = null;
            string error = null;
            yield return StartCoroutine(service.TestGeminiConnection(
                geminiKey,
                geminiModel,
                message => success = message,
                err => error = err));

            isTestingGeminiProvider = false;
            geminiProviderStatus = string.IsNullOrWhiteSpace(error)
                ? BuildGeminiConnectionSuccessStatus(success, service.GeminiModelsUsedSummary)
                : "Gemini connection test failed: " + error;
        }

        private static string BuildGeminiConnectionSuccessStatus(string success, string modelsUsed)
        {
            var message = string.IsNullOrWhiteSpace(success) ? "Gemini connection test succeeded." : success.Trim();
            return string.IsNullOrWhiteSpace(modelsUsed)
                ? message
                : message + " Used model(s): " + modelsUsed + ".";
        }

        private void RefreshPreGeneratedCatalogCoverage()
        {
            preGeneratedCatalogDetails.Clear();
            if (activeWordSet == null || activeWordSet.words == null || activeWordSet.words.Count == 0)
            {
                preGeneratedCatalogStatus = "No active word set is loaded.";
                return;
            }

            PreGeneratedMnemonicCatalog.Reload();
            var assignments = BuildMnemonicAnchorAssignments(activeWordSet.words.Count, new System.Random(20260617));
            var report = PreGeneratedMnemonicCatalog.BuildCoverageReport(activeWordSet.words, assignments);
            var randomNote = randomizeMnemonicAnchors ? " Fixed-seed random-anchor preview." : string.Empty;
            preGeneratedCatalogStatus = $"Pre-generated catalog coverage: {report.hits}/{report.total} ({report.ratio:P0}); missing {report.misses}.{randomNote}";

            var shown = 0;
            for (int i = 0; i < report.items.Count && shown < 8; i++)
            {
                var item = report.items[i];
                if (item == null || item.hit)
                {
                    continue;
                }

                preGeneratedCatalogDetails.Add($"Missing: {item.word} x {item.anchorType} ({item.anchorLabel})");
                shown++;
            }

            if (report.misses > shown)
            {
                preGeneratedCatalogDetails.Add($"...and {report.misses - shown} more missing combination(s).");
            }
        }

        private void ValidatePreGeneratedCatalog()
        {
            preGeneratedCatalogDetails.Clear();
            PreGeneratedMnemonicCatalog.Reload();
            var issues = PreGeneratedMnemonicCatalog.BuildDiagnostics();
            if (issues.Count == 0)
            {
                preGeneratedCatalogStatus = $"Pre-generated catalog diagnostics passed ({PreGeneratedMnemonicCatalog.EntryCount} entries).";
                return;
            }

            preGeneratedCatalogStatus = $"Pre-generated catalog diagnostics found {issues.Count} issue(s).";
            for (int i = 0; i < issues.Count && i < 10; i++)
            {
                preGeneratedCatalogDetails.Add(issues[i]);
            }

            if (issues.Count > preGeneratedCatalogDetails.Count)
            {
                preGeneratedCatalogDetails.Add($"...and {issues.Count - preGeneratedCatalogDetails.Count} more issue(s).");
            }
        }

        private void RefreshFullMnemonicMatrixCoverage()
        {
            preGeneratedCatalogDetails.Clear();
            var pool = GetFormalImageCatalogWordPool();
            if (pool == null || pool.words == null || pool.words.Count == 0)
            {
                preGeneratedCatalogStatus = "Formal 12-word pool is not available.";
                return;
            }

            PreGeneratedMnemonicCatalog.Reload();
            var report = PreGeneratedMnemonicCatalog.BuildMatrixCoverageReport(
                pool.words,
                PreGeneratedImageCueCatalog.FormalAnchorTypes);
            preGeneratedCatalogStatus = $"Full mnemonic matrix coverage: {report.hits}/{report.total} word-anchor pairs; missing {report.misses}.";

            var shown = 0;
            for (int i = 0; i < report.items.Count && shown < 16; i++)
            {
                var item = report.items[i];
                if (item == null || item.hit)
                {
                    continue;
                }

                preGeneratedCatalogDetails.Add($"Missing matrix pair: {item.word} x {item.anchorType}");
                shown++;
            }

            if (report.misses > shown)
            {
                preGeneratedCatalogDetails.Add($"...and {report.misses - shown} more missing matrix pair(s).");
            }
        }

        private void DrawMnemonicCatalogReviewBuilderPanel()
        {
            showMnemonicCatalogReviewBuilder = GUILayout.Toggle(showMnemonicCatalogReviewBuilder, "Show Mnemonic Catalog Review Builder");
            if (!showMnemonicCatalogReviewBuilder)
            {
                return;
            }

            GUILayout.BeginVertical(sectionStyle);
            GUILayout.Label("Mnemonic Catalog Review Builder", smallTitleStyle);
            var pool = GetFormalImageCatalogWordPool();
            if (pool == null || pool.words == null || pool.words.Count == 0)
            {
                GUILayout.Label("Formal 12-word pool is not available.", mutedStyle);
                GUILayout.EndVertical();
                return;
            }

            var report = PreGeneratedMnemonicCatalog.BuildMatrixCoverageReport(pool.words, PreGeneratedImageCueCatalog.FormalAnchorTypes);
            GUILayout.Label($"Matrix: {pool.words.Count} words x {PreGeneratedImageCueCatalog.FormalAnchorTypes.Length} anchors", labelStyle);
            GUILayout.Label($"Ready: {report.hits}/{report.total} reviewed mnemonic pairs; missing {report.misses}.", mutedStyle);
            GUILayout.Label("Export JSON, review it in GPT-5.5, paste the returned strict JSON here, then import it into PreGeneratedMnemonics.json.", mutedStyle);
            if (!string.IsNullOrWhiteSpace(mnemonicReviewBuilderStatus))
            {
                GUILayout.Label(mnemonicReviewBuilderStatus, mutedStyle);
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Export Missing Draft JSON", buttonStyle))
            {
                ExportMnemonicReviewJson(MnemonicReviewExportScope.MissingOnly);
            }

            if (GUILayout.Button("Export Review Needed JSON", buttonStyle))
            {
                ExportMnemonicReviewJson(MnemonicReviewExportScope.MissingOrUnreviewed);
            }

            if (GUILayout.Button("Export Full Review JSON", buttonStyle))
            {
                ExportMnemonicReviewJson(MnemonicReviewExportScope.Full);
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Copy Text Box", buttonStyle))
            {
                GUIUtility.systemCopyBuffer = mnemonicReviewJsonText ?? string.Empty;
                mnemonicReviewBuilderStatus = "Copied the mnemonic review JSON text box to the clipboard.";
            }

            if (GUILayout.Button("Paste Clipboard", buttonStyle))
            {
                mnemonicReviewJsonText = GUIUtility.systemCopyBuffer ?? string.Empty;
                mnemonicReviewBuilderStatus = "Pasted clipboard text into the mnemonic review text box.";
            }

            if (GUILayout.Button("Import Reviewed Mnemonics", buttonStyle))
            {
                ImportReviewedMnemonicJson();
            }
            GUILayout.EndHorizontal();

            mnemonicReviewJsonText = GUILayout.TextArea(mnemonicReviewJsonText, textAreaStyle, GUILayout.MinHeight(220f));
            GUILayout.EndVertical();
        }

        private void ExportMnemonicReviewJson(MnemonicReviewExportScope scope)
        {
            var batch = BuildMnemonicReviewBatch(scope);
            if (batch.items == null || batch.items.Count == 0)
            {
                mnemonicReviewBuilderStatus = scope switch
                {
                    MnemonicReviewExportScope.MissingOnly => "No missing mnemonic pairs to export.",
                    MnemonicReviewExportScope.MissingOrUnreviewed => "No missing or non-GPT-reviewed mnemonic pairs to export.",
                    _ => "No mnemonic pairs were available to export."
                };
                return;
            }

            var json = JsonUtility.ToJson(batch, true);
            mnemonicReviewJsonText = json;
            GUIUtility.systemCopyBuffer = json;

            try
            {
                var exportDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "ExperimentExports", "MnemonicCatalogReview"));
                Directory.CreateDirectory(exportDir);
                var mode = scope switch
                {
                    MnemonicReviewExportScope.MissingOnly => "missing",
                    MnemonicReviewExportScope.MissingOrUnreviewed => "review_needed",
                    _ => "full"
                };
                var exportPath = Path.Combine(exportDir, $"mnemonic_review_{mode}_{DateTime.Now:yyyyMMdd_HHmmss}.json");
                File.WriteAllText(exportPath, json, Encoding.UTF8);
                mnemonicReviewBuilderStatus = $"Exported {batch.items.Count} mnemonic pair(s), copied JSON to clipboard, and saved {exportPath}.";
            }
            catch (Exception ex)
            {
                mnemonicReviewBuilderStatus = $"Exported {batch.items.Count} mnemonic pair(s) to the text box, but failed to save file: {ex.Message}";
            }
        }

        private PreGeneratedMnemonicCatalog.ReviewedMnemonicBatch BuildMnemonicReviewBatch(MnemonicReviewExportScope scope)
        {
            var batch = new PreGeneratedMnemonicCatalog.ReviewedMnemonicBatch
            {
                instructions = BuildMnemonicReviewInstructions(scope),
                items = new List<PreGeneratedMnemonicCatalog.ReviewedMnemonicItem>()
            };

            var pool = GetFormalImageCatalogWordPool();
            if (pool == null || pool.words == null || pool.words.Count == 0)
            {
                return batch;
            }

            PreGeneratedMnemonicCatalog.Reload();
            var report = PreGeneratedMnemonicCatalog.BuildMatrixCoverageReport(pool.words, PreGeneratedImageCueCatalog.FormalAnchorTypes);
            var exportedIndex = 0;
            for (int i = 0; i < report.items.Count; i++)
            {
                var coverage = report.items[i];
                if (coverage == null)
                {
                    continue;
                }

                var word = FindWordEntry(pool.words, coverage.word);
                if (word == null)
                {
                    continue;
                }

                var anchor = BuildCatalogAnchor(coverage.anchorType);
                var hasCatalogItem = PreGeneratedMnemonicCatalog.TryCreateItem(word, anchor, exportedIndex, out var item);
                var needsReview = !hasCatalogItem || !IsGptReviewedMnemonic(item);
                if (scope == MnemonicReviewExportScope.MissingOnly && hasCatalogItem)
                {
                    continue;
                }

                if (scope == MnemonicReviewExportScope.MissingOrUnreviewed && !needsReview)
                {
                    continue;
                }

                item ??= BuildLocalFallbackMnemonicItem(word, anchor, exportedIndex);
                item.anchorType = coverage.anchorType;
                ApplyAnchorConsistency(item);
                batch.items.Add(BuildReviewedMnemonicItem(item, anchor, hasCatalogItem));
                exportedIndex++;
            }

            return batch;
        }

        private static string BuildMnemonicReviewInstructions(MnemonicReviewExportScope scope)
        {
            var scopeText = scope switch
            {
                MnemonicReviewExportScope.MissingOnly => "missing 12x10 mnemonic pairs",
                MnemonicReviewExportScope.MissingOrUnreviewed => "missing or non-GPT-reviewed 12x10 mnemonic pairs",
                _ => "all 12x10 mnemonic pairs"
            };
            return "Review and rewrite the " + scopeText + ". Return strict JSON with the same items array. " +
                   "Keep word, meaning, anchorType, and id. Set mnemonicSource to gpt55_reviewed. " +
                   "Use STORY_ONLY unless a natural high-quality phonetic/semantic hook exists; use HOOK_PLUS_STORY only when hookScore is at least 7. " +
                   "Each item must clearly bind the target meaning object/action with the assigned anchor object. " +
                   "Keep associationPrompt and imagePromptCandidates drawable as one simple realistic image with no text, logos, abstract icons, or translation explanations. " +
                   "Do not return Markdown fences.";
        }

        private static bool IsGptReviewedMnemonic(MnemonicItemData item)
        {
            var source = item?.mnemonicSource;
            return !string.IsNullOrWhiteSpace(source)
                   && source.IndexOf("gpt", StringComparison.OrdinalIgnoreCase) >= 0
                   && source.IndexOf("review", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private PreGeneratedMnemonicCatalog.ReviewedMnemonicItem BuildReviewedMnemonicItem(
            MnemonicItemData item,
            AnchorDefinition anchor,
            bool hasCatalogItem)
        {
            var anchorType = PreGeneratedMnemonicCatalog.NormalizeAnchorType(anchor);
            if (!string.IsNullOrWhiteSpace(item.anchorType))
            {
                anchorType = item.anchorType;
            }

            return new PreGeneratedMnemonicCatalog.ReviewedMnemonicItem
            {
                id = BuildMnemonicReviewItemId(item.word, anchorType),
                word = item.word,
                meaning = item.meaning,
                anchorType = anchorType,
                anchorLabel = string.IsNullOrWhiteSpace(anchor?.label) ? HumanizeAnchorType(anchorType) : anchor.label,
                mnemonicSource = hasCatalogItem ? "existing_catalog_needs_review" : "draft_needs_gpt55_review",
                visualCue = item.visualCue,
                mainCueObject = item.mainCueObject,
                associationPrompt = item.associationPrompt,
                mnemonic = item.mnemonic,
                mnemonicMode = string.IsNullOrWhiteSpace(item.mnemonicMode) ? "STORY_ONLY" : item.mnemonicMode,
                hookAccepted = item.hookAccepted,
                hookScore = item.hookScore,
                hookReason = item.hookReason,
                mnemonicHook = item.mnemonicHook,
                imagePrompt = item.imagePrompt,
                imagePromptCandidates = item.imagePromptCandidates == null
                    ? new List<string>()
                    : new List<string>(item.imagePromptCandidates),
                cueBlueprint = item.cueBlueprint,
                objectShape = item.objectShape,
                colorHex = item.colorHex,
                visualObjects = item.visualObjects == null
                    ? new List<VisualObjectSpec>()
                    : new List<VisualObjectSpec>(item.visualObjects)
            };
        }

        private static string BuildMnemonicReviewItemId(string word, string anchorType)
        {
            return SanitizeCatalogIdPart(word) + "_" + SanitizeCatalogIdPart(anchorType) + "_reviewed_v1";
        }

        private static string SanitizeCatalogIdPart(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "item";
            }

            var builder = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                var c = char.ToLowerInvariant(value[i]);
                builder.Append(char.IsLetterOrDigit(c) ? c : '_');
            }

            var result = builder.ToString().Trim('_');
            while (result.Contains("__"))
            {
                result = result.Replace("__", "_");
            }

            return string.IsNullOrWhiteSpace(result) ? "item" : result;
        }

        private void ImportReviewedMnemonicJson()
        {
            preGeneratedCatalogDetails.Clear();
            if (!PreGeneratedMnemonicCatalog.TryParseReviewedBatch(mnemonicReviewJsonText, out var reviewedItems, out var parseError))
            {
                mnemonicReviewBuilderStatus = "Import failed: " + parseError;
                return;
            }

            if (!PreGeneratedMnemonicCatalog.UpsertReviewedItems(
                    reviewedItems,
                    out var upsertedCount,
                    out var catalogPath,
                    out var issues))
            {
                mnemonicReviewBuilderStatus = "Import failed: " + string.Join(" ", issues);
                for (int i = 0; i < issues.Count && i < 12; i++)
                {
                    preGeneratedCatalogDetails.Add(issues[i]);
                }

                return;
            }

            PreGeneratedMnemonicCatalog.Reload();
            var pool = GetFormalImageCatalogWordPool();
            var matrixSummary = string.Empty;
            if (pool?.words != null && pool.words.Count > 0)
            {
                var report = PreGeneratedMnemonicCatalog.BuildMatrixCoverageReport(pool.words, PreGeneratedImageCueCatalog.FormalAnchorTypes);
                matrixSummary = $" Matrix now {report.hits}/{report.total}; missing {report.misses}.";
            }

            mnemonicReviewBuilderStatus = $"Imported {upsertedCount} reviewed mnemonic pair(s) into {catalogPath}.{matrixSummary}";
            preGeneratedCatalogStatus = mnemonicReviewBuilderStatus;
            for (int i = 0; i < issues.Count && i < 12; i++)
            {
                preGeneratedCatalogDetails.Add(issues[i]);
            }

            if (issues.Count > preGeneratedCatalogDetails.Count)
            {
                preGeneratedCatalogDetails.Add($"...and {issues.Count - preGeneratedCatalogDetails.Count} more skipped item issue(s).");
            }
        }

        private void RefreshPreGeneratedImageCueCatalogCoverage()
        {
            preGeneratedImageCueCatalogDetails.Clear();
            if (activeWordSet == null || activeWordSet.words == null || activeWordSet.words.Count == 0)
            {
                preGeneratedImageCueCatalogStatus = "No active word set is loaded.";
                return;
            }

            PreGeneratedImageCueCatalog.Reload();
            var assignments = BuildMnemonicAnchorAssignments(activeWordSet.words.Count, new System.Random(20260618));
            var report = PreGeneratedImageCueCatalog.BuildCoverageReport(activeWordSet.words, assignments);
            var randomNote = randomizeMnemonicAnchors ? " Fixed-seed random-anchor preview." : string.Empty;
            preGeneratedImageCueCatalogStatus = $"Pre-generated image catalog coverage: {report.hits}/{report.total} ({report.ratio:P0}); missing {report.misses}.{randomNote}";

            var shown = 0;
            for (int i = 0; i < report.items.Count && shown < 10; i++)
            {
                var item = report.items[i];
                if (item == null || item.hit)
                {
                    continue;
                }

                preGeneratedImageCueCatalogDetails.Add($"Missing images: {item.word} x {item.anchorType} ({item.imageCount}/{PreGeneratedImageCueCatalog.RequiredImagesPerPair})");
                shown++;
            }

            if (report.misses > shown)
            {
                preGeneratedImageCueCatalogDetails.Add($"...and {report.misses - shown} more missing image combination(s).");
            }
        }

        private void RefreshFullImageCueMatrixCoverage()
        {
            preGeneratedImageCueCatalogDetails.Clear();
            if (activeWordSet == null || activeWordSet.words == null || activeWordSet.words.Count == 0)
            {
                preGeneratedImageCueCatalogStatus = "No active word set is loaded.";
                return;
            }

            PreGeneratedImageCueCatalog.Reload();
            var report = PreGeneratedImageCueCatalog.BuildMatrixCoverageReport(
                activeWordSet.words,
                PreGeneratedImageCueCatalog.FormalAnchorTypes);
            var expectedImages = report.total * PreGeneratedImageCueCatalog.RequiredImagesPerPair;
            var readyImages = report.hits * PreGeneratedImageCueCatalog.RequiredImagesPerPair;
            preGeneratedImageCueCatalogStatus = $"Full image matrix coverage: {report.hits}/{report.total} word-anchor pairs ({readyImages}/{expectedImages} images); missing {report.misses}.";

            var shown = 0;
            for (int i = 0; i < report.items.Count && shown < 16; i++)
            {
                var item = report.items[i];
                if (item == null || item.hit)
                {
                    continue;
                }

                preGeneratedImageCueCatalogDetails.Add($"Missing matrix pair: {item.word} x {item.anchorType} ({item.imageCount}/{PreGeneratedImageCueCatalog.RequiredImagesPerPair})");
                shown++;
            }

            if (report.misses > shown)
            {
                preGeneratedImageCueCatalogDetails.Add($"...and {report.misses - shown} more missing matrix pair(s).");
            }
        }

        private void ValidatePreGeneratedImageCueCatalog()
        {
            preGeneratedImageCueCatalogDetails.Clear();
            PreGeneratedImageCueCatalog.Reload();
            var issues = PreGeneratedImageCueCatalog.BuildDiagnostics();
            if (issues.Count == 0)
            {
                preGeneratedImageCueCatalogStatus = $"Pre-generated image catalog diagnostics passed ({PreGeneratedImageCueCatalog.EntryCount} entries).";
                return;
            }

            preGeneratedImageCueCatalogStatus = $"Pre-generated image catalog diagnostics found {issues.Count} issue(s).";
            for (int i = 0; i < issues.Count && i < 12; i++)
            {
                preGeneratedImageCueCatalogDetails.Add(issues[i]);
            }

            if (issues.Count > preGeneratedImageCueCatalogDetails.Count)
            {
                preGeneratedImageCueCatalogDetails.Add($"...and {issues.Count - preGeneratedImageCueCatalogDetails.Count} more issue(s).");
            }
        }

        private void DrawImageCueCatalogBuilderPanel()
        {
            showImageCueCatalogBuilder = GUILayout.Toggle(showImageCueCatalogBuilder, "Show Image Catalog Builder");
            if (!showImageCueCatalogBuilder)
            {
                return;
            }

            GUILayout.BeginVertical(sectionStyle);
            GUILayout.Label("Image Catalog Builder", smallTitleStyle);
            var pool = GetFormalImageCatalogWordPool();
            if (pool == null || pool.words == null || pool.words.Count == 0)
            {
                GUILayout.Label("Formal 12-word pool is not available.", mutedStyle);
                GUILayout.EndVertical();
                return;
            }

            var report = PreGeneratedImageCueCatalog.BuildMatrixCoverageReport(pool.words, PreGeneratedImageCueCatalog.FormalAnchorTypes);
            var totalImages = report.total * PreGeneratedImageCueCatalog.RequiredImagesPerPair;
            var readyImages = report.hits * PreGeneratedImageCueCatalog.RequiredImagesPerPair;
            GUILayout.Label($"Matrix: {pool.words.Count} words x {PreGeneratedImageCueCatalog.FormalAnchorTypes.Length} anchors x {PreGeneratedImageCueCatalog.RequiredImagesPerPair} images", labelStyle);
            GUILayout.Label($"Ready: {report.hits}/{report.total} pairs ({readyImages}/{totalImages} images); missing {report.misses} pairs.", mutedStyle);
            imageCueCatalogSkipVisionScoring = GUILayout.Toggle(imageCueCatalogSkipVisionScoring, "Fast bulk build: skip Ollama Vision scoring");
            GUILayout.Label("Fast mode saves A-D images directly. Use human review and Regenerate Specific Pair for bad image sets.", mutedStyle);
            if (!string.IsNullOrWhiteSpace(imageCueCatalogBuilderStatus))
            {
                GUILayout.Label(imageCueCatalogBuilderStatus, mutedStyle);
            }

            GUILayout.BeginHorizontal();
            GUI.enabled = !isBuildingImageCueCatalog;
            if (GUILayout.Button("Generate Next Missing Pair", buttonStyle))
            {
                BeginImageCueCatalogBuild(1);
            }

            if (GUILayout.Button("Generate Next 8 Missing Pairs", buttonStyle))
            {
                BeginImageCueCatalogBuild(8);
            }

            var allRemainingLabel = report.misses > 0
                ? $"Generate All Remaining Pairs ({report.misses})"
                : "All Image Pairs Ready";
            GUI.enabled = !isBuildingImageCueCatalog && report.misses > 0;
            if (GUILayout.Button(allRemainingLabel, buttonStyle))
            {
                BeginImageCueCatalogBuild(0);
            }

            GUI.enabled = isBuildingImageCueCatalog;
            if (GUILayout.Button("Cancel Builder", buttonStyle))
            {
                CancelImageCueCatalogBuild();
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Regenerate Pair", mutedStyle, GUILayout.Width(120f));
            imageCueCatalogTargetWord = GUILayout.TextField(imageCueCatalogTargetWord ?? string.Empty, GUILayout.Width(160f));
            imageCueCatalogTargetAnchorType = GUILayout.TextField(imageCueCatalogTargetAnchorType ?? string.Empty, GUILayout.Width(180f));
            GUI.enabled = !isBuildingImageCueCatalog;
            if (GUILayout.Button("Regenerate Specific Pair", buttonStyle))
            {
                BeginSpecificImageCueCatalogRegeneration();
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            if (report.misses > 0)
            {
                var shown = 0;
                for (int i = 0; i < report.items.Count && shown < 8; i++)
                {
                    var item = report.items[i];
                    if (item == null || item.hit)
                    {
                        continue;
                    }

                    GUILayout.Label($"Next missing: {item.word} x {item.anchorType} ({item.imageCount}/{PreGeneratedImageCueCatalog.RequiredImagesPerPair})", mutedStyle);
                    shown++;
                }

                if (report.misses > shown)
                {
                    GUILayout.Label($"...and {report.misses - shown} more missing pair(s).", mutedStyle);
                }
            }

            GUILayout.Label("Generated PNGs are saved under Assets/Resources/PreGeneratedImageCues and written into PreGeneratedImageCueCatalog.json.", mutedStyle);
            GUILayout.EndVertical();
        }

        private void BeginImageCueCatalogBuild(int maxPairs)
        {
            if (isBuildingImageCueCatalog)
            {
                return;
            }

            var missingPairs = BuildMissingImageCueCatalogPairs();
            if (missingPairs.Count == 0)
            {
                imageCueCatalogBuilderStatus = "Image catalog matrix is already complete.";
                return;
            }

            imageCueCatalogBuilderCoroutine = StartCoroutine(BuildImageCueCatalogRoutine(missingPairs, maxPairs));
        }

        private void BeginSpecificImageCueCatalogRegeneration()
        {
            if (isBuildingImageCueCatalog)
            {
                return;
            }

            var pool = GetFormalImageCatalogWordPool();
            if (pool == null || pool.words == null || pool.words.Count == 0)
            {
                imageCueCatalogBuilderStatus = "Formal 12-word pool is not available.";
                return;
            }

            var word = FindWordEntry(pool.words, imageCueCatalogTargetWord);
            if (word == null)
            {
                imageCueCatalogBuilderStatus = $"Cannot regenerate: word '{imageCueCatalogTargetWord}' is not in the formal 12-word pool.";
                return;
            }

            var anchorType = PreGeneratedMnemonicCatalog.NormalizeAnchorType(string.Empty, imageCueCatalogTargetAnchorType);
            if (!IsFormalImageCueAnchorType(anchorType))
            {
                imageCueCatalogBuilderStatus = $"Cannot regenerate: anchorType '{imageCueCatalogTargetAnchorType}' is not one of the formal anchors.";
                return;
            }

            imageCueCatalogTargetWord = word.word;
            imageCueCatalogTargetAnchorType = anchorType;
            var pairs = new List<ImageCueCatalogBuildPair>
            {
                new()
                {
                    word = word,
                    anchorType = anchorType
                }
            };
            imageCueCatalogBuilderCoroutine = StartCoroutine(BuildImageCueCatalogRoutine(
                pairs,
                1,
                $"Regenerated image catalog pair: {word.word} x {anchorType}."));
        }

        private void CancelImageCueCatalogBuild()
        {
            if (imageCueCatalogBuilderCoroutine != null)
            {
                StopCoroutine(imageCueCatalogBuilderCoroutine);
                imageCueCatalogBuilderCoroutine = null;
            }

            isBuildingImageCueCatalog = false;
            generatingImageCueWords.Clear();
            imageCueCatalogBuilderStatus = "Image catalog builder cancelled. Completed entries were kept.";
        }

        private IEnumerator BuildImageCueCatalogRoutine(
            List<ImageCueCatalogBuildPair> missingPairs,
            int maxPairs,
            string successMessage = null)
        {
            isBuildingImageCueCatalog = true;
            imageCueCatalogBuilderCompletedCount = 0;
            imageCueCatalogBuilderTargetCount = maxPairs <= 0 ? missingPairs.Count : maxPairs;
            var targetCount = Mathf.Min(imageCueCatalogBuilderTargetCount, missingPairs.Count);
            for (int i = 0; i < targetCount; i++)
            {
                var pair = missingPairs[i];
                if (pair == null || pair.word == null)
                {
                    continue;
                }

                imageCueCatalogBuilderStatus = $"Building catalog pair {i + 1}/{targetCount}: {pair.word.word} x {pair.anchorType}.";
                string error = null;
                yield return BuildSingleImageCueCatalogPairRoutine(pair, i, err => error = err);
                if (!string.IsNullOrWhiteSpace(error))
                {
                    imageCueCatalogBuilderStatus = $"Builder stopped at {pair.word.word} x {pair.anchorType}: {error}";
                    break;
                }

                imageCueCatalogBuilderCompletedCount++;
                imageCueCatalogBuilderStatus = $"Saved catalog pair {imageCueCatalogBuilderCompletedCount}/{targetCount}: {pair.word.word} x {pair.anchorType}.";
            }

            PreGeneratedImageCueCatalog.Reload();
            RefreshFullImageCueMatrixCoverage();
            var autoLoadedCount = 0;
            var autoLoadFailedCount = 0;
            var autoLoadError = string.Empty;
            if (usePreGeneratedImageCueCatalog && currentItems != null && currentItems.Count > 0)
            {
                TryLoadCurrentImageCuesFromCatalog(false, out autoLoadedCount, out autoLoadFailedCount, out autoLoadError);
            }

            isBuildingImageCueCatalog = false;
            imageCueCatalogBuilderCoroutine = null;
            if (imageCueCatalogBuilderCompletedCount >= targetCount)
            {
                var autoLoadSuffix = autoLoadedCount > 0
                    ? $" Loaded {autoLoadedCount} current-session image cue set(s)."
                    : string.Empty;
                if (autoLoadFailedCount > 0)
                {
                    autoLoadSuffix += $" Current-session auto-load still has {autoLoadFailedCount} missing/failed item(s).";
                }

                imageCueCatalogBuilderStatus = !string.IsNullOrWhiteSpace(successMessage)
                    ? successMessage + autoLoadSuffix
                    : targetCount == 1
                        ? "Generated the next missing image catalog pair." + autoLoadSuffix
                        : $"Image catalog builder finished {imageCueCatalogBuilderCompletedCount} pair(s)." + autoLoadSuffix;
            }
        }

        private IEnumerator BuildSingleImageCueCatalogPairRoutine(ImageCueCatalogBuildPair pair, int itemIndex, Action<string> onError)
        {
            var anchor = BuildCatalogAnchor(pair.anchorType);
            var item = PreGeneratedMnemonicCatalog.TryCreateItem(pair.word, anchor, itemIndex, out var preGenerated)
                ? preGenerated
                : BuildLocalFallbackMnemonicItem(pair.word, anchor, itemIndex);
            item.anchorType = pair.anchorType;
            ApplyAnchorConsistency(item);
            ClearGeneratedImageCueForWord(item.word);

            var results = new List<ImageCueCandidateResult>();
            var crossVariantGuidance = string.Empty;
            for (int variantIndex = 0; variantIndex < PreGeneratedImageCueCatalog.RequiredImagesPerPair; variantIndex++)
            {
                ImageCueCandidateResult bestResult = null;
                string fatalError = null;
                yield return GenerateBestImageCueVariantRoutine(
                    item,
                    variantIndex,
                    PreGeneratedImageCueCatalog.RequiredImagesPerPair,
                    crossVariantGuidance,
                    imageCueCatalogSkipVisionScoring,
                    result => bestResult = result,
                    error => fatalError = error);

                if (!string.IsNullOrWhiteSpace(fatalError))
                {
                    DestroyImageCueCandidateResults(results);
                    onError?.Invoke(fatalError);
                    yield break;
                }

                if (bestResult == null || bestResult.texture == null)
                {
                    DestroyImageCueCandidateResults(results);
                    onError?.Invoke("Image generation returned no usable result.");
                    yield break;
                }

                results.Add(bestResult);
                crossVariantGuidance = imageCueCatalogSkipVisionScoring
                    ? string.Empty
                    : BuildImageCueEvolutionGuidance(bestResult);
            }

            var variants = new List<PreGeneratedImageCueCatalog.GeneratedImageCueVariant>();
            for (int i = 0; i < results.Count; i++)
            {
                variants.Add(new PreGeneratedImageCueCatalog.GeneratedImageCueVariant
                {
                    label = BuildImageCueResultLabel(i),
                    rawPrompt = results[i].rawPrompt,
                    fullPrompt = results[i].fullPrompt,
                    texture = results[i].texture,
                    score = results[i].score,
                    pass = results[i].pass,
                    reason = results[i].reason
                });
            }

            if (!PreGeneratedImageCueCatalog.UpsertGeneratedImageCueSet(
                    item.word,
                    item.meaning,
                    item.anchorType,
                    variants,
                    0,
                    out var catalogPath,
                    out var saveError))
            {
                DestroyImageCueCandidateResults(results);
                onError?.Invoke(saveError);
                yield break;
            }

            LogInteraction("build_image_cue_catalog_pair", item.word, item.anchorId, $"Saved {item.word} x {item.anchorType} to {catalogPath}.");
            DestroyImageCueCandidateResults(results);
            onError?.Invoke(null);
        }

        private List<ImageCueCatalogBuildPair> BuildMissingImageCueCatalogPairs()
        {
            var pairs = new List<ImageCueCatalogBuildPair>();
            var pool = GetFormalImageCatalogWordPool();
            if (pool == null || pool.words == null || pool.words.Count == 0)
            {
                return pairs;
            }

            PreGeneratedImageCueCatalog.Reload();
            var report = PreGeneratedImageCueCatalog.BuildMatrixCoverageReport(pool.words, PreGeneratedImageCueCatalog.FormalAnchorTypes);
            for (int i = 0; i < report.items.Count; i++)
            {
                var item = report.items[i];
                if (item == null || item.hit)
                {
                    continue;
                }

                var word = FindWordEntry(pool.words, item.word);
                if (word == null)
                {
                    continue;
                }

                pairs.Add(new ImageCueCatalogBuildPair
                {
                    word = word,
                    anchorType = item.anchorType
                });
            }

            return pairs;
        }

        private WordSetDefinition GetFormalImageCatalogWordPool()
        {
            if (library?.wordSets != null)
            {
                for (int i = 0; i < library.wordSets.Count; i++)
                {
                    if (string.Equals(library.wordSets[i].setId, FormalWordPoolSetId, StringComparison.OrdinalIgnoreCase))
                    {
                        return library.wordSets[i];
                    }
                }
            }

            return activeWordSet;
        }

        private static WordEntry FindWordEntry(List<WordEntry> words, string word)
        {
            if (words == null || string.IsNullOrWhiteSpace(word))
            {
                return null;
            }

            for (int i = 0; i < words.Count; i++)
            {
                if (string.Equals(words[i]?.word, word, StringComparison.OrdinalIgnoreCase))
                {
                    return words[i];
                }
            }

            return null;
        }

        private AnchorDefinition BuildCatalogAnchor(string anchorType)
        {
            for (int i = 0; i < RoomSpecCatalog.AnchorCount; i++)
            {
                var anchor = RoomSpecCatalog.Anchors[i];
                if (string.Equals(PreGeneratedMnemonicCatalog.NormalizeAnchorType(anchor), anchorType, StringComparison.OrdinalIgnoreCase))
                {
                    return anchor;
                }
            }

            return new AnchorDefinition
            {
                id = anchorType,
                label = HumanizeAnchorType(anchorType),
                primitiveShape = "Cube",
                colorHex = "#8B7A65",
                position = Vector3.zero,
                scale = Vector3.one,
                rotationEuler = Vector3.zero,
                mnemonicOffset = new Vector3(0f, 0.6f, 0f),
                labelHeight = 1.0f
            };
        }

        private static string HumanizeAnchorType(string anchorType)
        {
            if (string.IsNullOrWhiteSpace(anchorType))
            {
                return "Anchor";
            }

            return anchorType switch
            {
                "air_conditioner" => "Air Conditioner",
                "bookshelf" => "Bookshelf",
                "television" => "Television",
                _ => char.ToUpperInvariant(anchorType[0]) + anchorType[1..].Replace('_', ' ')
            };
        }

        private void DestroyImageCueCandidateResults(List<ImageCueCandidateResult> results)
        {
            if (results == null)
            {
                return;
            }

            for (int i = 0; i < results.Count; i++)
            {
                if (results[i]?.texture != null)
                {
                    Destroy(results[i].texture);
                }
            }
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
            if (!AreAllCurrentImageCuesReady())
            {
                if (usePreGeneratedImageCueCatalog)
                {
                    TryLoadCurrentImageCuesFromCatalog(false, out _, out _, out _);
                }
            }

            if (!AreAllCurrentImageCuesReady())
            {
                statusMessage = string.IsNullOrWhiteSpace(preStudyImageCueStatus)
                    ? "Generate all image cues before entering the study room."
                    : preStudyImageCueStatus;
                preStudyImageCueStatus = statusMessage;
                return;
            }

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

        private void DrawRegenerateMnemonicButton(MnemonicItemData item)
        {
            if (item == null || condition != ExperimentCondition.LlmGenerated)
            {
                return;
            }

            var isRegenerating = regeneratingMnemonicWords.Contains(item.word);
            var isGeneratingCue = generatingImageCueWords.Contains(item.word);
            var previousEnabled = GUI.enabled;
            GUI.enabled = previousEnabled && !isGenerating && !isRegenerating && !isGeneratingCue;

            if (GUILayout.Button(isRegenerating ? "Regenerating Mnemonic Scene..." : "Reject Cue Scene / Regenerate Mnemonic", buttonStyle))
            {
                StartCoroutine(RegenerateMnemonicItemRoutine(item, GetMnemonicRegenerationReason(item)));
            }

            GUI.enabled = previousEnabled;

            if (imageCueValidationFailures.TryGetValue(item.word, out var failure)
                && !string.IsNullOrWhiteSpace(failure))
            {
                GUILayout.Label("Last image validation: " + failure, mutedStyle);
            }
        }

        private void DrawImageCuePanel(MnemonicItemData item)
        {
            if (item == null)
            {
                return;
            }

            GUILayout.Label("Generated Image Cue", smallTitleStyle);
            var isGeneratingCue = generatingImageCueWords.Contains(item.word);
            if (mnemonicImageCues.TryGetValue(item.word, out var texture) && texture != null)
            {
                GUILayout.Box(texture, GUILayout.Width(220f), GUILayout.Height(220f));
                GUILayout.Label("This image will be used for the image-choice tests when you capture this memory.", mutedStyle);
                var candidateCount = imageCueCandidateResults.TryGetValue(item.word, out var candidateResults) && candidateResults != null
                    ? candidateResults.Count
                    : 0;
                var displayedCandidateIndex = GetDisplayedImageCueCandidateIndex(item.word);
                if (candidateCount > 0)
                {
                    var displayNumber = Mathf.Clamp(displayedCandidateIndex + 1, 1, candidateCount);
                    GUILayout.Label(
                        isGeneratingCue && candidateCount < BufferedImageCueResultCount
                            ? $"Reviewer image choice {displayNumber}/{candidateCount} shown; preparing more scored sets in background..."
                            : $"Reviewer image choice {displayNumber}/{candidateCount}",
                        mutedStyle);

                    GUILayout.BeginHorizontal();
                    GUI.enabled = candidateCount > 1;
                    if (GUILayout.Button("<", buttonStyle, GUILayout.Width(54f)))
                    {
                        TryCycleDisplayedImageCueCandidate(item, -1);
                    }

                    if (GUILayout.Button(">", buttonStyle, GUILayout.Width(54f)))
                    {
                        TryCycleDisplayedImageCueCandidate(item, 1);
                    }
                    GUI.enabled = true;
                    GUILayout.EndHorizontal();
                }

                if (item.selectedImageCandidateIndex > 0 && !string.IsNullOrWhiteSpace(item.imageSelectionReason))
                {
                    GUILayout.Label(item.imageSelectionReason, mutedStyle);
                }

                GUI.enabled = !isGeneratingCue;
                var regenerateLabel = isGeneratingCue
                    ? "Preparing More Best Images..."
                    : "Regenerate Image Cue Set";
                if (GUILayout.Button(regenerateLabel, buttonStyle))
                {
                    if (isGeneratingCue)
                    {
                        imageGenerationStatus = $"The current image cue set for {item.word} is still generating in the background.";
                    }
                    else
                    {
                        imageGenerationStatus = $"Starting a new four-result image cue set for {item.word}...";
                        StartCoroutine(GenerateMnemonicImageCueRoutine(item));
                    }
                }
                GUI.enabled = true;
                if (!string.IsNullOrWhiteSpace(imageGenerationStatus))
                {
                    GUILayout.Label(imageGenerationStatus, mutedStyle);
                }
                return;
            }

            GUI.enabled = !isGeneratingCue;
            if (GUILayout.Button(isGeneratingCue ? "Generating Image Cue..." : "Generate Image Cue (Local SD)", buttonStyle))
            {
                imageGenerationStatus = $"Starting image cue generation for {item.word}...";
                StartCoroutine(GenerateMnemonicImageCueRoutine(item));
            }
            GUI.enabled = true;

            if (!string.IsNullOrWhiteSpace(imageGenerationStatus))
            {
                GUILayout.Label(imageGenerationStatus, mutedStyle);
            }
        }

        private IEnumerator RegenerateMnemonicItemRoutine(MnemonicItemData item, string rejectionReason)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.word))
            {
                yield break;
            }

            if (regeneratingMnemonicWords.Contains(item.word))
            {
                yield break;
            }

            var itemIndex = GetCurrentItemIndex(item);
            if (itemIndex < 0)
            {
                statusMessage = "Cannot regenerate mnemonic: selected item was not found in the current list.";
                yield break;
            }

            regeneratingMnemonicWords.Add(item.word);
            var liveProviderLabel = GetSelectedLiveMnemonicProviderLabel();
            var liveSourceTag = GetSelectedLiveMnemonicSourceTag();
            var liveModelLabel = GetSelectedLiveMnemonicModelLabel();
            statusMessage = $"Regenerating cue package for {item.word} with {liveProviderLabel}...";
            generationError = string.Empty;

            var service = new OllamaLlmService();
            MnemonicItemData replacement = null;
            string error = null;
            var word = new WordEntry
            {
                word = item.word,
                meaning = item.meaning
            };

            if (providerMode == LlmProviderMode.GeminiOnline)
            {
                var geminiKey = ResolveGeminiApiKey();
                if (string.IsNullOrWhiteSpace(geminiKey) || string.IsNullOrWhiteSpace(geminiModel))
                {
                    regeneratingMnemonicWords.Remove(item.word);
                    statusMessage = "Cannot regenerate mnemonic: Gemini API key or model is empty.";
                    yield break;
                }

                yield return StartCoroutine(service.RegenerateGeminiMnemonicItem(
                    geminiKey,
                    geminiModel,
                    word,
                    itemIndex,
                    currentItems.Count,
                    item.anchorId,
                    item.anchorLabel,
                    item.visualCue,
                    item.mnemonic,
                    item.imagePrompt,
                    rejectionReason,
                    result => replacement = result,
                    err => error = err));
            }
            else
            {
                if (string.IsNullOrWhiteSpace(ollamaBaseUrl) || string.IsNullOrWhiteSpace(ollamaModel))
                {
                    regeneratingMnemonicWords.Remove(item.word);
                    statusMessage = "Cannot regenerate mnemonic: Ollama endpoint or mnemonic text model is empty.";
                    yield break;
                }

                yield return StartCoroutine(service.RegenerateMnemonicItem(
                    ollamaBaseUrl,
                    ollamaModel,
                    word,
                    itemIndex,
                    currentItems.Count,
                    item.anchorId,
                    item.anchorLabel,
                    item.visualCue,
                    item.mnemonic,
                    item.imagePrompt,
                    rejectionReason,
                    result => replacement = result,
                    err => error = err));
            }

            regeneratingMnemonicWords.Remove(item.word);

            if (!string.IsNullOrWhiteSpace(error))
            {
                generationError = error;
                statusMessage = $"Failed to regenerate cue package for {item.word}.";
                LogInteraction("regenerate_mnemonic_failed", item.word, item.anchorId, error);
                yield break;
            }

            if (replacement == null)
            {
                statusMessage = $"{liveProviderLabel} returned no replacement mnemonic for {item.word}.";
                yield break;
            }

            if (providerMode == LlmProviderMode.GeminiOnline
                && !string.IsNullOrWhiteSpace(service.GeminiModelsUsedSummary))
            {
                liveModelLabel = service.GeminiModelsUsedSummary;
            }

            ReplaceMnemonicItemFields(item, replacement);
            ApplyAnchorConsistency(item);
            if (ApplyMeaningFirstMnemonicGuardrails(item))
            {
                ApplyAnchorConsistency(item);
            }

            ClearGeneratedImageCueForWord(item.word);
            ClearMemorySnapshotForWord(item.word);
            imageCueValidationFailures.Remove(item.word);
            imageGenerationStatus = string.Empty;
            usedLiveLlmForCurrentSession = true;
            liveMnemonicProviderLabelForCurrentSession = liveProviderLabel;
            liveMnemonicModelForCurrentSession = liveModelLabel;
            liveMnemonicSourceForCurrentSession = liveSourceTag;
            item.mnemonicSource = string.IsNullOrWhiteSpace(item.mnemonicSource) ? liveSourceTag : item.mnemonicSource;
            statusMessage = $"Regenerated cue package for {item.word}. Generate the image cue again.";
            LogInteraction("regenerate_mnemonic", item.word, item.anchorId, "Replaced weak cue scene. Reason: " + rejectionReason);
        }

        private int GetCurrentItemIndex(MnemonicItemData item)
        {
            if (item == null)
            {
                return -1;
            }

            for (int i = 0; i < currentItems.Count; i++)
            {
                if (ReferenceEquals(currentItems[i], item)
                    || string.Equals(currentItems[i].word, item.word, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        private void ReplaceMnemonicItemFields(MnemonicItemData target, MnemonicItemData replacement)
        {
            if (target == null || replacement == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(replacement.anchorId))
            {
                target.anchorId = replacement.anchorId;
            }

            if (!string.IsNullOrWhiteSpace(replacement.anchorLabel))
            {
                target.anchorLabel = replacement.anchorLabel;
            }

            target.anchorType = string.IsNullOrWhiteSpace(replacement.anchorType)
                ? PreGeneratedMnemonicCatalog.NormalizeAnchorType(target.anchorId, target.anchorLabel)
                : replacement.anchorType;

            if (!string.IsNullOrWhiteSpace(replacement.mnemonicSource))
            {
                target.mnemonicSource = replacement.mnemonicSource;
            }

            if (!string.IsNullOrWhiteSpace(replacement.visualCue))
            {
                target.visualCue = replacement.visualCue;
            }

            target.associationPrompt = string.IsNullOrWhiteSpace(replacement.associationPrompt)
                ? FirstNonEmptyPrompt(replacement.imagePrompt, replacement.visualCue, target.associationPrompt)
                : replacement.associationPrompt;

            if (!string.IsNullOrWhiteSpace(replacement.mainCueObject))
            {
                target.mainCueObject = replacement.mainCueObject;
            }

            if (!string.IsNullOrWhiteSpace(replacement.mnemonic))
            {
                target.mnemonic = replacement.mnemonic;
            }

            if (!string.IsNullOrWhiteSpace(replacement.mnemonicMode))
            {
                target.mnemonicMode = replacement.mnemonicMode;
            }

            target.hookAccepted = replacement.hookAccepted;
            target.hookScore = replacement.hookScore;
            target.hookReason = replacement.hookReason;
            target.mnemonicHook = replacement.mnemonicHook;

            target.storyCue = replacement.storyCue ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(replacement.imagePrompt))
            {
                target.imagePrompt = replacement.imagePrompt;
            }

            if (string.IsNullOrWhiteSpace(target.imagePrompt))
            {
                target.imagePrompt = BuildMnemonicImagePrompt(target);
            }

            target.objectShape = string.IsNullOrWhiteSpace(replacement.objectShape) ? target.objectShape : replacement.objectShape;
            target.colorHex = string.IsNullOrWhiteSpace(replacement.colorHex) ? target.colorHex : replacement.colorHex;
            target.visualObjects = replacement.visualObjects ?? new List<VisualObjectSpec>();
            target.imagePromptCandidates = replacement.imagePromptCandidates == null
                ? new List<string>()
                : new List<string>(replacement.imagePromptCandidates);
            target.selectedImagePrompt = string.Empty;
            target.selectedImageCandidateIndex = -1;
            target.imageSelectionReason = string.Empty;
            target.imageCuePath = string.Empty;
        }

        private string GetMnemonicRegenerationReason(MnemonicItemData item)
        {
            if (item == null)
            {
                return "The previous cue scene was rejected.";
            }

            if (imageCueValidationFailures.TryGetValue(item.word, out var validationFailure)
                && !string.IsNullOrWhiteSpace(validationFailure))
            {
                return "Image generation/validation failed for the current cue scene: " + validationFailure;
            }

            return "User rejected the current cue scene as too weak, forced, or hard to generate as a clear anchor-plus-cue image.";
        }

        private List<ImagePromptCandidate> BuildMnemonicImagePromptCandidates(MnemonicItemData item)
        {
            var targetCount = RequiredImagePromptCandidateCount;
            var rawPrompts = new List<string>();
            AddImagePromptCandidates(rawPrompts, item?.imagePromptCandidates);
            AddImagePromptCandidate(rawPrompts, item?.imagePrompt);
            AddImagePromptCandidate(rawPrompts, item?.associationPrompt);
            AddImagePromptCandidate(rawPrompts, item?.visualCue);

            var baseAssociation = FirstNonEmptyPrompt(
                item?.associationPrompt,
                item?.imagePrompt,
                item?.visualCue,
                BuildRecoveredSceneDetail(item));

            for (int i = 0; rawPrompts.Count < targetCount && i < targetCount * 2; i++)
            {
                AddImagePromptCandidate(rawPrompts, BuildFallbackImagePromptCandidate(item, baseAssociation, i));
            }

            var candidates = new List<ImagePromptCandidate>();
            for (int i = 0; i < rawPrompts.Count && candidates.Count < targetCount; i++)
            {
                var label = BuildImagePromptCandidateLabel(candidates.Count);
                var rawPrompt = rawPrompts[i];
                var fullPrompt = BuildAnchorGroundedImagePrompt(item, rawPrompt, candidates.Count);
                candidates.Add(new ImagePromptCandidate
                {
                    index = candidates.Count + 1,
                    label = label,
                    rawPrompt = rawPrompt,
                    fullPrompt = fullPrompt
                });
            }

            if (item != null)
            {
                item.imagePromptCandidates = new List<string>();
                for (int i = 0; i < candidates.Count; i++)
                {
                    item.imagePromptCandidates.Add(candidates[i].rawPrompt);
                }
            }

            return candidates;
        }

        private static void AddImagePromptCandidates(List<string> prompts, List<string> candidates)
        {
            if (candidates == null)
            {
                return;
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                AddImagePromptCandidate(prompts, candidates[i]);
            }
        }

        private static void AddImagePromptCandidate(List<string> prompts, string prompt)
        {
            if (prompts == null || string.IsNullOrWhiteSpace(prompt))
            {
                return;
            }

            var cleaned = NormalizeGeneratedImageCueText(ExtractPromptSceneDetail(prompt));
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                cleaned = prompt.Trim();
            }

            for (int i = 0; i < prompts.Count; i++)
            {
                if (string.Equals(prompts[i], cleaned, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            prompts.Add(cleaned);
        }

        private string BuildFallbackImagePromptCandidate(MnemonicItemData item, string baseAssociation, int variantIndex)
        {
            var anchor = GetAnchorDisplayName(item);
            var cueSubject = BuildConcreteCueSubjectPhrase(item, anchor);
            var association = string.IsNullOrWhiteSpace(baseAssociation)
                ? $"clear foreground prop for {GetMeaningText(item)} interacting with {anchor}"
                : baseAssociation.Trim();

            switch (variantIndex)
            {
                case 0:
                    return $"{association}, {cueSubject} on-anchor close-up at the {anchor}";
                case 1:
                    return $"{association}, action-focused {cueSubject} physically interacting with the {anchor}";
                case 2:
                    return $"{association}, unusual but physically possible relation to the {anchor}";
                case 3:
                    return $"{association}, simplest literal foreground prop on the {anchor}";
                default:
                    return $"{association}, compressed foreground scene on the {anchor}, no room overview";
            }
        }

        private static string BuildImagePromptCandidateLabel(int variantIndex)
        {
            switch (variantIndex)
            {
                case 0:
                    return "object-on-anchor";
                case 1:
                    return "action-focused";
                case 2:
                    return "novel-possible";
                case 3:
                    return "literal-simple";
                default:
                    return "compressed-close-up";
            }
        }

        private static string BuildImagePromptCandidateComposition(int variantIndex)
        {
            switch (variantIndex)
            {
                case 0:
                    return "object-on-anchor close-up, large foreground prop, clear contact point, no room overview.";
                case 1:
                    return "action-focused close-up, visible physical interaction, simple uncluttered foreground.";
                case 2:
                    return "novel but physically possible object relation, realistic and distinctive, no fantasy.";
                case 3:
                    return "simple literal foreground, familiar recognizable objects, clear meaning prop.";
                default:
                    return "compressed close-up, anchor and foreground prop occupy most of the frame, minimal background.";
            }
        }

        private static string FirstNonEmptyPrompt(params string[] prompts)
        {
            if (prompts == null)
            {
                return string.Empty;
            }

            for (int i = 0; i < prompts.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(prompts[i]))
                {
                    return prompts[i].Trim();
                }
            }

            return string.Empty;
        }

        private void ClearImageCueCandidatePool(string word, bool keepDisplayedTexture)
        {
            if (string.IsNullOrWhiteSpace(word))
            {
                return;
            }

            mnemonicImageCues.TryGetValue(word, out var displayedTexture);
            if (imageCueCandidateResults.TryGetValue(word, out var results))
            {
                for (int i = 0; i < results.Count; i++)
                {
                    var texture = results[i]?.texture;
                    if (texture == null || (keepDisplayedTexture && texture == displayedTexture))
                    {
                        continue;
                    }

                    Destroy(texture);
                }
            }

            imageCueCandidateResults.Remove(word);
            displayedImageCueCandidateIndexes.Remove(word);
        }

        private bool IsTextureInImageCueCandidatePool(string word, Texture2D texture)
        {
            if (string.IsNullOrWhiteSpace(word) || texture == null)
            {
                return false;
            }

            if (!imageCueCandidateResults.TryGetValue(word, out var results) || results == null)
            {
                return false;
            }

            for (int i = 0; i < results.Count; i++)
            {
                if (results[i]?.texture == texture)
                {
                    return true;
                }
            }

            return false;
        }

        private List<ImageCueCandidateResult> GetImageCueCandidateResults(string word)
        {
            if (string.IsNullOrWhiteSpace(word))
            {
                return null;
            }

            if (!imageCueCandidateResults.TryGetValue(word, out var results) || results == null)
            {
                results = new List<ImageCueCandidateResult>();
                imageCueCandidateResults[word] = results;
            }

            return results;
        }

        private int GetDisplayedImageCueCandidateIndex(string word)
        {
            if (string.IsNullOrWhiteSpace(word))
            {
                return -1;
            }

            return displayedImageCueCandidateIndexes.TryGetValue(word, out var index) ? index : -1;
        }

        private bool TryCycleDisplayedImageCueCandidate(MnemonicItemData item, int direction)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.word))
            {
                return false;
            }

            if (!imageCueCandidateResults.TryGetValue(item.word, out var results) || results == null || results.Count <= 1)
            {
                return false;
            }

            var currentIndex = GetDisplayedImageCueCandidateIndex(item.word);
            if (currentIndex < 0 || currentIndex >= results.Count)
            {
                currentIndex = 0;
            }

            var nextIndex = (currentIndex + direction + results.Count) % results.Count;
            return TryDisplayImageCueCandidate(item, nextIndex, true);
        }

        private void AddImageCueCandidateResult(MnemonicItemData item, ImageCueCandidateResult result)
        {
            if (item == null || result == null || result.texture == null || string.IsNullOrWhiteSpace(item.word))
            {
                return;
            }

            var results = GetImageCueCandidateResults(item.word);
            result.listIndex = results.Count;
            results.Add(result);

            if (!mnemonicImageCues.TryGetValue(item.word, out var currentTexture) || currentTexture == null || results.Count == 1)
            {
                TryDisplayImageCueCandidate(item, result.listIndex, false);
            }
            else
            {
                var displayedIndex = GetDisplayedImageCueCandidateIndex(item.word);
                if (displayedIndex >= 0 && displayedIndex < results.Count)
                {
                    RefreshDisplayedImageCueCandidateMetadata(item, results[displayedIndex]);
                }
            }
        }

        private bool TryDisplayImageCueCandidate(MnemonicItemData item, int listIndex, bool userSelected)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.word))
            {
                return false;
            }

            if (!imageCueCandidateResults.TryGetValue(item.word, out var results)
                || results == null
                || listIndex < 0
                || listIndex >= results.Count)
            {
                return false;
            }

            var result = results[listIndex];
            if (result == null || result.texture == null)
            {
                return false;
            }

            if (mnemonicImageCues.TryGetValue(item.word, out var previousTexture)
                && previousTexture != null
                && previousTexture != result.texture
                && !IsTextureInImageCueCandidatePool(item.word, previousTexture))
            {
                Destroy(previousTexture);
            }

            mnemonicImageCues[item.word] = result.texture;
            displayedImageCueCandidateIndexes[item.word] = listIndex;
            item.imageCuePath = SaveMnemonicImageCue(item, result.texture);
            if (string.IsNullOrWhiteSpace(item.imageCuePath))
            {
                item.imageCuePath = GetSourceImagePath(result);
            }
            item.selectedImagePrompt = result.fullPrompt;
            item.selectedImageCandidateIndex = result.candidateIndex;
            item.imageSelectionReason = BuildImageCueCandidateDisplaySummary(item, result, listIndex, results.Count);

            if (result.validationComplete && !result.pass)
            {
                imageCueValidationFailures[item.word] = result.reason;
            }
            else if (result.validationComplete && result.pass)
            {
                imageCueValidationFailures.Remove(item.word);
            }

            if (userSelected)
            {
                imageGenerationStatus = $"Showing image cue {listIndex + 1}/{results.Count} for {item.word}.";
                LogInteraction("select_image_cue_candidate", item.word, item.anchorId, item.imageSelectionReason);
            }

            return true;
        }

        private void RefreshDisplayedImageCueCandidateMetadata(MnemonicItemData item, ImageCueCandidateResult result)
        {
            if (item == null || result == null || string.IsNullOrWhiteSpace(item.word))
            {
                return;
            }

            if (!displayedImageCueCandidateIndexes.TryGetValue(item.word, out var displayedIndex)
                || !imageCueCandidateResults.TryGetValue(item.word, out var results)
                || results == null
                || displayedIndex < 0
                || displayedIndex >= results.Count
                || results[displayedIndex] != result)
            {
                return;
            }

            item.imageSelectionReason = BuildImageCueCandidateDisplaySummary(item, result, displayedIndex, results.Count);
            if (result.validationComplete && !result.pass)
            {
                imageCueValidationFailures[item.word] = result.reason;
            }
            else if (result.validationComplete && result.pass)
            {
                imageCueValidationFailures.Remove(item.word);
            }
        }

        private static string GetSourceImagePath(ImageCueCandidateResult result)
        {
            if (result?.innerCandidates == null)
            {
                return string.Empty;
            }

            for (int i = 0; i < result.innerCandidates.Count; i++)
            {
                var path = result.innerCandidates[i]?.imagePath;
                if (!string.IsNullOrWhiteSpace(path))
                {
                    return path;
                }
            }

            return string.Empty;
        }

        private static string BuildImageCueCandidateDisplaySummary(
            MnemonicItemData item,
            ImageCueCandidateResult result,
            int listIndex,
            int totalCount)
        {
            var state = result.validationComplete
                ? (result.pass ? "passed" : "best available / did not fully pass")
                : "validation pending";
            var reason = string.IsNullOrWhiteSpace(result.reason)
                ? "Generated image is available while background validation continues."
                : result.reason.Trim();
            var candidate = string.IsNullOrWhiteSpace(result.innerLabel)
                ? $"prompt {result.candidateIndex}"
                : $"prompt {result.candidateIndex} ({result.innerLabel})";
            return $"Showing result {result.label} ({listIndex + 1}/{Mathf.Max(1, totalCount)}): {candidate}, score {result.score}, {state}. {reason}";
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
            imageCueValidationFailures.Remove(item.word);
            if (string.IsNullOrWhiteSpace(ollamaBaseUrl) || string.IsNullOrWhiteSpace(imageCueValidationModel))
            {
                generatingImageCueWords.Remove(item.word);
                imageGenerationStatus = "Image cue validation requires an Ollama endpoint and an image-capable validation model.";
                imageCueValidationFailures[item.word] = imageGenerationStatus;
                yield break;
            }

            ClearGeneratedImageCueForWord(item.word);

            var crossVariantGuidance = string.Empty;
            for (int variantIndex = 0; variantIndex < BufferedImageCueResultCount; variantIndex++)
            {
                ImageCueCandidateResult bestResult = null;
                string fatalError = null;
                yield return GenerateBestImageCueVariantRoutine(
                    item,
                    variantIndex,
                    BufferedImageCueResultCount,
                    crossVariantGuidance,
                    false,
                    result => bestResult = result,
                    error => fatalError = error);

                if (bestResult != null)
                {
                    AddImageCueCandidateResult(item, bestResult);
                    LogInteraction(
                        "generate_single_pass_image_cue",
                        item.word,
                        item.anchorId,
                        $"Generated result {bestResult.label} from prompt {bestResult.candidateIndex}; score={bestResult.score}. {bestResult.reason}");
                    crossVariantGuidance = BuildImageCueEvolutionGuidance(bestResult);
                    if (!string.IsNullOrWhiteSpace(crossVariantGuidance))
                    {
                        LogInteraction(
                            "evolve_image_prompt_guidance",
                            item.word,
                            item.anchorId,
                            $"Next image set will use feedback from {bestResult.label}; score={bestResult.score}. {crossVariantGuidance}");
                    }
                }

                if (!string.IsNullOrWhiteSpace(fatalError))
                {
                    if (!imageCueCandidateResults.TryGetValue(item.word, out var partialResults)
                        || partialResults == null
                        || partialResults.Count == 0)
                    {
                        generatingImageCueWords.Remove(item.word);
                        imageGenerationStatus = fatalError;
                        imageCueValidationFailures[item.word] = fatalError;
                        yield break;
                    }

                    imageGenerationStatus = $"Stopped after preparing {partialResults.Count} image cue result(s) for {item.word}: {fatalError}";
                    break;
                }

                var readyCount = imageCueCandidateResults.TryGetValue(item.word, out var currentResults) && currentResults != null
                    ? currentResults.Count
                    : 0;
                imageGenerationStatus = variantIndex == 0
                    ? $"Selected image set A for {item.word}. Preparing B, C, and D with feedback from previous sets..."
                    : $"Prepared {readyCount}/{BufferedImageCueResultCount} scored image set(s) for {item.word}.";
            }

            generatingImageCueWords.Remove(item.word);
            if (!imageCueCandidateResults.TryGetValue(item.word, out var generatedResults) || generatedResults == null || generatedResults.Count == 0)
            {
                imageGenerationStatus = $"Image generation finished for {item.word}, but no image cue result could be kept.";
                yield break;
            }

            var displayedIndex = GetDisplayedImageCueCandidateIndex(item.word);
            if (displayedIndex < 0)
            {
                TryDisplayImageCueCandidate(item, 0, false);
                displayedIndex = 0;
            }

            imageGenerationStatus = $"Prepared {generatedResults.Count}/{BufferedImageCueResultCount} scored image cue result(s) for {item.word}. Use the arrow buttons for human review/selection; Regenerate starts a fresh full set.";
            var displayedResult = displayedIndex >= 0 && displayedIndex < generatedResults.Count ? generatedResults[displayedIndex] : null;
            LogInteraction(
                "generate_image_cue_result_pool",
                item.word,
                item.anchorId,
                $"Prepared {generatedResults.Count} single-pass image cue result(s). Displayed: {(displayedResult == null ? "none" : BuildImageCueCandidateDisplaySummary(item, displayedResult, displayedIndex, generatedResults.Count))}");
        }

        private IEnumerator GenerateBestImageCueVariantRoutine(
            MnemonicItemData item,
            int variantIndex,
            int variantCount,
            string initialGuidance,
            bool skipVisionScoring,
            Action<ImageCueCandidateResult> onResult,
            Action<string> onError)
        {
            var retryGuidance = string.IsNullOrWhiteSpace(initialGuidance) ? string.Empty : initialGuidance.Trim();
            var variantLabel = BuildImageCueResultLabel(variantIndex);
            var promptCandidates = BuildMnemonicImagePromptCandidates(item);
            var innerCandidates = new List<ImageCueInnerCandidateResult>();
            if (promptCandidates.Count == 0)
            {
                onError?.Invoke($"Image set {variantLabel} had no prompt candidates.");
                yield break;
            }

            var candidate = promptCandidates[Mathf.Clamp(variantIndex, 0, promptCandidates.Count - 1)];
            imageGenerationStatus = $"Generating image {variantLabel}/{BuildImageCueResultLabel(variantCount - 1)} for {item.word} ({candidate.label})...";
            var prompt = BuildFinalStableDiffusionPrompt(item, candidate, retryGuidance, out var promptRepairSummary);
            if (!string.IsNullOrWhiteSpace(promptRepairSummary))
            {
                LogInteraction("repair_image_prompt_preflight", item.word, item.anchorId, promptRepairSummary);
            }

            Debug.Log($"FINAL_IMAGE_PROMPT_SENT_TO_MODEL [{item.word} {variantLabel}/{candidate.label}] = {prompt}");
            LogInteraction("stable_diffusion_prompt", item.word, item.anchorId, BuildShortPreview(prompt));

            var requestBody = new StableDiffusionTxt2ImgRequest
            {
                prompt = prompt,
                negative_prompt = BuildMnemonicImageNegativePrompt(),
                width = 512,
                height = 512,
                steps = 28,
                cfg_scale = 8.5f,
                sampler_name = "DPM++ 2M",
                batch_size = 1,
                n_iter = 1,
                tiling = false,
                do_not_save_grid = true,
                send_images = true,
                save_images = false
            };
            requestBody.override_settings = BuildImageOverrideSettings();
            requestBody.override_settings_restore_afterwards = requestBody.override_settings != null;
            var json = JsonUtility.ToJson(requestBody);

            using var request = new UnityWebRequest(imageGenerationEndpoint.Trim(), UnityWebRequest.kHttpVerbPOST);
            var bodyRaw = Encoding.UTF8.GetBytes(json);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = 180;
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                onError?.Invoke(BuildStableDiffusionErrorStatus(request));
                yield break;
            }

            StableDiffusionTxt2ImgResponse response = null;
            try
            {
                response = JsonUtility.FromJson<StableDiffusionTxt2ImgResponse>(request.downloadHandler.text);
            }
            catch (Exception ex)
            {
                onError?.Invoke("Failed to parse image response: " + ex.Message);
                yield break;
            }

            if (response == null || response.images == null || response.images.Length == 0 || string.IsNullOrWhiteSpace(response.images[0]))
            {
                onError?.Invoke("Image response did not contain any images.");
                yield break;
            }

            if (!TryLoadBase64Image(response.images[0], out var texture, out var loadError))
            {
                onError?.Invoke(loadError);
                yield break;
            }

            var imagePath = SaveMnemonicImageCue(item, texture, variantLabel);
            if (skipVisionScoring)
            {
                var skipReason = "Vision scoring skipped for fast image catalog build; keep or regenerate after human review.";
                innerCandidates.Add(new ImageCueInnerCandidateResult
                {
                    index = candidate.index,
                    label = candidate.label,
                    rawPrompt = candidate.rawPrompt,
                    fullPrompt = prompt,
                    imagePath = imagePath,
                    validation = null,
                    score = 0,
                    pass = false,
                    validationComplete = false,
                    reason = skipReason
                });

                onResult?.Invoke(new ImageCueCandidateResult
                {
                    candidateIndex = candidate.index,
                    label = variantLabel,
                    innerLabel = candidate.label,
                    rawPrompt = candidate.rawPrompt,
                    fullPrompt = prompt,
                    texture = texture,
                    validation = null,
                    score = 0,
                    pass = false,
                    validationComplete = false,
                    reason = skipReason,
                    innerCandidates = innerCandidates
                });
                yield break;
            }

            imageGenerationStatus = $"Scoring image {variantLabel} for {item.word} ({candidate.label})...";
            ImageCueValidationResult validation = null;
            string validationError = null;
            yield return ValidateImageCueSubjectsRoutine(
                item,
                response.images[0],
                prompt,
                result => validation = result,
                errorMessage => validationError = errorMessage);

            var score = 0;
            var candidatePassed = false;
            var validationComplete = false;
            var reason = string.Empty;
            if (!string.IsNullOrWhiteSpace(validationError))
            {
                reason = "Vision validation was unavailable, so the generated image was kept: " + validationError;
                LogInteraction("keep_unvalidated_image_cue", item.word, item.anchorId, reason);
            }
            else
            {
                validationComplete = true;
                candidatePassed = validation != null && validation.pass;
                score = ScoreImageCueValidation(validation);
                reason = candidatePassed
                    ? BuildImageCueValidationPassSummary(validation)
                    : BuildImageCueValidationFailureSummary(validation);
                LogInteraction(
                    candidatePassed ? "score_image_cue_candidate" : "flag_image_cue_candidate",
                    item.word,
                    item.anchorId,
                    $"Image {variantLabel}, prompt {candidate.index} ({candidate.label}) score={score}. {reason}");
            }

            innerCandidates.Add(new ImageCueInnerCandidateResult
            {
                index = candidate.index,
                label = candidate.label,
                rawPrompt = candidate.rawPrompt,
                fullPrompt = prompt,
                imagePath = imagePath,
                validation = validation,
                score = score,
                pass = candidatePassed,
                validationComplete = validationComplete,
                reason = reason
            });

            onResult?.Invoke(new ImageCueCandidateResult
            {
                candidateIndex = candidate.index,
                label = variantLabel,
                innerLabel = candidate.label,
                rawPrompt = candidate.rawPrompt,
                fullPrompt = prompt,
                texture = texture,
                validation = validation,
                score = score,
                pass = candidatePassed,
                validationComplete = validationComplete,
                reason = reason,
                innerCandidates = innerCandidates
            });
        }

        private static string BuildImageCueEvolutionGuidance(ImageCueCandidateResult result)
        {
            if (result == null)
            {
                return string.Empty;
            }

            if (result.validation == null)
            {
                return string.IsNullOrWhiteSpace(result.reason)
                    ? string.Empty
                    : "Make the assigned room object and concrete foreground prop large, separate, clearly visible together, physically touching, and shown in one continuous close-up frame.";
            }

            if (result.pass)
            {
                return "Preserve these validated strengths: assigned room object visible, concrete foreground prop visible, clear physical contact, tight single close-up frame, simple background, and meaning-specific action. Use a distinct safe contact layout.";
            }

            return BuildImageCueRetryGuidance(result.validation)
                   + " Preserve the same Association Image Cue and use a distinct safe contact layout.";
        }

        private string BuildFinalStableDiffusionPrompt(
            MnemonicItemData item,
            ImagePromptCandidate candidate,
            string retryGuidance,
            out string repairSummary)
        {
            repairSummary = string.Empty;
            var layoutVariant = Mathf.Max(0, (candidate?.index ?? 1) - 1);
            var prompt = string.IsNullOrWhiteSpace(candidate?.fullPrompt)
                ? BuildAnchorGroundedImagePrompt(item, candidate?.rawPrompt, layoutVariant)
                : candidate.fullPrompt.Trim();

            prompt = RemoveImagePromptProcessTerms(prompt);
            var issues = BuildImagePromptPreflightIssues(item, prompt);
            if (!string.IsNullOrWhiteSpace(issues))
            {
                prompt = RemoveImagePromptProcessTerms(BuildAnchorGroundedImagePrompt(item, candidate?.rawPrompt, layoutVariant));
                repairSummary = "Image prompt preflight repaired: " + issues;
            }

            var anchor = GetAnchorDisplayName(item);
            var concreteCueSubject = BuildConcreteCueSubjectPhrase(item, anchor);
            var anchorRequirement = BuildAnchorVisualRequirement(item, anchor);
            var cueRequirement = BuildCueVisualRequirement(item, concreteCueSubject);
            prompt += " Mandatory two-subject frame: the assigned room object and " + concreteCueSubject + " must both be clearly visible, close together, and dominate the image; do not show only one of them.";
            prompt += " " + anchorRequirement;
            if (!string.IsNullOrWhiteSpace(cueRequirement))
            {
                prompt += " " + cueRequirement;
            }

            if (!string.IsNullOrWhiteSpace(retryGuidance))
            {
                prompt += " Visual correction from previous validation: " + RemoveImagePromptProcessTerms(retryGuidance);
            }

            prompt += " Single image only: one uninterrupted frame, one camera viewpoint, no split-screen, no collage, no contact sheet.";
            return prompt;
        }

        private string BuildImagePromptPreflightIssues(MnemonicItemData item, string prompt)
        {
            var issues = new List<string>();
            var normalized = string.IsNullOrWhiteSpace(prompt) ? string.Empty : prompt.Trim();
            var lower = normalized.ToLowerInvariant();
            var anchor = GetAnchorDisplayName(item);

            if (!lower.StartsWith("single close-up image,", StringComparison.Ordinal))
            {
                issues.Add("missing Single close-up image lead");
            }

            if (!TextMentionsAnchor(normalized, anchor))
            {
                issues.Add("missing assigned anchor label");
            }

            if (ContainsImagePromptProcessTerms(lower))
            {
                issues.Add("contains generation-process terms");
            }

            if (TryFindAssociationPlaceholderLeakage(normalized, out var leakedTerm))
            {
                issues.Add("contains placeholder phrase: " + leakedTerm);
            }

            if (!ContainsAny(lower, "smaller than", "larger than", "one third", "half the size", "much smaller", "percent", "30-45", "35-55"))
            {
                issues.Add("missing relative size phrase");
            }

            if (!ContainsAny(lower, "beside", "next to", "on ", "under", "attached", "fixed", "leaning", "hanging", "inside", "in front", "behind", "across", "positioned"))
            {
                issues.Add("missing spatial relation phrase");
            }

            if (!ContainsAny(lower, "touching", "attached", "fixed", "hanging", "leaning", "supported", "wedged", "clipped", "wrapped", "spilling", "pouring"))
            {
                issues.Add("missing physical contact/support phrase");
            }

            var foregroundObjects = BuildImageForegroundObjectList(item, anchor);
            if (!string.IsNullOrWhiteSpace(foregroundObjects) && !AnyForegroundObjectMentioned(lower, foregroundObjects))
            {
                issues.Add("missing foreground cue object");
            }

            return string.Join("; ", issues);
        }

        private static bool AnyForegroundObjectMentioned(string lowerPrompt, string foregroundObjects)
        {
            if (string.IsNullOrWhiteSpace(lowerPrompt) || string.IsNullOrWhiteSpace(foregroundObjects))
            {
                return true;
            }

            var parts = foregroundObjects.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                var label = parts[i]?.Trim().ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(label))
                {
                    continue;
                }

                if (lowerPrompt.IndexOf(label, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsImagePromptProcessTerms(string lowerPrompt)
        {
            return ContainsAny(
                lowerPrompt,
                "candidate composition",
                "best-of-four",
                "image set",
                "generated sets",
                "inner candidate",
                "candidate",
                "version",
                "multiple views",
                "selected image",
                "story cue",
                "mnemonic",
                "different versions",
                "selected image set");
        }

        private static string RemoveImagePromptProcessTerms(string prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt))
            {
                return string.Empty;
            }

            var cleaned = prompt.Trim();
            cleaned = ReplaceCaseInsensitive(cleaned, "Candidate composition:", "Composition:");
            cleaned = ReplaceCaseInsensitive(cleaned, "Independent best-of-four image set", "Single image");
            cleaned = ReplaceCaseInsensitive(cleaned, "best-of-four", "single-image");
            cleaned = ReplaceCaseInsensitive(cleaned, "inner candidate", "image attempt");
            cleaned = ReplaceCaseInsensitive(cleaned, "candidate correction", "image correction");
            cleaned = ReplaceCaseInsensitive(cleaned, "different versions", "distinct camera-safe layouts");
            cleaned = ReplaceCaseInsensitive(cleaned, "generated sets", "generated images");
            cleaned = ReplaceCaseInsensitive(cleaned, "selected image set", "selected image");
            cleaned = ReplaceCaseInsensitive(cleaned, "candidate", "cue");
            cleaned = ReplaceCaseInsensitive(cleaned, "version", "layout");
            cleaned = ReplaceCaseInsensitive(cleaned, "multiple views", "extra views");
            cleaned = ReplaceCaseInsensitive(cleaned, "story cue", "imagined scene");
            cleaned = ReplaceCaseInsensitive(cleaned, "mnemonic", "association");
            cleaned = ReplaceCaseInsensitive(cleaned, "association image cue object", "concrete foreground prop");
            cleaned = ReplaceCaseInsensitive(cleaned, "concrete cue object", "concrete foreground prop");
            cleaned = ReplaceCaseInsensitive(cleaned, "main cue object", "concrete foreground prop");
            cleaned = ReplaceCaseInsensitive(cleaned, "target object", "target prop");
            cleaned = ReplaceCaseInsensitive(cleaned, "anchor object", "room anchor");
            cleaned = ReplaceCaseInsensitive(cleaned, "visible object", "visible prop");
            cleaned = ReplaceCaseInsensitive(cleaned, "foreground cue object", "foreground prop");
            cleaned = ReplaceCaseInsensitive(cleaned, "foreground cue", "foreground prop");
            cleaned = ReplaceCaseInsensitive(cleaned, "physical object", "physical prop");
            cleaned = ReplaceCaseInsensitive(cleaned, "anchor contact point", "visible contact point");
            cleaned = ReplaceCaseInsensitive(cleaned, "visible action or state", "visible action");
            cleaned = ReplaceCaseInsensitive(cleaned, "cue object", "foreground prop");
            return cleaned;
        }

        private static string BuildImageCueResultLabel(int variantIndex)
        {
            return ((char)('A' + Mathf.Clamp(variantIndex, 0, 25))).ToString();
        }

        private IEnumerator ValidateImageCueSubjectsRoutine(
            MnemonicItemData item,
            string rawBase64Image,
            string generationPrompt,
            Action<ImageCueValidationResult> onSuccess,
            Action<string> onError)
        {
            var imagePayload = ExtractBase64ImagePayload(rawBase64Image);
            if (string.IsNullOrWhiteSpace(imagePayload))
            {
                onError?.Invoke("Generated image payload was empty.");
                yield break;
            }

            var requestBody = new OllamaVisionGenerateRequest
            {
                model = imageCueValidationModel.Trim(),
                prompt = BuildImageCueValidationPrompt(item, generationPrompt),
                stream = false,
                format = "json",
                images = new[] { imagePayload },
                options = new OllamaVisionOptions
                {
                    temperature = 0f,
                    num_predict = 360
                }
            };
            var json = JsonUtility.ToJson(requestBody);

            using (var request = new UnityWebRequest(ollamaBaseUrl.Trim(), UnityWebRequest.kHttpVerbPOST))
            {
                var bodyRaw = Encoding.UTF8.GetBytes(json);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = 180;
                request.SetRequestHeader("Content-Type", "application/json");

                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    onError?.Invoke($"{request.error}\n{BuildShortPreview(request.downloadHandler?.text)}");
                    yield break;
                }

                OllamaVisionGenerateResponse response = null;
                try
                {
                    response = JsonUtility.FromJson<OllamaVisionGenerateResponse>(request.downloadHandler.text);
                }
                catch (Exception ex)
                {
                    onError?.Invoke("Failed to parse vision response envelope: " + ex.Message);
                    yield break;
                }

                if (response == null)
                {
                    onError?.Invoke("Vision response envelope was empty.");
                    yield break;
                }

                if (!string.IsNullOrWhiteSpace(response.error))
                {
                    onError?.Invoke("Vision model returned an error: " + response.error);
                    yield break;
                }

                if (!TryParseImageCueValidationResult(response.response, out var validation, out var parseError))
                {
                    onError?.Invoke(parseError + "\nRaw vision response: " + BuildShortPreview(response.response));
                    yield break;
                }

                onSuccess?.Invoke(validation);
            }
        }

        private string BuildImageCueValidationPrompt(MnemonicItemData item, string generationPrompt)
        {
            var anchor = GetAnchorDisplayName(item);
            var cueObjects = BuildImageForegroundObjectList(item, anchor);
            if (string.IsNullOrWhiteSpace(cueObjects))
            {
                cueObjects = "the foreground cue object described in the expected scene";
            }
            var anchorRequirement = BuildAnchorVisualRequirement(item, anchor);
            var cueRequirement = BuildCueVisualRequirement(item, BuildConcreteCueSubjectPhrase(item, anchor));

            return
                "You are checking whether a generated association image cue is usable.\n" +
                "Only evaluate visible image content. Be strict.\n\n" +
                "Assigned anchor that must be visible: " + anchor + "\n" +
                "Target vocabulary meaning: " + GetMeaningText(item) + "\n" +
                "Cue object(s) that must be visible: " + cueObjects + "\n" +
                anchorRequirement + "\n" +
                cueRequirement + "\n" +
                "Expected scene text: " + (item?.visualCue ?? string.Empty) + "\n" +
                "Association prompt: " + (item?.associationPrompt ?? string.Empty) + "\n" +
                "Image generation prompt: " + generationPrompt + "\n\n" +
                "Evidence requirement:\n" +
                "- anchor_evidence must name visible parts of the assigned anchor object, not just repeat the anchor label.\n" +
                "- cue_evidence must name visible parts of the cue object, not just repeat the target meaning.\n" +
                "- contact_evidence must describe where the cue physically touches or interacts with the anchor.\n" +
                "- If any evidence field is empty, vague, or contradicted by the image, pass must be false.\n\n" +
                "Pass only if all are true:\n" +
                "1. The assigned anchor is clearly visible and recognizable.\n" +
                "2. The cue object(s) for the vocabulary meaning are clearly visible as separate objects.\n" +
                "3. The image points to the specific target meaning, not just a broad theme.\n" +
                "4. The main cue is large, sharp, and in the foreground.\n" +
                "5. The cue physically interacts with the assigned anchor.\n" +
                "6. The scene is visually simple, familiar, and uncluttered.\n" +
                "7. The cue-anchor relation is novel but physically possible.\n" +
                "8. The image is tightly focused on the anchor plus cue object(s), with no irrelevant room overview.\n" +
                "9. The cue object is not replaced by a recolored or restyled anchor.\n" +
                "10. The image is one single continuous camera view, not a collage, split-screen, contact sheet, grid, or multi-panel layout.\n" +
                "11. The anchor and cue object are both large enough to inspect; neither should be a tiny background detail.\n" +
                "12. The image is not a floor-plan-like layout, room tour, showroom, or interior design overview.\n\n" +
                "Reject if only the anchor is visible, only the cue object is visible, the anchor is cropped out, the cue is missing, hidden inside the anchor, tucked into a pocket or drawer, covered by fabric, blended into furniture, unrelated objects dominate, the cue or anchor is tiny, the image looks like interior design / a whole-room overview / showroom / floor plan, or the image contains separate panels / multiple views inside one output image.\n" +
                "Reject abstract_or_iconic=true if the assigned anchor is represented as a logo, icon, abstract symbol, colored curve, decorative shape, or isolated machine part instead of the real room object.\n" +
                "Examples: a red chair is not a red hat; a tray with a tiny object is not a chair anchor; a jar alone is not an air conditioner anchor; a blue C-shaped symbol or mechanical nozzle is not a wall-mounted air conditioner with grille and vent flap.\n\n" +
                "Return only valid JSON in this exact shape:\n" +
                "{\"pass\":false,\"anchor_visible\":false,\"cue_visible\":false,\"focus_ok\":false,\"meaning_specific\":false,\"foreground_clear\":false,\"anchor_interaction\":false,\"simple_scene\":false,\"familiar_objects\":false,\"novel_possible_relation\":false,\"no_room_overview\":false,\"single_continuous_image\":false,\"no_split_screen_or_collage\":false,\"abstract_or_iconic\":false,\"anchor_evidence\":\"visible anchor parts\",\"cue_evidence\":\"visible cue parts\",\"contact_evidence\":\"visible contact point\",\"caption\":\"short factual caption\",\"reason\":\"short reason\",\"missing_or_wrong\":[]}";
        }

        private static bool TryParseImageCueValidationResult(string raw, out ImageCueValidationResult validation, out string error)
        {
            validation = null;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(raw))
            {
                error = "Vision response was empty.";
                return false;
            }

            var json = ExtractFirstJsonObject(raw);
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "Vision response did not contain a JSON object.";
                return false;
            }

            try
            {
                validation = JsonUtility.FromJson<ImageCueValidationResult>(json);
            }
            catch (Exception ex)
            {
                error = "Failed to parse vision validation JSON: " + ex.Message;
                return false;
            }

            if (validation == null)
            {
                error = "Vision validation JSON parsed to an empty result.";
                return false;
            }

            validation.pass = validation.pass
                              && validation.anchor_visible
                              && validation.cue_visible
                              && validation.focus_ok
                              && validation.meaning_specific
                              && validation.foreground_clear
                              && validation.anchor_interaction
                              && validation.simple_scene
                              && validation.familiar_objects
                              && validation.novel_possible_relation
                              && validation.no_room_overview
                              && validation.single_continuous_image
                              && validation.no_split_screen_or_collage
                              && !validation.abstract_or_iconic
                              && HasValidationEvidence(validation.anchor_evidence)
                              && HasValidationEvidence(validation.cue_evidence)
                              && HasValidationEvidence(validation.contact_evidence);
            return true;
        }

        private static bool HasValidationEvidence(string evidence)
        {
            if (string.IsNullOrWhiteSpace(evidence))
            {
                return false;
            }

            var cleaned = evidence.Trim().ToLowerInvariant();
            if (cleaned.Length < 8)
            {
                return false;
            }

            return !string.Equals(cleaned, "visible", StringComparison.Ordinal)
                   && !string.Equals(cleaned, "clear", StringComparison.Ordinal)
                   && !string.Equals(cleaned, "present", StringComparison.Ordinal)
                   && !cleaned.Contains("not specified")
                   && !cleaned.Contains("unclear");
        }

        private static string ExtractFirstJsonObject(string raw)
        {
            var text = string.IsNullOrWhiteSpace(raw) ? string.Empty : raw.Trim();
            var start = text.IndexOf('{');
            if (start < 0)
            {
                return string.Empty;
            }

            var depth = 0;
            var inString = false;
            var escaped = false;
            for (int i = start; i < text.Length; i++)
            {
                var ch = text[i];
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (ch == '\\' && inString)
                {
                    escaped = true;
                    continue;
                }

                if (ch == '"')
                {
                    inString = !inString;
                    continue;
                }

                if (inString)
                {
                    continue;
                }

                if (ch == '{')
                {
                    depth++;
                }
                else if (ch == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return text.Substring(start, i - start + 1);
                    }
                }
            }

            return string.Empty;
        }

        private static string ExtractBase64ImagePayload(string rawImage)
        {
            var base64 = string.IsNullOrWhiteSpace(rawImage) ? string.Empty : rawImage.Trim();
            var commaIndex = base64.IndexOf(',');
            if (base64.StartsWith("data:image", StringComparison.OrdinalIgnoreCase) && commaIndex >= 0)
            {
                base64 = base64.Substring(commaIndex + 1);
            }

            return base64;
        }

        private static string BuildImageCueValidationFailureSummary(ImageCueValidationResult validation)
        {
            if (validation == null)
            {
                return "vision model did not return a validation result.";
            }

            var reason = string.IsNullOrWhiteSpace(validation.reason) ? "anchor and cue object were not both clearly visible." : validation.reason.Trim();
            var criteriaIssues = BuildValidationCriteriaIssues(validation);
            var issues = FormatValidationIssues(validation.missing_or_wrong);
            if (!string.IsNullOrWhiteSpace(criteriaIssues))
            {
                issues = string.IsNullOrWhiteSpace(issues) ? criteriaIssues : issues + "; " + criteriaIssues;
            }

            return string.IsNullOrWhiteSpace(issues) ? reason : reason + " Issues: " + issues;
        }

        private static int ScoreImageCueValidation(ImageCueValidationResult validation)
        {
            if (validation == null)
            {
                return 0;
            }

            var score = 0;
            if (validation.anchor_visible)
            {
                score += 25;
            }

            if (validation.cue_visible)
            {
                score += 25;
            }

            if (validation.focus_ok)
            {
                score += 15;
            }

            if (validation.meaning_specific)
            {
                score += 15;
            }

            if (validation.foreground_clear)
            {
                score += 15;
            }

            if (validation.anchor_interaction)
            {
                score += 15;
            }

            if (validation.simple_scene)
            {
                score += 10;
            }

            if (validation.familiar_objects)
            {
                score += 8;
            }

            if (validation.novel_possible_relation)
            {
                score += 12;
            }

            if (validation.no_room_overview)
            {
                score += 15;
            }

            if (validation.single_continuous_image)
            {
                score += 18;
            }

            if (validation.no_split_screen_or_collage)
            {
                score += 18;
            }

            if (validation.missing_or_wrong != null)
            {
                score -= Mathf.Min(validation.missing_or_wrong.Length * 5, 20);
            }

            if (validation.abstract_or_iconic)
            {
                score -= 35;
            }

            if (!HasValidationEvidence(validation.anchor_evidence))
            {
                score -= 20;
            }

            if (!HasValidationEvidence(validation.cue_evidence))
            {
                score -= 20;
            }

            if (!HasValidationEvidence(validation.contact_evidence))
            {
                score -= 20;
            }

            return score;
        }

        private static string BuildImageCueRetryGuidance(ImageCueValidationResult validation)
        {
            if (validation != null && (!validation.single_continuous_image || !validation.no_split_screen_or_collage))
            {
                return "Fix this failed image: " + BuildImageCueValidationFailureSummary(validation) + " Generate one uninterrupted close-up camera view only. Do not create a split-screen, collage, contact sheet, grid, side-by-side layout, or separate panels inside the image.";
            }

            if (validation != null && !validation.cue_visible)
            {
                return "Fix this failed image: " + BuildImageCueValidationFailureSummary(validation) + " The concrete foreground prop is missing or hidden. Make it fully exposed, separate from the room object, high contrast, and impossible to miss. Do not put it inside pockets, drawers, cushions, covers, or furniture.";
            }

            if (validation != null && (validation.abstract_or_iconic || !HasValidationEvidence(validation.anchor_evidence)))
            {
                return "Fix this failed image: " + BuildImageCueValidationFailureSummary(validation) + " Replace any icon, abstract symbol, logo, decorative curve, or machine fragment with the real assigned room object, large and recognizable with its normal parts visible.";
            }

            if (validation != null && !validation.no_room_overview)
            {
                return "Fix this failed image: " + BuildImageCueValidationFailureSummary(validation) + " Use a tight foreground close-up, no whole-room overview, no interior-design composition, and keep unrelated background minimal.";
            }

            if (validation != null && !validation.anchor_interaction)
            {
                return "Fix this failed image: " + BuildImageCueValidationFailureSummary(validation) + " Make the cue physically interact with the assigned room object through a visible contact point rather than floating or sitting unrelated nearby.";
            }

            return "Fix this failed image: " + BuildImageCueValidationFailureSummary(validation) + " Make the assigned room object and concrete foreground prop both large, separate, clearly visible together, simple, and meaning-specific.";
        }

        private static string BuildImageCueValidationPassSummary(ImageCueValidationResult validation)
        {
            if (validation == null || string.IsNullOrWhiteSpace(validation.caption))
            {
                return "anchor and cue object are both visible.";
            }

            return validation.caption.Trim();
        }

        private static string BuildValidationCriteriaIssues(ImageCueValidationResult validation)
        {
            if (validation == null)
            {
                return string.Empty;
            }

            var issues = new List<string>();
            if (!validation.anchor_visible)
            {
                issues.Add("anchor not clearly visible");
            }

            if (!validation.cue_visible)
            {
                issues.Add("cue object missing or unclear");
            }

            if (!validation.meaning_specific)
            {
                issues.Add("target meaning not specific");
            }

            if (!validation.foreground_clear)
            {
                issues.Add("foreground cue not clear");
            }

            if (!validation.anchor_interaction)
            {
                issues.Add("no clear anchor interaction");
            }

            if (!validation.simple_scene)
            {
                issues.Add("scene too cluttered or complex");
            }

            if (!validation.familiar_objects)
            {
                issues.Add("objects not familiar enough");
            }

            if (!validation.novel_possible_relation)
            {
                issues.Add("cue relation not distinctive or not physically plausible");
            }

            if (!validation.no_room_overview || !validation.focus_ok)
            {
                issues.Add("irrelevant room overview or weak focus");
            }

            if (!validation.single_continuous_image || !validation.no_split_screen_or_collage)
            {
                issues.Add("split-screen, collage, contact sheet, or multi-panel image");
            }

            if (validation.abstract_or_iconic)
            {
                issues.Add("anchor shown as abstract/iconic shape or unrelated part");
            }

            if (!HasValidationEvidence(validation.anchor_evidence))
            {
                issues.Add("missing concrete anchor evidence");
            }

            if (!HasValidationEvidence(validation.cue_evidence))
            {
                issues.Add("missing concrete cue evidence");
            }

            if (!HasValidationEvidence(validation.contact_evidence))
            {
                issues.Add("missing contact evidence");
            }

            return issues.Count == 0 ? string.Empty : string.Join("; ", issues);
        }

        private static string FormatValidationIssues(string[] issues)
        {
            if (issues == null || issues.Length == 0)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            for (int i = 0; i < issues.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(issues[i]))
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append("; ");
                }

                builder.Append(issues[i].Trim());
            }

            return builder.ToString();
        }

        private static string BuildShortPreview(string raw)
        {
            raw = string.IsNullOrWhiteSpace(raw) ? string.Empty : raw.Replace('\n', ' ').Replace('\r', ' ').Trim();
            return raw.Length > 220 ? raw.Substring(0, 220) + "..." : raw;
        }

        private bool TryLoadBase64Image(string rawImage, out Texture2D texture, out string error)
        {
            texture = null;
            error = string.Empty;
            var base64 = ExtractBase64ImagePayload(rawImage);

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

        private string SaveMnemonicImageCue(MnemonicItemData item, Texture2D texture, string suffix = null)
        {
            if (texture == null)
            {
                return string.Empty;
            }

            var exportFolder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "ExperimentExports", "GeneratedMnemonicImages", string.IsNullOrWhiteSpace(sessionId) ? "unsaved_session" : sessionId));
            Directory.CreateDirectory(exportFolder);
            var safeSuffix = string.IsNullOrWhiteSpace(suffix) ? "image_cue" : SanitizeIdPrefix(suffix);
            var fileName = SanitizeIdPrefix(item.word) + "_" + safeSuffix + ".png";
            var path = Path.Combine(exportFolder, fileName);
            try
            {
                File.WriteAllBytes(path, texture.EncodeToPNG());
                return path;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Failed to save mnemonic image cue: " + ex.Message);
                return string.Empty;
            }
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
                return "association image cue illustration, no text, no letters, no captions";
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

            item.anchorType = PreGeneratedMnemonicCatalog.NormalizeAnchorType(item.anchorId, item.anchorLabel);
            if (ApplyMeaningFirstMnemonicGuardrails(item))
            {
                item.imageCuePath = string.Empty;
            }
            item.visualCue = AnchorGroundSceneEnglish(item, item.visualCue);
            if (string.IsNullOrWhiteSpace(item.associationPrompt))
            {
                item.associationPrompt = FirstNonEmptyPrompt(
                    ExtractPromptSceneDetail(item.visualCue),
                    ExtractPromptSceneDetail(item.imagePrompt),
                    BuildRecoveredSceneDetail(item));
            }

            item.mnemonic = AnchorGroundMnemonicEnglish(item, item.mnemonic);
            item.imagePrompt = BuildAnchorGroundedImagePrompt(item, item.imagePrompt);
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

        private string BuildAnchorGroundedImagePrompt(MnemonicItemData item, string promptOverride, int layoutVariant = -1)
        {
            var anchor = GetAnchorDisplayName(item);
            var backgroundScene = BuildImageBackgroundScene(item);
            var foregroundFocus = BuildImageForegroundFocus(item, promptOverride);
            var foregroundObjects = BuildImageForegroundObjectList(item, anchor);
            var concreteCueSubject = BuildConcreteCueSubjectPhrase(item, anchor);

            if (string.IsNullOrWhiteSpace(backgroundScene))
            {
                backgroundScene = BuildRecoveredSceneDetail(item);
            }

            if (string.IsNullOrWhiteSpace(backgroundScene))
            {
                backgroundScene = $"a vivid association image cue for {GetMeaningText(item)}";
            }

            if (IsAlreadyAnchorGroundedImagePrompt(promptOverride, anchor)
                && (promptOverride.TrimStart().StartsWith("Single close-up image,", StringComparison.OrdinalIgnoreCase)
                    || promptOverride.IndexOf("large foreground cue objects", StringComparison.OrdinalIgnoreCase) >= 0
                    || promptOverride.IndexOf("room object and cue object together", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return RemoveImagePromptProcessTerms(promptOverride);
            }

            backgroundScene = NormalizeGeneratedImageCueText(ExtractPromptSceneDetail(backgroundScene));
            if (string.IsNullOrWhiteSpace(backgroundScene))
            {
                backgroundScene = $"one concrete association image scene for {item.word}, meaning {GetMeaningText(item)}";
            }

            foregroundFocus = NormalizeGeneratedImageCueText(ExtractPromptSceneDetail(foregroundFocus));
            if (string.IsNullOrWhiteSpace(foregroundFocus))
            {
                foregroundFocus = backgroundScene;
            }

            var subjects = string.IsNullOrWhiteSpace(foregroundObjects)
                ? anchor
                : $"{anchor}, {foregroundObjects}";
            var proxySafety = BuildNatureOutdoorProxyImageSafetyClause(item, anchor, backgroundScene, foregroundFocus);
            var smallCueSafety = BuildSmallCueVisibilityImageSafetyClause(item, backgroundScene, foregroundFocus, foregroundObjects);
            var anchorRequirement = BuildAnchorVisualRequirement(item, anchor);
            var cueRequirement = BuildCueVisualRequirement(item, concreteCueSubject);

            var composition = layoutVariant >= 0
                ? BuildImagePromptCandidateComposition(layoutVariant)
                : "anchor and " + concreteCueSubject + " share a tight foreground frame, clear contact point, simple background.";

            return $"Single close-up image, one continuous scene from one camera view, {subjects}. {composition} The assigned room object and {concreteCueSubject} must be separate, large, sharp, and visible together. {concreteCueSubject} is smaller than the room object but large enough to inspect, positioned beside, on, under, hanging from, attached to, or leaning against the assigned room object according to the cue description. The room object should occupy about 30-45 percent of the image, and {concreteCueSubject} should occupy about 35-55 percent. {anchorRequirement} {cueRequirement} Main visible action or state: {foregroundFocus}. {proxySafety}{smallCueSafety}Scene context for accuracy: {backgroundScene}. Simple background, no readable text, no captions, no logos, no watermark, no split-screen, no collage, no contact sheet.";
        }

        private static string BuildAnchorVisualRequirement(MnemonicItemData item, string anchor)
        {
            var text = ((item?.anchorType ?? string.Empty) + " "
                + (item?.anchorId ?? string.Empty) + " "
                + (item?.anchorLabel ?? string.Empty) + " "
                + (anchor ?? string.Empty)).ToLowerInvariant();

            if (ContainsAny(text, "air_conditioner", "air conditioner", "aircon", "ac unit", "a/c"))
            {
                return "Anchor visual requirement: show a real wall-mounted rectangular air conditioner unit with a visible front grille, horizontal slats, and a loose vent flap or louver. Do not show abstract C-shaped logos, icons, isolated hoses, nozzles, valves, or unrelated machine parts.";
            }

            if (ContainsAny(text, "chair", "seat"))
            {
                return "Anchor visual requirement: show a recognizable chair with seat and backrest visible, not an abstract block or partial furniture fragment.";
            }

            if (ContainsAny(text, "door"))
            {
                return "Anchor visual requirement: show a recognizable door panel with frame or handle visible, not an abstract rectangle or sign.";
            }

            if (ContainsAny(text, "wardrobe", "closet", "armoire"))
            {
                return "Anchor visual requirement: show a tall wardrobe or closet cabinet with doors or shelves visible, not a generic box.";
            }

            if (ContainsAny(text, "bookshelf", "shelf"))
            {
                return "Anchor visual requirement: show a recognizable shelf or bookshelf with horizontal shelves visible.";
            }

            return "Anchor visual requirement: show the assigned room object as a recognizable real object, not an icon, logo, abstract symbol, or unrelated part.";
        }

        private static string BuildCueVisualRequirement(MnemonicItemData item, string concreteCueSubject)
        {
            var text = ((item?.word ?? string.Empty) + " "
                + (item?.meaning ?? string.Empty) + " "
                + (item?.mainCueObject ?? string.Empty) + " "
                + (item?.associationPrompt ?? string.Empty) + " "
                + (item?.imagePrompt ?? string.Empty) + " "
                + (concreteCueSubject ?? string.Empty)).ToLowerInvariant();

            if (ContainsAny(text, "martillo", "hammer"))
            {
                return "Cue visual requirement: show a complete small hammer with a clear head and handle, angled toward the anchor, with the hammer visibly touching the target surface.";
            }

            if (ContainsAny(text, "bottle", "botella"))
            {
                return "Cue visual requirement: show a recognizable bottle silhouette with neck and body visible.";
            }

            if (ContainsAny(text, "curtain", "cortina"))
            {
                return "Cue visual requirement: show fabric curtain folds or draped cloth clearly, not an abstract colored sheet.";
            }

            return string.Empty;
        }

        private string BuildConcreteCueSubjectPhrase(MnemonicItemData item, string anchor)
        {
            var objectName = GetPrimaryConcreteCueObjectName(item, anchor);
            if (!string.IsNullOrWhiteSpace(objectName))
            {
                return AddDefiniteArticle(objectName);
            }

            var meaning = GetMeaningText(item);
            return string.IsNullOrWhiteSpace(meaning)
                ? "the concrete foreground prop"
                : "the concrete foreground prop for " + meaning;
        }

        private string GetPrimaryConcreteCueObjectName(MnemonicItemData item, string anchor)
        {
            if (item == null)
            {
                return string.Empty;
            }

            var mainCueObject = NormalizeImageObjectLabel(item.mainCueObject, anchor);
            if (!string.IsNullOrWhiteSpace(mainCueObject))
            {
                return mainCueObject;
            }

            if (item.visualObjects != null)
            {
                for (int i = 0; i < item.visualObjects.Count; i++)
                {
                    var label = NormalizeImageObjectLabel(item.visualObjects[i]?.label, anchor);
                    if (!string.IsNullOrWhiteSpace(label))
                    {
                        return label;
                    }
                }
            }

            var foregroundObjects = BuildImageForegroundObjectList(item, anchor);
            if (string.IsNullOrWhiteSpace(foregroundObjects))
            {
                return string.Empty;
            }

            var first = foregroundObjects.Split(',')[0].Trim();
            return NormalizeImageObjectLabel(first, anchor);
        }

        private static string AddDefiniteArticle(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var trimmed = text.Trim();
            var lower = trimmed.ToLowerInvariant();
            if (lower.StartsWith("the ", StringComparison.Ordinal)
                || lower.StartsWith("a ", StringComparison.Ordinal)
                || lower.StartsWith("an ", StringComparison.Ordinal))
            {
                return trimmed;
            }

            return "the " + trimmed;
        }

        private static string BuildSmallCueVisibilityImageSafetyClause(MnemonicItemData item, string backgroundScene, string foregroundFocus, string foregroundObjects)
        {
            var text = ((item?.word ?? string.Empty) + " "
                + (item?.meaning ?? string.Empty) + " "
                + (item?.visualCue ?? string.Empty) + " "
                + (item?.associationPrompt ?? string.Empty) + " "
                + (item?.imagePrompt ?? string.Empty) + " "
                + (backgroundScene ?? string.Empty) + " "
                + (foregroundFocus ?? string.Empty) + " "
                + (foregroundObjects ?? string.Empty)).ToLowerInvariant();

            if (!ContainsAny(text,
                    "wallet", "cartera", "purse", "card", "coin", "key", "ring", "ticket", "passport", "stamp",
                    "anillo", "llave", "tarjeta", "moneda", "boleto"))
            {
                return string.Empty;
            }

            return "Small prop visibility requirement: the foreground prop must be fully exposed outside pockets, drawers, cushions, fabric, and covers; keep it visually separate from the room object, high contrast, and large enough to inspect. ";
        }

        private static string BuildNatureOutdoorProxyImageSafetyClause(MnemonicItemData item, string anchor, string backgroundScene, string foregroundFocus)
        {
            var text = ((item?.word ?? string.Empty) + " "
                + (item?.meaning ?? string.Empty) + " "
                + (item?.visualCue ?? string.Empty) + " "
                + (item?.associationPrompt ?? string.Empty) + " "
                + (item?.imagePrompt ?? string.Empty) + " "
                + (backgroundScene ?? string.Empty) + " "
                + (foregroundFocus ?? string.Empty)).ToLowerInvariant();

            if (ContainsAny(text, "cloud", "nube", "rain", "snow", "sky", "storm", "wind"))
            {
                return $"Nature proxy requirement: show a crafted indoor weather proxy at the {anchor}, such as a cotton cloud mobile clipped to the room object with paper raindrops; do not show a real sky or a vague floating cloud. ";
            }

            if (ContainsAny(text, "sun", "moon", "star"))
            {
                return $"Nature proxy requirement: show a crafted indoor sky-object proxy at the {anchor}, such as a felt sun, moon ornament, or hanging star mobile; do not show a real sky. ";
            }

            if (ContainsAny(text, "waterfall", "cascada", "river", "stream", "ocean", "sea", "beach"))
            {
                return $"Nature proxy requirement: show a contained indoor water/landscape proxy at the {anchor}, such as a miniature waterfall model pouring into a bowl or bucket; do not show a real outdoor landscape. ";
            }

            if (ContainsAny(text, "neighborhood", "barrio", "city", "street", "village", "market", "park", "plaza"))
            {
                return $"Outdoor-place proxy requirement: show a tiny diorama or prop cluster at the {anchor}, such as miniature houses and neighbors on a mat; do not show a real street or city view. ";
            }

            return string.Empty;
        }

        private string BuildImageForegroundObjectList(MnemonicItemData item, string anchor)
        {
            if (item == null)
            {
                return string.Empty;
            }

            var labels = new List<string>();
            var mainCueObject = NormalizeImageObjectLabel(item.mainCueObject, anchor);
            if (!string.IsNullOrWhiteSpace(mainCueObject))
            {
                labels.Add(mainCueObject);
            }

            if (item.visualObjects != null)
            {
                for (int i = 0; i < item.visualObjects.Count; i++)
                {
                    var label = NormalizeImageObjectLabel(item.visualObjects[i]?.label, anchor);
                    if (!string.IsNullOrWhiteSpace(label) && !ContainsCaseInsensitive(labels, label))
                    {
                        labels.Add(label);
                    }
                }
            }

            AddKnownForegroundObjects(item.imagePrompt, labels);
            AddKnownForegroundObjects(item.associationPrompt, labels);
            AddKnownForegroundObjects(item.visualCue, labels);
            if (item.imagePromptCandidates != null)
            {
                for (int i = 0; i < item.imagePromptCandidates.Count; i++)
                {
                    AddKnownForegroundObjects(item.imagePromptCandidates[i], labels);
                }
            }

            AddKnownForegroundObjects(item.word, labels);
            AddKnownForegroundObjects(item.meaning, labels);

            return labels.Count == 0 ? string.Empty : string.Join(", ", labels);
        }

        private static string NormalizeImageObjectLabel(string label, string anchor)
        {
            if (string.IsNullOrWhiteSpace(label))
            {
                return string.Empty;
            }

            var cleaned = label.Trim();
            if (ContainsAny(cleaned.ToLowerInvariant(), "anchor"))
            {
                return string.Empty;
            }

            if (TryFindAssociationPlaceholderLeakage(cleaned, out _))
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(anchor)
                && string.Equals(cleaned, anchor.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            return cleaned;
        }

        private static bool TryFindAssociationPlaceholderLeakage(string text, out string leakedTerm)
        {
            leakedTerm = string.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var lower = text.ToLowerInvariant();
            var bannedTerms = new[]
            {
                "concrete cue object",
                "main cue object",
                "target object",
                "anchor object",
                "visible object",
                "foreground cue object",
                "foreground cue",
                "physical object",
                "association image cue object",
                "cue object",
                "concrete foreground object",
                "concrete foreground cue action",
                "assigned anchor",
                "anchor contact point",
                "visible action or state",
                "visible action for",
                "memory cue",
                "symbolic scene",
                "the object",
                "the item",
                "the thing"
            };

            for (int i = 0; i < bannedTerms.Length; i++)
            {
                if (lower.IndexOf(bannedTerms[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    leakedTerm = bannedTerms[i];
                    return true;
                }
            }

            return false;
        }

        private static void AddKnownForegroundObjects(string text, List<string> labels)
        {
            if (string.IsNullOrWhiteSpace(text) || labels == null)
            {
                return;
            }

            var lower = text.ToLowerInvariant();
            AddKnownForegroundObject(lower, labels, "suitcase");
            AddKnownForegroundObject(lower, labels, "passport");
            AddKnownForegroundObject(lower, labels, "wallet");
            AddKnownForegroundObject(lower, labels, "purse");
            AddKnownForegroundObjectWholeToken(lower, labels, "card");
            AddKnownForegroundObject(lower, labels, "cards");
            AddKnownForegroundObjectWholeToken(lower, labels, "coin");
            AddKnownForegroundObject(lower, labels, "coins");
            AddKnownForegroundObjectWholeToken(lower, labels, "key");
            AddKnownForegroundObject(lower, labels, "stamp");
            AddKnownForegroundObject(lower, labels, "airplane");
            AddKnownForegroundObject(lower, labels, "building model");
            AddKnownForegroundObject(lower, labels, "model building");
            AddKnownForegroundObject(lower, labels, "poster");
            AddKnownForegroundObject(lower, labels, "tape");
            AddKnownForegroundObject(lower, labels, "clip");
            AddKnownForegroundObject(lower, labels, "ribbon");
            AddKnownForegroundObjectWholeToken(lower, labels, "hat");
            AddKnownForegroundObject(lower, labels, "campfire");
            AddKnownForegroundObject(lower, labels, "flames");
            if (lower.IndexOf("glass jar", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                AddKnownForegroundObject(lower, labels, "glass jar");
            }
            else
            {
                AddKnownForegroundObject(lower, labels, "jar");
            }
            AddKnownForegroundObject(lower, labels, "bird");
            if (lower.IndexOf("metal cage", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                AddKnownForegroundObject(lower, labels, "metal cage");
            }
            else
            {
                AddKnownForegroundObject(lower, labels, "cage");
            }
            AddKnownForegroundObject(lower, labels, "boarding pass");
            AddKnownForegroundObject(lower, labels, "ticket");
            AddKnownForegroundObject(lower, labels, "luggage tag");
            if (ContainsAny(lower, "cloud", "nube", "rain", "sky", "storm"))
            {
                AddRequiredForegroundObject(labels, "cotton cloud mobile");
                AddRequiredForegroundObject(labels, "paper raindrops");
            }
            if (ContainsAny(lower, "sun"))
            {
                AddRequiredForegroundObject(labels, "felt sun");
            }
            if (ContainsAny(lower, "moon"))
            {
                AddRequiredForegroundObject(labels, "moon ornament");
            }
            if (ContainsAny(lower, "star"))
            {
                AddRequiredForegroundObject(labels, "hanging star mobile");
            }
            if (ContainsAny(lower, "mist", "fog"))
            {
                AddRequiredForegroundObject(labels, "mist jar");
            }
            if (ContainsAny(lower, "waterfall", "cascada", "river", "stream"))
            {
                AddRequiredForegroundObject(labels, "miniature waterfall model");
                AddRequiredForegroundObject(labels, "bucket");
            }
            AddKnownForegroundObject(lower, labels, "bowl");
            if (ContainsAny(lower, "neighborhood", "barrio", "city", "street", "village"))
            {
                AddRequiredForegroundObject(labels, "miniature houses");
                AddRequiredForegroundObject(labels, "neighbors");
                AddRequiredForegroundObject(labels, "doormat");
            }
        }

        private static void AddRequiredForegroundObject(List<string> labels, string label)
        {
            if (labels != null && !string.IsNullOrWhiteSpace(label) && !ContainsCaseInsensitive(labels, label))
            {
                labels.Add(label);
            }
        }

        private static void AddKnownForegroundObject(string lowerText, List<string> labels, string label)
        {
            if (lowerText.IndexOf(label, StringComparison.OrdinalIgnoreCase) >= 0 && !ContainsCaseInsensitive(labels, label))
            {
                labels.Add(label);
            }
        }

        private static void AddKnownForegroundObjectWholeToken(string lowerText, List<string> labels, string label)
        {
            if (ContainsWholeToken(lowerText, label) && !ContainsCaseInsensitive(labels, label))
            {
                labels.Add(label);
            }
        }

        private static bool ContainsWholeToken(string value, string term)
        {
            if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(term))
            {
                return false;
            }

            var startIndex = 0;
            while (startIndex < value.Length)
            {
                var index = value.IndexOf(term, startIndex, StringComparison.OrdinalIgnoreCase);
                if (index < 0)
                {
                    return false;
                }

                var beforeOk = index == 0 || !char.IsLetterOrDigit(value[index - 1]);
                var afterIndex = index + term.Length;
                var afterOk = afterIndex >= value.Length || !char.IsLetterOrDigit(value[afterIndex]);
                if (beforeOk && afterOk)
                {
                    return true;
                }

                startIndex = index + 1;
            }

            return false;
        }

        private static bool ContainsCaseInsensitive(List<string> values, string value)
        {
            if (values == null || string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            for (int i = 0; i < values.Count; i++)
            {
                if (string.Equals(values[i], value, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string NormalizeGeneratedImageCueText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var result = text;
            result = ReplaceCaseInsensitive(result, "airport codes", "airport stamp marks");
            result = ReplaceCaseInsensitive(result, "stamped with airport codes", "covered with airport stamp marks");
            result = ReplaceCaseInsensitive(result, "passport stamped with codes", "passport covered with stamp marks");
            result = ReplaceCaseInsensitive(result, "fluffy white cloud shape", "cotton cloud mobile with paper raindrops");
            result = ReplaceCaseInsensitive(result, "cloud shape", "cotton cloud mobile");
            result = ReplaceCaseInsensitive(result, "floating cloud", "cotton cloud mobile clipped to the room object");
            result = ReplaceCaseInsensitive(result, "cloud floats", "cotton cloud mobile hangs");
            result = ReplaceCaseInsensitive(result, "floats above the backrest", "is clipped to the backrest");
            result = ReplaceCaseInsensitive(result, "floats above the chair", "is clipped to the chair");
            result = ReplaceCaseInsensitive(result, "a waterfall flows", "a miniature waterfall model pours");
            result = ReplaceCaseInsensitive(result, "waterfall flows", "miniature waterfall model pours");
            result = ReplaceCaseInsensitive(result, "dog runs through the neighborhood", "miniature houses and two neighbors sit on a doormat");
            result = ReplaceCaseInsensitive(result, "runs through the neighborhood", "moves between miniature houses on a doormat");
            result = ReplaceCaseInsensitive(result, "a wallet rests securely in the chair's armrest pocket", "an open wallet sits fully visible on the chair armrest with cards showing");
            result = ReplaceCaseInsensitive(result, "wallet rests securely in the chair's armrest pocket", "open wallet sits fully visible on the chair armrest with cards showing");
            result = ReplaceCaseInsensitive(result, "in the chair's armrest pocket", "fully visible on the chair armrest");
            result = ReplaceCaseInsensitive(result, "inside the chair's armrest pocket", "fully visible on the chair armrest");
            result = ReplaceCaseInsensitive(result, "armrest pocket", "armrest surface");
            result = ReplaceCaseInsensitive(result, "drug trafficker's face poster", "neutral poster");
            result = ReplaceCaseInsensitive(result, "poster of a drug trafficker's face", "neutral poster");
            result = ReplaceCaseInsensitive(result, "wanted poster", "neutral poster");
            result = ReplaceCaseInsensitive(result, "mugshot poster", "neutral poster");
            return result;
        }

        private string BuildMnemonicImageNegativePrompt()
        {
            return "text, letters, words, captions, readable writing, logo, icon, symbol, abstract symbol, abstract shape, C-shaped logo, decorative curve, isolated mechanical part, isolated nozzle, isolated valve, unrelated machine part, watermark, signature, blurry, low quality, distorted, extra limbs, split-screen, split screen, collage, contact sheet, grid layout, tiled image, image sequence, storyboard, diptych, triptych, multiple panels, multiple views, side-by-side views, before and after layout, empty room, bare room, furniture only, chair only, table only, chairs and table only, dining set, interior design photo, generic room photo, window only, shelf only, landscape view, room overview, full room, whole room, establishing shot, wide shot, long shot, distant subject, tiny subject, small subject, architectural rendering, background emphasis, real sky, cloudscape, outdoor weather photo, forest waterfall, cliff waterfall, real beach, city street view, outdoor city view, smoke, fog, haze, abstract light-only cue, atmosphere-only glow, vague beam with no object source, abstract atmosphere, cluttered background, sexual content, nudity, pornographic content, casino, gambling, betting, drugs, narcotics, drug trafficking, smoking, alcohol, crime, criminal, mafia, gang, mugshot, wanted poster, weapon, gun, knife, blood, gore, horror, violence";
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

            if (!string.IsNullOrWhiteSpace(item.associationPrompt))
            {
                return item.associationPrompt.Trim();
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

            if (!string.IsNullOrWhiteSpace(item.associationPrompt))
            {
                return $"foreground association scene: {item.associationPrompt.Trim()}";
            }

            if (!string.IsNullOrWhiteSpace(item.visualCue))
            {
                return $"foreground visual cue/action: {item.visualCue.Trim()}";
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

        private static string BuildStableDiffusionErrorStatus(UnityWebRequest request)
        {
            var detail = ExtractStableDiffusionErrorDetail(request);
            if (request == null)
            {
                return "Image generation failed.";
            }

            if (string.IsNullOrWhiteSpace(detail))
            {
                detail = request.error;
            }

            if (string.IsNullOrWhiteSpace(detail))
            {
                detail = "Unknown Stable Diffusion error.";
            }

            return request.responseCode > 0
                ? $"Image generation failed ({request.responseCode}): {detail}"
                : $"Image generation failed: {detail}";
        }

        private static string ExtractStableDiffusionErrorDetail(UnityWebRequest request)
        {
            if (request?.downloadHandler == null)
            {
                return string.Empty;
            }

            var raw = request.downloadHandler.text;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return string.Empty;
            }

            try
            {
                var parsed = JsonUtility.FromJson<StableDiffusionErrorResponse>(raw);
                if (!string.IsNullOrWhiteSpace(parsed?.error))
                {
                    return parsed.error.Trim();
                }

                if (!string.IsNullOrWhiteSpace(parsed?.detail))
                {
                    return parsed.detail.Trim();
                }
            }
            catch
            {
            }

            raw = raw.Replace('\n', ' ').Replace('\r', ' ').Trim();
            return raw.Length > 240 ? raw.Substring(0, 240) + "..." : raw;
        }

        private static bool IsAlreadyAnchorGroundedImagePrompt(string prompt, string anchor)
        {
            if (string.IsNullOrWhiteSpace(prompt) || string.IsNullOrWhiteSpace(anchor))
            {
                return false;
            }

            var normalized = prompt.Trim().ToLowerInvariant();
            var anchorLower = anchor.ToLowerInvariant();
            return (normalized.StartsWith("single close-up image,", StringComparison.Ordinal)
                    || normalized.StartsWith("tight two-subject association image close-up", StringComparison.Ordinal)
                    || normalized.StartsWith("tight two-subject mnemonic close-up", StringComparison.Ordinal)
                    || normalized.StartsWith("((single association image subject))", StringComparison.Ordinal)
                    || normalized.StartsWith("((single mnemonic subject))", StringComparison.Ordinal)
                    || (normalized.Contains("scene to imagine only as background context")
                        && normalized.Contains("clear foreground focus"))
                    || normalized.StartsWith($"simple indoor association image cue illustration staged at the {anchorLower}", StringComparison.Ordinal)
                    || normalized.StartsWith($"association image cue illustration staged at the {anchorLower}", StringComparison.Ordinal)
                    || normalized.StartsWith($"simple indoor mnemonic illustration staged at the {anchorLower}", StringComparison.Ordinal)
                    || normalized.StartsWith($"mnemonic illustration staged at the {anchorLower}", StringComparison.Ordinal)
                    || normalized.StartsWith($"centered on the {anchorLower}", StringComparison.Ordinal))
                && (normalized.Contains("no text") || normalized.Contains("no readable text"));
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

            return "the target meaning";
        }

        private string GetDisplayMeaningText(MnemonicItemData item)
        {
            if (item == null)
            {
                return "the target meaning";
            }

            var english = item.meaning?.Trim();

            return string.IsNullOrWhiteSpace(english) ? "the target meaning" : english;
        }

        private bool ApplyMeaningFirstMnemonicGuardrails(MnemonicItemData item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.word))
            {
                return false;
            }

            if (ApplyAcademicSafetyGuardrails(item))
            {
                RefreshAssociationPromptsAfterCueRewrite(item);
                return true;
            }

            if (ApplyHiddenCueVisibilityGuardrails(item))
            {
                RefreshAssociationPromptsAfterCueRewrite(item);
                return true;
            }

            if (ApplyTargetObjectDisplayGuardrails(item))
            {
                RefreshAssociationPromptsAfterCueRewrite(item);
                return true;
            }

            if (ApplyWeakMnemonicHookGuardrails(item))
            {
                return true;
            }

            if (ApplyDeadCueStoryGuardrails(item))
            {
                return true;
            }

            if (string.Equals(item.word.Trim(), "iconoclast", StringComparison.OrdinalIgnoreCase)
                && IsWeakIconoclastMnemonic(item))
            {
                var anchor = GetAnchorDisplayName(item);
                item.visualCue = $"At the {anchor}, a small figure chips a cherished ceramic idol with a tiny hammer.";
                item.mnemonic = $"At the {anchor}, the little figure hesitates, then taps the idol until a crack appears. The challenge to cherished beliefs makes iconoclast feel like the name for that defiant person.";
                SetMnemonicJudgeState(item, "STORY_ONLY", false, 0, "Runtime fallback uses a story-only mnemonic.", string.Empty);
                item.imagePrompt = $"Close-up indoor association image cue at the {anchor}: a small figure uses a tiny hammer to chip a cherished ceramic idol, with cracked fragments visible in the foreground. Keep the {anchor} visible in the background, no text, no letters, no captions, no logos, no watermark.";
                RefreshAssociationPromptsAfterCueRewrite(item);
                return true;
            }

            if (IsKnownWeakGeneratedMnemonic(item)
                && ApplyDistinctFallbackMnemonic(item))
            {
                RefreshAssociationPromptsAfterCueRewrite(item);
                return true;
            }

            return false;
        }

        private bool ApplyTargetObjectDisplayGuardrails(MnemonicItemData item)
        {
            return item != null
                   && ContainsTargetObjectDisplayFailure(item)
                   && ApplyTargetMeaningEventFallback(item);
        }

        private bool ApplyWeakMnemonicHookGuardrails(MnemonicItemData item)
        {
            if (condition == ExperimentCondition.SelfGenerated)
            {
                return false;
            }

            if (item == null || string.IsNullOrWhiteSpace(item.mnemonic))
            {
                return false;
            }

            var hasJudge = !string.IsNullOrWhiteSpace(item.mnemonicMode)
                           || item.hookScore > 0
                           || !string.IsNullOrWhiteSpace(item.hookReason)
                           || !string.IsNullOrWhiteSpace(item.mnemonicHook);
            var rejectedByJudge = hasJudge
                                  && (!item.hookAccepted
                                      || item.hookScore < 7
                                      || string.Equals(item.mnemonicMode, "STORY_ONLY", StringComparison.OrdinalIgnoreCase));
            if (rejectedByJudge && ContainsRejectedHookLanguage(item.mnemonic))
            {
                item.mnemonic = BuildMnemonicLinkFallback(item);
                SetMnemonicJudgeState(
                    item,
                    "STORY_ONLY",
                    false,
                    Mathf.Clamp(item.hookScore, 0, 6),
                    string.IsNullOrWhiteSpace(item.hookReason)
                        ? "Rejected weak hook was removed from the learner-facing mnemonic."
                        : item.hookReason,
                    item.mnemonicHook);
                return true;
            }

            if (ContainsSpellingOnlyHookLanguage(item.mnemonic))
            {
                item.mnemonic = BuildMnemonicLinkFallback(item);
                SetMnemonicJudgeState(
                    item,
                    "STORY_ONLY",
                    false,
                    Mathf.Clamp(item.hookScore, 0, 6),
                    "Spelling-only hook was removed because the story-only mnemonic is cleaner.",
                    item.mnemonicHook);
                return true;
            }

            return false;
        }

        private static bool ContainsRejectedHookLanguage(string mnemonic)
        {
            return ContainsAny(mnemonic,
                "shares",
                "same letters",
                "few letters",
                "letters with",
                "looks like",
                "resembles",
                "sounds like",
                "sounds similar",
                "sound-alike",
                "spelling",
                "syllable",
                "links to",
                "is associated with",
                "helps remember");
        }

        private static bool ContainsSpellingOnlyHookLanguage(string mnemonic)
        {
            return ContainsAny(mnemonic,
                "shares letters",
                "shared letters",
                "same letters",
                "few letters",
                "letters with",
                "spelling overlap",
                "starts with the same",
                "ends with the same");
        }

        private static void SetMnemonicJudgeState(
            MnemonicItemData item,
            string mode,
            bool accepted,
            int score,
            string reason,
            string hook)
        {
            if (item == null)
            {
                return;
            }

            item.mnemonicMode = string.IsNullOrWhiteSpace(mode) ? "STORY_ONLY" : mode.Trim();
            item.hookAccepted = accepted;
            item.hookScore = Mathf.Clamp(score, 0, 10);
            item.hookReason = reason ?? string.Empty;
            item.mnemonicHook = hook ?? string.Empty;
            item.storyCue = string.Empty;
        }

        private static bool ContainsTargetObjectDisplayFailure(MnemonicItemData item)
        {
            var text = BuildMnemonicSearchText(item);
            return ContainsAny(text,
                "model sits",
                "model is displayed",
                "model displayed",
                "miniature airport model",
                "airport model",
                "hallway model",
                "narrow hallway model",
                "sand dune is balanced",
                "large sand dune",
                "perched on",
                "balanced on the chair",
                "balanced on the chair's",
                "displayed on the air conditioner",
                "sits on the cabinet shelf");
        }

        private bool ApplyTargetMeaningEventFallback(MnemonicItemData item)
        {
            var key = ((item.word ?? string.Empty) + " " + (item.meaning ?? string.Empty)).ToLowerInvariant();
            if (ContainsAny(key, "aeropuerto", "airport"))
            {
                ApplyAirportEventCue(item);
                return true;
            }

            if (ContainsAny(key, "pasillo", "hallway", "corridor"))
            {
                ApplyHallwayEventCue(item);
                return true;
            }

            if (ContainsAny(key, "desierto", "desert"))
            {
                ApplyDesertEventCue(item);
                return true;
            }

            if (ContainsAny(key, "fogata", "fuego", "campfire", "fire"))
            {
                ApplySafeFireEventCue(item);
                return true;
            }

            return false;
        }

        private void ApplyAirportEventCue(MnemonicItemData item)
        {
            var anchor = GetAnchorDisplayName(item);
            var contact = GetVisibleWalletContactPoint(anchor);
            item.visualCue = $"At the {anchor}, an open security tray on {contact} holds shoes, a passport, boarding pass, and luggage tag.";
            item.associationPrompt = $"open security tray holds shoes passport boarding pass and luggage tag on {contact}";
            item.mnemonic = $"Aeropuerto can become air-port: aero feels like air, and puerto feels like a port of departure.\n\nAt the {anchor}, the security tray feels like the start of a trip, with shoes off, papers ready, and airport air rushing around it.";
            SetMnemonicJudgeState(item, "HOOK_PLUS_STORY", true, 8, "Air-port is a clear phrase-like hook that supports airport retrieval.", "air-port");
            item.imagePrompt = item.associationPrompt;
            item.visualObjects = new List<VisualObjectSpec>
            {
                new() { label = "security tray", primitiveShape = "Cube", colorHex = "#8D99AE", localPosition = new Vector3(0f, 0.30f, 0f), scale = new Vector3(0.46f, 0.08f, 0.32f), effect = "airport meaning event" },
                new() { label = "shoes", primitiveShape = "Cube", colorHex = "#5C4033", localPosition = new Vector3(-0.12f, 0.38f, 0.02f), scale = new Vector3(0.16f, 0.06f, 0.10f), effect = "security-check cue" },
                new() { label = "passport", primitiveShape = "Cube", colorHex = "#1D3557", localPosition = new Vector3(0.07f, 0.39f, 0.01f), scale = new Vector3(0.12f, 0.03f, 0.16f), effect = "travel document cue" },
                new() { label = "boarding pass", primitiveShape = "Cube", colorHex = "#F1FAEE", localPosition = new Vector3(0.16f, 0.40f, -0.03f), scale = new Vector3(0.18f, 0.02f, 0.08f), effect = "airport document cue" }
            };
        }

        private void ApplyHallwayEventCue(MnemonicItemData item)
        {
            var anchor = GetAnchorDisplayName(item);
            var contact = GetVisibleWalletContactPoint(anchor);
            item.visualCue = $"At the {anchor}, two upright books on {contact} squeeze a narrow strip into a tiny passage with a toy door.";
            item.associationPrompt = $"two upright books squeeze a narrow passage with a toy door on {contact}";
            item.mnemonic = $"At the {anchor}, the tiny passage seems just wide enough for one careful step. As you imagine passing through the narrow hallway, pasillo becomes the name attached to that moment.";
            SetMnemonicJudgeState(item, "STORY_ONLY", false, 5, "The pas/passing hook is weak by itself, so the mnemonic stays story-only.", "pas -> passing");
            item.imagePrompt = item.associationPrompt;
            item.visualObjects = new List<VisualObjectSpec>
            {
                new() { label = "upright books", primitiveShape = "Cube", colorHex = "#457B9D", localPosition = new Vector3(-0.12f, 0.40f, 0f), scale = new Vector3(0.08f, 0.34f, 0.18f), effect = "passage wall cue" },
                new() { label = "narrow passage strip", primitiveShape = "Cube", colorHex = "#DDBEA9", localPosition = new Vector3(0f, 0.32f, 0f), scale = new Vector3(0.30f, 0.03f, 0.08f), effect = "hallway meaning event" },
                new() { label = "toy door", primitiveShape = "Cube", colorHex = "#A0522D", localPosition = new Vector3(0.16f, 0.40f, 0f), scale = new Vector3(0.07f, 0.18f, 0.03f), effect = "passage endpoint cue" }
            };
        }

        private void ApplyDesertEventCue(MnemonicItemData item)
        {
            var anchor = GetAnchorDisplayName(item);
            var contact = GetVisibleWalletContactPoint(anchor);
            item.visualCue = $"At the {anchor}, dry sand spills from a small pouch and piles into a low dune across {contact}.";
            item.associationPrompt = $"dry sand spills from pouch into low dune across {contact}";
            item.mnemonic = $"At the {anchor}, sand keeps slipping out until the little dune feels warm, dry, and impossible to ignore. The quiet empty scene lets desierto settle onto desert.";
            SetMnemonicJudgeState(item, "STORY_ONLY", false, 4, "The des/desert overlap is only a weak spelling cue.", "des -> desert");
            item.imagePrompt = item.associationPrompt;
            item.visualObjects = new List<VisualObjectSpec>
            {
                new() { label = "sand pouch", primitiveShape = "Cube", colorHex = "#B08968", localPosition = new Vector3(-0.14f, 0.42f, 0f), scale = new Vector3(0.16f, 0.10f, 0.10f), effect = "sand source cue" },
                new() { label = "spilling sand", primitiveShape = "Cylinder", colorHex = "#D6AD60", localPosition = new Vector3(0f, 0.36f, 0f), scale = new Vector3(0.10f, 0.08f, 0.10f), effect = "desert action cue" },
                new() { label = "low sand dune", primitiveShape = "Sphere", colorHex = "#C9A66B", localPosition = new Vector3(0.12f, 0.32f, 0f), scale = new Vector3(0.24f, 0.08f, 0.18f), effect = "desert meaning event" }
            };
        }

        private void ApplySafeFireEventCue(MnemonicItemData item)
        {
            var anchor = GetAnchorDisplayName(item);
            var contact = anchor.IndexOf("door", StringComparison.OrdinalIgnoreCase) >= 0
                ? "the door handle"
                : GetVisibleWalletContactPoint(anchor);
            var spanish = string.IsNullOrWhiteSpace(item.word) ? "the Spanish word" : item.word.Trim();
            item.visualCue = $"At the {anchor}, a safe electric flame lantern hangs from {contact}, glowing like a compact campfire.";
            item.associationPrompt = $"safe electric flame lantern hangs from {contact} like compact campfire";
            item.mnemonic = $"At the {anchor}, the lantern flickers softly, making the room feel like a tiny campsite without real danger. As you imagine warming your hands near it, {spanish} becomes the name attached to fire.";
            SetMnemonicJudgeState(item, "STORY_ONLY", false, 0, "Runtime fire fallback uses a story-only mnemonic.", string.Empty);
            item.imagePrompt = item.associationPrompt;
            item.visualObjects = new List<VisualObjectSpec>
            {
                new() { label = "electric flame lantern", primitiveShape = "Cylinder", colorHex = "#F4A261", localPosition = new Vector3(0f, 0.42f, 0f), scale = new Vector3(0.18f, 0.22f, 0.18f), effect = "safe fire meaning event" },
                new() { label = "flame glow", primitiveShape = "Sphere", colorHex = "#E76F51", localPosition = new Vector3(0f, 0.45f, 0f), scale = new Vector3(0.12f, 0.12f, 0.12f), effect = "campfire cue" }
            };
        }

        private bool ApplyDeadCueStoryGuardrails(MnemonicItemData item)
        {
            if (item == null || !ContainsDeadCueStoryPhrase(item.mnemonic))
            {
                return false;
            }

            item.mnemonic = BuildMnemonicLinkFallback(item);
            SetMnemonicJudgeState(item, "STORY_ONLY", false, 0, "Removed meta mnemonic wording and replaced it with story-only text.", string.Empty);
            return true;
        }

        private static bool ContainsDeadCueStoryPhrase(string mnemonic)
        {
            return ContainsAny(mnemonic,
                "visible cue retrieves",
                "visible cue",
                "points to",
                "retrieves",
                "repeat the word",
                "repeat ",
                "mentally replaying",
                "same scene",
                "bind syllables",
                "action rhythm",
                "represents the meaning",
                "symbolizes",
                "embodies",
                "shows the word");
        }

        private string BuildLearnerFriendlyCueStoryFallback(MnemonicItemData item)
        {
            return BuildMnemonicLinkFallback(item);
        }

        private string BuildMnemonicLinkFallback(MnemonicItemData item)
        {
            var spanish = string.IsNullOrWhiteSpace(item?.word) ? "the Spanish word" : item.word.Trim();
            var meaning = GetMeaningText(item);
            var anchor = GetAnchorDisplayName(item);
            var lower = spanish.ToLowerInvariant();

            if (lower == "aeropuerto")
            {
                return $"At the {anchor}, the travel cue feels like an air-port moment: papers, luggage, and moving air all point toward departure. That busy airport scene gives aeropuerto a place to land.";
            }

            if (lower == "pasillo")
            {
                return $"At the {anchor}, the narrow passage squeezes forward just enough for one careful step. As you imagine passing through it, pasillo settles onto hallway.";
            }

            if (lower == "cartera")
            {
                return $"At the {anchor}, an open wallet shows cards and coins spilling into view. The everyday wallet moment fixes cartera to wallet.";
            }

            if (lower == "cartel")
            {
                return $"At the {anchor}, a bright poster is taped flat where your eyes cannot miss it. The posted display makes cartel feel like the name for poster.";
            }

            var detail = BuildCueStoryEventDetail(item);
            return $"At the {anchor}, {detail} becomes the focus of a small {meaning} moment. As you picture it clearly, {spanish} settles onto that meaning.";
        }

        private static string BuildCueStoryEventDetail(MnemonicItemData item)
        {
            var detail = ExtractPromptSceneDetail(FirstNonEmptyPrompt(item?.associationPrompt, item?.visualCue, item?.imagePrompt));
            if (string.IsNullOrWhiteSpace(detail))
            {
                return "the memory cue";
            }

            detail = detail.Trim().TrimEnd('.', ';', ',');
            if (detail.StartsWith("At the ", StringComparison.OrdinalIgnoreCase))
            {
                var commaIndex = detail.IndexOf(',');
                if (commaIndex >= 0 && commaIndex + 1 < detail.Length)
                {
                    detail = detail.Substring(commaIndex + 1).Trim();
                }
            }

            if (detail.Length > 96)
            {
                detail = detail.Substring(0, 96).TrimEnd(' ', ',', ';');
            }

            return string.IsNullOrWhiteSpace(detail)
                ? "the memory cue"
                : char.ToLowerInvariant(detail[0]) + detail.Substring(1);
        }

        private static void RefreshAssociationPromptsAfterCueRewrite(MnemonicItemData item)
        {
            if (item == null)
            {
                return;
            }

            item.associationPrompt = FirstNonEmptyPrompt(
                ExtractPromptSceneDetail(item.associationPrompt),
                ExtractPromptSceneDetail(item.imagePrompt),
                ExtractPromptSceneDetail(item.visualCue));
            item.imagePromptCandidates = new List<string>();
            item.selectedImagePrompt = string.Empty;
            item.selectedImageCandidateIndex = -1;
            item.imageSelectionReason = string.Empty;
        }

        private bool ApplyHiddenCueVisibilityGuardrails(MnemonicItemData item)
        {
            if (item == null || !ContainsHiddenCueFailure(item))
            {
                return false;
            }

            var text = BuildMnemonicSearchText(item);
            if (ContainsAny(text, "wallet", "cartera", "財布"))
            {
                ApplyVisibleWalletMnemonic(item);
                return true;
            }

            return false;
        }

        private void ApplyVisibleWalletMnemonic(MnemonicItemData item)
        {
            var anchor = GetAnchorDisplayName(item);
            var contact = GetVisibleWalletContactPoint(anchor);
            var spanish = string.IsNullOrWhiteSpace(item.word) ? "cartera" : item.word.Trim();

            item.visualCue = $"At the {anchor}, an open wallet sits clearly on {contact} with cards and coins visible.";
            item.associationPrompt = $"open wallet clearly visible on {contact}, cards and coins visible";
            item.mnemonic = $"At the {anchor}, an open wallet shows cards and coins spilling into view. The everyday wallet moment fixes {spanish} to wallet.";
            SetMnemonicJudgeState(item, "STORY_ONLY", false, 5, "Carte/card is not strong enough to require a separate hook paragraph.", "carte -> card");
            item.imagePrompt = $"open wallet clearly visible on {contact}, cards and coins visible";
            item.visualObjects = new List<VisualObjectSpec>
            {
                new()
                {
                    label = "open wallet",
                    primitiveShape = "Cube",
                    colorHex = "#6B4F3F",
                    localPosition = new Vector3(0f, 0.32f, 0f),
                    scale = new Vector3(0.34f, 0.10f, 0.22f),
                    effect = "meaning cue"
                },
                new()
                {
                    label = "cards",
                    primitiveShape = "Cube",
                    colorHex = "#F2F2E8",
                    localPosition = new Vector3(0.05f, 0.38f, 0.02f),
                    scale = new Vector3(0.18f, 0.03f, 0.10f),
                    effect = "visible contents"
                },
                new()
                {
                    label = "coins",
                    primitiveShape = "Cylinder",
                    colorHex = "#D4AF37",
                    localPosition = new Vector3(-0.08f, 0.39f, 0.02f),
                    scale = new Vector3(0.08f, 0.03f, 0.08f),
                    effect = "visible contents"
                }
            };
        }

        private static bool ContainsHiddenCueFailure(MnemonicItemData item)
        {
            var text = BuildMnemonicSearchText(item);
            return ContainsAny(text,
                "armrest pocket",
                "in a pocket",
                "in the pocket",
                "inside a pocket",
                "inside the pocket",
                "inside a drawer",
                "inside the drawer",
                "tucked away",
                "tucked into",
                "hidden inside",
                "hidden under",
                "covered by",
                "covered with",
                "stored securely",
                "rests securely in",
                "blended into",
                "embedded in",
                "埋もれ",
                "隠れ",
                "ポケットに",
                "引き出しの中");
        }

        private static string GetVisibleWalletContactPoint(string anchor)
        {
            var normalized = string.IsNullOrWhiteSpace(anchor) ? string.Empty : anchor.Trim().ToLowerInvariant();
            if (normalized.Contains("chair"))
            {
                return "the chair armrest";
            }

            if (normalized.Contains("desk"))
            {
                return "the desk edge";
            }

            if (normalized.Contains("table"))
            {
                return "the tabletop";
            }

            if (normalized.Contains("door"))
            {
                return "the floor directly in front of the door";
            }

            if (normalized.Contains("wardrobe") || normalized.Contains("closet"))
            {
                return "the wardrobe shelf edge";
            }

            if (normalized.Contains("shelf"))
            {
                return "the shelf edge";
            }

            return "the room object surface";
        }

        private bool ApplyAcademicSafetyGuardrails(MnemonicItemData item)
        {
            if (item == null || !ContainsParticipantUnsafeCue(item))
            {
                return false;
            }

            var text = BuildMnemonicSearchText(item);
            var meaning = (item.meaning ?? string.Empty).Trim().ToLowerInvariant();
            var word = (item.word ?? string.Empty).Trim().ToLowerInvariant();
            if (word == "cartel"
                || meaning.Contains("poster")
                || text.Contains("ポスター"))
            {
                ApplySafePosterMnemonic(item);
                return true;
            }

            ApplyGenericAcademicSafetyFallback(item);
            return true;
        }

        private void ApplySafePosterMnemonic(MnemonicItemData item)
        {
            var anchor = GetAnchorDisplayName(item);
            var contact = GetSafePosterContactPoint(anchor);

            item.visualCue = $"At the {anchor}, a neutral poster is fastened to {contact} with bright tape.";
            item.associationPrompt = $"neutral poster fastened with bright tape to {contact}";
            item.mnemonic = $"At the {anchor}, the taped poster looks freshly put up, bright enough to pull your attention but with no readable words. As you notice it, {item.word.Trim()} becomes the poster's name.";
            SetMnemonicJudgeState(item, "STORY_ONLY", false, 4, "The cart/card-like sound is not strong enough for a hook paragraph.", "cart/card-like poster");
            item.imagePrompt = $"neutral poster fastened with bright tape to {contact}";
            item.visualObjects = new List<VisualObjectSpec>
            {
                new()
                {
                    label = "neutral poster",
                    primitiveShape = "Cube",
                    colorHex = "#F4F1E8",
                    localPosition = new Vector3(0f, 0.32f, 0f),
                    scale = new Vector3(0.34f, 0.48f, 0.02f),
                    effect = "meaning cue"
                },
                new()
                {
                    label = "bright tape",
                    primitiveShape = "Cube",
                    colorHex = "#E9C46A",
                    localPosition = new Vector3(0f, 0.58f, 0.01f),
                    scale = new Vector3(0.32f, 0.04f, 0.02f),
                    effect = "attachment detail"
                }
            };
        }

        private void ApplyGenericAcademicSafetyFallback(MnemonicItemData item)
        {
            var anchor = GetAnchorDisplayName(item);
            var meaning = GetMeaningText(item);
            item.visualCue = $"At the {anchor}, a neutral study-safe prop for {meaning} is fastened with colored tape.";
            item.associationPrompt = $"neutral study-safe prop for {meaning} fastened with colored tape at the {anchor}";
            item.mnemonic = $"At the {anchor}, the taped prop feels deliberately placed for you to notice. Let the moment expand until {item.word.Trim()} becomes the name attached to {meaning}.";
            SetMnemonicJudgeState(item, "STORY_ONLY", false, 0, "Runtime safety fallback uses a story-only mnemonic.", string.Empty);
            item.imagePrompt = $"neutral study-safe prop for {meaning} with colored tape";
            item.visualObjects = new List<VisualObjectSpec>
            {
                new()
                {
                    label = "neutral study prop",
                    primitiveShape = "Cube",
                    colorHex = "#8ECAE6",
                    localPosition = new Vector3(0f, 0.28f, 0f),
                    scale = new Vector3(0.32f, 0.32f, 0.32f),
                    effect = "safe meaning cue"
                },
                new()
                {
                    label = "colored tape",
                    primitiveShape = "Cube",
                    colorHex = "#F4A261",
                    localPosition = new Vector3(0.18f, 0.42f, 0.01f),
                    scale = new Vector3(0.22f, 0.04f, 0.02f),
                    effect = "safe detail"
                }
            };
        }

        private static string GetSafePosterContactPoint(string anchor)
        {
            var normalized = string.IsNullOrWhiteSpace(anchor) ? string.Empty : anchor.Trim().ToLowerInvariant();
            if (normalized.Contains("door"))
            {
                return "the door frame";
            }

            if (normalized.Contains("wardrobe") || normalized.Contains("closet"))
            {
                return "the wardrobe door";
            }

            if (normalized.Contains("chair"))
            {
                return "the chair backrest";
            }

            if (normalized.Contains("desk"))
            {
                return "the desk edge";
            }

            if (normalized.Contains("table"))
            {
                return "the table edge";
            }

            if (normalized.Contains("shelf"))
            {
                return "the shelf edge";
            }

            if (normalized.Contains("air conditioner"))
            {
                return "the wall just below the air conditioner";
            }

            return "the room object";
        }

        private static bool ContainsParticipantUnsafeCue(MnemonicItemData item)
        {
            var text = BuildMnemonicSearchText(item);
            return ContainsAny(text,
                "drug",
                "drug trafficker",
                "narcotic",
                "cocaine",
                "heroin",
                "marijuana",
                "casino",
                "gambling",
                "betting",
                "porn",
                "sexual",
                "nude",
                "nudity",
                "crime cartel",
                "mafia",
                "gang",
                "criminal",
                "crime",
                "wanted poster",
                "mugshot",
                "weapon",
                "gun",
                "knife",
                "blood",
                "gore",
                "horror",
                "violence",
                "violent",
                "abuse",
                "self-harm",
                "薬物",
                "麻薬",
                "ドラッグ",
                "賭博",
                "カジノ",
                "性的",
                "ポルノ",
                "犯罪",
                "マフィア",
                "ギャング",
                "暴力",
                "血",
                "ホラー",
                "銃",
                "刃物");
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
                + (item.associationPrompt ?? string.Empty) + " "
                + (item.imagePrompt ?? string.Empty) + " "
                + (item.imagePromptCandidates == null ? string.Empty : string.Join(" ", item.imagePromptCandidates.ToArray()))).ToLowerInvariant();
        }

        private string BuildRecoveredSceneDetail(MnemonicItemData item)
        {
            if (item == null)
            {
                return string.Empty;
            }

            var candidates = new[]
            {
                item.associationPrompt,
                item.imagePrompt,
                item.visualCue
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
                if (prefix.StartsWith("Simple indoor association image cue illustration", StringComparison.OrdinalIgnoreCase)
                    || prefix.StartsWith("Association image cue illustration", StringComparison.OrdinalIgnoreCase)
                    || prefix.StartsWith("Tight two-subject association image close-up", StringComparison.OrdinalIgnoreCase)
                    || prefix.StartsWith("Close-up indoor association image cue", StringComparison.OrdinalIgnoreCase)
                    || prefix.StartsWith("Simple indoor mnemonic illustration", StringComparison.OrdinalIgnoreCase)
                    || prefix.StartsWith("Simple indoor anchor-cue mnemonic illustration", StringComparison.OrdinalIgnoreCase)
                    || prefix.StartsWith("Mnemonic illustration", StringComparison.OrdinalIgnoreCase)
                    || prefix.StartsWith("Tight two-subject mnemonic close-up", StringComparison.OrdinalIgnoreCase)
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
                ". Show this specific association image scene:",
                ". Show this specific mnemonic scene:",
                ". Soft natural room lighting",
                ", soft natural room lighting",
                ", close-up composition",
                ", one clear everyday association image detail",
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
                return $"At the {anchor}, imagine a vivid association image scene.";
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
                $"The {anchor} is the memory location;"
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
                llmModel = ResolveLlmModelLabelForExport(),
                llmStatus = GetLlmStatusText(),
                preGeneratedMnemonicCount = preGeneratedMnemonicHitCount,
                liveGeneratedMnemonicCount = liveGeneratedMnemonicCount,
                localFallbackMnemonicCount = localFallbackMnemonicCount,
                usedLocalFallback = usedLocalFallbackForCurrentSession,
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
                    anchorId = item.anchorId,
                    anchorType = item.anchorType,
                    mnemonicSource = item.mnemonicSource,
                    cue = item.visualCue,
                    mainCueObject = item.mainCueObject,
                    associationPrompt = item.associationPrompt,
                    mnemonic = item.mnemonic,
                    mnemonicMode = item.mnemonicMode,
                    hookAccepted = item.hookAccepted,
                    hookScore = item.hookScore,
                    hookReason = item.hookReason,
                    mnemonicHook = item.mnemonicHook,
                    storyCue = item.storyCue,
                    imagePrompt = item.imagePrompt,
                    imagePromptCandidates = item.imagePromptCandidates == null ? new List<string>() : new List<string>(item.imagePromptCandidates),
                    cueBlueprint = item.cueBlueprint,
                    selectedImagePrompt = item.selectedImagePrompt,
                    selectedImageCandidateIndex = item.selectedImageCandidateIndex,
                    imageSelectionReason = item.imageSelectionReason,
                    imageCuePath = item.imageCuePath,
                    imageCueResults = BuildImageCueResultExports(item),
                    visualObjects = item.visualObjects == null ? new List<VisualObjectSpec>() : new List<VisualObjectSpec>(item.visualObjects)
                });
            }

            return export;
        }

        private List<ImageCueResultExport> BuildImageCueResultExports(MnemonicItemData item)
        {
            var exports = new List<ImageCueResultExport>();
            if (item == null
                || string.IsNullOrWhiteSpace(item.word)
                || !imageCueCandidateResults.TryGetValue(item.word, out var results)
                || results == null)
            {
                return exports;
            }

            for (int i = 0; i < results.Count; i++)
            {
                var result = results[i];
                if (result == null)
                {
                    continue;
                }

                var resultExport = new ImageCueResultExport
                {
                    listIndex = result.listIndex,
                    resultLabel = result.label,
                    selectedInnerCandidateIndex = result.candidateIndex,
                    selectedInnerLabel = result.innerLabel,
                    selectedRawPrompt = result.rawPrompt,
                    selectedFullPrompt = result.fullPrompt,
                    selectedImagePath = i == GetDisplayedImageCueCandidateIndex(item.word) ? item.imageCuePath : GetSourceImagePath(result),
                    score = result.score,
                    pass = result.pass,
                    validationComplete = result.validationComplete,
                    reason = result.reason,
                    innerCandidates = new List<ImageCueInnerCandidateExport>()
                };

                if (result.innerCandidates != null)
                {
                    for (int j = 0; j < result.innerCandidates.Count; j++)
                    {
                        var inner = result.innerCandidates[j];
                        if (inner == null)
                        {
                            continue;
                        }

                        resultExport.innerCandidates.Add(new ImageCueInnerCandidateExport
                        {
                            index = inner.index,
                            label = inner.label,
                            rawPrompt = inner.rawPrompt,
                            fullPrompt = inner.fullPrompt,
                            imagePath = inner.imagePath,
                            score = inner.score,
                            pass = inner.pass,
                            validationComplete = inner.validationComplete,
                            reason = inner.reason,
                            validation = ConvertImageCueValidation(inner.validation)
                        });
                    }
                }

                exports.Add(resultExport);
            }

            return exports;
        }

        private static ImageCueValidationExport ConvertImageCueValidation(ImageCueValidationResult validation)
        {
            if (validation == null)
            {
                return null;
            }

            var result = new ImageCueValidationExport
            {
                pass = validation.pass,
                anchorVisible = validation.anchor_visible,
                cueVisible = validation.cue_visible,
                focusOk = validation.focus_ok,
                meaningSpecific = validation.meaning_specific,
                foregroundClear = validation.foreground_clear,
                anchorInteraction = validation.anchor_interaction,
                simpleScene = validation.simple_scene,
                familiarObjects = validation.familiar_objects,
                novelPossibleRelation = validation.novel_possible_relation,
                noRoomOverview = validation.no_room_overview,
                singleContinuousImage = validation.single_continuous_image,
                noSplitScreenOrCollage = validation.no_split_screen_or_collage,
                abstractOrIconic = validation.abstract_or_iconic,
                caption = validation.caption,
                reason = validation.reason,
                anchorEvidence = validation.anchor_evidence,
                cueEvidence = validation.cue_evidence,
                contactEvidence = validation.contact_evidence,
                missingOrWrong = new List<string>()
            };

            if (validation.missing_or_wrong != null)
            {
                for (int i = 0; i < validation.missing_or_wrong.Length; i++)
                {
                    if (!string.IsNullOrWhiteSpace(validation.missing_or_wrong[i]))
                    {
                        result.missingOrWrong.Add(validation.missing_or_wrong[i].Trim());
                    }
                }
            }

            return result;
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
            usedPreGeneratedForCurrentSession = false;
            usedLocalFallbackForCurrentSession = false;
            preGeneratedMnemonicHitCount = 0;
            liveGeneratedMnemonicCount = 0;
            localFallbackMnemonicCount = 0;
            liveMnemonicProviderLabelForCurrentSession = string.Empty;
            liveMnemonicModelForCurrentSession = string.Empty;
            liveMnemonicSourceForCurrentSession = string.Empty;
            flashOverlayAlpha = 0f;
            teleportOverlayAlpha = 0f;
            currentRecognitionTargetWord = string.Empty;
            recognitionFeedback = string.Empty;
            imageGenerationStatus = string.Empty;
            preStudyImageCueStatus = string.Empty;
            isPreparingImageCuesBeforeStudy = false;
            preStudyImageCueFailureCount = 0;
            preStudyImageCueCoroutine = null;
            lastJsonExportPath = string.Empty;
            lastCsvExportPath = string.Empty;
            generatingImageCueWords.Clear();
            regeneratingMnemonicWords.Clear();
            imageCueValidationFailures.Clear();

            foreach (var snapshot in memorySnapshots.Values)
            {
                if (snapshot != null)
                {
                    Destroy(snapshot);
                }
            }
            memorySnapshots.Clear();

            ClearAllGeneratedImageCueTextures();

            if (roomRoot != null)
            {
                Destroy(roomRoot.gameObject);
                roomRoot = null;
            }
        }

        private void ClearStudyRoom()
        {
            ClearVrStudyRuntime();
            studyItemTargets.Clear();

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
            vrPreviewHeaderText = null;
            vrPreviewImage = null;
            vrPreviewInfoText = null;
            vrGenerateButton = null;
            vrPreviousImageButton = null;
            vrNextImageButton = null;
            vrCaptureButton = null;
            vrAdvanceButton = null;
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
            var selectableSets = GetSelectableWordSets();
            WordSetDefinition pool = null;
            if (selectableSets.Count > 0)
            {
                var selected = selectableSets[Mathf.Clamp(selectedWordSetIndex, 0, selectableSets.Count - 1)];
                if (selected != null && selected.words != null && selected.words.Count > RandomAdvancedWordCount)
                {
                    pool = selected;
                }
            }

            pool ??= GetAdvancedWordPool();
            if (pool == null || pool.words.Count == 0)
            {
                statusMessage = "No Spanish noun pool was loaded.";
                return;
            }

            var sampleCount = Mathf.Min(RandomAdvancedWordCount, RoomSpecCatalog.AnchorCount, pool.words.Count);
            var sampledWords = new List<WordEntry>();
            const int maxSampleAttempts = 12;
            for (int attempt = 0; attempt < maxSampleAttempts; attempt++)
            {
                var shuffledPool = CloneWordEntries(pool.words);
                ShuffleList(shuffledPool, randomAdvancedWordSampler);
                sampledWords.Clear();
                for (int i = 0; i < sampleCount; i++)
                {
                    sampledWords.Add(shuffledPool[i]);
                }

                if (!IsSameRandomAdvancedSample(sampledWords))
                {
                    break;
                }
            }

            activeWordSet = new WordSetDefinition
            {
                setId = pool.setId + "_sample_" + sampleCount,
                displayName = $"{pool.displayName} Sample ({sampleCount})",
                description = $"Sampled {sampleCount} words from {pool.displayName}.",
                words = sampledWords
            };

            usingRandomAdvancedWordSet = true;
            useCustomCsv = false;
            customCsvText = BuildCsvText(activeWordSet);
            RememberRandomAdvancedSample(sampledWords);
            statusMessage = $"Sampled {sampleCount} words from {pool.displayName}.";
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
                    meaning = word.meaning
                });
            }

            return clone;
        }

        private static List<WordEntry> CloneWordEntries(List<WordEntry> sourceWords)
        {
            var clone = new List<WordEntry>();
            if (sourceWords == null)
            {
                return clone;
            }

            for (int i = 0; i < sourceWords.Count; i++)
            {
                clone.Add(new WordEntry
                {
                    word = sourceWords[i].word,
                    meaning = sourceWords[i].meaning
                });
            }

            return clone;
        }

        private bool IsSameRandomAdvancedSample(List<WordEntry> sample)
        {
            if (sample == null || sample.Count == 0 || previousRandomAdvancedSampleWords.Count != sample.Count)
            {
                return false;
            }

            var matched = 0;
            for (int i = 0; i < sample.Count; i++)
            {
                var key = NormalizeWordSampleKey(sample[i]?.word);
                if (!string.IsNullOrWhiteSpace(key) && previousRandomAdvancedSampleWords.Contains(key))
                {
                    matched++;
                }
            }

            return matched == sample.Count;
        }

        private void RememberRandomAdvancedSample(List<WordEntry> sample)
        {
            previousRandomAdvancedSampleWords.Clear();
            if (sample == null)
            {
                return;
            }

            for (int i = 0; i < sample.Count; i++)
            {
                var key = NormalizeWordSampleKey(sample[i]?.word);
                if (!string.IsNullOrWhiteSpace(key))
                {
                    previousRandomAdvancedSampleWords.Add(key);
                }
            }
        }

        private static string NormalizeWordSampleKey(string word)
        {
            return string.IsNullOrWhiteSpace(word) ? string.Empty : word.Trim().ToLowerInvariant();
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
                    meaning = parts[1].Trim()
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
            builder.AppendLine("word,meaning");
            foreach (var word in wordSet.words)
            {
                builder.Append(word.word).Append(',')
                    .Append(word.meaning)
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
                    anchorId = anchor.id,
                    anchorLabel = anchor.label,
                    anchorType = PreGeneratedMnemonicCatalog.NormalizeAnchorType(anchor),
                    mnemonicSource = "self_authored",
                    visualCue = string.Empty,
                    associationPrompt = string.Empty,
                    mnemonic = string.Empty,
                    mnemonicMode = "STORY_ONLY",
                    hookAccepted = false,
                    hookScore = 0,
                    hookReason = string.Empty,
                    mnemonicHook = string.Empty,
                    storyCue = string.Empty,
                    imagePrompt = string.Empty,
                    imagePromptCandidates = new List<string>(),
                    selectedImageCandidateIndex = -1,
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
                ClearGeneratedImageCueForWord(currentItems[i].word);
                currentItems[i].visualCue = autoFilled[i].visualCue;
                currentItems[i].mainCueObject = autoFilled[i].mainCueObject;
                currentItems[i].associationPrompt = autoFilled[i].associationPrompt;
                currentItems[i].mnemonic = autoFilled[i].mnemonic;
                currentItems[i].mnemonicMode = autoFilled[i].mnemonicMode;
                currentItems[i].hookAccepted = autoFilled[i].hookAccepted;
                currentItems[i].hookScore = autoFilled[i].hookScore;
                currentItems[i].hookReason = autoFilled[i].hookReason;
                currentItems[i].mnemonicHook = autoFilled[i].mnemonicHook;
                currentItems[i].storyCue = autoFilled[i].storyCue;
                currentItems[i].imagePrompt = autoFilled[i].imagePrompt;
                currentItems[i].imagePromptCandidates = autoFilled[i].imagePromptCandidates == null
                    ? new List<string>()
                    : new List<string>(autoFilled[i].imagePromptCandidates);
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

                if (string.IsNullOrWhiteSpace(item.mnemonic))
                {
                    item.mnemonic = BuildMnemonicLinkFallback(item);
                }

                item.storyCue = string.Empty;

                if (string.IsNullOrWhiteSpace(item.associationPrompt))
                {
                    item.associationPrompt = ExtractPromptSceneDetail(item.visualCue);
                }

                ApplyAnchorConsistency(item);
            }
        }

        private List<MnemonicItemData> EnsureMnemonicDefaults(List<MnemonicItemData> items)
        {
            items ??= new List<MnemonicItemData>();
            for (int i = items.Count - 1; i >= 0; i--)
            {
                if (items[i] == null)
                {
                    items.RemoveAt(i);
                }
            }

            for (int i = 0; i < items.Count; i++)
            {
                var anchor = RoomSpecCatalog.TryGetAnchor(items[i].anchorId, out var existingAnchor)
                    ? existingAnchor
                    : RoomSpecCatalog.GetAssignmentAnchor(i, items.Count);
                items[i].anchorId = anchor.id;
                items[i].anchorLabel = anchor.label;
                items[i].anchorType = PreGeneratedMnemonicCatalog.NormalizeAnchorType(anchor);
                if (string.IsNullOrWhiteSpace(items[i].mnemonicSource))
                {
                    items[i].mnemonicSource = condition == ExperimentCondition.SelfGenerated
                        ? "self_authored"
                        : "unknown";
                }

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

                if (string.IsNullOrWhiteSpace(items[i].associationPrompt))
                {
                    items[i].associationPrompt = FirstNonEmptyPrompt(
                        ExtractPromptSceneDetail(items[i].imagePrompt),
                        ExtractPromptSceneDetail(items[i].visualCue));
                }

                if (string.IsNullOrWhiteSpace(items[i].imagePrompt))
                {
                    items[i].imagePrompt = BuildMnemonicImagePrompt(items[i]);
                }

                if (string.IsNullOrWhiteSpace(items[i].mnemonic))
                {
                    items[i].mnemonic = BuildMnemonicLinkFallback(items[i]);
                }

                if (string.IsNullOrWhiteSpace(items[i].mnemonicMode))
                {
                    items[i].mnemonicMode = items[i].hookAccepted && items[i].hookScore >= 7
                        ? "HOOK_PLUS_STORY"
                        : "STORY_ONLY";
                }

                items[i].storyCue = string.Empty;

                if (items[i].imagePromptCandidates == null)
                {
                    items[i].imagePromptCandidates = new List<string>();
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
                        RefreshAssociationPromptsAfterCueRewrite(items[i]);
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

            var text = ((item.visualCue ?? string.Empty) + " " + (item.associationPrompt ?? string.Empty) + " " + (item.imagePrompt ?? string.Empty)).ToLowerInvariant();
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
                    item.mnemonic = $"At the {anchor}, the gear looks ready to spin, but the resin holds it perfectly still. You imagine trying to move it and failing, so immutable becomes the name for something that cannot change.";
                    SetMnemonicJudgeState(item, "STORY_ONLY", false, 0, "Runtime fallback uses a story-only mnemonic.", string.Empty);
                    item.imagePrompt = $"Close-up at the {anchor}: a transparent resin cube seals a small metal gear, with the gear visibly trapped and unable to turn. Keep the {anchor} only as background context, no text, no letters, no captions, no logos, no watermark.";
                    return true;

                case "intransigent":
                    item.visualCue = $"At the {anchor}, two puzzle pieces meet while a tiny steel wedge refuses to slide into place.";
                    item.mnemonic = $"At the {anchor}, the puzzle almost works, but the steel wedge refuses to move no matter how gently you press it. That stubborn refusal makes intransigent feel like a person or thing that will not compromise.";
                    SetMnemonicJudgeState(item, "STORY_ONLY", false, 0, "Runtime fallback uses a story-only mnemonic.", string.Empty);
                    item.imagePrompt = $"Close-up at the {anchor}: two puzzle pieces almost connect, but a tiny steel wedge blocks the join and refuses to slide into place. Keep the {anchor} only as background context, no text, no letters, no captions, no logos, no watermark.";
                    return true;

                case "capricious":
                    item.visualCue = $"At the {anchor}, a small puppet face snaps from laughing to crying to angry without warning.";
                    item.mnemonic = $"At the {anchor}, the puppet seems impossible to predict: one second it laughs, then it cries, then it looks furious. The sudden shifts make capricious feel like a mood that changes without warning.";
                    SetMnemonicJudgeState(item, "STORY_ONLY", false, 0, "Runtime fallback uses a story-only mnemonic.", string.Empty);
                    item.imagePrompt = $"Close-up at the {anchor}: a small puppet face flips from laughing to crying to angry, dominating the foreground. Keep the {anchor} only as background context, no text, no letters, no captions, no logos, no watermark.";
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
            item.anchorType = PreGeneratedMnemonicCatalog.NormalizeAnchorType(nextAnchor);
            ClearImagePromptCandidateState(item);
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
                var anchorChanged = !string.Equals(currentItems[i].anchorId, anchor.id, StringComparison.Ordinal)
                                    || !string.Equals(currentItems[i].anchorLabel, anchor.label, StringComparison.Ordinal);
                if (anchorChanged)
                {
                    changed++;
                }

                currentItems[i].anchorId = anchor.id;
                currentItems[i].anchorLabel = anchor.label;
                currentItems[i].anchorType = PreGeneratedMnemonicCatalog.NormalizeAnchorType(anchor);
                if (anchorChanged)
                {
                    ClearImagePromptCandidateState(currentItems[i]);
                }

                ApplyAnchorConsistency(currentItems[i]);
            }

            if (changed > 0)
            {
                ResetAnchorDependentProgress();
            }

            return changed;
        }

        private static void ClearImagePromptCandidateState(MnemonicItemData item)
        {
            if (item == null)
            {
                return;
            }

            item.imagePromptCandidates = new List<string>();
            item.associationPrompt = string.Empty;
            item.selectedImagePrompt = string.Empty;
            item.selectedImageCandidateIndex = -1;
            item.imageSelectionReason = string.Empty;
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
            ClearAllGeneratedImageCueTextures();
            imageCueValidationFailures.Clear();
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

        private void ClearAllGeneratedImageCueTextures()
        {
            var destroyed = new HashSet<Texture2D>();
            foreach (var pair in imageCueCandidateResults)
            {
                var results = pair.Value;
                if (results == null)
                {
                    continue;
                }

                for (int i = 0; i < results.Count; i++)
                {
                    var texture = results[i]?.texture;
                    if (texture != null && destroyed.Add(texture))
                    {
                        Destroy(texture);
                    }
                }
            }

            foreach (var imageCue in mnemonicImageCues.Values)
            {
                if (imageCue != null && destroyed.Add(imageCue))
                {
                    Destroy(imageCue);
                }
            }

            imageCueCandidateResults.Clear();
            displayedImageCueCandidateIndexes.Clear();
            mnemonicImageCues.Clear();
        }

        private void ClearGeneratedImageCueForWord(string word)
        {
            if (string.IsNullOrWhiteSpace(word))
            {
                return;
            }

            var destroyed = new HashSet<Texture2D>();
            if (imageCueCandidateResults.TryGetValue(word, out var results) && results != null)
            {
                for (int i = 0; i < results.Count; i++)
                {
                    var texture = results[i]?.texture;
                    if (texture != null && destroyed.Add(texture))
                    {
                        Destroy(texture);
                    }
                }
            }

            if (mnemonicImageCues.TryGetValue(word, out var imageCue) && imageCue != null && destroyed.Add(imageCue))
            {
                Destroy(imageCue);
            }

            imageCueCandidateResults.Remove(word);
            displayedImageCueCandidateIndexes.Remove(word);
            mnemonicImageCues.Remove(word);

            var item = FindItemByWord(word);
            if (item != null)
            {
                item.imageCuePath = string.Empty;
                item.selectedImagePrompt = string.Empty;
                item.selectedImageCandidateIndex = -1;
                item.imageSelectionReason = string.Empty;
            }
        }

        private void ClearMemorySnapshotForWord(string word)
        {
            if (string.IsNullOrWhiteSpace(word))
            {
                return;
            }

            if (memorySnapshots.TryGetValue(word, out var snapshot) && snapshot != null)
            {
                Destroy(snapshot);
            }

            memorySnapshots.Remove(word);
            memorizedWords.Remove(word);
            recognitionQueue.Remove(word);
            recognitionOptions.Remove(word);
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
                return new FurnitureTemplate(label, "toilet", "Cylinder", "#E9ECEF", new Vector3(0.52f, 0.50f, 0.56f), 0.29f, 0.58f);
            }

            if (ContainsAny(lower, "bath", "bathtub", "tub", "\u6d74\u69fd", "\u98a8\u5442"))
            {
                return new FurnitureTemplate(label, "bathtub", "Cube", "#DDE7EF", new Vector3(0.94f, 0.38f, 0.54f), 0.24f, 0.54f);
            }

            if (ContainsAny(lower, "sink", "\u6d17\u9762", "\u6d41\u3057"))
            {
                return new FurnitureTemplate(label, "sink", "Cube", "#C9D6E2", new Vector3(0.56f, 0.46f, 0.40f), 0.38f, 0.60f);
            }

            if (ContainsAny(lower, "fridge", "refrigerator", "\u51b7\u8535", "\u51b0\u7bb1"))
            {
                return new FurnitureTemplate(label, "fridge", "Cube", "#D4D7DD", new Vector3(0.56f, 1.34f, 0.44f), 0.67f, 0.84f);
            }

            if (ContainsAny(lower, "bookcase", "bookshelf", "book shelf"))
            {
                return new FurnitureTemplate(label, "bookshelf", "Cube", "#8B6A3A", new Vector3(0.60f, 1.46f, 0.23f), 0.73f, 0.92f);
            }

            if (ContainsAny(lower, "stove", "cooktop", "range", "hob"))
            {
                return new FurnitureTemplate(label, "stove", "Cube", "#4D525A", new Vector3(0.64f, 0.56f, 0.46f), 0.33f, 0.66f);
            }

            if (ContainsAny(lower, "air conditioner", "aircon", "air conditioning", "ac unit", "a/c"))
            {
                return new FurnitureTemplate(label, "air_conditioner", "Cube", "#E6ECEF", new Vector3(0.74f, 0.28f, 0.10f), 1.62f, 0.40f);
            }

            if (ContainsAny(lower, "television", "tv", "monitor", "screen", "\u30c6\u30ec\u30d3", "\u7535\u89c6", "\u96fb\u8996"))
            {
                return new FurnitureTemplate(label, "television", "Cube", "#252A32", new Vector3(0.82f, 0.60f, 0.10f), 0.50f, 0.72f);
            }

            if (ContainsAny(lower, "computer", "pc", "laptop", "desktop", "keyboard", "comput", "omputer", "macbook", "\u30b3\u30f3\u30d4\u30e5\u30fc\u30bf", "\u7535\u8111", "\u96fb\u8133"))
            {
                return new FurnitureTemplate(label, "computer", "Cube", "#303845", new Vector3(0.64f, 0.56f, 0.34f), 0.36f, 0.66f);
            }

            if (ContainsAny(lower, "door", "\u30c9\u30a2", "\u95e8", "\u9580"))
            {
                return new FurnitureTemplate(label, "door", "Cube", "#7B5032", new Vector3(0.62f, 1.56f, 0.035f), 0.78f, 0.16f);
            }

            if (ContainsAny(lower, "window", "balcony", "\u7a93", "\u7a97", "\u30d9\u30e9\u30f3\u30c0"))
            {
                return new FurnitureTemplate(label, "window", "Cube", "#779CCB", new Vector3(0.68f, 0.52f, 0.024f), 1.12f, 0.30f);
            }

            if (ContainsAny(lower, "wardrobe", "closet", "cabinet", "\u8863\u67dc", "\u30af\u30ed\u30fc\u30bc\u30c3\u30c8"))
            {
                return new FurnitureTemplate(label, "cabinet", "Cube", "#7E6B55", new Vector3(0.68f, 1.34f, 0.30f), 0.67f, 0.84f);
            }

            return new FurnitureTemplate(label, SanitizeIdPrefix(label), "Cube", "#8B7A65", new Vector3(0.56f, 0.56f, 0.44f), 0.36f, 0.60f);
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

            var position = runtimeCamera.transform.position + forward.normalized * 1.85f;
            position.x = Mathf.Clamp(position.x, -4.0f, 4.0f);
            position.y = defaultY;
            position.z = Mathf.Clamp(position.z, -4.0f, 4.0f);
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
            AddRoomShellPrimitive("Floor Patch", "Cube", "#E7D9C1", new Vector3(0f, 0f, 0f), new Vector3(1.4f, 0.06f, 1.4f));
            BeginFloorPatchPlacement(selectedRoomPrimitiveIndex, true);
        }

        private void AddWallSegmentPrimitive()
        {
            var wallHeight = RoomSpecCatalog.DefaultShellWallHeight;
            AddRoomShellPrimitive("Wall Segment", "Cube", "#F7F4EC", new Vector3(0f, wallHeight * 0.5f, 0f), new Vector3(1.4f, wallHeight, 0.09f));
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
            RoomSpecCatalog.EnsureDefaults(room);

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
            if (id == "ceiling"
                || id.StartsWith("ceiling_", StringComparison.Ordinal)
                || id.StartsWith("auto_ceiling_", StringComparison.Ordinal)
                || id.StartsWith("auto_wall_", StringComparison.Ordinal)
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

        private void AddAutoHorizontalShellSegment(float xMin, float xMax, float z, int outwardSign, int index)
        {
            var room = RoomSpecCatalog.CurrentRoom;
            var length = xMax - xMin;
            var centerX = (xMin + xMax) * 0.5f;
            var wallHeight = RoomSpecCatalog.DefaultShellWallHeight;
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
            var wallHeight = RoomSpecCatalog.DefaultShellWallHeight;
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
            midpoint.y = RoomSpecCatalog.DefaultShellWallHeight * 0.5f;

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
                scale = new Vector3(length, RoomSpecCatalog.DefaultShellWallHeight, 0.12f),
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
            var wallHeight = Mathf.Clamp(Mathf.Max(wall.scale.y, RoomSpecCatalog.DefaultShellWallHeight), 1.25f, 3.8f);
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
            RoomSpecCatalog.EnsureDefaults(RoomSpecCatalog.CurrentRoom);
            var exportFolder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "GeneratedRooms"));
            Directory.CreateDirectory(exportFolder);
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var path = Path.Combine(exportFolder, $"room_{timestamp}.json");
            File.WriteAllText(path, JsonUtility.ToJson(RoomSpecCatalog.CurrentRoom, true), Encoding.UTF8);
            statusMessage = $"Room JSON saved: {path}";
        }

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
                AddFurnitureParts(root.transform, editable, editableIndex, Prop("Tank", PrimitiveType.Cube, V(0f, 0.20f, 0.28f), V(0.62f, 0.38f, 0.18f), color), Prop("Bowl", PrimitiveType.Sphere, V(0f, -0.05f, -0.04f), V(0.58f, 0.34f, 0.68f), Color.Lerp(color, Color.white, 0.2f)), Prop("Base", PrimitiveType.Cylinder, V(0f, -0.32f, -0.04f), V(0.34f, 0.16f, 0.34f), color));
            }
            else if (TryAddFurnitureParts(label, root.transform, editable, editableIndex, K("sink", "\u6d17\u9762", "\u6d41\u3057"), Prop("Cabinet", PrimitiveType.Cube, V(0f, -0.20f, 0f), V(0.82f, 0.56f, 0.62f), color), Prop("CounterTop", PrimitiveType.Cube, V(0f, 0.14f, 0f), V(0.92f, 0.10f, 0.70f), Color.Lerp(color, Color.white, 0.25f)), Prop("Basin", PrimitiveType.Cube, V(0f, 0.23f, -0.04f), V(0.56f, 0.10f, 0.42f), C(0.88f, 0.92f, 0.95f)), Prop("FaucetStem", PrimitiveType.Cylinder, V(0f, 0.42f, 0.18f), V(0.07f, 0.22f, 0.07f), C(0.65f, 0.70f, 0.72f)), Prop("FaucetHead", PrimitiveType.Cube, V(0f, 0.52f, 0.04f), V(0.24f, 0.05f, 0.08f), C(0.65f, 0.70f, 0.72f)))) { }
            else if (TryAddFurnitureParts(label, root.transform, editable, editableIndex, K("bath", "bathtub", "tub", "\u6d74\u69fd", "\u98a8\u5442"), Prop("Tub", PrimitiveType.Cube, Vector3.zero, V(1.0f, 0.34f, 0.58f), color), Prop("Water", PrimitiveType.Cube, V(0f, 0.20f, 0f), V(0.82f, 0.04f, 0.42f), C(0.45f, 0.68f, 0.88f)))) { }
            else if (TryAddFurnitureParts(label, root.transform, editable, editableIndex, K("television", "tv", "monitor", "screen", "\u30c6\u30ec\u30d3", "\u7535\u89c6", "\u96fb\u8996"), Prop("Screen", PrimitiveType.Cube, V(0f, 0.18f, 0f), V(1.0f, 0.62f, 0.08f), C(0.05f, 0.06f, 0.08f)), Prop("ScreenGlow", PrimitiveType.Cube, V(0f, 0.18f, -0.05f), V(0.86f, 0.48f, 0.03f), C(0.18f, 0.30f, 0.42f)), Prop("StandNeck", PrimitiveType.Cube, V(0f, -0.20f, 0.02f), V(0.10f, 0.32f, 0.10f), Color.Lerp(color, Color.black, 0.1f)), Prop("StandBase", PrimitiveType.Cube, V(0f, -0.38f, 0.02f), V(0.46f, 0.08f, 0.28f), Color.Lerp(color, Color.black, 0.1f)))) { }
            else if (TryAddFurnitureParts(label, root.transform, editable, editableIndex, K("computer", "pc", "laptop", "desktop", "keyboard", "comput", "omputer", "macbook", "\u30b3\u30f3\u30d4\u30e5\u30fc\u30bf", "\u7535\u8111", "\u96fb\u8133"), Prop("MonitorFrame", PrimitiveType.Cube, V(0f, 0.24f, 0.03f), V(0.76f, 0.52f, 0.08f), C(0.06f, 0.07f, 0.09f)), Prop("MonitorGlow", PrimitiveType.Cube, V(0f, 0.24f, -0.02f), V(0.62f, 0.38f, 0.035f), C(0.18f, 0.45f, 0.70f)), Prop("MonitorStand", PrimitiveType.Cube, V(0f, -0.08f, 0.04f), V(0.10f, 0.28f, 0.10f), Color.Lerp(color, Color.black, 0.12f)), Prop("Keyboard", PrimitiveType.Cube, V(0f, -0.28f, -0.26f), V(0.72f, 0.06f, 0.22f), C(0.12f, 0.13f, 0.15f)), Prop("Mouse", PrimitiveType.Sphere, V(0.42f, -0.26f, -0.24f), V(0.18f, 0.08f, 0.24f), C(0.16f, 0.17f, 0.19f)))) { }
            else if (TryAddFurnitureParts(label, root.transform, editable, editableIndex, K("air conditioner", "aircon", "air conditioning", "ac unit", "a/c"), Prop("UnitBody", PrimitiveType.Cube, Vector3.zero, V(1.0f, 0.72f, 0.9f), color), Prop("VentLineA", PrimitiveType.Cube, V(0f, -0.20f, -0.48f), V(0.86f, 0.04f, 0.05f), C(0.45f, 0.52f, 0.56f)), Prop("VentLineB", PrimitiveType.Cube, V(0f, -0.08f, -0.48f), V(0.86f, 0.035f, 0.05f), C(0.60f, 0.66f, 0.70f)))) { }
            else if (TryAddFurnitureParts(label, root.transform, editable, editableIndex, K("stove", "cooktop", "range", "hob"), Prop("StoveBody", PrimitiveType.Cube, V(0f, -0.10f, 0f), V(1.0f, 0.76f, 0.92f), color), Prop("Cooktop", PrimitiveType.Cube, V(0f, 0.32f, -0.02f), V(0.92f, 0.08f, 0.82f), C(0.12f, 0.13f, 0.14f)), Prop("BurnerA", PrimitiveType.Cylinder, V(-0.25f, 0.39f, -0.18f), V(0.22f, 0.03f, 0.22f), C(0.72f, 0.72f, 0.68f)), Prop("BurnerB", PrimitiveType.Cylinder, V(0.25f, 0.39f, 0.16f), V(0.22f, 0.03f, 0.22f), C(0.72f, 0.72f, 0.68f)), Prop("OvenDoor", PrimitiveType.Cube, V(0f, -0.20f, -0.48f), V(0.72f, 0.36f, 0.04f), C(0.16f, 0.18f, 0.20f)))) { }
            else if (TryAddFurnitureParts(label, root.transform, editable, editableIndex, K("bed", "\u30d9\u30c3\u30c9", "\u5e8a"), Prop("Frame", PrimitiveType.Cube, V(0f, -0.20f, 0f), V(1.0f, 0.20f, 1.0f), Color.Lerp(color, Color.black, 0.16f)), Prop("Mattress", PrimitiveType.Cube, V(0f, 0.02f, 0f), V(0.92f, 0.22f, 0.92f), color), Prop("Blanket", PrimitiveType.Cube, V(0f, 0.17f, -0.10f), V(0.88f, 0.08f, 0.58f), C(0.64f, 0.48f, 0.36f)), Prop("Pillow", PrimitiveType.Cube, V(0f, 0.20f, 0.34f), V(0.62f, 0.13f, 0.22f), C(0.92f, 0.89f, 0.80f)))) { }
            else if (TryAddFurnitureParts(label, root.transform, editable, editableIndex, K("sofa", "couch", "\u30bd\u30d5\u30a1"), Prop("Seat", PrimitiveType.Cube, V(0f, -0.10f, 0f), V(1.0f, 0.36f, 0.72f), color), Prop("Back", PrimitiveType.Cube, V(0f, 0.20f, 0.34f), V(1.0f, 0.62f, 0.18f), Color.Lerp(color, Color.black, 0.08f)), Prop("LeftArm", PrimitiveType.Cube, V(-0.56f, 0.05f, 0f), V(0.14f, 0.48f, 0.72f), color), Prop("RightArm", PrimitiveType.Cube, V(0.56f, 0.05f, 0f), V(0.14f, 0.48f, 0.72f), color))) { }
            else if (TryAddFurnitureParts(label, root.transform, editable, editableIndex, K("chair", "\u6905\u5b50", "\u30a4\u30b9"), Prop("Seat", PrimitiveType.Cube, Vector3.zero, V(0.78f, 0.18f, 0.78f), color), Prop("Back", PrimitiveType.Cube, V(0f, 0.46f, 0.32f), V(0.78f, 0.74f, 0.16f), Color.Lerp(color, Color.black, 0.08f)), Prop("LegFL", PrimitiveType.Cube, V(-0.25f, -0.34f, -0.25f), V(0.08f, 0.58f, 0.08f), Color.Lerp(color, Color.black, 0.18f)), Prop("LegFR", PrimitiveType.Cube, V(0.25f, -0.34f, -0.25f), V(0.08f, 0.58f, 0.08f), Color.Lerp(color, Color.black, 0.18f)), Prop("LegBL", PrimitiveType.Cube, V(-0.25f, -0.34f, 0.25f), V(0.08f, 0.58f, 0.08f), Color.Lerp(color, Color.black, 0.18f)), Prop("LegBR", PrimitiveType.Cube, V(0.25f, -0.34f, 0.25f), V(0.08f, 0.58f, 0.08f), Color.Lerp(color, Color.black, 0.18f)))) { }
            else if (ContainsAny(label, "table", "desk", "counter", "\u673a", "\u30c6\u30fc\u30d6\u30eb", "\u30ab\u30a6\u30f3\u30bf\u30fc"))
            {
                AddFurnitureParts(root.transform, editable, editableIndex, Prop("Top", PrimitiveType.Cube, V(0f, 0.22f, 0f), V(1.0f, 0.14f, 1.0f), color), Prop("LegFL", PrimitiveType.Cube, V(-0.38f, -0.25f, -0.34f), V(0.08f, 0.78f, 0.08f), Color.Lerp(color, Color.black, 0.2f)), Prop("LegFR", PrimitiveType.Cube, V(0.38f, -0.25f, -0.34f), V(0.08f, 0.78f, 0.08f), Color.Lerp(color, Color.black, 0.2f)), Prop("LegBL", PrimitiveType.Cube, V(-0.38f, -0.25f, 0.34f), V(0.08f, 0.78f, 0.08f), Color.Lerp(color, Color.black, 0.2f)), Prop("LegBR", PrimitiveType.Cube, V(0.38f, -0.25f, 0.34f), V(0.08f, 0.78f, 0.08f), Color.Lerp(color, Color.black, 0.2f)));
                if (ContainsAny(label, "desk", "counter", "\u30ab\u30a6\u30f3\u30bf\u30fc"))
                {
                    AddFurniturePart(root.transform, "Drawer", PrimitiveType.Cube, V(0.23f, -0.05f, -0.42f), V(0.34f, 0.22f, 0.08f), Color.Lerp(color, Color.black, 0.08f), editable, editableIndex);
                }
            }
            else if (TryAddFurnitureParts(label, root.transform, editable, editableIndex, K("shelf", "book", "cabinet", "wardrobe", "closet", "\u68da", "\u672c\u68da", "\u8863\u67dc", "\u30af\u30ed\u30fc\u30bc\u30c3\u30c8"), Prop("Frame", PrimitiveType.Cube, Vector3.zero, V(1.0f, 1.0f, 0.42f), color), Prop("OpenFace", PrimitiveType.Cube, V(0f, 0f, -0.24f), V(0.82f, 0.86f, 0.05f), Color.Lerp(color, Color.white, 0.20f)), Prop("ShelfLine1", PrimitiveType.Cube, V(0f, 0.22f, -0.29f), V(0.9f, 0.04f, 0.08f), Color.Lerp(color, Color.black, 0.12f)), Prop("ShelfLine2", PrimitiveType.Cube, V(0f, -0.20f, -0.29f), V(0.9f, 0.04f, 0.08f), Color.Lerp(color, Color.black, 0.12f)), Prop("BookA", PrimitiveType.Cube, V(-0.25f, 0.42f, -0.34f), V(0.10f, 0.28f, 0.10f), C(0.65f, 0.22f, 0.18f)), Prop("BookB", PrimitiveType.Cube, V(-0.12f, 0.40f, -0.34f), V(0.09f, 0.24f, 0.10f), C(0.20f, 0.38f, 0.65f)), Prop("BookC", PrimitiveType.Cube, V(0.03f, -0.02f, -0.34f), V(0.12f, 0.30f, 0.10f), C(0.75f, 0.62f, 0.24f)))) { }
            else if (TryAddFurnitureParts(label, root.transform, editable, editableIndex, K("plant", "\u690d\u7269", "\u89b3\u8449"), Prop("Pot", PrimitiveType.Cylinder, V(0f, -0.30f, 0f), V(0.5f, 0.35f, 0.5f), C(0.48f, 0.30f, 0.20f)), Prop("Stem", PrimitiveType.Cylinder, V(0f, 0.08f, 0f), V(0.08f, 0.62f, 0.08f), C(0.35f, 0.55f, 0.28f)), Prop("LeafA", PrimitiveType.Sphere, V(-0.18f, 0.34f, 0f), V(0.46f, 0.25f, 0.30f), color), Prop("LeafB", PrimitiveType.Sphere, V(0.18f, 0.48f, 0.02f), V(0.46f, 0.25f, 0.30f), color), Prop("LeafC", PrimitiveType.Sphere, V(0f, 0.62f, -0.12f), V(0.40f, 0.24f, 0.28f), Color.Lerp(color, Color.white, 0.1f)))) { }
            else if (TryAddFurnitureParts(label, root.transform, editable, editableIndex, K("lamp", "light", "\u7167\u660e", "\u30e9\u30a4\u30c8", "\u30e9\u30f3\u30d7"), Prop("Pole", PrimitiveType.Cylinder, V(0f, -0.12f, 0f), V(0.12f, 0.78f, 0.12f), C(0.55f, 0.55f, 0.58f)), Prop("Shade", PrimitiveType.Sphere, V(0f, 0.48f, 0f), V(0.72f, 0.42f, 0.72f), color))) { }
            else if (TryAddFurnitureParts(label, root.transform, editable, editableIndex, K("fridge", "refrigerator", "\u51b7\u8535", "\u51b0\u7bb1"), Prop("Body", PrimitiveType.Cube, Vector3.zero, Vector3.one, color), Prop("FreezerDoor", PrimitiveType.Cube, V(0f, 0.24f, -0.52f), V(0.94f, 0.42f, 0.05f), Color.Lerp(color, Color.white, 0.12f)), Prop("FridgeDoor", PrimitiveType.Cube, V(0f, -0.25f, -0.52f), V(0.94f, 0.50f, 0.05f), Color.Lerp(color, Color.white, 0.06f)), Prop("Handle", PrimitiveType.Cube, V(0.42f, 0.05f, -0.52f), V(0.06f, 0.62f, 0.06f), C(0.65f, 0.68f, 0.70f)))) { }
            else if (TryAddFurnitureParts(label, root.transform, editable, editableIndex, K("door", "\u30c9\u30a2", "\u95e8", "\u9580"), Prop("Panel", PrimitiveType.Cube, Vector3.zero, V(0.92f, 1.0f, 0.58f), color), Prop("FrameTop", PrimitiveType.Cube, V(0f, 0.48f, 0f), V(1.0f, 0.06f, 0.76f), Color.Lerp(color, Color.black, 0.14f)), Prop("FrameLeft", PrimitiveType.Cube, V(-0.46f, 0f, 0f), V(0.06f, 0.94f, 0.76f), Color.Lerp(color, Color.black, 0.14f)), Prop("FrameRight", PrimitiveType.Cube, V(0.46f, 0f, 0f), V(0.06f, 0.94f, 0.76f), Color.Lerp(color, Color.black, 0.14f)), Prop("Knob", PrimitiveType.Sphere, V(0.34f, -0.02f, -0.34f), V(0.10f, 0.10f, 0.10f), C(0.86f, 0.70f, 0.38f)))) { }
            else if (TryAddFurnitureParts(label, root.transform, editable, editableIndex, K("window", "balcony", "\u7a93", "\u7a97", "\u30d9\u30e9\u30f3\u30c0"), Prop("Glass", PrimitiveType.Cube, Vector3.zero, V(0.84f, 0.78f, 0.28f), C(0.60f, 0.76f, 0.92f, 0.72f)), Prop("FrameTop", PrimitiveType.Cube, V(0f, 0.39f, 0f), V(0.94f, 0.055f, 0.46f), Color.white), Prop("FrameBottom", PrimitiveType.Cube, V(0f, -0.39f, 0f), V(0.94f, 0.055f, 0.46f), Color.white), Prop("FrameLeft", PrimitiveType.Cube, V(-0.44f, 0f, 0f), V(0.055f, 0.84f, 0.46f), Color.white), Prop("FrameRight", PrimitiveType.Cube, V(0.44f, 0f, 0f), V(0.055f, 0.84f, 0.46f), Color.white), Prop("FrameMid", PrimitiveType.Cube, Vector3.zero, V(0.045f, 0.78f, 0.42f), Color.white), Prop("Sill", PrimitiveType.Cube, V(0f, -0.46f, 0f), V(0.96f, 0.07f, 0.64f), C(0.72f, 0.68f, 0.58f)))) { }
            else
            {
                AddFurniturePart(root.transform, "Generic", RoomSpecCatalog.ParsePrimitiveType(anchor.primitiveShape), Vector3.zero, Vector3.one, color, editable, editableIndex);
            }

            return root;
        }

        private bool TryAddFurnitureParts(string label, Transform root, bool editable, int editableIndex, string[] terms, params PrimitivePartSpec[] parts)
        {
            if (!ContainsAny(label, terms))
            {
                return false;
            }

            AddFurnitureParts(root, editable, editableIndex, parts);
            return true;
        }

        private void AddFurnitureParts(Transform root, bool editable, int editableIndex, params PrimitivePartSpec[] parts)
        {
            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                AddFurniturePart(root, part.name, part.shape, part.localPosition, part.localScale, part.color, editable, editableIndex);
            }
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

        private Vector3 GetFurnitureRenderScale(AnchorDefinition anchor)
        {
            if (anchor == null)
            {
                return Vector3.one;
            }

            var scale = anchor.scale;
            if (scale == default)
            {
                return new Vector3(0.5f, 0.5f, 0.5f);
            }

            return new Vector3(
                Mathf.Clamp(scale.x, 0.03f, 4f),
                Mathf.Clamp(scale.y, 0.03f, 4f),
                Mathf.Clamp(scale.z, 0.02f, 4f));
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
            studyItemTargets.Clear();
            var roomSpec = RoomSpecCatalog.CurrentRoom;
            RoomSpecCatalog.EnsureDefaults(roomSpec);

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
            studyItemTargets[item.word] = go.transform;

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
            root.localScale = NormalizePropScale(baseScale);

            return TryAddSemanticMnemonicProp(text, root, item, K("thread", "string", "wire", "rope", "line", "thin", "tenuous", "\u7cf8", "\u7dda", "\u7d30"), Prop("Thread", PrimitiveType.Cylinder, V(0f, 0.20f, 0f), V(0.10f, 2.3f, 0.10f), C(0.92f, 0.90f, 0.78f)), Prop("TinyWeight", PrimitiveType.Sphere, V(0f, -0.28f, 0f), V(0.42f, 0.42f, 0.42f), color))
                || TryAddSemanticMnemonicProp(text, root, item, K("fire", "flame", "burn", "spark", "ignite", "blaze", "\u706b", "\u708e"), Prop("FlameCore", PrimitiveType.Capsule, V(0f, 0.12f, 0f), V(0.42f, 1.0f, 0.42f), C(1.0f, 0.42f, 0.10f), true), Prop("FlameGlow", PrimitiveType.Sphere, V(0f, 0.02f, 0f), V(0.72f, 0.42f, 0.72f), C(1.0f, 0.82f, 0.18f), true), Prop("Ember", PrimitiveType.Sphere, V(0.28f, -0.20f, -0.05f), V(0.18f, 0.18f, 0.18f), C(1.0f, 0.26f, 0.06f), true))
                || TryAddSemanticMnemonicProp(text, root, item, K("lantern", "lamp", "light", "glow", "luminous", "\u30e9\u30f3\u30bf\u30f3", "\u30e9\u30a4\u30c8", "\u5149"), Prop("LanternGlass", PrimitiveType.Sphere, V(0f, 0.10f, 0f), V(1.0f, 0.85f, 1.0f), C(1.0f, 0.82f, 0.28f), true), Prop("LanternTop", PrimitiveType.Cube, V(0f, 0.40f, 0f), V(0.72f, 0.16f, 0.72f), C(0.42f, 0.28f, 0.18f)), Prop("LanternBase", PrimitiveType.Cube, V(0f, -0.22f, 0f), V(0.72f, 0.12f, 0.72f), C(0.42f, 0.28f, 0.18f)))
                || TryAddSemanticMnemonicProp(text, root, item, K("smoke", "fume", "poison", "toxic", "cloud", "mist", "\u7159", "\u6bd2", "\u96f2"), Prop("SmokeA", PrimitiveType.Sphere, V(-0.16f, 0.02f, 0f), V(0.72f, 0.52f, 0.72f), C(0.42f, 0.78f, 0.50f), true), Prop("SmokeB", PrimitiveType.Sphere, V(0.12f, 0.25f, 0.03f), V(0.62f, 0.48f, 0.62f), C(0.54f, 0.86f, 0.62f), true), Prop("SmokeC", PrimitiveType.Sphere, V(0.24f, 0.48f, -0.03f), V(0.48f, 0.36f, 0.48f), C(0.74f, 0.90f, 0.70f), true))
                || TryAddSemanticMnemonicProp(text, root, item, K("jar", "bottle", "spice", "vial", "potion", "\u74f6", "\u7f50", "\u30b8\u30e3\u30fc"), Prop("BottleBody", PrimitiveType.Cylinder, V(0f, 0.02f, 0f), V(0.62f, 1.15f, 0.62f), color), Prop("BottleCap", PrimitiveType.Cylinder, V(0f, 0.42f, 0f), V(0.46f, 0.18f, 0.46f), C(0.25f, 0.25f, 0.28f)))
                || TryAddSemanticMnemonicProp(text, root, item, K("plate", "plain", "dish", "austere", "\u76bf", "\u30d7\u30ec\u30fc\u30c8"), Prop("Plate", PrimitiveType.Cylinder, V(0f, -0.06f, 0f), V(1.15f, 0.10f, 1.15f), C(0.92f, 0.92f, 0.86f)), Prop("HardLight", PrimitiveType.Cube, V(0f, 0.22f, 0f), V(0.62f, 0.05f, 0.62f), C(1.0f, 0.96f, 0.72f), true))
                || TryAddSemanticMnemonicProp(text, root, item, K("person", "guest", "people", "crowd", "friend", "social", "gregarious", "\u4eba", "\u53cb", "\u5ba2"), Prop("BodyA", PrimitiveType.Capsule, V(-0.16f, 0f, 0f), V(0.34f, 0.95f, 0.34f), color), Prop("HeadA", PrimitiveType.Sphere, V(-0.16f, 0.58f, 0f), V(0.28f, 0.28f, 0.28f), C(0.94f, 0.70f, 0.52f)), Prop("BodyB", PrimitiveType.Capsule, V(0.22f, -0.04f, 0.08f), V(0.30f, 0.82f, 0.30f), Color.Lerp(color, Color.white, 0.22f)), Prop("HeadB", PrimitiveType.Sphere, V(0.22f, 0.48f, 0.08f), V(0.24f, 0.24f, 0.24f), C(0.94f, 0.70f, 0.52f)))
                || TryAddSemanticMnemonicProp(text, root, item, K("book", "paper", "note", "word", "study", "\u672c", "\u7d19", "\u30ce\u30fc\u30c8"), Prop("OpenBookLeft", PrimitiveType.Cube, V(-0.12f, 0f, 0f), V(0.52f, 0.08f, 0.72f), C(0.92f, 0.88f, 0.76f)), Prop("OpenBookRight", PrimitiveType.Cube, V(0.12f, 0f, 0f), V(0.52f, 0.08f, 0.72f), C(0.96f, 0.92f, 0.80f)), Prop("Spine", PrimitiveType.Cube, V(0f, 0.04f, 0f), V(0.06f, 0.10f, 0.75f), C(0.36f, 0.20f, 0.14f)))
                || TryAddSemanticMnemonicProp(text, root, item, K("crack", "broken", "shatter", "fragment", "fragile", "\u5272", "\u58ca", "\u7834"), Prop("ShardA", PrimitiveType.Cube, V(-0.12f, 0.08f, 0f), V(0.08f, 0.78f, 0.08f), color, false, V(0f, 0f, 28f)), Prop("ShardB", PrimitiveType.Cube, V(0.12f, 0.02f, 0f), V(0.08f, 0.68f, 0.08f), Color.Lerp(color, Color.white, 0.2f), false, V(0f, 0f, -22f)))
                || TryAddSemanticMnemonicProp(text, root, item, K("water", "drip", "rain", "melt", "ephemeral", "\u6c34", "\u96e8", "\u6ef4"), Prop("DropA", PrimitiveType.Sphere, V(-0.12f, 0.28f, 0f), V(0.32f, 0.42f, 0.32f), C(0.35f, 0.72f, 1f), true), Prop("DropB", PrimitiveType.Sphere, V(0.15f, 0.02f, 0.05f), V(0.26f, 0.34f, 0.26f), C(0.45f, 0.82f, 1f), true), Prop("Puddle", PrimitiveType.Cylinder, V(0f, -0.22f, 0f), V(0.88f, 0.06f, 0.52f), C(0.32f, 0.62f, 0.82f)))
                || TryAddSemanticMnemonicProp(text, root, item, K("ice", "frost", "freeze", "snow", "cold", "\u6c37", "\u96ea", "\u51b7"), Prop("IceBlock", PrimitiveType.Cube, V(0f, 0.04f, 0f), V(0.82f, 0.62f, 0.82f), C(0.62f, 0.88f, 1.0f), true, V(0f, 18f, 0f)), Prop("FrostEdgeA", PrimitiveType.Cube, V(-0.34f, 0.36f, 0.02f), V(0.08f, 0.22f, 0.72f), C(0.86f, 0.96f, 1.0f), true, V(0f, 0f, 12f)), Prop("FrostEdgeB", PrimitiveType.Cube, V(0.22f, -0.18f, -0.22f), V(0.55f, 0.06f, 0.08f), C(0.86f, 0.96f, 1.0f), true))
                || TryAddSemanticMnemonicProp(text, root, item, K("chain", "lock", "locked", "shackle", "bind", "\u9396", "\u30ed\u30c3\u30af"), Prop("ChainA", PrimitiveType.Capsule, V(-0.22f, 0.18f, 0f), V(0.16f, 0.58f, 0.16f), C(0.60f, 0.62f, 0.66f), false, V(0f, 0f, 50f)), Prop("ChainB", PrimitiveType.Capsule, V(0.18f, 0.18f, 0f), V(0.16f, 0.58f, 0.16f), C(0.66f, 0.68f, 0.72f), false, V(0f, 0f, -50f)), Prop("LockBody", PrimitiveType.Cube, V(0f, -0.22f, 0f), V(0.50f, 0.36f, 0.18f), C(0.95f, 0.68f, 0.20f)))
                || TryAddSemanticMnemonicProp(text, root, item, K("mirror", "reflect", "reflection", "glass", "\u93e1", "\u53cd\u5c04"), Prop("MirrorGlass", PrimitiveType.Cube, V(0f, 0.06f, 0f), V(0.76f, 0.92f, 0.06f), C(0.62f, 0.82f, 0.95f), true), Prop("MirrorFrameH", PrimitiveType.Cube, V(0f, 0.54f, -0.03f), V(0.88f, 0.08f, 0.10f), C(0.72f, 0.55f, 0.34f)), Prop("MirrorFrameV", PrimitiveType.Cube, V(-0.44f, 0.06f, -0.03f), V(0.08f, 0.98f, 0.10f), C(0.72f, 0.55f, 0.34f)))
                || TryAddSemanticMnemonicProp(text, root, item, K("clock", "time", "timer", "hour", "temporal", "\u6642", "\u6642\u9593", "\u6642\u8a08"), Prop("ClockFace", PrimitiveType.Cylinder, V(0f, 0.08f, 0f), V(0.82f, 0.10f, 0.82f), C(0.92f, 0.88f, 0.74f), false, V(90f, 0f, 0f)), Prop("HourHand", PrimitiveType.Cube, V(0.08f, 0.10f, -0.04f), V(0.08f, 0.38f, 0.04f), C(0.12f, 0.12f, 0.14f), false, V(0f, 0f, -35f)), Prop("MinuteHand", PrimitiveType.Cube, V(-0.10f, 0.11f, -0.04f), V(0.06f, 0.52f, 0.04f), C(0.12f, 0.12f, 0.14f), false, V(0f, 0f, 55f)))
                || TryAddSemanticMnemonicProp(text, root, item, K("feather", "lightweight", "soft", "gentle", "\u7fbd", "\u8efd"), Prop("FeatherSpine", PrimitiveType.Capsule, V(0f, 0.04f, 0f), V(0.08f, 0.92f, 0.08f), C(0.92f, 0.88f, 0.72f), false, V(0f, 0f, -25f)), Prop("FeatherLeft", PrimitiveType.Cube, V(-0.18f, 0.08f, 0f), V(0.34f, 0.08f, 0.04f), C(0.78f, 0.86f, 0.92f), false, V(0f, 0f, -18f)), Prop("FeatherRight", PrimitiveType.Cube, V(0.18f, 0.18f, 0f), V(0.34f, 0.08f, 0.04f), C(0.86f, 0.92f, 0.96f), false, V(0f, 0f, 18f)))
                || TryAddSemanticMnemonicProp(text, root, item, K("stone", "rock", "weight", "heavy", "burden", "\u77f3", "\u91cd"), Prop("StoneBody", PrimitiveType.Sphere, V(0f, -0.04f, 0f), V(0.84f, 0.62f, 0.76f), C(0.45f, 0.46f, 0.45f)), Prop("StoneFacet", PrimitiveType.Cube, V(0.20f, 0.20f, -0.06f), V(0.32f, 0.08f, 0.34f), C(0.62f, 0.62f, 0.58f), false, V(0f, 0f, -24f)))
                || TryAddSemanticMnemonicProp(text, root, item, K("arrow", "path", "point", "direction", "route", "\u77e2", "\u65b9\u5411"), Prop("ArrowShaft", PrimitiveType.Cube, V(-0.10f, 0.02f, 0f), V(0.78f, 0.08f, 0.08f), color), Prop("ArrowHeadA", PrimitiveType.Cube, V(0.34f, 0.14f, 0f), V(0.34f, 0.08f, 0.08f), color, false, V(0f, 0f, 38f)), Prop("ArrowHeadB", PrimitiveType.Cube, V(0.34f, -0.10f, 0f), V(0.34f, 0.08f, 0.08f), color, false, V(0f, 0f, -38f)))
                || TryAddSemanticMnemonicProp(text, root, item, K("eye", "watch", "observe", "look", "visible", "\u76ee", "\u898b"), Prop("EyeWhite", PrimitiveType.Sphere, V(0f, 0.06f, 0f), V(0.86f, 0.42f, 0.18f), C(0.96f, 0.96f, 0.90f)), Prop("Iris", PrimitiveType.Sphere, V(0f, 0.06f, -0.08f), V(0.26f, 0.26f, 0.08f), color, true))
                || TryAddSemanticMnemonicProp(text, root, item, K("hand", "grab", "hold", "touch", "grasp", "\u624b", "\u63b4"), Prop("Palm", PrimitiveType.Sphere, V(0f, -0.06f, 0f), V(0.48f, 0.36f, 0.20f), C(0.94f, 0.70f, 0.52f)), Prop("FingerA", PrimitiveType.Capsule, V(-0.18f, 0.22f, 0f), V(0.10f, 0.46f, 0.10f), C(0.94f, 0.70f, 0.52f)), Prop("FingerB", PrimitiveType.Capsule, V(0.00f, 0.26f, 0f), V(0.10f, 0.54f, 0.10f), C(0.94f, 0.70f, 0.52f)), Prop("FingerC", PrimitiveType.Capsule, V(0.18f, 0.22f, 0f), V(0.10f, 0.46f, 0.10f), C(0.94f, 0.70f, 0.52f)))
                || TryAddSemanticMnemonicProp(text, root, item, K("web", "net", "mesh", "trap", "network", "\u7db2"), Prop("WebA", PrimitiveType.Cube, V(0f, 0.10f, 0f), V(0.92f, 0.035f, 0.035f), C(0.84f, 0.88f, 0.92f), false, V(0f, 0f, 0f)), Prop("WebB", PrimitiveType.Cube, V(0f, 0.10f, 0f), V(0.92f, 0.035f, 0.035f), C(0.84f, 0.88f, 0.92f), false, V(0f, 0f, 60f)), Prop("WebC", PrimitiveType.Cube, V(0f, 0.10f, 0f), V(0.92f, 0.035f, 0.035f), C(0.84f, 0.88f, 0.92f), false, V(0f, 0f, -60f)))
                || TryAddSemanticMnemonicProp(text, root, item, K("balance", "scale", "weigh", "justice", "equal", "\u79e4", "\u5929\u79e4"), Prop("ScaleStand", PrimitiveType.Cylinder, V(0f, -0.06f, 0f), V(0.08f, 0.70f, 0.08f), C(0.64f, 0.55f, 0.36f)), Prop("ScaleBeam", PrimitiveType.Cube, V(0f, 0.34f, 0f), V(1.05f, 0.06f, 0.06f), C(0.74f, 0.63f, 0.38f)), Prop("PanLeft", PrimitiveType.Cylinder, V(-0.45f, -0.08f, 0f), V(0.34f, 0.04f, 0.34f), C(0.72f, 0.65f, 0.50f)), Prop("PanRight", PrimitiveType.Cylinder, V(0.45f, -0.08f, 0f), V(0.34f, 0.04f, 0.34f), C(0.72f, 0.65f, 0.50f)))
                || TryAddSemanticMnemonicProp(text, root, item, K("coin", "money", "gold", "wealth", "price", "\u91d1", "\u30b3\u30a4\u30f3"), Prop("CoinA", PrimitiveType.Cylinder, V(-0.18f, 0.00f, 0f), V(0.36f, 0.08f, 0.36f), C(1.0f, 0.76f, 0.20f), true, V(90f, 0f, 0f)), Prop("CoinB", PrimitiveType.Cylinder, V(0.18f, 0.16f, 0f), V(0.32f, 0.08f, 0.32f), C(0.95f, 0.66f, 0.18f), true, V(90f, 0f, 0f)))
                || TryAddSemanticMnemonicProp(text, root, item, K("flower", "vine", "leaf", "grow", "bloom", "\u82b1", "\u8449", "\u8513"), Prop("Stem", PrimitiveType.Cylinder, V(0f, 0.00f, 0f), V(0.07f, 0.72f, 0.07f), C(0.32f, 0.56f, 0.28f)), Prop("PetalA", PrimitiveType.Sphere, V(-0.18f, 0.38f, 0f), V(0.28f, 0.18f, 0.22f), color, true), Prop("PetalB", PrimitiveType.Sphere, V(0.18f, 0.38f, 0f), V(0.28f, 0.18f, 0.22f), Color.Lerp(color, Color.white, 0.18f), true), Prop("Center", PrimitiveType.Sphere, V(0f, 0.34f, -0.02f), V(0.20f, 0.20f, 0.20f), C(1.0f, 0.78f, 0.22f), true))
                || TryAddSemanticMnemonicProp(text, root, item, K("crown", "king", "royal", "queen", "\u738b", "\u51a0"), Prop("CrownBand", PrimitiveType.Cube, V(0f, -0.10f, 0f), V(0.76f, 0.16f, 0.30f), C(1.0f, 0.74f, 0.18f), true), Prop("CrownPointA", PrimitiveType.Capsule, V(-0.28f, 0.16f, 0f), V(0.14f, 0.44f, 0.14f), C(1.0f, 0.78f, 0.24f), true), Prop("CrownPointB", PrimitiveType.Capsule, V(0f, 0.24f, 0f), V(0.16f, 0.56f, 0.16f), C(1.0f, 0.82f, 0.28f), true), Prop("CrownPointC", PrimitiveType.Capsule, V(0.28f, 0.16f, 0f), V(0.14f, 0.44f, 0.14f), C(1.0f, 0.78f, 0.24f), true))
                || TryAddSemanticMnemonicProp(text, root, item, K("heart", "love", "care", "warm", "\u5fc3", "\u611b"), Prop("HeartLeft", PrimitiveType.Sphere, V(-0.16f, 0.12f, 0f), V(0.36f, 0.36f, 0.24f), C(0.95f, 0.16f, 0.22f), true), Prop("HeartRight", PrimitiveType.Sphere, V(0.16f, 0.12f, 0f), V(0.36f, 0.36f, 0.24f), C(0.95f, 0.16f, 0.22f), true), Prop("HeartPoint", PrimitiveType.Cube, V(0f, -0.12f, 0f), V(0.36f, 0.36f, 0.22f), C(0.85f, 0.08f, 0.16f), true, V(0f, 0f, 45f)));
        }

        private bool TryAddSemanticMnemonicProp(string text, Transform root, MnemonicItemData item, string[] terms, params PrimitivePartSpec[] parts)
        {
            if (!ContainsAny(text, terms))
            {
                return false;
            }

            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                AddMnemonicPropPart(root, part.name, part.shape, part.localPosition, part.localScale, part.color, item, part.emissive, part.localEuler);
            }

            return true;
        }

        private struct PrimitivePartSpec
        {
            public string name; public PrimitiveType shape; public Vector3 localPosition; public Vector3 localScale; public Color color; public bool emissive; public Vector3 localEuler;
            public PrimitivePartSpec(string name, PrimitiveType shape, Vector3 localPosition, Vector3 localScale, Color color, bool emissive, Vector3 localEuler)
            {
                this.name = name; this.shape = shape; this.localPosition = localPosition; this.localScale = localScale; this.color = color; this.emissive = emissive; this.localEuler = localEuler;
            }
        }

        private static string[] K(params string[] terms) => terms;
        private static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);
        private static Color C(float r, float g, float b, float a = 1f) => new Color(r, g, b, a);
        private static PrimitivePartSpec Prop(string name, PrimitiveType shape, Vector3 localPosition, Vector3 localScale, Color color) => new PrimitivePartSpec(name, shape, localPosition, localScale, color, false, Vector3.zero);
        private static PrimitivePartSpec Prop(string name, PrimitiveType shape, Vector3 localPosition, Vector3 localScale, Color color, bool emissive) => new PrimitivePartSpec(name, shape, localPosition, localScale, color, emissive, Vector3.zero);
        private static PrimitivePartSpec Prop(string name, PrimitiveType shape, Vector3 localPosition, Vector3 localScale, Color color, bool emissive, Vector3 localEuler) => new PrimitivePartSpec(name, shape, localPosition, localScale, color, emissive, localEuler);

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
            builder.Append(item?.associationPrompt).Append(' ');
            builder.Append(item?.imagePrompt);

            return builder.ToString().ToLowerInvariant();
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

            vrHeadTrackingActive = TryInitializeVrRig();

            BuildVrStudyPanel();
            BuildVrPointer();
            UpdateVrStudyPanelText();
            statusMessage = vrHeadTrackingActive
                ? "VR study runtime ready. Use controller ray to inspect markers."
                : "VR study mode is enabled. Waiting for XR headset and controllers to become active.";
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
            if (runtimeCamera == null)
            {
                return;
            }

            if (vrRigRoot == null && !TryInitializeVrRig())
            {
                vrHeadTrackingActive = false;
                return;
            }

            if (TryGetXrNodePose(XRNode.Head, out var headLocalPosition, out var headLocalRotation))
            {
                runtimeCamera.transform.localPosition = NormalizeHeadLocalPosition(headLocalPosition);
                runtimeCamera.transform.localRotation = headLocalRotation;
                vrHeadTrackingActive = true;
            }
            else
            {
                vrHeadTrackingActive = false;
            }
        }

        private void HandleVrLocomotion()
        {
            if (vrRigRoot == null || runtimeCamera == null)
            {
                return;
            }

            var leftHand = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            if (!leftHand.isValid || !leftHand.TryGetFeatureValue(XRCommonUsages.primary2DAxis, out var axis))
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
            var pointedInteractable = FindStudyInteractable(ray, VrPointerDistance, out var itemHitPoint);
            var pointedButton = FindVrPanelButton(ray, VrPointerDistance, out var buttonHitPoint);

            var itemDistance = pointedInteractable != null ? Vector3.Distance(ray.origin, itemHitPoint) : float.PositiveInfinity;
            var buttonDistance = pointedButton != null ? Vector3.Distance(ray.origin, buttonHitPoint) : float.PositiveInfinity;
            var buttonHasPriority = pointedButton != null && buttonDistance <= itemDistance;
            var finalHitPoint = buttonHasPriority
                ? buttonHitPoint
                : (pointedInteractable != null ? itemHitPoint : ray.origin + ray.direction * VrPointerDistance);

            UpdateVrPointerVisual(ray.origin, finalHitPoint, pointedInteractable != null || pointedButton != null);
            UpdateVrPanelButtonHighlight(pointedButton);

            var rightHand = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            var leftHand = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            var triggerPressed =
                GetXrButton(rightHand, XRCommonUsages.triggerButton) ||
                GetXrAxisPressed(rightHand, XRCommonUsages.trigger) ||
                GetXrButton(leftHand, XRCommonUsages.triggerButton) ||
                GetXrAxisPressed(leftHand, XRCommonUsages.trigger);
            var primaryPressed =
                GetXrButton(rightHand, XRCommonUsages.primaryButton) ||
                GetXrButton(leftHand, XRCommonUsages.primaryButton);
            var gripPressed =
                GetXrButton(rightHand, XRCommonUsages.gripButton) ||
                GetXrAxisPressed(rightHand, XRCommonUsages.grip) ||
                GetXrButton(leftHand, XRCommonUsages.gripButton) ||
                GetXrAxisPressed(leftHand, XRCommonUsages.grip);
            var selectPressed = triggerPressed || primaryPressed;
            var capturePressed = primaryPressed || gripPressed;

            if (Time.unscaledTime < nextVrActionTime)
            {
                return;
            }

            if (selectPressed && buttonHasPriority && pointedButton != null)
            {
                HandleVrPanelButtonPress(pointedButton);
                nextVrActionTime = Time.unscaledTime + VrActionCooldownSeconds;
                return;
            }

            var pointingAtDifferentItem = pointedInteractable != null &&
                                          (selectedStudyItem == null || !string.Equals(pointedInteractable.Data.word, selectedStudyItem.word, StringComparison.Ordinal));

            if (selectPressed && pointedInteractable != null && (triggerPressed || pointingAtDifferentItem || !hasControllerRay))
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

        private void HandleVrPanelButtonPress(VrPanelButtonInteractable button)
        {
            if (button == null || !button.Enabled)
            {
                return;
            }

            switch (button.Action)
            {
                case VrPanelButtonAction.GenerateImageCue:
                    if (selectedStudyItem == null)
                    {
                        return;
                    }
                    if (generatingImageCueWords.Contains(selectedStudyItem.word))
                    {
                        imageGenerationStatus = $"The current image cue set for {selectedStudyItem.word} is still generating in the background.";
                    }
                    else
                    {
                        StartCoroutine(GenerateMnemonicImageCueRoutine(selectedStudyItem));
                    }
                    break;

                case VrPanelButtonAction.CaptureSnapshot:
                    if (selectedStudyItem == null)
                    {
                        return;
                    }
                    if (!isCapturingSnapshot)
                    {
                        StartCoroutine(CaptureMemorySnapshotRoutine(selectedStudyItem));
                    }
                    break;

                case VrPanelButtonAction.PreviousImageCue:
                    if (selectedStudyItem != null)
                    {
                        TryCycleDisplayedImageCueCandidate(selectedStudyItem, -1);
                    }
                    break;

                case VrPanelButtonAction.NextImageCue:
                    if (selectedStudyItem != null)
                    {
                        TryCycleDisplayedImageCueCandidate(selectedStudyItem, 1);
                    }
                    break;

                case VrPanelButtonAction.AdvancePhase:
                    if (!midTestCompleted && memorizedWords.Count >= MidTestTriggerCount)
                    {
                        BeginSnapshotTest(false);
                    }
                    else if (midTestCompleted && !finalTestCompleted && memorizedWords.Count == currentItems.Count && currentItems.Count >= MidTestTriggerCount)
                    {
                        BeginSnapshotTest(true);
                    }
                    break;
            }
        }

        private void UpdateVrPanelButtonHighlight(VrPanelButtonInteractable hoveredButton)
        {
            UpdateVrPanelButtonVisual(vrGenerateButton, hoveredButton == vrGenerateButton);
            UpdateVrPanelButtonVisual(vrPreviousImageButton, hoveredButton == vrPreviousImageButton);
            UpdateVrPanelButtonVisual(vrNextImageButton, hoveredButton == vrNextImageButton);
            UpdateVrPanelButtonVisual(vrCaptureButton, hoveredButton == vrCaptureButton);
            UpdateVrPanelButtonVisual(vrAdvanceButton, hoveredButton == vrAdvanceButton);
        }

        private static void UpdateVrPanelButtonVisual(VrPanelButtonInteractable button, bool hovered)
        {
            if (button == null || button.Background == null)
            {
                return;
            }

            if (!button.Enabled)
            {
                button.Background.color = new Color(0.11f, 0.14f, 0.18f, 0.85f);
                return;
            }

            button.Background.color = hovered
                ? new Color(0.30f, 0.38f, 0.48f, 0.98f)
                : new Color(0.18f, 0.22f, 0.28f, 0.96f);
        }

        private Ray BuildVrPointerRay(out bool hasControllerRay)
        {
            if (vrRigRoot == null)
            {
                TryInitializeVrRig();
            }

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

        private bool TryInitializeVrRig()
        {
            if (vrRigRoot != null || runtimeCamera == null)
            {
                return vrRigRoot != null;
            }

            if (!TryGetXrNodePose(XRNode.Head, out var headLocalPosition, out var headLocalRotation))
            {
                return false;
            }

            var cameraPose = runtimeCamera.transform;
            var normalizedHeadLocalPosition = NormalizeHeadLocalPosition(headLocalPosition);
            vrRigRoot = new GameObject("VRStudyRig").transform;
            vrRigRoot.position = new Vector3(
                cameraPose.position.x,
                cameraPose.position.y - normalizedHeadLocalPosition.y + 0.16f,
                cameraPose.position.z);
            vrRigRoot.rotation = Quaternion.Euler(0f, cameraPose.rotation.eulerAngles.y, 0f);
            runtimeCamera.transform.SetParent(vrRigRoot, false);
            runtimeCamera.transform.localPosition = normalizedHeadLocalPosition;
            runtimeCamera.transform.localRotation = headLocalRotation;
            vrHeadTrackingActive = true;
            statusMessage = "XR headset detected. Controller ray and head tracking are active.";
            return true;
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
            SetObjectMaterial(vrPointerReticle, new Color(0.42f, 0.86f, 1f, 0.9f));
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

        private static void SetObjectMaterial(GameObject target, Color color)
        {
            if (target == null)
            {
                return;
            }

            var renderer = target.GetComponent<Renderer>();
            if (renderer == null)
            {
                return;
            }

            var shader =
                Shader.Find("Universal Render Pipeline/Unlit") ??
                Shader.Find("Standard") ??
                Shader.Find("Sprites/Default");
            if (shader == null)
            {
                return;
            }

            var material = new Material(shader);
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }

            renderer.material = material;
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
            rect.sizeDelta = new Vector2(760f, 760f);
            panel.transform.localScale = Vector3.one * 0.00155f;

            var image = panel.AddComponent<Image>();
            image.color = new Color(0.05f, 0.07f, 0.10f, 0.88f);

            vrProgressText = CreateVrPanelText(panel.transform, "Progress", 16, new Rect(26f, -34f, 708f, 44f), new Color(0.76f, 0.84f, 0.94f));
            vrTitleText = CreateVrPanelText(panel.transform, "Title", 34, new Rect(26f, -96f, 708f, 58f), Color.white);
            vrMeaningText = CreateVrPanelText(panel.transform, "Meaning", 20, new Rect(26f, -152f, 708f, 44f), new Color(0.90f, 0.94f, 1f));
            vrAnchorText = CreateVrPanelText(panel.transform, "Anchor", 18, new Rect(26f, -198f, 708f, 36f), new Color(0.70f, 0.78f, 0.90f));
            vrCueText = CreateVrPanelText(panel.transform, "Mnemonic", 18, new Rect(26f, -282f, 708f, 170f), new Color(0.94f, 0.96f, 1f));
            vrStoryText = null;
            vrPreviewHeaderText = CreateVrPanelText(panel.transform, "PreviewHeader", 20, new Rect(26f, -462f, 708f, 28f), Color.white);
            vrPreviewImage = CreateVrPanelImage(panel.transform, "PreviewImage", new Rect(26f, -494f, 290f, 170f), new Color(0.14f, 0.16f, 0.20f, 0.98f));
            vrPreviewInfoText = CreateVrPanelText(panel.transform, "PreviewInfo", 15, new Rect(336f, -494f, 398f, 84f), new Color(0.84f, 0.88f, 0.94f));
            vrPreviousImageButton = CreateVrPanelButton(panel.transform, "PreviousImageButton", "Previous Image", new Rect(336f, -586f, 190f, 38f), VrPanelButtonAction.PreviousImageCue);
            vrNextImageButton = CreateVrPanelButton(panel.transform, "NextImageButton", "Next Image", new Rect(544f, -586f, 190f, 38f), VrPanelButtonAction.NextImageCue);
            vrGenerateButton = CreateVrPanelButton(panel.transform, "GenerateButton", "Generate Image Cue", new Rect(336f, -634f, 398f, 42f), VrPanelButtonAction.GenerateImageCue);
            vrCaptureButton = CreateVrPanelButton(panel.transform, "CaptureButton", "Capture Memory Snapshot", new Rect(26f, -634f, 290f, 42f), VrPanelButtonAction.CaptureSnapshot);
            vrAdvanceButton = null;
            vrActionText = CreateVrPanelText(panel.transform, "Action", 16, new Rect(26f, -684f, 708f, 24f), new Color(0.95f, 0.86f, 0.48f));

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

        private RawImage CreateVrPanelImage(Transform parent, string name, Rect rect, Color color)
        {
            var imageObject = new GameObject(name);
            imageObject.transform.SetParent(parent, false);

            var image = imageObject.AddComponent<RawImage>();
            image.texture = Texture2D.whiteTexture;
            image.color = color;

            var imageRect = imageObject.GetComponent<RectTransform>();
            imageRect.anchorMin = new Vector2(0f, 1f);
            imageRect.anchorMax = new Vector2(0f, 1f);
            imageRect.pivot = new Vector2(0f, 1f);
            imageRect.anchoredPosition = new Vector2(rect.x, rect.y);
            imageRect.sizeDelta = new Vector2(rect.width, rect.height);
            return image;
        }

        private VrPanelButtonInteractable CreateVrPanelButton(Transform parent, string name, string label, Rect rect, VrPanelButtonAction action)
        {
            var buttonObject = new GameObject(name);
            buttonObject.transform.SetParent(parent, false);

            var buttonImage = buttonObject.AddComponent<Image>();
            buttonImage.color = new Color(0.18f, 0.22f, 0.28f, 0.96f);

            var buttonRect = buttonObject.GetComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(0f, 1f);
            buttonRect.anchorMax = new Vector2(0f, 1f);
            buttonRect.pivot = new Vector2(0f, 1f);
            buttonRect.anchoredPosition = new Vector2(rect.x, rect.y);
            buttonRect.sizeDelta = new Vector2(rect.width, rect.height);

            var labelObject = new GameObject("Label");
            labelObject.transform.SetParent(buttonObject.transform, false);
            var labelText = labelObject.AddComponent<Text>();
            labelText.font = GetLabelFont();
            labelText.fontSize = 17;
            labelText.color = Color.white;
            labelText.alignment = TextAnchor.MiddleCenter;
            labelText.horizontalOverflow = HorizontalWrapMode.Wrap;
            labelText.verticalOverflow = VerticalWrapMode.Truncate;
            labelText.text = label;

            var labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(10f, 6f);
            labelRect.offsetMax = new Vector2(-10f, -6f);

            var collider = buttonObject.AddComponent<BoxCollider>();
            collider.center = new Vector3(rect.width * 0.5f, -rect.height * 0.5f, 0f);
            collider.size = new Vector3(rect.width, rect.height, 18f);

            var interactable = buttonObject.AddComponent<VrPanelButtonInteractable>();
            interactable.Action = action;
            interactable.Background = buttonImage;
            interactable.Label = labelText;
            return interactable;
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
                vrMeaningText.text = "Aim the controller ray at a floating marker and press Trigger or A.";
                vrAnchorText.text = "Detailed cue text appears only while the selected object stays in view.";
                vrCueText.text = string.Empty;
                if (vrStoryText != null)
                {
                    vrStoryText.text = string.Empty;
                }
                vrPreviewHeaderText.text = string.Empty;
                vrPreviewInfoText.text = string.Empty;
                SetVrPanelPreview(vrPreviewImage, null);
                SetVrButtonState(vrGenerateButton, false, "Generate Image Cue");
                SetVrButtonState(vrPreviousImageButton, false, "Previous Image");
                SetVrButtonState(vrNextImageButton, false, "Next Image");
                SetVrButtonState(vrCaptureButton, false, "Capture Memory Snapshot");
                vrActionText.text = vrHeadTrackingActive
                    ? "Trigger or A: inspect marker"
                    : "No XR headset detected. Use desktop mouse and keyboard for now.";
                return;
            }

            vrTitleText.text = selectedStudyItem.word;
            vrMeaningText.text = GetDisplayMeaningText(selectedStudyItem);
            vrAnchorText.text = "Anchor: " + selectedStudyItem.anchorLabel;
            vrCueText.text = BuildVrSectionText("Mnemonic", selectedStudyItem.mnemonic);
            if (vrStoryText != null)
            {
                vrStoryText.text = string.Empty;
            }

            var hasSnapshot = memorySnapshots.ContainsKey(selectedStudyItem.word);
            var hasGeneratedCue = mnemonicImageCues.TryGetValue(selectedStudyItem.word, out var cueTexture) && cueTexture != null;
            var isGeneratingCue = generatingImageCueWords.Contains(selectedStudyItem.word);
            var imageChoiceCount = imageCueCandidateResults.TryGetValue(selectedStudyItem.word, out var imageChoices) && imageChoices != null
                ? imageChoices.Count
                : 0;
            if (hasSnapshot && memorySnapshots.TryGetValue(selectedStudyItem.word, out var snapshotTexture) && snapshotTexture != null)
            {
                vrPreviewHeaderText.text = "Stored Snapshot";
                vrPreviewInfoText.text = "This memory has already been captured. You can replace it with A / Grip.";
                SetVrPanelPreview(vrPreviewImage, snapshotTexture);
            }
            else if (hasGeneratedCue)
            {
                vrPreviewHeaderText.text = "Generated Image Cue";
                vrPreviewInfoText.text = BuildVrImageCuePreviewInfo(selectedStudyItem, isGeneratingCue);
                SetVrPanelPreview(vrPreviewImage, cueTexture);
            }
            else
            {
                vrPreviewHeaderText.text = "Generated Image Cue";
                vrPreviewInfoText.text = isGeneratingCue
                    ? "Generating image for this item..."
                    : "No generated image is available for this item yet.";
                SetVrPanelPreview(vrPreviewImage, null);
            }

            SetVrButtonState(
                vrGenerateButton,
                !isGeneratingCue,
                isGeneratingCue
                    ? "Generating Image Cue..."
                    : hasGeneratedCue
                        ? "Regenerate Image Set"
                        : "Generate Image Cue");

            var canReviewImages = hasGeneratedCue && imageChoiceCount > 1;
            SetVrButtonState(vrPreviousImageButton, canReviewImages, "Previous Image");
            SetVrButtonState(vrNextImageButton, canReviewImages, "Next Image");

            SetVrButtonState(
                vrCaptureButton,
                !isCapturingSnapshot,
                hasSnapshot ? "Replace Stored Snapshot" : "Capture Memory Snapshot");

            vrActionText.text = hasSnapshot
                ? "Use the button or press A / Grip to replace the stored snapshot."
                : "Use the buttons or press A / Grip to capture this memory.";
        }

        private static void SetVrButtonState(VrPanelButtonInteractable button, bool enabled, string label)
        {
            if (button == null)
            {
                return;
            }

            button.gameObject.SetActive(true);
            button.Enabled = enabled;

            if (button.Label != null)
            {
                button.Label.text = label;
                button.Label.color = enabled ? Color.white : new Color(0.72f, 0.76f, 0.82f, 0.92f);
            }

            if (button.Background != null)
            {
                button.Background.color = enabled
                    ? new Color(0.18f, 0.22f, 0.28f, 0.96f)
                    : new Color(0.11f, 0.14f, 0.18f, 0.85f);
            }
        }

        private static string BuildVrSectionText(string heading, string text)
        {
            var displayText = CleanUserFacingMnemonicText(text);
            if (!string.IsNullOrWhiteSpace(displayText))
            {
                return $"{heading}: {displayText}";
            }

            return $"{heading}:";
        }

        private string BuildVrImageCuePreviewInfo(MnemonicItemData item, bool isGeneratingCue)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.word))
            {
                return "Look at this image, then press A / Grip to store it into the snapshot panel.";
            }

            if (!imageCueCandidateResults.TryGetValue(item.word, out var results) || results == null || results.Count == 0)
            {
                return "Look at this image, then press A / Grip to store it into the snapshot panel.";
            }

            var displayedIndex = Mathf.Clamp(GetDisplayedImageCueCandidateIndex(item.word), 0, results.Count - 1);
            var text = $"Showing reviewer image choice {displayedIndex + 1}/{results.Count}.";
            if (isGeneratingCue && results.Count < BufferedImageCueResultCount)
            {
                text += " More single-pass image choices are being generated and scored in the background.";
            }

            text += " Use Previous/Next to review choices. A / Grip stores the currently shown image.";
            return text;
        }

        private static void SetVrPanelPreview(RawImage image, Texture texture)
        {
            if (image == null)
            {
                return;
            }

            if (texture == null)
            {
                image.texture = Texture2D.whiteTexture;
                image.color = new Color(0.14f, 0.16f, 0.20f, 0.98f);
                return;
            }

            image.texture = texture;
            image.color = Color.white;
        }

        private static bool TryGetXrNodePose(XRNode node, out Vector3 localPosition, out Quaternion localRotation)
        {
            localPosition = Vector3.zero;
            localRotation = Quaternion.identity;

            var device = InputDevices.GetDeviceAtXRNode(node);
            if (device.isValid)
            {
                var hasPosition = device.TryGetFeatureValue(XRCommonUsages.devicePosition, out localPosition);
                var hasRotation = device.TryGetFeatureValue(XRCommonUsages.deviceRotation, out localRotation);

                if (node == XRNode.Head)
                {
                    hasPosition |= device.TryGetFeatureValue(XRCommonUsages.centerEyePosition, out localPosition);
                    hasRotation |= device.TryGetFeatureValue(XRCommonUsages.centerEyeRotation, out localRotation);
                }

                if (hasPosition || hasRotation)
                {
                    return true;
                }
            }

            var nodeStates = new List<XRNodeState>();
            InputTracking.GetNodeStates(nodeStates);
            for (var i = 0; i < nodeStates.Count; i++)
            {
                if (nodeStates[i].nodeType != node)
                {
                    continue;
                }

                var hasPosition = nodeStates[i].TryGetPosition(out localPosition);
                var hasRotation = nodeStates[i].TryGetRotation(out localRotation);
                if (hasPosition || hasRotation)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool GetXrButton(XRInputDevice device, InputFeatureUsage<bool> usage)
        {
            return device.isValid && device.TryGetFeatureValue(usage, out var pressed) && pressed;
        }

        private static bool GetXrAxisPressed(XRInputDevice device, InputFeatureUsage<float> usage, float threshold = 0.6f)
        {
            return device.isValid && device.TryGetFeatureValue(usage, out var value) && value >= threshold;
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
            studyDetailScroll = Vector2.zero;
            viewedWords.Add(selectedStudyItem.word);
            LogInteraction("inspect", selectedStudyItem.word, selectedStudyItem.anchorId, details);
        }

        private static StudyInteractable FindStudyInteractable(Ray ray, float maxDistance, out Vector3 hitPoint)
        {
            hitPoint = ray.origin + ray.direction * maxDistance;
            var hits = Physics.RaycastAll(ray, maxDistance);
            if (hits == null || hits.Length == 0)
            {
                return null;
            }

            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            hitPoint = hits[0].point;
            for (var i = 0; i < hits.Length; i++)
            {
                var interactable = hits[i].collider.GetComponent<StudyInteractable>();
                if (interactable == null)
                {
                    interactable = hits[i].collider.GetComponentInParent<StudyInteractable>();
                }

                if (interactable != null)
                {
                    hitPoint = hits[i].point;
                    return interactable;
                }
            }

            return null;
        }

        private static VrPanelButtonInteractable FindVrPanelButton(Ray ray, float maxDistance, out Vector3 hitPoint)
        {
            hitPoint = ray.origin + ray.direction * maxDistance;
            var hits = Physics.RaycastAll(ray, maxDistance);
            if (hits == null || hits.Length == 0)
            {
                return null;
            }

            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            for (var i = 0; i < hits.Length; i++)
            {
                var button = hits[i].collider.GetComponent<VrPanelButtonInteractable>();
                if (button == null)
                {
                    button = hits[i].collider.GetComponentInParent<VrPanelButtonInteractable>();
                }

                if (button != null && button.Enabled)
                {
                    hitPoint = hits[i].point;
                    return button;
                }
            }

            return null;
        }

        private void UpdateSelectedStudyItemVisibility()
        {
            if (selectedStudyItem == null)
            {
                return;
            }

            if (ShouldHideSelectedStudyItem())
            {
                selectedStudyItem = null;
                studyDetailScroll = Vector2.zero;
                nextVrActionTime = 0f;
            }
        }

        private bool ShouldHideSelectedStudyItem()
        {
            if (runtimeCamera == null || selectedStudyItem == null)
            {
                return false;
            }

            if (!studyItemTargets.TryGetValue(selectedStudyItem.word, out var target) || target == null)
            {
                return false;
            }

            var targetPosition = target.position + Vector3.up * 0.08f;
            var toTarget = targetPosition - runtimeCamera.transform.position;
            if (toTarget.sqrMagnitude > StudyDetailMaxDistance * StudyDetailMaxDistance)
            {
                return true;
            }

            var distance = toTarget.magnitude;
            if (distance < 0.001f)
            {
                return false;
            }

            var facingDot = Vector3.Dot(runtimeCamera.transform.forward.normalized, toTarget / distance);
            return facingDot < StudyDetailFacingDotThreshold;
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
                var interactable = FindStudyInteractable(ray, 100f, out _);
                if (interactable != null)
                {
                    SelectStudyItem(interactable.Data, "Clicked mnemonic object.");
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

        private static void ShuffleList<T>(List<T> list, System.Random rng)
        {
            if (list == null || rng == null)
            {
                return;
            }

            for (int i = list.Count - 1; i > 0; i--)
            {
                var swapIndex = rng.Next(i + 1);
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

            var parts = new List<string>();
            if (usedPreGeneratedForCurrentSession)
            {
                parts.Add("Pre-generated Catalog");
            }

            if (usedLiveLlmForCurrentSession)
            {
                parts.Add(GetCurrentLiveMnemonicProviderLabel());
            }

            if (usedLocalFallbackForCurrentSession)
            {
                parts.Add("Local Story Fallback");
            }

            if (parts.Count > 0)
            {
                return string.Join(" + ", parts);
            }

            return GetCurrentLiveMnemonicProviderLabel();
        }

        private string ResolveLlmModelLabelForExport()
        {
            if (condition == ExperimentCondition.SelfGenerated)
            {
                return "self-authored";
            }

            if (usedLiveLlmForCurrentSession)
            {
                var modelLabel = string.IsNullOrWhiteSpace(liveMnemonicModelForCurrentSession)
                    ? GetSelectedLiveMnemonicModelLabel()
                    : liveMnemonicModelForCurrentSession;
                return usedLocalFallbackForCurrentSession
                    ? modelLabel + " + local story fallback"
                    : modelLabel;
            }

            if (usedPreGeneratedForCurrentSession && usedLocalFallbackForCurrentSession)
            {
                return "pre-generated catalog + local story fallback";
            }

            if (usedPreGeneratedForCurrentSession)
            {
                return "pre-generated catalog";
            }

            if (usedLocalFallbackForCurrentSession)
            {
                return "local story fallback";
            }

            return GetSelectedLiveMnemonicModelLabel();
        }

        private string GetCurrentLiveMnemonicProviderLabel()
        {
            return string.IsNullOrWhiteSpace(liveMnemonicProviderLabelForCurrentSession)
                ? GetSelectedLiveMnemonicProviderLabel()
                : liveMnemonicProviderLabelForCurrentSession;
        }

        private string GetSelectedLiveMnemonicProviderLabel()
        {
            return providerMode == LlmProviderMode.GeminiOnline ? "Gemini Online" : "Ollama Local";
        }

        private string GetSelectedLiveMnemonicModelLabel()
        {
            return providerMode == LlmProviderMode.GeminiOnline ? geminiModel : ollamaModel;
        }

        private string GetSelectedLiveMnemonicSourceTag()
        {
            return providerMode == LlmProviderMode.GeminiOnline ? "gemini_live" : "ollama_live";
        }

        private string ResolveGeminiApiKey()
        {
            if (!string.IsNullOrWhiteSpace(geminiApiKey))
            {
                return geminiApiKey.Trim();
            }

            var key = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
            if (!string.IsNullOrWhiteSpace(key))
            {
                return key.Trim();
            }

            key = Environment.GetEnvironmentVariable("GOOGLE_API_KEY");
            return string.IsNullOrWhiteSpace(key) ? string.Empty : key.Trim();
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

            if (usedPreGeneratedForCurrentSession && usedLiveLlmForCurrentSession)
            {
                var liveUse = liveGeneratedMnemonicCount > 0
                    ? $"for {liveGeneratedMnemonicCount} missing item(s)"
                    : "for regeneration";
                var fallbackUse = localFallbackMnemonicCount > 0
                    ? $" It also filled {localFallbackMnemonicCount} item(s) with local story-only fallback."
                    : string.Empty;
                return $"This run loaded {preGeneratedMnemonicHitCount} pre-generated item(s) and used {GetCurrentLiveMnemonicProviderLabel()} {liveUse}.{fallbackUse}";
            }

            if (usedPreGeneratedForCurrentSession && usedLocalFallbackForCurrentSession)
            {
                return $"This run loaded {preGeneratedMnemonicHitCount} pre-generated item(s) and filled {localFallbackMnemonicCount} missing item(s) with local story-only fallback.";
            }

            if (usedPreGeneratedForCurrentSession)
            {
                return $"This run uses {preGeneratedMnemonicHitCount} pre-generated word x furniture mnemonic item(s).";
            }

            if (usedLocalFallbackForCurrentSession && usedLiveLlmForCurrentSession)
            {
                return $"This run used {GetCurrentLiveMnemonicProviderLabel()} for {liveGeneratedMnemonicCount} item(s) and local story-only fallback for {localFallbackMnemonicCount} item(s).";
            }

            if (IsUsingLiveLlm())
            {
                return $"This run is using a direct {GetCurrentLiveMnemonicProviderLabel()} response.";
            }

            if (usedLocalFallbackForCurrentSession)
            {
                return $"This run filled {localFallbackMnemonicCount} item(s) with local story-only fallback because catalog coverage or live generation was unavailable.";
            }

            if (!string.IsNullOrWhiteSpace(generationError))
            {
                return "Mnemonic generation failed before a fallback item could be produced.";
            }

            return $"This condition is configured for direct {GetSelectedLiveMnemonicProviderLabel()} generation only.";
        }

        private static Texture2D MakeTexture(Color color)
        {
            var texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
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

        private static string CleanUserFacingMnemonicText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var cleaned = StripUserFacingEnglishAnchorLead(text.Trim());
            return cleaned.Trim();
        }

        private static string StripUserFacingEnglishAnchorLead(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var trimmed = text.Trim();
            var lower = trimmed.ToLowerInvariant();
            var prefixes = new[]
            {
                "at the ",
                "at ",
                "centered on the ",
                "centered on ",
                "using the ",
                "using "
            };

            for (int i = 0; i < prefixes.Length; i++)
            {
                var prefix = prefixes[i];
                if (!lower.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                var commaIndex = trimmed.IndexOf(',');
                if (commaIndex < 0 || commaIndex > 80)
                {
                    continue;
                }

                var prefixSegment = lower.Substring(0, commaIndex);
                if (prefix.StartsWith("using", StringComparison.Ordinal)
                    && !prefixSegment.Contains("memory location"))
                {
                    continue;
                }

                return CapitalizeFirst(trimmed.Substring(commaIndex + 1));
            }

            return trimmed;
        }

        private static string CapitalizeFirst(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var trimmed = text.Trim();
            return trimmed.Length == 1
                ? trimmed.ToUpperInvariant()
                : char.ToUpperInvariant(trimmed[0]) + trimmed.Substring(1);
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

        private void DrawTextSection(string heading, string text, GUIStyle headingStyle, GUIStyle bodyStyle)
        {
            GUILayout.Label(heading, headingStyle);
            var displayText = CleanUserFacingMnemonicText(text);
            if (!string.IsNullOrWhiteSpace(displayText))
            {
                GUILayout.Label(displayText, bodyStyle);
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
            public int batch_size;
            public int n_iter;
            public bool tiling;
            public bool do_not_save_grid;
            public bool send_images;
            public bool save_images;
            public StableDiffusionOverrideSettings override_settings;
            public bool override_settings_restore_afterwards;
        }

        [Serializable]
        private sealed class StableDiffusionTxt2ImgResponse
        {
            public string[] images;
        }

        [Serializable]
        private sealed class StableDiffusionErrorResponse
        {
            public string error;
            public string detail;
        }

        private sealed class ImagePromptCandidate
        {
            public int index;
            public string label;
            public string rawPrompt;
            public string fullPrompt;
        }

        private sealed class ImageCueCatalogBuildPair
        {
            public WordEntry word;
            public string anchorType;
        }

        private sealed class ImageCueCandidateResult
        {
            public int listIndex;
            public int candidateIndex;
            public string label;
            public string innerLabel;
            public string rawPrompt;
            public string fullPrompt;
            public Texture2D texture;
            public ImageCueValidationResult validation;
            public int score;
            public bool pass;
            public bool validationComplete;
            public string reason;
            public List<ImageCueInnerCandidateResult> innerCandidates = new();
        }

        private sealed class ImageCueInnerCandidateResult
        {
            public int index;
            public string label;
            public string rawPrompt;
            public string fullPrompt;
            public string imagePath;
            public ImageCueValidationResult validation;
            public int score;
            public bool pass;
            public bool validationComplete;
            public string reason;
        }

        [Serializable]
        private sealed class OllamaVisionOptions
        {
            public float temperature;
            public int num_predict;
        }

        [Serializable]
        private sealed class OllamaVisionGenerateRequest
        {
            public string model;
            public string prompt;
            public bool stream;
            public string format;
            public string[] images;
            public OllamaVisionOptions options;
        }

        [Serializable]
        private sealed class OllamaVisionGenerateResponse
        {
            public string response;
            public string error;
        }

        [Serializable]
        private sealed class ImageCueValidationResult
        {
            public bool pass;
            public bool anchor_visible;
            public bool cue_visible;
            public bool focus_ok;
            public bool meaning_specific;
            public bool foreground_clear;
            public bool anchor_interaction;
            public bool simple_scene;
            public bool familiar_objects;
            public bool novel_possible_relation;
            public bool no_room_overview;
            public bool single_continuous_image;
            public bool no_split_screen_or_collage;
            public bool abstract_or_iconic;
            public string caption;
            public string reason;
            public string anchor_evidence;
            public string cue_evidence;
            public string contact_evidence;
            public string[] missing_or_wrong;
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

    public enum VrPanelButtonAction
    {
        GenerateImageCue,
        CaptureSnapshot,
        PreviousImageCue,
        NextImageCue,
        AdvancePhase
    }

    public sealed class VrPanelButtonInteractable : MonoBehaviour
    {
        public VrPanelButtonAction Action;
        public Image Background;
        public Text Label;
        public bool Enabled = true;
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
