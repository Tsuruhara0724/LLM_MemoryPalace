"use strict";

const PROTOCOL = "delayed-story-spatial-typed-retest-v3";
const byId = (id) => document.getElementById(id);
const views = Object.fromEntries(["lookup", "session", "recall", "test", "transition", "story", "result"].map((name) => [name, byId(name + "View")]));
const state = {
  attemptId: "", session: null, stage: "baseline", phase: "manual", questions: [],
  index: 0, questionStartedAt: 0, responses: [], selectedOptionId: "",
  busy: false, pending: null, snapshotReady: false,
  recallDeadlineAt: 0, recallComplete: false,
};
let recallInterval = null;

function message(id, text = "") {
  byId(id).textContent = text;
  byId(id).hidden = !text;
}
function showView(name) {
  for (const [key, element] of Object.entries(views)) element.hidden = key !== name;
  byId("phaseBadge").hidden = name !== "test";
  const active = name === "recall" ? "recall" : state.phase === "story" ? "story" : state.phase === "matching" ? "matching" : state.stage;
  document.querySelectorAll("[data-stage]").forEach((item) => item.classList.toggle("active", !["lookup", "session", "result"].includes(name) && item.dataset.stage === active));
  window.scrollTo({ top: 0, behavior: "auto" });
}
let invitationError = null;
const invitationReady = (async () => {
  const token = new URLSearchParams(window.location.hash?.slice(1) || "").get("access");
  if (!token) return;
  // Fragments are not sent to servers or referrers. Remove the secret from history
  // after reading it; the server exchanges it for a short-lived HttpOnly cookie.
  window.history.replaceState(null, "", window.location.pathname + window.location.search);
  byId("lookupButton").disabled = true;
  const response = await fetch("/api/access", { method: "POST", headers: {
    "Content-Type": "application/json", "Authorization": "Bearer " + token,
  }, body: "{}" });
  const data = await response.json();
  if (!response.ok) throw new Error(data.message || "This invitation could not be opened.");
  byId("lookupId").value = data.sessionId;
  byId("lookupButton").disabled = false;
})().catch((error) => {
  invitationError = error;
  message("lookupError", error.message);
  byId("lookupButton").disabled = true;
});
async function apiPost(route, payload) {
  await invitationReady;
  if (invitationError) throw invitationError;
  const response = await fetch(route, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(payload) });
  let data;
  try { data = await response.json(); } catch { throw new Error("The server returned an unreadable response. Retry saving this stage."); }
  if (!response.ok) throw new Error(data.message || "The request could not be completed.");
  return data;
}
function controls() {
  const blocked = state.busy || Boolean(state.pending);
  byId("manualAnswer").disabled = blocked;
  byId("manualUnknownButton").disabled = blocked;
  byId("manualSubmitButton").disabled = blocked;
  document.querySelectorAll(".choice-option").forEach((button) => {
    button.disabled = blocked || (state.phase === "matching" && !state.snapshotReady);
  });
  byId("choiceNextButton").disabled = blocked || !state.selectedOptionId || (state.phase === "matching" && !state.snapshotReady);
  byId("retrySubmissionButton").hidden = !state.pending;
  byId("retrySubmissionButton").disabled = state.busy;
}

byId("lookupForm").addEventListener("submit", async (event) => {
  event.preventDefault();
  if (state.busy) return;
  const lookupId = byId("lookupId").value.trim();
  if (!lookupId) { message("lookupError", "Enter your Participant ID or Session ID."); return; }
  state.busy = true;
  byId("lookupButton").disabled = true;
  message("lookupError");
  try {
    const data = await apiPost("/api/start", { lookupId });
    if (data.protocolVersion !== PROTOCOL) throw new Error("This server is running an older test version. Ask the researcher to restart it or provide the updated link.");
    resetRecall();
    Object.assign(state, { attemptId: data.attemptId, session: data, stage: "baseline", phase: "manual",
      questions: data.manualQuestions, index: 0, responses: [], pending: null });
    byId("participantValue").textContent = data.participantId;
    byId("sessionValue").textContent = data.sessionId;
    byId("studyDateValue").textContent = formatDate(data.studyCreatedAtUtc);
    byId("wordCountValue").textContent = data.wordCount;
    byId("scheduleMessage").textContent = data.daysSinceStudy >= 7
      ? "This one-week test was scheduled for " + formatDate(data.scheduledForUtc) + "."
      : "Scheduled for " + formatDate(data.scheduledForUtc) + ". The researcher may still continue early.";
    const notices = [];
    if (data.matchingSessionCount > 1) notices.push("Multiple sessions match this Participant ID; the latest was selected.");
    if (data.priorDelayedAttemptCount > 0) notices.push(data.priorDelayedAttemptCount + " saved attempt(s) already exist for this session.");
    message("matchMessage", notices.join(" "));
    showView("session");
  } catch (error) { message("lookupError", error.message); }
  finally { state.busy = false; byId("lookupButton").disabled = false; }
});
byId("backToLookupButton").addEventListener("click", () => { resetRecall(); state.attemptId = ""; showView("lookup"); });
byId("startRecallButton").addEventListener("click", () => {
  if (state.recallDeadlineAt || !state.attemptId) return;
  state.recallDeadlineAt = Date.now() + 3 * 60 * 1000;
  byId("beginManualButton").disabled = true;
  byId("recallStatus").hidden = true;
  showView("recall");
  updateRecallTimer();
  recallInterval = window.setInterval(updateRecallTimer, 250);
});
byId("beginManualButton").addEventListener("click", () => {
  if (!state.recallComplete) return;
  showView("test");
  renderQuestion();
});
byId("beginRetestButton").addEventListener("click", () => { showView("test"); renderQuestion(); });

