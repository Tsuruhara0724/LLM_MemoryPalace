"use strict";

const views = {
  lookup: document.querySelector("#lookupView"),
  session: document.querySelector("#sessionView"),
  test: document.querySelector("#testView"),
  transition: document.querySelector("#transitionView"),
  result: document.querySelector("#resultView"),
};

const state = {
  attemptId: "",
  session: null,
  phase: "manual",
  questions: [],
  index: 0,
  questionStartedAt: 0,
  manualResponses: [],
  choiceResponses: [],
  selectedOptionId: "",
};

const phaseBadge = document.querySelector("#phaseBadge");
const lookupForm = document.querySelector("#lookupForm");
const lookupId = document.querySelector("#lookupId");
const lookupButton = document.querySelector("#lookupButton");
const lookupError = document.querySelector("#lookupError");
const sessionValue = document.querySelector("#sessionValue");
const participantValue = document.querySelector("#participantValue");
const studyDateValue = document.querySelector("#studyDateValue");
const wordCountValue = document.querySelector("#wordCountValue");
const scheduleMessage = document.querySelector("#scheduleMessage");
const matchMessage = document.querySelector("#matchMessage");
const roundKicker = document.querySelector("#roundKicker");
const roundTitle = document.querySelector("#roundTitle");
const roundInstruction = document.querySelector("#roundInstruction");
const questionCounter = document.querySelector("#questionCounter");
const progressBar = document.querySelector("#progressBar");
const spanishWord = document.querySelector("#spanishWord");
const manualForm = document.querySelector("#manualForm");
const manualAnswer = document.querySelector("#manualAnswer");
const choiceForm = document.querySelector("#choiceForm");
const choiceOptions = document.querySelector("#choiceOptions");
const choiceNextButton = document.querySelector("#choiceNextButton");
const testError = document.querySelector("#testError");

function showView(name) {
  Object.entries(views).forEach(([key, element]) => {
    element.hidden = key !== name;
  });
  phaseBadge.hidden = name !== "test";
  window.scrollTo({ top: 0, behavior: "smooth" });
}

function setMessage(element, message) {
  element.textContent = message || "";
  element.hidden = !message;
}

async function apiPost(path, payload) {
  const response = await fetch(path, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(payload),
  });
  let data = {};
  try {
    data = await response.json();
  } catch (_) {
    throw new Error("The server returned an unreadable response.");
  }
  if (!response.ok) {
    throw new Error(data.message || "The request could not be completed.");
  }
  return data;
}

function setBusy(button, busy, busyLabel, normalLabel) {
  button.disabled = busy;
  button.textContent = busy ? busyLabel : normalLabel;
}

lookupForm.addEventListener("submit", async (event) => {
  event.preventDefault();
  const value = lookupId.value.trim();
  if (!value) {
    setMessage(lookupError, "Enter your Participant ID or Session ID.");
    lookupId.focus();
    return;
  }

  setMessage(lookupError, "");
  setBusy(lookupButton, true, "Finding session…", "Continue");
  try {
    const data = await apiPost("/api/start", { lookupId: value });
    state.attemptId = data.attemptId;
    state.session = data;
    state.phase = "manual";
    state.questions = data.manualQuestions;
    state.index = 0;
    state.manualResponses = [];
    state.choiceResponses = [];

    participantValue.textContent = data.participantId;
    sessionValue.textContent = data.sessionId;
    studyDateValue.textContent = formatDate(data.studyCreatedAtUtc);
    wordCountValue.textContent = String(data.wordCount);
    const dueDate = formatDate(data.scheduledForUtc);
    if (data.daysSinceStudy >= 7) {
      scheduleMessage.textContent = `This one-week test was scheduled for ${dueDate}.`;
    } else {
      const remaining = Math.max(0, 7 - data.daysSinceStudy).toFixed(1);
      scheduleMessage.textContent = `Scheduled for ${dueDate} (${remaining} day(s) remaining). The researcher may still continue.`;
    }

    const notices = [];
    if (data.matchedBy === "participantId" && data.matchingSessionCount > 1) {
      notices.push(`${data.matchingSessionCount} sessions use this Participant ID; the latest session was selected.`);
    }
    if (data.priorDelayedAttemptCount > 0) {
      notices.push(`This session already has ${data.priorDelayedAttemptCount} saved delayed-test attempt(s).`);
    }
    setMessage(matchMessage, notices.join(" "));
    showView("session");
  } catch (error) {
    setMessage(lookupError, error.message);
  } finally {
    setBusy(lookupButton, false, "Finding session…", "Continue");
  }
});

document.querySelector("#backToLookupButton").addEventListener("click", () => {
  state.attemptId = "";
  state.session = null;
  showView("lookup");
  lookupId.focus();
});

document.querySelector("#beginManualButton").addEventListener("click", () => {
  state.phase = "manual";
  state.questions = state.session.manualQuestions;
  state.index = 0;
  showView("test");
  renderQuestion();
});

manualForm.addEventListener("submit", (event) => {
  event.preventDefault();
  saveManualResponse(manualAnswer.value);
});

document.querySelector("#manualUnknownButton").addEventListener("click", () => saveManualResponse(""));

