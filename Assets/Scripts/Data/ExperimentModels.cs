using System;
using System.Collections.Generic;
using UnityEngine;

namespace MemPalaceLLM
{
    public enum ExperimentStage
    {
        Setup,
        RoomBuilder,
        RoomFamiliarization,
        PreTest,
        Generation,
        StoryAuthoring,
        SelfAuthoring,
        Study,
        Recall,
        Questionnaire,
        Result
    }

    public enum ExperimentCondition
    {
        SelfRoomSelfStory = 0,
        SelfRoomLlmStory = 1,
        DefaultRoomSelfStory = 2,
        DefaultRoomLlmStory = 3
    }

    public enum LlmProviderMode
    {
        OllamaLocal,
        GeminiOnline
    }

    [Serializable]
    public class DemoDataLibrary
    {
        public List<WordSetDefinition> wordSets = new();
        public List<SampleMnemonicSet> sampleMnemonics = new();
    }

    [Serializable]
    public class WordSetDefinition
    {
        public string setId;
        public string displayName;
        public string description;
        public List<WordEntry> words = new();
    }

    [Serializable]
    public class WordEntry
    {
        public string word;
        public string meaning;
    }

    [Serializable]
    public class SampleMnemonicSet
    {
        public string setId;
        public List<SampleMnemonicItem> items = new();
    }

    [Serializable]
    public class SampleMnemonicItem
    {
        public string word;
        public string anchorId;
        public string anchorType;
        public string mnemonicSource;
        public string visualCue;
        public string mainCueObject;
        public string associationPrompt;
        public string mnemonic;
        public string mnemonicMode;
        public bool hookAccepted;
        public int hookScore;
        public string hookReason;
        public string mnemonicHook;
        public string storyCue;
        public string imagePrompt;
        public List<string> imagePromptCandidates = new();
        public CueBlueprintData cueBlueprint;
        public string selectedImagePrompt;
        public int selectedImageCandidateIndex = -1;
        public string imageSelectionReason;
        public string imageCuePath;
        public string objectShape;
        public string colorHex;
        public List<VisualObjectSpec> visualObjects = new();
    }

    [Serializable]
    public class VisualObjectSpec
    {
        public string label;
        public string primitiveShape;
        public string colorHex;
        public Vector3 localPosition;
        public Vector3 scale;
        public string effect;
    }

    [Serializable]
    public class CueBlueprintData
    {
        public string targetMeaning;
        public string visualSceneCore;
        public string mainObject;
        public string anchorRelation;
        public string relativeSize;
        public string mainActionOrState;
        public List<string> visibleObjects = new();
        public string mnemonicHookNote;
        public string mnemonicMode;
    }

    [Serializable]
    public class MnemonicItemData
    {
        public string word;
        public string meaning;
        public string anchorId;
        public string anchorLabel;
        public string anchorType;
        public string mnemonicSource;
        public string visualCue;
        public string mainCueObject;
        public string associationPrompt;
        public string mnemonic;
        public string mnemonicMode;
        public bool hookAccepted;
        public int hookScore;
        public string hookReason;
        public string mnemonicHook;
        public string storyCue;
        public string imagePrompt;
        public List<string> imagePromptCandidates = new();
        public CueBlueprintData cueBlueprint;
        public string selectedImagePrompt;
        public int selectedImageCandidateIndex = -1;
        public string imageSelectionReason;
        public string imageCuePath;
        public string objectShape;
        public string colorHex;
        public List<VisualObjectSpec> visualObjects = new();
    }

    [Serializable]
    public class WordImageItemData
    {
        public string word;
        public string meaning;
        public string anchorId;
        public string anchorLabel;
        public string anchorType;
        public int storyOrder;
        public string storySegment;
        public string imageResourcePath;
        public string imageFilePath;
        public bool imageLoaded;
    }