function resetRecall() {
  if (recallInterval !== null) window.clearInterval(recallInterval);
  recallInterval = null;
  state.recallDeadlineAt = 0;
  state.recallComplete = false;
  byId("recallTimer").textContent = "03:00";
  byId("recallStatus").hidden = true;
  byId("beginManualButton").disabled = true;
}

function updateRecallTimer() {
  const remaining = Math.max(0, Math.ceil((state.recallDeadlineAt - Date.now()) / 1000));
  byId("recallTimer").textContent = String(Math.floor(remaining / 60)).padStart(2, "0")
    + ":" + String(remaining % 60).padStart(2, "0");
  if (remaining > 0) return;
  state.recallComplete = true;
  byId("beginManualButton").disabled = false;
  byId("recallStatus").hidden = false;
  if (recallInterval !== null) window.clearInterval(recallInterval);
  recallInterval = null;
}

byId("manualForm").addEventListener("submit", (event) => { event.preventDefault(); saveAnswer(byId("manualAnswer").value); });
byId("manualUnknownButton").addEventListener("click", () => saveAnswer(""));
byId("choiceNextButton").addEventListener("click", () => saveAnswer(state.selectedOptionId));
byId("retrySubmissionButton").addEventListener("click", sendPending);

function saveAnswer(answer) {
  if (state.busy || state.pending || !["manual", "matching"].includes(state.phase)) return;
  if (state.phase !== "manual" && (!answer || (state.phase === "matching" && !state.snapshotReady))) return;
  const response = { questionId: state.questions[state.index].questionId,
    responseTimeMs: Math.max(0, Math.round(performance.now() - state.questionStartedAt)) };
  if (state.phase === "manual") response.answer = String(answer || "").trim();
  else response.optionId = answer;
  state.responses.push(response);
  state.index++;
  if (state.index < state.questions.length) { renderQuestion(); return; }
  state.pending = { route: "/api/" + state.phase,
    body: { attemptId: state.attemptId, responses: state.responses, stage: state.stage } };
  sendPending();
}

async function sendPending() {
  if (!state.pending || state.busy) return;
  state.busy = true;
  message("testError");
  controls();
  try {
    const request = state.pending;
    const data = await apiPost(request.route, request.body);
    const previousPhase = state.phase;
    state.pending = null;
    state.responses = [];
    state.index = 0;
    if (previousPhase === "matching") {
      state.stage = "post";
      state.phase = "manual";
      state.questions = data.manualQuestions;
      transition("Furniture matching saved", "Take the word test again",
        "Type the English meanings again, in a newly randomized word order. There is no English multiple-choice round. Your first responses are kept separately.",
        "Begin second word test");
    } else if (state.stage === "baseline") {
      state.phase = "story";
      byId("storyText").textContent = data.storyText;
      message("storyError");
      showView("story");
    } else {
      state.phase = "complete";
      byId("resultIdValue").textContent = data.resultId;
      showView("result");
    }
  } catch (error) {
    message("testError", error.message + " Your answers remain on this page. Use Retry saving this stage; do not reload.");
  } finally { state.busy = false; controls(); }
}

