using System;
using System.Collections.Generic;
using UnityEngine;

namespace MemPalaceLLM
{
    public enum ExperimentStage
    {
        Setup,
        RoomBuilder,
        Generation,
        SelfAuthoring,
        Study,
        Recall,
        Questionnaire,
        Result
    }

    public enum ExperimentCondition
    {
        LlmGenerated,
        SelfGenerated
    }

    public enum LlmProviderMode
    {
        OllamaLocal
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
        public string selectedImagePrompt;
        public int selectedImageCandidateIndex = -1;
        public string imageSelectionReason;
        public string imageCuePath;
        public string objectShape;
        public string colorHex;
        public List<VisualObjectSpec> visualObjects = new();
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
        public string chosenWord;
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
        public string anchorId;
        public string anchorType;
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
        public string selectedImagePrompt;
        public int selectedImageCandidateIndex = -1;
        public string imageSelectionReason;
        public string imageCuePath;
        public List<VisualObjectSpec> visualObjects = new();
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
        public ExperimentCondition condition;
        public float studyDurationSeconds;
        public int viewedCount;
        public int memorizedCount;
        public int totalItems;
        public int correctMeaningCount;
        public int correctWordCount;
        public int midTestCorrectCount;
        public int midTestTotal;
        public int finalTestCorrectCount;
        public int finalTestTotal;
        public QuestionnaireResponse questionnaire;
        public List<ExportWordEntry> items = new();
        public List<RecallResponse> recallResponses = new();
        public List<SnapshotTestResponse> snapshotTestResponses = new();
        public List<InteractionLog> interactionLogs = new();
        public string exportPath;
    }
}
