using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace MemPalaceLLM
{
    public sealed partial class MemoryPalaceExperimentController
    {
        private readonly List<MemoryPalaceSentenceAssignmentRow> sceneUiSentenceRows = new();
        private bool sceneUiEventsBound;

        private bool IsEditableSceneUiActive()
        {
            return useEditableSceneUi && editableSceneUi != null && editableSceneUi.IsReady;
        }

        private void InitializeEditableSceneUiRuntime()
        {
            if (!useEditableSceneUi || editableSceneUi == null)
            {
                return;
            }

            if (!editableSceneUi.RebuildLookup())
            {
                Debug.LogWarning(
                    "Memory Palace editable Scene UI is incomplete. The legacy IMGUI interface remains active. " +
                    "Run Tools > Memory Palace > Rebuild Editable Scene UI.");
                return;
            }

            BindEditableSceneUiEvents();
            UpdateEditableSceneUiRuntime();
        }

        private void BindEditableSceneUiEvents()
        {
            if (sceneUiEventsBound || editableSceneUi == null)
            {
                return;
            }

            BindInput("Setup_ParticipantInput", value => participantId = value);
            BindInput("Setup_RoomLoadInput", value => roomArchiveLoadFileName = value);
            BindButton("Setup_Condition1", () => condition = ExperimentCondition.SelfRoomSelfStory);
            BindButton("Setup_Condition2", () => condition = ExperimentCondition.SelfRoomLlmStory);
            BindButton("Setup_Condition3", () => condition = ExperimentCondition.DefaultRoomSelfStory);
            BindButton("Setup_Condition4", () => condition = ExperimentCondition.DefaultRoomLlmStory);
            BindButton("Setup_Start", BeginSimpleSetupFlow);
            BindButton("Setup_StartSaved", BeginSimpleSetupFlowWithSavedRoom);
            BindButton("Setup_ReloadExample", ReloadDefaultRoomForSetup);
            BindButton("Setup_Researcher", SwitchToLegacyUi);
            BindButton("Common_LegacyUi", SwitchToLegacyUi);

            BindButton("Room_ModeFloor", () => SetGridEditorMode(GridEditorMode.Floor));
            BindButton("Room_ModeWall", () => SetGridEditorMode(GridEditorMode.Wall));
            BindButton("Room_ModeFurniture", () => SetGridEditorMode(GridEditorMode.Furniture));
            BindButton("Room_ModeSelect", () => SetGridEditorMode(GridEditorMode.Select));
            BindButton("Room_Center", MoveCameraToPlanView);
            BindButton("Room_Undo", UndoGridRoomEdit);
            BindButton("Room_Redo", RedoGridRoomEdit);
            BindButton("Room_ToggleOptions", ToggleSceneUiRoomOptions);
            BindButton("Room_FillArea", ResetSceneUiRoomShapeToBuildArea);
            BindButton("Room_LShape", ResetSceneUiRoomShapeToLShape);
            BindInput("Room_ArchiveName", value => roomArchiveName = value);
            BindInput("Room_LoadName", value => roomArchiveLoadFileName = value);
            BindButton("Room_Save", SaveGridRoomLayout);
            BindButton("Room_SaveParticipant", SaveSceneUiRoomWithParticipantId);
            BindButton("Room_Load", () => LoadGridRoomLayoutByFileName(roomArchiveLoadFileName));
            BindButton("Room_LoadLatest", LoadLatestGridRoomLayout);
            BindButton("Room_Refresh", RefreshSavedRoomArchiveFileNames);
            BindButton("Room_Move", ToggleSelectedGridFurnitureMove);
            BindButton("Room_Rotate", RotateSelectedGridFurniture);
            BindButton("Room_Remove", DeleteSelectedGridFurniture);
            BindButton("Room_Done", CompleteRoomBuilding);
            for (var i = 0; i < GridFurnitureDefinitions.Length; i++)
            {
                var captured = i;
                BindButton($"Room_Furniture_{i}", () => SelectGridFurnitureDefinitionFromSceneUi(captured));
            }

            BindButton("Familiarization_Done", CompleteSceneUiRoomFamiliarization);

            BindInput("PreTest_Answer", value => preTestAnswer = value);
            BindButton("PreTest_Submit", SubmitPreTestAnswer);

            BindButton("Generation_Cancel", CancelMnemonicGeneration);
            BindButton("Generation_Retry", RetryLlmStoryGeneration);
            BindButton("Generation_Confirm", ConfirmSelectedLlmStory);
            BindButton("Generation_Back", ReturnFromGenerationToSetup);
            for (var i = 0; i < LlmStoryCandidateCount; i++)
            {
                var captured = i;
                BindButton($"Generation_Select_{i}", () => SelectLlmStoryCandidateFromSceneUi(captured));
            }

            BindInput("Story_FullText", HandleSceneUiStoryTextChanged);
            BindButton("Story_Split", RefreshParticipantStorySentencesFromFullText);
            BindButton("Story_ClearAssignments", ClearParticipantStorySentenceAssignments);
            BindButton("Story_Continue", FinalizeParticipantStory);
            BindButton("Story_Back", ReturnToSetupFromSceneUi);

            BindButton("Assignment_Clear", ClearSelectedFurnitureAssignment);
            BindButton("Assignment_WritePackage", WriteCurrentVRSessionPackageFromUi);
            BindButton("Assignment_EnterStudy", () => FinalizeSelfChoiceAndEnterStudy());
            BindButton("Assignment_DebugEnter", () => FinalizeSelfChoiceAndEnterStudy(true));
            BindButton("Assignment_Leave", LeaveFurnitureAssignmentFromSceneUi);
            for (var i = 0; i < RandomAdvancedWordCount; i++)
            {
                var captured = i;
                BindButton($"Assignment_Word_{i}", () => AssignSceneUiWordToSelectedFurniture(captured));
            }

            BindButton("Study_Replay", ReplayCurrentVoiceStep);
            BindButton("Study_Restart", StartVoiceRoute);
            BindButton("Study_Finish", () => BeginSnapshotTest(true));
            BindButton("Study_Back", LeaveStudyToSetupFromSceneUi);
            BindButton("Study_Capture", CaptureSelectedStudyItemFromSceneUi);
            BindButton("Study_Pronounce", PlaySelectedStudyWordPronunciation);
            BindButton("Overlay_SubtitleAdvance", () => AdvanceVoiceRouteAfterStorySegment(true));

            BindInput("Recall_TypedAnswer", value => postTestAnswer = value);
            BindButton("Recall_SubmitTyped", SubmitTypedPostTestAnswer);
            BindButton("Recall_Next", AdvanceRecognitionFlow);
            BindButton("Recall_Back", ReturnToStudyFromSceneUi);
            BindButton("Recall_ContinueQuestionnaire", ContinueEmptyRecallToQuestionnaireFromSceneUi);
            for (var i = 0; i < RequiredImagePromptCandidateCount; i++)
            {
                var captured = i;
                BindButton($"Recall_Option_{i}", () => SubmitSceneUiRecognitionOption(captured));
            }

            BindQuestionnaireSlider("Questionnaire_Mental", value => questionnaire.mentalDemand = value);
            BindQuestionnaireSlider("Questionnaire_Physical", value => questionnaire.physicalDemand = value);
            BindQuestionnaireSlider("Questionnaire_Temporal", value => questionnaire.temporalDemand = value);
            BindQuestionnaireSlider("Questionnaire_Performance", value => questionnaire.performance = value);
            BindQuestionnaireSlider("Questionnaire_Effort", value => questionnaire.effort = value);
            BindQuestionnaireSlider("Questionnaire_Frustration", value => questionnaire.frustration = value);
            BindQuestionnaireSlider("Questionnaire_Vividness", value => questionnaire.vividness = value);
            BindQuestionnaireSlider("Questionnaire_Helpfulness", value => questionnaire.helpfulness = value);
            BindQuestionnaireSlider("Questionnaire_Trust", value => questionnaire.trust = value);
            BindInput("Questionnaire_Notes", value => questionnaire.notes = value);
            BindButton("Questionnaire_Finish", FinishQuestionnaireFromSceneUi);
            BindButton("Questionnaire_Back", ReturnToStudyFromSceneUi);

            BindButton("Result_Return", ReturnToSetupFromSceneUi);
            sceneUiEventsBound = true;
        }

        private void BindButton(string objectName, Action callback)
        {
            var button = editableSceneUi.Get<Button>(objectName);
            if (button != null)
            {
                button.onClick.AddListener(() => callback?.Invoke());
            }
        }

        private void BindInput(string objectName, Action<string> callback)
        {
            var input = editableSceneUi.Get<InputField>(objectName);
            if (input != null)
            {
                input.onValueChanged.AddListener(value => callback?.Invoke(value));
            }
        }

        private void BindQuestionnaireSlider(string objectName, Action<int> callback)
        {
            var slider = editableSceneUi.Get<Slider>(objectName);
            if (slider != null)
            {
                slider.onValueChanged.AddListener(value => callback?.Invoke(Mathf.RoundToInt(value)));
            }
        }

        private void UpdateEditableSceneUiRuntime()
        {
            if (!useEditableSceneUi || editableSceneUi == null)
            {
                return;
            }

            if (!editableSceneUi.IsReady && !editableSceneUi.RebuildLookup())
            {
                return;
            }

            if (!sceneUiEventsBound)
            {
                BindEditableSceneUiEvents();
            }

            var hideForHeadset = IsVrParticipantViewActive();
            editableSceneUi.SetInterfaceVisible(!hideForHeadset);
            if (hideForHeadset)
            {
                return;
            }

            editableSceneUi.ShowOnlyStage("Stage_" + stage);
            editableSceneUi.ApplyLegacyStageTheme(stage);
            editableSceneUi.SetText("Common_Title", GetSceneUiHeaderTitle());
            editableSceneUi.SetText("Common_Status", GetSceneUiHeaderSubtitle());
            editableSceneUi.SetText("Common_Stage", GetSceneUiHeaderBadge());

            switch (stage)
            {
                case ExperimentStage.Setup:
                    RefreshSceneUiSetup();
                    break;
                case ExperimentStage.RoomBuilder:
                    RefreshSceneUiRoomBuilder();
                    break;
                case ExperimentStage.RoomFamiliarization:
                    RefreshSceneUiFamiliarization();
                    break;
                case ExperimentStage.PreTest:
                    RefreshSceneUiPreTest();
                    break;
                case ExperimentStage.Generation:
                    RefreshSceneUiGeneration();
                    break;
                case ExperimentStage.StoryAuthoring:
                    RefreshSceneUiStoryAuthoring();
                    break;
                case ExperimentStage.SelfAuthoring:
                    RefreshSceneUiFurnitureAssignment();
                    break;
                case ExperimentStage.Study:
                    RefreshSceneUiStudy();
                    break;
                case ExperimentStage.Recall:
                    RefreshSceneUiRecall();
                    break;
                case ExperimentStage.Questionnaire:
                    RefreshSceneUiQuestionnaire();
                    break;
                case ExperimentStage.Result:
                    RefreshSceneUiResult();
                    break;
            }

            RefreshSceneUiOverlays();
        }

        private string GetSceneUiHeaderTitle()
        {
            return stage switch
            {
                ExperimentStage.Setup => "Memory Palace",
                ExperimentStage.RoomBuilder => "Create your memory room",
                _ => "LLM Memory Palace Experiment Demo"
            };
        }

        private string GetSceneUiHeaderSubtitle()
        {
            return stage switch
            {
                ExperimentStage.Setup => "Set up a vocabulary study session",
                ExperimentStage.RoomBuilder => "Build a place that feels familiar and easy to remember.",
                _ => "Flow: PC room phase -> Spanish pre-test -> Story -> Furniture mapping -> HMD study -> Immediate post-test -> Google Form later"
            };
        }

        private string GetSceneUiHeaderBadge()
        {
            return stage switch
            {
                ExperimentStage.Setup => "Step 1 / 7",
                ExperimentStage.RoomBuilder => "Room builder",
                _ => $"Step {GetStageStepNumber()} / 7 - {GetStageDisplayName()}"
            };
        }

        private void RefreshSceneUiSetup()
        {
            EnsureFormalWordSetForSimpleSetup();
            editableSceneUi.SetInputText("Setup_ParticipantInput", participantId);
            editableSceneUi.SetInputText("Setup_RoomLoadInput", roomArchiveLoadFileName);
            for (var i = 0; i < 4; i++)
            {
                editableSceneUi.SetSelected($"Setup_Condition{i + 1}", (int)condition == i);
                editableSceneUi.SetInteractable($"Setup_Condition{i + 1}", !playSessionActive);
            }

            editableSceneUi.SetText("Setup_ConditionDescription", GetSimpleSetupConditionDescription());
            editableSceneUi.SetText("Setup_NextHint", GetSimpleSetupNextStepHint());
            editableSceneUi.SetText("Setup_StartLabel", GetSimpleSetupNextStepLabel());
            editableSceneUi.SetVisible("Setup_SavedRoomGroup", ConditionUsesSelfRoom());
            editableSceneUi.SetVisible("Setup_ReloadExample", !ConditionUsesSelfRoom());
            editableSceneUi.SetText(
                "Setup_RoomDescription",
                ConditionUsesSelfRoom()
                    ? "Participant-created familiar room. A saved room JSON may be reused."
                    : "Prepared example_room.json. The participant explores it before the pre-test.");
            editableSceneUi.SetInteractable("Setup_Start", !isGeneratingRoom && !isGenerating);
            editableSceneUi.SetInteractable("Setup_StartSaved", !playSessionActive);
        }

        private void RefreshSceneUiRoomBuilder()
        {
            EnsureGridRoomEditorInitialized();
            EnsureGridRoomLayoutLists();
            editableSceneUi.SetText("Room_Title", GetGridBuilderStepTitle());
            editableSceneUi.SetText("Room_Instructions", GetGridBuilderStepInstruction());
            editableSceneUi.SetText("Room_BuildArea", GetSelfRoomBuildAreaSummary());
            editableSceneUi.SetText("Room_Status", gridEditorStatus ?? string.Empty);
            editableSceneUi.SetText(
                "Room_Count",
                $"{GetPlacedGridFurnitureTypeCount()}/{MinimumGridFurnitureTypeCount} different items placed | " +
                $"{gridRoomLayout.floorCells.Count} floor tiles | {gridRoomLayout.manualWalls.Count} inside walls");
            editableSceneUi.SetInputText("Room_ArchiveName", roomArchiveName);
            editableSceneUi.SetInputText("Room_LoadName", roomArchiveLoadFileName);
            editableSceneUi.SetText("Room_LastSaved", string.IsNullOrWhiteSpace(lastSavedRoomArchiveFileName)
                ? string.Empty
                : "Last saved: " + lastSavedRoomArchiveFileName);
            editableSceneUi.SetInteractable("Room_Undo", gridUndoStack.Count > 0);
            editableSceneUi.SetInteractable("Room_Redo", gridRedoStack.Count > 0);
            editableSceneUi.SetText("Room_ToggleOptionsLabel", showRoomBuilderOptions ? "Hide room options" : "Room options");
            editableSceneUi.SetVisible("Room_OptionsGroup", showRoomBuilderOptions);
            editableSceneUi.SetVisible("Room_FurnitureSection", gridEditorMode == GridEditorMode.Furniture);
            editableSceneUi.SetText("Room_HudTitle", GetGridBuilderStepTitle());

            for (var i = 0; i < 4; i++)
            {
                editableSceneUi.SetSelected($"Room_Mode{(i == 0 ? "Floor" : i == 1 ? "Wall" : i == 2 ? "Furniture" : "Select")}", (int)gridEditorMode == i);
            }

            for (var i = 0; i < GridFurnitureDefinitions.Length; i++)
            {
                var definition = GridFurnitureDefinitions[i];
                var alreadyPlaced = IsGridFurnitureDefinitionAlreadyPlaced(definition.id);
                editableSceneUi.SetText(
                    $"Room_FurnitureLabel_{i}",
                    alreadyPlaced ? "Placed - " + definition.displayName : definition.displayName);
                editableSceneUi.SetInteractable($"Room_Furniture_{i}", !alreadyPlaced);
                editableSceneUi.SetSelected($"Room_Furniture_{i}", selectedGridFurnitureDefinitionIndex == i);
            }

            var hasSelected = TryGetSelectedGridFurniture(out _, out var selectedDefinition);
            var showSelected = hasSelected && gridEditorMode != GridEditorMode.Furniture;
            editableSceneUi.SetVisible("Room_SelectedGroup", showSelected);
            if (showSelected)
            {
                editableSceneUi.SetText("Room_SelectedName", selectedDefinition.displayName);
                editableSceneUi.SetText(
                    "Room_MoveLabel",
                    movingGridFurnitureInstanceIndex == selectedGridFurnitureInstanceIndex ? "Cancel move" : "Move");
                editableSceneUi.SetInteractable("Room_Rotate", selectedDefinition.canRotate);
            }

            var canComplete = HasMinimumGridFurnitureTypes(out _) && TryValidateSelfRoomBuildArea(out _);
            editableSceneUi.SetInteractable("Room_ModeSelect", HasMinimumGridFurnitureTypes(out _));
            editableSceneUi.SetInteractable("Room_Done", canComplete);
            editableSceneUi.SetText("Room_DoneLabel", playSessionActive && ConditionUsesSelfRoom() ? "Done - Continue" : "Done building");
        }

        private void RefreshSceneUiFamiliarization()
        {
            var elapsed = Time.unscaledTime - roomPhaseStartTime;
            editableSceneUi.SetText(
                "Familiarization_Timer",
                $"Target 10:00 | Elapsed {FormatPhaseTime(elapsed)} | Remaining {FormatPhaseTime(RoomPhaseTargetSeconds - elapsed)}");
        }

        private void RefreshSceneUiPreTest()
        {
            var enoughCandidates = preTestCandidateWords.Count >= RandomAdvancedWordCount;
            var hasQuestion = enoughCandidates && preTestIndex >= 0 && preTestIndex < preTestCandidateWords.Count;
            editableSceneUi.SetVisible("PreTest_QuestionGroup", hasQuestion);
            editableSceneUi.SetVisible("PreTest_ErrorGroup", !hasQuestion);
            editableSceneUi.SetInteractable("PreTest_Submit", hasQuestion);
            editableSceneUi.SetInputText("PreTest_Answer", preTestAnswer);
            if (!hasQuestion)
            {
                editableSceneUi.SetText(
                    "PreTest_Error",
                    enoughCandidates
                        ? $"All {preTestCandidateWords.Count} candidates were screened, but fewer than 8 unknown words were found."
                        : statusMessage);
                return;
            }

            var elapsed = Time.unscaledTime - preTestStartTime;
            var unknownSlot = Mathf.Clamp(preTestUnknownWords.Count + 1, 1, RandomAdvancedWordCount);
            editableSceneUi.SetText("PreTest_Counter", $"Candidate {unknownSlot} / {RandomAdvancedWordCount}");
            editableSceneUi.SetText("PreTest_Word", preTestCandidateWords[preTestIndex].word);
            editableSceneUi.SetText(
                "PreTest_Timer",
                elapsed <= PreTestTargetSeconds
                    ? "Target time remaining: " + FormatPhaseTime(PreTestTargetSeconds - elapsed)
                    : "Five-minute target exceeded by " + FormatPhaseTime(elapsed - PreTestTargetSeconds));
        }

        private void RefreshSceneUiGeneration()
        {
            var elapsed = Time.unscaledTime - storyAuthoringStartTime;
            editableSceneUi.SetText(
                "Generation_Timer",
                $"Target 10:00 | Elapsed {FormatPhaseTime(elapsed)} | Remaining {FormatPhaseTime(StoryPhaseTargetSeconds - elapsed)}");
            editableSceneUi.SetVisible("Generation_ProgressGroup", isGenerating);
            editableSceneUi.SetText(
                "Generation_Progress",
                $"Generating candidate {Mathf.Min(llmStoryCandidateGenerationIndex + 1, LlmStoryCandidateCount)} / {LlmStoryCandidateCount}\n{statusMessage}");
            editableSceneUi.SetText("Generation_Error", generationError ?? string.Empty);
            for (var i = 0; i < LlmStoryCandidateCount; i++)
            {
                var available = i < llmStoryCandidates.Count;
                editableSceneUi.SetVisible($"Generation_Card_{i}", available);
                if (!available)
                {
                    continue;
                }

                editableSceneUi.SetText($"Generation_Story_{i}", llmStoryCandidates[i]?.fullStory ?? string.Empty);
                editableSceneUi.SetText(
                    $"Generation_SelectLabel_{i}",
                    selectedLlmStoryCandidateIndex == i ? "Selected" : $"Select Story {i + 1}");
                editableSceneUi.SetSelected($"Generation_Select_{i}", selectedLlmStoryCandidateIndex == i);
            }

            editableSceneUi.SetInteractable(
                "Generation_Confirm",
                !isGenerating && selectedLlmStoryCandidateIndex >= 0 && selectedLlmStoryCandidateIndex < llmStoryCandidates.Count);
            editableSceneUi.SetVisible("Generation_Retry", !isGenerating && llmStoryCandidates.Count == 0);
        }

        private void RefreshSceneUiStoryAuthoring()
        {
            var elapsed = Time.unscaledTime - storyAuthoringStartTime;
            editableSceneUi.SetText(
                "Story_Timer",
                $"Target 10:00 | Elapsed {FormatPhaseTime(elapsed)} | Remaining {FormatPhaseTime(StoryPhaseTargetSeconds - elapsed)}");
            editableSceneUi.SetText(
                "Story_TargetWords",
                activeWordSet?.words == null ? string.Empty : BuildHighlightedParticipantStoryTargetList(activeWordSet.words));
            editableSceneUi.SetInputText("Story_FullText", participantFullStoryText);
            editableSceneUi.SetText(
                "Story_SplitStatus",
                participantStorySentences.Count == 0
                    ? "Write the complete story, then split it into sentences."
                    : $"Detected {participantStorySentences.Count} sentence(s); assigned {CountParticipantAssignedSentences()} / {participantStorySentences.Count}.");
            editableSceneUi.SetText("Story_SplitLabel", participantStorySentences.Count == 0
                ? "Split Story Into Sentences"
                : "Refresh Sentence Split");
            editableSceneUi.SetInteractable("Story_Split", !string.IsNullOrWhiteSpace(participantFullStoryText));
            editableSceneUi.SetInteractable("Story_ClearAssignments", participantStorySentences.Count > 0);
            editableSceneUi.SetInteractable("Story_Continue", AreParticipantStorySegmentsComplete());
            editableSceneUi.SetText("Story_Readiness", AreParticipantStorySegmentsComplete() ? "Story ready." : GetParticipantStoryReadinessHint());
            RefreshSceneUiSentenceRows();

            var preview = new StringBuilder();
            if (activeWordSet?.words != null)
            {
                for (var i = 0; i < activeWordSet.words.Count; i++)
                {
                    var word = activeWordSet.words[i];
                    var segment = BuildParticipantStorySegmentForWord(i);
                    preview.Append(GetParticipantStoryTargetWord(word));
                    preview.Append(" (Spanish: ");
                    preview.Append(word.word);
                    preview.Append(")\n");
                    preview.AppendLine(string.IsNullOrWhiteSpace(segment) ? "No sentence assigned yet." : segment);
                    preview.AppendLine();
                }
            }
            editableSceneUi.SetText("Story_Preview", preview.ToString());
        }

        private void RefreshSceneUiSentenceRows()
        {
            var container = editableSceneUi.GetTransform("Story_SentenceRows");
            var template = editableSceneUi.Get<MemoryPalaceSentenceAssignmentRow>("Story_SentenceTemplate");
            if (container == null || template == null)
            {
                return;
            }

            while (sceneUiSentenceRows.Count < participantStorySentences.Count)
            {
                var row = Instantiate(template, container);
                row.name = "Story_SentenceRuntime_" + sceneUiSentenceRows.Count;
                row.gameObject.SetActive(true);
                sceneUiSentenceRows.Add(row);
            }

            var optionLabels = new List<string> { "Unassigned" };
            if (activeWordSet?.words != null)
            {
                for (var i = 0; i < activeWordSet.words.Count; i++)
                {
                    optionLabels.Add(GetParticipantStoryTargetWord(activeWordSet.words[i]));
                }
            }

            EnsureParticipantStorySentenceAssignmentShape();
            for (var i = 0; i < sceneUiSentenceRows.Count; i++)
            {
                var visible = i < participantStorySentences.Count;
                sceneUiSentenceRows[i].gameObject.SetActive(visible);
                if (visible)
                {
                    sceneUiSentenceRows[i].Configure(
                        i,
                        participantStorySentences[i],
                        optionLabels,
                        participantStorySentenceWordIndexes[i],
                        SetParticipantStorySentenceAssignmentFromSceneUi);
                }
            }
        }

        private void RefreshSceneUiFurnitureAssignment()
        {
            var elapsed = Time.unscaledTime - selfChoiceStartTime;
            editableSceneUi.SetText("Assignment_Count", $"Assigned {CountSelfChoiceAssignments()} / {currentItems.Count}");
            editableSceneUi.SetText(
                "Assignment_Timer",
                $"Target 03:00 | Elapsed {FormatPhaseTime(elapsed)} | Remaining {FormatPhaseTime(FurnitureAssignmentTargetSeconds - elapsed)}");
            var summary = new StringBuilder();
            for (var i = 0; i < currentItems.Count; i++)
            {
                var item = currentItems[i];
                summary.Append(i + 1).Append(". ").Append(item.word).Append(" (").Append(item.meaning).Append("): ")
                    .AppendLine(string.IsNullOrWhiteSpace(item.anchorId) ? "Not assigned" : item.anchorLabel);
            }
            editableSceneUi.SetText("Assignment_Summary", summary.ToString());

            var selected = selectedStudyItem != null;
            editableSceneUi.SetVisible("Assignment_SelectedGroup", selected);
            editableSceneUi.SetVisible("Assignment_SelectHint", !selected);
            MnemonicItemData assigned = null;
            if (selected)
            {
                assigned = GetAssignedSelfChoiceItem(selectedStudyItem.anchorId);
                editableSceneUi.SetText("Assignment_SelectedFurniture", selectedStudyItem.anchorLabel);
                editableSceneUi.SetText("Assignment_Current", assigned == null
                    ? "No word assigned"
                    : $"Current: {assigned.word} ({assigned.meaning})");
                editableSceneUi.SetInteractable("Assignment_Clear", assigned != null);
            }

            for (var i = 0; i < RandomAdvancedWordCount; i++)
            {
                var available = i < currentItems.Count;
                editableSceneUi.SetVisible($"Assignment_Word_{i}", available);
                if (!available)
                {
                    continue;
                }

                var item = currentItems[i];
                var assignedHere = selected && string.Equals(item.anchorId, selectedStudyItem.anchorId, StringComparison.OrdinalIgnoreCase);
                var usedElsewhere = !string.IsNullOrWhiteSpace(item.anchorId) && !assignedHere;
                editableSceneUi.SetText(
                    $"Assignment_WordLabel_{i}",
                    assignedHere ? $"Selected: {item.word}\n{item.meaning}"
                        : usedElsewhere ? $"{item.word}\nUsed: {item.anchorLabel}"
                        : $"{item.word}\n{item.meaning}");
                editableSceneUi.SetInteractable($"Assignment_Word_{i}", selected && !usedElsewhere);
                editableSceneUi.SetSelected($"Assignment_Word_{i}", assignedHere);
            }

            editableSceneUi.SetRawImage(
                "Assignment_Image",
                assigned != null && mnemonicImageCues.TryGetValue(assigned.word, out var texture) ? texture : null);
            var canEnter = CanEnterStudyAfterAssignment();
            editableSceneUi.SetInteractable("Assignment_WritePackage", canEnter);
            editableSceneUi.SetInteractable("Assignment_EnterStudy", canEnter && !isPreparingVoiceAudioBeforeStudy);
            editableSceneUi.SetInteractable("Assignment_DebugEnter", canEnter);
            editableSceneUi.SetText("Assignment_EnterLabel", isPreparingVoiceAudioBeforeStudy
                ? "Preparing Voice Audio..."
                : "Finish Assignment And Enter VR Study");
            editableSceneUi.SetText(
                "Assignment_Status",
                isPreparingVoiceAudioBeforeStudy ? preStudyVoiceStatus
                    : !canEnter ? GetAssignmentCompletionHint()
                    : preStudyVoiceStatus ?? string.Empty);
            editableSceneUi.SetText("Assignment_PackagePath", lastVrSessionPackagePath ?? string.Empty);
        }

        private void RefreshSceneUiStudy()
        {
            var timer = studyAwaitingParticipantStart
                ? "Target 10:00 | Waiting for participant to press Start learning"
                : $"Target 10:00 | Elapsed {FormatPhaseTime(GetStudyElapsedSeconds())} | Remaining {FormatPhaseTime(StudyPhaseTargetSeconds - GetStudyElapsedSeconds())}";
            editableSceneUi.SetText(
                "Study_Info",
                $"Participant: {participantId}\nStory source: {ResolveProviderLabel()}\nWord set: {activeWordSet?.displayName}\n" +
                $"Room: {RoomSpecCatalog.RoomName}\nViewed images: {viewedWords.Count}/{currentItems.Count}\n" +
                $"Snapshots: {memorizedWords.Count}/{currentItems.Count}\n{timer}");
            editableSceneUi.SetText("Study_VoiceStatus", voiceRouteStatus ?? string.Empty);
            editableSceneUi.SetText("Study_Story", currentStory?.fullStory ?? string.Empty);
            editableSceneUi.SetText("Study_ProgressHint", GetStudyProgressHint());
            editableSceneUi.SetInteractable("Study_Replay", enableVoiceGuidance);
            editableSceneUi.SetInteractable("Study_Restart", enableVoiceGuidance);
            editableSceneUi.SetInteractable("Study_Finish", IsReadyForPostStudyShowcase());

            var selected = selectedStudyItem != null;
            editableSceneUi.SetVisible("Study_SelectedGroup", selected);
            if (!selected)
            {
                return;
            }

            editableSceneUi.SetText(
                "Study_SelectedInfo",
                $"{selectedStudyItem.word}\n{GetDisplayMeaningText(selectedStudyItem)}\nAnchor: {selectedStudyItem.anchorLabel}\n\n{selectedStudyItem.mnemonic}");
            editableSceneUi.SetRawImage("Study_WordImage", GetStudyWordImageTexture(selectedStudyItem));
            var hasSnapshot = memorySnapshots.ContainsKey(selectedStudyItem.word);
            editableSceneUi.SetRawImage("Study_Snapshot", hasSnapshot ? memorySnapshots[selectedStudyItem.word] : null);
            editableSceneUi.SetText("Study_CaptureLabel", hasSnapshot ? "Replace Scene Snapshot" : "Capture Scene Snapshot");
            editableSceneUi.SetInteractable("Study_Capture", !isCapturingSnapshot);
            editableSceneUi.SetInteractable("Study_Pronounce", CanPlayStudyWordPronunciation(selectedStudyItem));
        }

        private void RefreshSceneUiRecall()
        {
            var elapsed = Time.unscaledTime - postTestStartTime;
            editableSceneUi.SetText("Recall_Title", "Immediate Post-test | " + GetRecognitionTestDisplayName());
            editableSceneUi.SetText("Recall_Instructions", GetRecognitionTestInstructionText());
            editableSceneUi.SetText(
                "Recall_Timer",
                $"Target 05:00 | Elapsed {FormatPhaseTime(elapsed)} | Remaining {FormatPhaseTime(PreTestTargetSeconds - elapsed)}");
            var hasQuestion = recognitionQueue.Count > 0 && !string.IsNullOrWhiteSpace(currentRecognitionTargetWord);
            editableSceneUi.SetVisible("Recall_QuestionGroup", hasQuestion);
            editableSceneUi.SetVisible("Recall_EmptyGroup", !hasQuestion);
            if (!hasQuestion)
            {
                editableSceneUi.SetText("Recall_EmptyMessage", GetEmptyRecognitionBlockMessage());
                editableSceneUi.SetVisible("Recall_ContinueQuestionnaire", CanContinueFromEmptyRecognitionBlock());
                editableSceneUi.SetVisible("Recall_Back", !CanContinueFromEmptyRecognitionBlock());
                return;
            }

            editableSceneUi.SetText("Recall_Counter", $"Question {recognitionIndex + 1} / {recognitionQueue.Count}");
            editableSceneUi.SetText("Recall_Target", "Target Spanish Word: " + currentRecognitionTargetWord);
            var typed = activeRecognitionTestKind == RecognitionTestKind.WordMeaning;
            editableSceneUi.SetVisible("Recall_TypedGroup", typed);
            editableSceneUi.SetVisible("Recall_OptionsGroup", !typed);
            editableSceneUi.SetInputText("Recall_TypedAnswer", postTestAnswer);
            editableSceneUi.SetInteractable("Recall_SubmitTyped", !awaitingRecognitionAdvance);
            for (var i = 0; i < RequiredImagePromptCandidateCount; i++)
            {
                var available = !typed && i < recognitionOptions.Count;
                editableSceneUi.SetVisible($"Recall_Option_{i}", available);
                if (!available)
                {
                    continue;
                }

                var optionWord = recognitionOptions[i];
                editableSceneUi.SetRawImage(
                    $"Recall_OptionImage_{i}",
                    TryGetRecognitionOptionTexture(optionWord, out var optionTexture) ? optionTexture : null);
                editableSceneUi.SetText($"Recall_OptionLabel_{i}", GetRecognitionOptionCaption(optionWord));
                editableSceneUi.SetInteractable($"Recall_Option_{i}", !awaitingRecognitionAdvance && !isCapturingSnapshot);
            }
            editableSceneUi.SetVisible("Recall_FeedbackGroup", !string.IsNullOrWhiteSpace(recognitionFeedback));
            editableSceneUi.SetText("Recall_Feedback", recognitionFeedback ?? string.Empty);
            editableSceneUi.SetVisible("Recall_Next", awaitingRecognitionAdvance);
            editableSceneUi.SetText("Recall_NextLabel", GetRecognitionAdvanceButtonText());
            editableSceneUi.SetVisible("Recall_Back", !awaitingRecognitionAdvance);
        }

        private void RefreshSceneUiQuestionnaire()
        {
            editableSceneUi.SetText(
                "Questionnaire_Timer",
                "Target 05:00 | Elapsed " + FormatPhaseTime(Time.unscaledTime - questionnaireStartTime));
            SetQuestionnaireSlider("Questionnaire_Mental", "Questionnaire_MentalValue", questionnaire.mentalDemand);
            SetQuestionnaireSlider("Questionnaire_Physical", "Questionnaire_PhysicalValue", questionnaire.physicalDemand);
            SetQuestionnaireSlider("Questionnaire_Temporal", "Questionnaire_TemporalValue", questionnaire.temporalDemand);
            SetQuestionnaireSlider("Questionnaire_Performance", "Questionnaire_PerformanceValue", questionnaire.performance);
            SetQuestionnaireSlider("Questionnaire_Effort", "Questionnaire_EffortValue", questionnaire.effort);
            SetQuestionnaireSlider("Questionnaire_Frustration", "Questionnaire_FrustrationValue", questionnaire.frustration);
            SetQuestionnaireSlider("Questionnaire_Vividness", "Questionnaire_VividnessValue", questionnaire.vividness);
            SetQuestionnaireSlider("Questionnaire_Helpfulness", "Questionnaire_HelpfulnessValue", questionnaire.helpfulness);
            SetQuestionnaireSlider("Questionnaire_Trust", "Questionnaire_TrustValue", questionnaire.trust);
            editableSceneUi.SetInputText("Questionnaire_Notes", questionnaire.notes);
        }

        private void SetQuestionnaireSlider(string sliderName, string valueName, int value)
        {
            editableSceneUi.SetSliderValue(sliderName, value);
            editableSceneUi.SetText(valueName, value.ToString());
        }

        private void RefreshSceneUiResult()
        {
            var spatialCorrect = CountSnapshotResponses(FinalSpatialAnchorPhaseId, true);
            var spatialTotal = CountSnapshotResponses(FinalSpatialAnchorPhaseId, null);
            var wordImageCorrect = CountSnapshotResponses(FinalWordImagePhaseId, true);
            var wordImageTotal = CountSnapshotResponses(FinalWordImagePhaseId, null);
            var wordMeaningCorrect = CountSnapshotResponses(FinalWordMeaningPhaseId, true);
            var wordMeaningTotal = CountSnapshotResponses(FinalWordMeaningPhaseId, null);
            editableSceneUi.SetText(
                "Result_Summary",
                $"Participant: {participantId}\nStory source: {ResolveProviderLabel()}\nRoom: {RoomSpecCatalog.RoomName}\n" +
                $"Study duration: {studyDurationSeconds:F1}s\nPre-test candidates screened: {preTestScreenedWordCount}\n" +
                $"Snapshots: {memorizedWords.Count}/{currentItems.Count}\nSpatial anchor: {spatialCorrect}/{spatialTotal}\n" +
                $"Word image: {wordImageCorrect}/{wordImageTotal}\nSpanish post-test: {wordMeaningCorrect}/{wordMeaningTotal}\n" +
                $"Immediate total: {spatialCorrect + wordImageCorrect + wordMeaningCorrect}/{spatialTotal + wordImageTotal + wordMeaningTotal}");
            editableSceneUi.SetText(
                "Result_Exports",
                $"JSON\n{lastJsonExportPath}\n\nCSV\n{lastCsvExportPath}\n\n{exportMessage}");
            var items = new StringBuilder();
            for (var i = 0; i < currentItems.Count; i++)
            {
                var item = currentItems[i];
                items.Append(i + 1).Append(". ").Append(item.word).Append(" @ ").AppendLine(item.anchorLabel);
                items.AppendLine(item.mnemonic);
                items.AppendLine();
            }
            editableSceneUi.SetText("Result_Items", items.ToString());
        }

        private void RefreshSceneUiOverlays()
        {
            SetCanvasGroup("Overlay_Flash", flashOverlayAlpha, flashOverlayAlpha > 0.001f);
            SetCanvasGroup("Overlay_Teleport", teleportOverlayAlpha, teleportOverlayAlpha > 0.001f);

            var contextAlpha = GetContextInstructionAlpha();
            SetCanvasGroup("Overlay_Context", contextAlpha, contextAlpha > 0.001f);
            editableSceneUi.SetText("Overlay_ContextTitle", contextInstructionTitle);
            editableSceneUi.SetText("Overlay_ContextBody", contextInstructionBody);

            var showSubtitle = stage == ExperimentStage.Study && ShouldKeepVoiceSubtitleVisible();
            editableSceneUi.SetVisible("Overlay_Subtitle", showSubtitle);
            editableSceneUi.SetText("Overlay_SubtitleText", currentVoiceSubtitle);
            editableSceneUi.SetVisible("Overlay_SubtitleAdvance", showSubtitle && ShouldShowStoryAdvanceButton());

            var showWordImage = stage == ExperimentStage.Study && ConditionUsesStoryNarration() && selectedStudyItem != null && !allPhotoShowcaseActive;
            var wordTexture = showWordImage ? GetStudyWordImageTexture(selectedStudyItem) : null;
            editableSceneUi.SetVisible("Overlay_WordImage", wordTexture != null);
            editableSceneUi.SetRawImage("Overlay_WordImageTexture", wordTexture);
            editableSceneUi.SetText(
                "Overlay_WordImageLabel",
                selectedStudyItem == null ? string.Empty : $"{selectedStudyItem.word}\n{selectedStudyItem.meaning}");
        }

        private void SetCanvasGroup(string objectName, float alpha, bool visible)
        {
            editableSceneUi.SetVisible(objectName, visible);
            var group = editableSceneUi.Get<CanvasGroup>(objectName);
            if (group != null)
            {
                group.alpha = Mathf.Clamp01(alpha);
                group.blocksRaycasts = visible;
            }
        }

        private void SwitchToLegacyUi()
        {
            useEditableSceneUi = false;
            showLegacySetupUi = true;
            editableSceneUi?.SetInterfaceVisible(false);
            statusMessage = "Legacy code-drawn UI enabled. Re-enable 'Use Editable Scene UI' in the controller Inspector to return.";
        }

        private void ToggleSceneUiRoomOptions()
        {
            showRoomBuilderOptions = !showRoomBuilderOptions;
        }

        private void ResetSceneUiRoomShapeToBuildArea()
        {
            PushGridRoomUndo();
            CreateEmptyGridRoomForSelfBuildArea();
            ApplyGridRoomLayoutToCurrentRoom(true);
            gridEditorStatus = "Reset the floor to the current physical build area.";
        }

        private void ResetSceneUiRoomShapeToLShape()
        {
            PushGridRoomUndo();
            CreateDefaultLShapeGridRoom();
            ApplyGridRoomLayoutToCurrentRoom(true);
            gridEditorStatus = "Started with a clear L-shaped room.";
        }

        private void SaveSceneUiRoomWithParticipantId()
        {
            roomArchiveName = BuildDefaultRoomArchiveName(participantId);
            roomArchiveNameParticipantId = participantId ?? string.Empty;
            SaveGridRoomLayout();
        }

        private void SelectGridFurnitureDefinitionFromSceneUi(int index)
        {
            if (index < 0 || index >= GridFurnitureDefinitions.Length || IsGridFurnitureDefinitionAlreadyPlaced(GridFurnitureDefinitions[index].id))
            {
                return;
            }

            if (movingGridFurnitureInstanceIndex >= 0)
            {
                CancelGridFurnitureMove("Move cancelled.");
            }
            selectedGridFurnitureInstanceIndex = -1;
            selectedBuilderAnchorIndex = -1;
            gridEditorMode = GridEditorMode.Furniture;
            selectedGridFurnitureDefinitionIndex = index;
            gridGhostRotation = 0;
            gridEditorStatus = $"{GridFurnitureDefinitions[index].displayName} selected. Choose a green spot in the room.";
            UpdateGridFurniturePreview(true);
        }

        private void ToggleSelectedGridFurnitureMove()
        {
            if (movingGridFurnitureInstanceIndex == selectedGridFurnitureInstanceIndex)
            {
                CancelGridFurnitureMove("Move cancelled.");
            }
            else
            {
                BeginMoveSelectedGridFurniture();
            }
        }

        private void CompleteSceneUiRoomFamiliarization()
        {
            var elapsed = Time.unscaledTime - roomPhaseStartTime;
            roomPhaseDurationSeconds = elapsed;
            LogInteraction("default_room_familiarization_completed", string.Empty, string.Empty, $"Duration {elapsed:F1}s.");
            BeginPreTest();
        }

        private void SelectLlmStoryCandidateFromSceneUi(int index)
        {
            if (index < 0 || index >= llmStoryCandidates.Count)
            {
                return;
            }
            selectedLlmStoryCandidateIndex = index;
            statusMessage = $"Story {index + 1} selected. Confirm to continue.";
        }

        private void ReturnFromGenerationToSetup()
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

        private void HandleSceneUiStoryTextChanged(string value)
        {
            if (!string.Equals(participantFullStoryText, value, StringComparison.Ordinal) && participantStorySentences.Count > 0)
            {
                statusMessage = "Story text changed. Split the story again before continuing.";
            }
            participantFullStoryText = value ?? string.Empty;
        }

        private void SetParticipantStorySentenceAssignmentFromSceneUi(int sentenceIndex, int wordIndex)
        {
            EnsureParticipantStorySentenceAssignmentShape();
            if (sentenceIndex >= 0 && sentenceIndex < participantStorySentenceWordIndexes.Count)
            {
                participantStorySentenceWordIndexes[sentenceIndex] = wordIndex;
            }
        }

        private void AssignSceneUiWordToSelectedFurniture(int index)
        {
            if (selectedStudyItem != null && index >= 0 && index < currentItems.Count)
            {
                AssignWordToSelectedFurniture(currentItems[index]);
            }
        }

        private void LeaveFurnitureAssignmentFromSceneUi()
        {
            if (isPreparingVoiceAudioBeforeStudy)
            {
                CancelPreStudyVoiceAudioPreparation();
            }
            if (isGenerating)
            {
                CancelMnemonicGeneration();
            }
            ClearStudyRoom();
            stage = ExperimentStage.Setup;
            selectedStudyItem = null;
            MoveCameraToOverview();
        }

        private void CaptureSelectedStudyItemFromSceneUi()
        {
            if (selectedStudyItem != null && !isCapturingSnapshot)
            {
                StartCoroutine(CaptureMemorySnapshotRoutine(selectedStudyItem));
            }
        }

        private void LeaveStudyToSetupFromSceneUi()
        {
            ClearStudyRoom();
            stage = ExperimentStage.Setup;
            statusMessage = "Returned to setup.";
            selectedStudyItem = null;
            MoveCameraToOverview();
        }

        private void SubmitSceneUiRecognitionOption(int index)
        {
            if (!awaitingRecognitionAdvance && index >= 0 && index < recognitionOptions.Count)
            {
                SubmitRecognitionChoice(recognitionOptions[index]);
            }
        }

        private void ContinueEmptyRecallToQuestionnaireFromSceneUi()
        {
            if (!CanContinueFromEmptyRecognitionBlock())
            {
                return;
            }
            finalTestCompleted = true;
            stage = ExperimentStage.Questionnaire;
            statusMessage = "Final test block skipped as not applicable. Questionnaire is ready.";
        }

        private void ReturnToStudyFromSceneUi()
        {
            stage = ExperimentStage.Study;
            ResetCameraForStudy();
            SetupVrStudyRuntime();
        }

        private void FinishQuestionnaireFromSceneUi()
        {
            questionnaireDurationSeconds = Mathf.Max(0f, Time.unscaledTime - questionnaireStartTime);
            FinalizeAndExport();
        }

        private void ReturnToSetupFromSceneUi()
        {
            stage = ExperimentStage.Setup;
            statusMessage = "Returned to setup.";
            MoveCameraToOverview();
        }
    }
}