function transition(kicker, title, instruction, buttonText) {
  byId("transitionKicker").textContent = kicker;
  byId("transitionTitle").textContent = title;
  byId("transitionInstruction").textContent = instruction;
  byId("beginRetestButton").textContent = buttonText;
  showView("transition");
}

byId("finishStoryButton").addEventListener("click", async () => {
  if (state.busy || state.phase !== "story") return;
  state.busy = true;
  byId("finishStoryButton").disabled = true;
  message("storyError");
  try {
    const data = await apiPost("/api/story", { attemptId: state.attemptId });
    state.phase = "matching";
    state.questions = data.matchingQuestions;
    state.responses = [];
    state.index = 0;
    showView("test");
    renderQuestion();
  } catch (error) { message("storyError", error.message + " You can retry this button."); }
  finally { state.busy = false; byId("finishStoryButton").disabled = false; controls(); }
});

function renderQuestion() {
  const question = state.questions[state.index];
  const matching = state.phase === "matching";
  message("testError");
  state.selectedOptionId = "";
  state.questionStartedAt = performance.now();
  byId("questionCounter").textContent = (state.index + 1) + " / " + state.questions.length;
  byId("progressBar").style.width = ((state.index + 1) / state.questions.length * 100) + "%";
  byId("promptLabel").textContent = matching ? "Furniture in your original study room" : "Spanish word";
  byId("spanishWord").hidden = matching;
  byId("spanishWord").textContent = matching ? "" : question.word;
  byId("snapshotFigure").hidden = !matching;
  byId("manualForm").hidden = state.phase !== "manual";
  byId("choiceForm").hidden = state.phase === "manual";
  const stageName = state.stage === "baseline" ? "First word test" : "Second word test · After review";
  byId("phaseBadge").textContent = matching ? "Furniture matching" : stageName;
  byId("roundKicker").textContent = matching ? "Stage 3 of 4" : stageName + " · Typed response";
  byId("roundTitle").textContent = matching ? "Which Spanish word belongs here?" : "Type the English meaning";
  byId("roundInstruction").textContent = matching
    ? "Look at the original study snapshot. Choose the Spanish word you assigned to this furniture. No correctness feedback is shown."
    : "Enter the English meaning of each Spanish word. Common word-form changes are accepted. Choose I don't know if you cannot recall it.";
  if (state.phase === "manual") {
    byId("manualAnswer").value = "";
    window.setTimeout(() => byId("manualAnswer").focus(), 0);
  } else {
    byId("choiceOptions").replaceChildren();
    for (const option of question.options) {
      const button = document.createElement("button");
      button.type = "button";
      button.className = "choice-option";
      button.textContent = option.label;
      button.dataset.optionId = option.optionId;
      button.setAttribute("aria-pressed", "false");
      button.lang = "es";
      button.addEventListener("click", () => {
        if (state.busy || state.pending || (matching && !state.snapshotReady)) return;
        state.selectedOptionId = option.optionId;
        byId("choiceOptions").querySelectorAll("button").forEach((candidate) => {
          const selected = candidate.dataset.optionId === option.optionId;
          candidate.classList.toggle("selected", selected);
          candidate.setAttribute("aria-pressed", String(selected));
        });
        controls();
      });
      byId("choiceOptions").append(button);
    }
    if (matching) loadSnapshot(question);
  }
  controls();
}

function loadSnapshot(question) {
  state.snapshotReady = false;
  byId("snapshotCaption").textContent = question.furnitureLabel;
  byId("snapshotStatus").textContent = "Loading the original study snapshot…";
  byId("snapshotStatus").hidden = false;
  byId("retrySnapshotButton").hidden = true;
  const image = byId("snapshotImage");
  image.onload = () => {
    state.snapshotReady = true;
    state.questionStartedAt = performance.now();
    byId("snapshotStatus").hidden = true;
    controls();
  };
  image.onerror = () => {
    state.snapshotReady = false;
    byId("snapshotStatus").textContent = "The original snapshot could not be loaded. Reload it before answering, or contact the researcher.";
    byId("retrySnapshotButton").hidden = false;
    controls();
  };
  image.src = question.snapshotUrl;
}
byId("retrySnapshotButton").addEventListener("click", () => { loadSnapshot(state.questions[state.index]); controls(); });

function formatDate(value) {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return "Unknown";
  return new Intl.DateTimeFormat(undefined, { year: "numeric", month: "short", day: "numeric", hour: "2-digit", minute: "2-digit" }).format(date);
}
const queryId = new URLSearchParams(window.location.search).get("id");
if (queryId) byId("lookupId").value = queryId;
byId("lookupId").focus();