    [Serializable]
    public class StorySessionData
    {
        public string fullStory;
        public string storySource;
        public string storyProvider;
        public string storyModel;
        public string generatedAtUtc;
        public List<WordImageItemData> orderedItems = new();
    }

    [Serializable]
    public class RecallResponse
    {
        public string word;
        public string expectedMeaning;
        public string promptMeaning;
        public string answerMeaning;
        public string answerWord;
        public bool meaningCorrect;
        public bool wordCorrect;
    }

    [Serializable]
    public class SnapshotTestResponse
    {
        public string phase;
        public string targetWord;
        public string targetAnchorId;
        public List<string> optionWords = new();
        public List<string> optionLabels = new();
        public string chosenWord;
        public string chosenLabel;
        public float responseTimeSeconds;
        public bool isCorrect;
    }

    [Serializable]
    public class QuestionnaireResponse
    {
        public int mentalDemand = 10;
        public int physicalDemand = 5;
        public int temporalDemand = 8;
        public int performance = 12;
        public int effort = 10;
        public int frustration = 6;
        public int vividness = 4;
        public int helpfulness = 4;
        public int trust = 4;
        public string notes = string.Empty;
    }

    [Serializable]
    public class InteractionLog
    {
        public string timestampUtc;
        public string type;
        public string word;
        public string anchorId;
        public string details;
    }

    [Serializable]
    public class ExportWordEntry
    {
        public string word;
        public string meaning;
        public int storyOrder;
        public string anchorId;
        public string anchorLabel;
        public string anchorType;
        public bool furnitureAssigned;
        public string mnemonicSource;
        public string cue;
        public string mainCueObject;
        public string associationPrompt;
        public string mnemonic;
        public string mnemonicMode;
        public bool hookAccepted;
        public int hookScore;
        public string hookReason;
        public string mnemonicHook;
        public string storyCue;
        public string imagePrompt;
        public List<string> imagePromptCandidates = new();
        public CueBlueprintData cueBlueprint;
        public string selectedImagePrompt;
        public int selectedImageCandidateIndex = -1;
        public string imageSelectionReason;
        public string imageCuePath;
        public string sceneSnapshotPath;
        public List<ImageCueResultExport> imageCueResults = new();
        public List<VisualObjectSpec> visualObjects = new();
    }

    [Serializable]
    public class ImageCueResultExport
    {
        public int listIndex;
        public string resultLabel;
        public int selectedInnerCandidateIndex;
        public string selectedInnerLabel;
        public string selectedRawPrompt;
        public string selectedFullPrompt;
        public string selectedImagePath;
        public int score;
        public bool pass;
        public bool validationComplete;
        public string reason;
        public List<ImageCueInnerCandidateExport> innerCandidates = new();
    }

    [Serializable]
    public class ImageCueInnerCandidateExport
    {
        public int index;
        public string label;
        public string rawPrompt;
        public string fullPrompt;
        public string imagePath;
        public int score;
        public bool pass;
        public bool validationComplete;
        public string reason;
        public ImageCueValidationExport validation;
    }

    [Serializable]
    public class ImageCueValidationExport
    {
        public bool pass;
        public bool anchorVisible;
        public bool cueVisible;
        public bool focusOk;
        public bool meaningSpecific;
        public bool foregroundClear;
        public bool anchorInteraction;
        public bool simpleScene;
        public bool familiarObjects;
        public bool novelPossibleRelation;
        public bool noRoomOverview;
        public bool singleContinuousImage;
        public bool noSplitScreenOrCollage;
        public bool abstractOrIconic;
        public string caption;
        public string reason;
        public string anchorEvidence;
        public string cueEvidence;
        public string contactEvidence;
        public List<string> missingOrWrong = new();
    }

    [Serializable]
    public class PreTestResponse
    {
        public string targetWord;
        public string expectedMeaning;
        public string answerMeaning;
        public bool isCorrect;
        public float responseTimeSeconds;
        public string answeredAtUtc;
    }