function saveManualResponse(answer) {
  if (state.phase !== "manual") return;
  const question = state.questions[state.index];
  state.manualResponses.push({
    questionId: question.questionId,
    answer: String(answer || "").trim(),
    responseTimeMs: elapsedQuestionMs(),
  });
  state.index += 1;
  if (state.index < state.questions.length) {
    renderQuestion();
  } else {
    finishManualRound();
  }
}

async function finishManualRound() {
  setMessage(testError, "");
  const submitButton = manualForm.querySelector("button[type='submit']");
  setBusy(submitButton, true, "Saving Round 1…", "Save and continue");
  try {
    const data = await apiPost("/api/manual", {
      attemptId: state.attemptId,
      responses: state.manualResponses,
    });
    state.phase = "choice";
    state.questions = data.choiceQuestions;
    state.index = 0;
    showView("transition");
  } catch (error) {
    state.index = Math.max(0, state.questions.length - 1);
    state.manualResponses.pop();
    renderQuestion();
    setMessage(testError, error.message);
  } finally {
    setBusy(submitButton, false, "Saving Round 1…", "Save and continue");
  }
}

document.querySelector("#beginChoiceButton").addEventListener("click", () => {
  state.index = 0;
  showView("test");
  renderQuestion();
});

choiceNextButton.addEventListener("click", () => {
  if (!state.selectedOptionId || state.phase !== "choice") return;
  const question = state.questions[state.index];
  state.choiceResponses.push({
    questionId: question.questionId,
    optionId: state.selectedOptionId,
    responseTimeMs: elapsedQuestionMs(),
  });
  state.index += 1;
  if (state.index < state.questions.length) {
    renderQuestion();
  } else {
    finishChoiceRound();
  }
});

async function finishChoiceRound() {
  setMessage(testError, "");
  setBusy(choiceNextButton, true, "Saving results…", "Save and continue");
  try {
    const data = await apiPost("/api/choice", {
      attemptId: state.attemptId,
      responses: state.choiceResponses,
    });
    document.querySelector("#manualScoreValue").textContent = `${data.manualScore} / ${data.wordCount}`;
    document.querySelector("#choiceScoreValue").textContent = `${data.choiceScore} / ${data.wordCount}`;
    document.querySelector("#resultIdValue").textContent = data.resultId;
    state.phase = "complete";
    showView("result");
  } catch (error) {
    state.index = Math.max(0, state.questions.length - 1);
    state.choiceResponses.pop();
    renderQuestion();
    setMessage(testError, error.message);
  } finally {
    setBusy(choiceNextButton, false, "Saving results…", "Save and continue");
  }
}

function renderQuestion() {
  setMessage(testError, "");
  const question = state.questions[state.index];
  const total = state.questions.length;
  questionCounter.textContent = `${state.index + 1} / ${total}`;
  progressBar.style.width = `${((state.index + 1) / total) * 100}%`;
  spanishWord.textContent = question.word;
  state.questionStartedAt = performance.now();

  if (state.phase === "manual") {
    phaseBadge.textContent = "Round 1 of 2";
    roundKicker.textContent = "Round 1 of 2 · Typed response";
    roundTitle.textContent = "Type the English meaning";
    roundInstruction.textContent = "Enter the English meaning of each Spanish word. Common word-form changes are accepted. No answers or correctness feedback are shown during the round.";
    manualForm.hidden = false;
    choiceForm.hidden = true;
    manualAnswer.value = "";
    window.setTimeout(() => manualAnswer.focus(), 0);
  } else {
    phaseBadge.textContent = "Round 2 of 2";
    roundKicker.textContent = "Round 2 of 2 · Three-choice";
    roundTitle.textContent = "Choose the English meaning";
    roundInstruction.textContent = "Select one of the three English meanings. Correctness feedback is withheld until the entire test is complete.";
    manualForm.hidden = true;
    choiceForm.hidden = false;
    state.selectedOptionId = "";
    choiceNextButton.disabled = true;
    choiceOptions.replaceChildren();
    question.options.forEach((option) => {
      const button = document.createElement("button");
      button.type = "button";
      button.className = "choice-option";
      button.textContent = option.label;
      button.dataset.optionId = option.optionId;
      button.addEventListener("click", () => {
        state.selectedOptionId = option.optionId;
        choiceOptions.querySelectorAll(".choice-option").forEach((element) => {
          element.classList.toggle("selected", element.dataset.optionId === option.optionId);
          element.setAttribute("aria-pressed", element.dataset.optionId === option.optionId ? "true" : "false");
        });
        choiceNextButton.disabled = false;
      });
      button.setAttribute("aria-pressed", "false");
      choiceOptions.append(button);
    });
  }
}

function elapsedQuestionMs() {
  return Math.max(0, Math.round(performance.now() - state.questionStartedAt));
}

function formatDate(isoText) {
  const date = new Date(isoText);
  if (Number.isNaN(date.getTime())) return "Unknown";
  return new Intl.DateTimeFormat(undefined, {
    year: "numeric",
    month: "short",
    day: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  }).format(date);
}

const queryId = new URLSearchParams(window.location.search).get("id");
if (queryId) lookupId.value = queryId;
lookupId.focus();