    [Serializable]
    public class VRSessionPackage
    {
        public string packageVersion;
        public string exportedAtUtc;
        public string sessionId;
        public string participantId;
        public string sourcePcHost;
        public int condition;
        public string wordSetId;
        public string wordSetName;
        public float roomPhaseDurationSeconds;
        public float preTestDurationSeconds;
        public float storyAuthoringDurationSeconds;
        public float furnitureAssignmentDurationSeconds;
        public List<PreTestResponse> preTestResponses = new();
        public bool hasRuntimeSettings;
        public bool enableVrStudyMode;
        public bool enableVoiceGuidance;
        public bool useLocalUnlimitedTts;
        public string localTtsEndpoint;
        public string localTtsModel;
        public string localTtsVoice;
        public float localTtsSpeed;
        public float localTtsExaggeration;
        public float localTtsCfgWeight;
        public float localTtsTemperature;
        public RoomSpecDefinition roomSpec;
        public StorySessionData storySession;
        public List<MnemonicItemData> mnemonicItems = new();
    }

    [Serializable]
    public class ExperimentSessionExport
    {
        public string participantId;
        public string sessionId;
        public string createdAtUtc;
        public string wordSetId;
        public string wordSetName;
        public string roomId;
        public string roomName;
        public string roomGeneratedBy;
        public string roomSourcePrompt;
        public string llmProvider;
        public string llmModel;
        public string llmStatus;
        public bool usedLiveLlmForStory;
        public bool storyGeneratedInBackground;
        public bool llmStoryGenerationInProgressAtExport;
        public bool llmStoryGenerationCancelled;
        public int llmStoryGenerationAttemptCount;
        public string llmGenerationError;
        public int preGeneratedMnemonicCount;
        public int liveGeneratedMnemonicCount;
        public int localFallbackMnemonicCount;
        public bool usedLocalFallback;
        public ExperimentCondition condition;
        public string conditionLabel;
        public string roomSource;
        public string storySource;
        public string storyWorkflow;
        public bool storyNarrationRequired;
        public bool storyContentReady;
        public string storyReadinessStatus;
        public bool furnitureWordAssignmentRequired;
        public string furnitureWordAssignmentStatus;
        public int furnitureWordAssignmentCount;
        public int furnitureWordAssignmentTotal;
        public bool hmdEntryReady;
        public string hmdEntryReadinessStatus;
        public string hmdEntryReadinessMessage;
        public float studyDurationSeconds;
        public float roomPhaseDurationSeconds;
        public float preTestDurationSeconds;
        public float immediatePostTestDurationSeconds;
        public float questionnaireDurationSeconds;
        public float storyAuthoringDurationSeconds;
        public float selfChoiceDurationSeconds;
        public int preTestCorrectCount;
        public int preTestTotal;
        public bool allPhotoShowcaseEntered;
        public float allPhotoShowcaseDurationSeconds;
        public int viewedCount;
        public int memorizedCount;
        public int totalItems;
        public int correctMeaningCount;
        public int correctWordCount;
        public int midTestCorrectCount;
        public int midTestTotal;
        public int finalSpatialAnchorCorrectCount;
        public int finalSpatialAnchorTotal;
        public int finalWordImageCorrectCount;
        public int finalWordImageTotal;
        public int finalWordMeaningCorrectCount;
        public int finalWordMeaningTotal;
        public int finalTestCorrectCount;
        public int finalTestTotal;
        public string questionnaireMode;
        public string questionnaireJoinId;
        public QuestionnaireResponse questionnaire;
        public StorySessionData storySession;
        public List<StorySessionData> llmStoryCandidates = new();
        public int selectedLlmStoryCandidateIndex = -1;
        public List<PreTestResponse> preTestResponses = new();
        public List<ExportWordEntry> items = new();
        public List<RecallResponse> recallResponses = new();
        public List<SnapshotTestResponse> snapshotTestResponses = new();
        public List<InteractionLog> interactionLogs = new();
        public string exportPath;
    }
}
