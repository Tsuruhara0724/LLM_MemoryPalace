"use strict";

// Embedded only in exported offline pages. No network requests or answer keys.
const offlineData = JSON.parse(document.getElementById("offlineData").textContent);
let offlineAttempt = null;
let offlineDownloaded = false;
const offlineNow = () => new Date().toISOString();
function offlineShuffle(values) {
  const result = [...values];
  for (let i = result.length - 1; i > 0; i--) {
    const random = new Uint32Array(1);
    crypto.getRandomValues(random);
    const j = Math.floor((random[0] / 4294967296) * (i + 1));
    [result[i], result[j]] = [result[j], result[i]];
  }
  return result;
}
function offlineId() {
  const bytes = new Uint8Array(16);
  crypto.getRandomValues(bytes);
  return Array.from(bytes, (value) => value.toString(16).padStart(2, "0")).join("");
}
function offlineManualQuestions() {
  return offlineShuffle(offlineData.words).map((word) => ({ questionId: offlineId(), word }));
}
function offlineResponses(responses, questions, matching) {
  if (!Array.isArray(responses) || responses.length !== questions.length) throw new Error("Answer every question before continuing.");
  const seen = new Set();
  return responses.map((raw) => {
    const question = questions.find((entry) => entry.questionId === raw.questionId);
    if (!question || seen.has(raw.questionId)) throw new Error("Invalid or duplicated question.");
    seen.add(raw.questionId);
    const result = { questionId: raw.questionId, responseTimeMs: Math.max(0, Math.min(3600000, Number(raw.responseTimeMs) || 0)) };
    if (matching) {
      const option = question.options.find((entry) => entry.optionId === raw.optionId);
      if (!option) throw new Error("Choose one of the displayed Spanish words.");
      Object.assign(result, { optionId: option.optionId, chosenWord: option.label });
    } else {
      Object.assign(result, { word: question.word, answer: String(raw.answer || "").trim().slice(0, 300) });
    }
    return result;
  });
}
function offlineApi(route, body) {
  if (route === "/api/start") {
    const query = String(body.lookupId || "").trim().toLowerCase();
    if (![offlineData.participantId.toLowerCase(), offlineData.sessionId.toLowerCase()].includes(query)) {
      throw new Error("This file is only for " + offlineData.participantId + ". Check your ID with the researcher.");
    }
    if (offlineAttempt && offlineAttempt.phase !== "complete") throw new Error("A test is already open. Continue on this page.");
    offlineDownloaded = false;
    const attemptId = offlineId();
    offlineAttempt = { attemptId, phase: "baseline_manual", cache: {}, baselineQuestions: offlineManualQuestions(),
      result: { schemaVersion: 1, protocolVersion: "offline-delayed-story-spatial-typed-v1", deliveryMode: "offline",
        packageId: offlineData.packageId, attemptId, participantId: offlineData.participantId, sessionId: offlineData.sessionId,
        startedAtUtc: offlineNow(), completedAtUtc: null, clientReportedTimestamps: true,
        baselineResponses: [], matchingResponses: [], postResponses: [] } };
    return { protocolVersion: "delayed-story-spatial-typed-retest-v3", attemptId: offlineAttempt.attemptId,
      participantId: offlineData.participantId, sessionId: offlineData.sessionId,
      studyCreatedAtUtc: offlineData.studyCreatedAtUtc, scheduledForUtc: offlineData.scheduledForUtc,
      daysSinceStudy: (Date.now() - Date.parse(offlineData.studyCreatedAtUtc)) / 86400000,
      wordCount: offlineData.words.length, matchingSessionCount: 1, priorDelayedAttemptCount: 0,
      manualQuestions: offlineAttempt.baselineQuestions };
  }
  if (!offlineAttempt || body.attemptId !== offlineAttempt.attemptId) throw new Error("No matching test is open.");
  const stage = body.stage || "baseline";
  const key = route + (route === "/api/manual" ? ":" + stage : "");
  const fingerprint = JSON.stringify(body);
  const cached = offlineAttempt.cache[key];
  if (cached) {
    if (cached.fingerprint !== fingerprint) throw new Error("This stage has already been saved.");
    return cached.reply;
  }
  let reply;
  if (route === "/api/manual" && stage === "baseline" && offlineAttempt.phase === "baseline_manual") {
    offlineAttempt.result.baselineResponses = offlineResponses(body.responses, offlineAttempt.baselineQuestions, false);
    offlineAttempt.result.baselineSubmittedAtUtc = offlineNow();
    offlineAttempt.result.storyShownAtUtc = offlineNow();
    offlineAttempt.phase = "story";
    reply = { nextPhase: "story", storyText: offlineData.storyText, baselineSaved: true };
  } else if (route === "/api/story" && offlineAttempt.phase === "story") {
    offlineAttempt.result.storyAcknowledgedAtUtc = offlineNow();
    offlineAttempt.result.storyReviewDurationMs = Math.max(0, Date.now() - Date.parse(offlineAttempt.result.storyShownAtUtc));
    offlineAttempt.matchingQuestions = offlineShuffle(offlineData.matchingQuestions).map((question) => ({ ...question, options: offlineShuffle(question.options) }));
    offlineAttempt.phase = "matching";
    reply = { nextPhase: "matching", matchingQuestions: offlineAttempt.matchingQuestions };
  } else if (route === "/api/matching" && offlineAttempt.phase === "matching") {
    offlineAttempt.result.matchingResponses = offlineResponses(body.responses, offlineAttempt.matchingQuestions, true);
    offlineAttempt.result.matchingSubmittedAtUtc = offlineNow();
    offlineAttempt.postQuestions = offlineManualQuestions();
    offlineAttempt.phase = "post_manual";
    reply = { nextPhase: "post_manual", manualQuestions: offlineAttempt.postQuestions };
  } else if (route === "/api/manual" && stage === "post" && offlineAttempt.phase === "post_manual") {
    offlineAttempt.result.postResponses = offlineResponses(body.responses, offlineAttempt.postQuestions, false);
    offlineAttempt.result.completedAtUtc = offlineNow();
    offlineAttempt.phase = "complete";
    reply = { nextPhase: "complete", scorePending: true, wordCount: offlineData.words.length,
      resultId: "offline_" + offlineData.participantId + "_" + offlineAttempt.attemptId };
  } else {
    throw new Error("Continue in the displayed test order.");
  }
  offlineAttempt.result.status = offlineAttempt.phase === "complete" ? "complete" : "in_progress";
  offlineAttempt.result.phase = offlineAttempt.phase;
  offlineAttempt.cache[key] = { fingerprint, reply };
  return reply;
}
// The shared frontend calls this local adapter instead of the browser's fetch.
const fetch = async (route, options) => {
  try { const reply = offlineApi(route, JSON.parse(options.body)); return { ok: true, json: async () => reply }; }
  catch (error) { return { ok: false, json: async () => ({ message: error.message }) }; }
};
document.getElementById("offlineDownload").addEventListener("click", () => {
  if (!offlineAttempt || offlineAttempt.phase !== "complete") return;
  const name = "delayed_offline_" + offlineData.participantId.replace(/[^A-Za-z0-9_-]/g, "_") + "_" + offlineAttempt.attemptId + ".json";
  const url = URL.createObjectURL(new Blob([JSON.stringify(offlineAttempt.result, null, 2)], { type: "application/json" }));
  const link = document.createElement("a");
  link.href = url;
  link.download = name;
  document.body.append(link);
  link.click();
  link.remove();
  setTimeout(() => URL.revokeObjectURL(url), 60000);
  offlineDownloaded = true;
  document.getElementById("offlineDownloadStatus").textContent = "Download requested. Check your Downloads folder and send the JSON file to the researcher before closing this page.";
});
window.addEventListener("beforeunload", (event) => {
  if (offlineAttempt && !offlineDownloaded) { event.preventDefault(); event.returnValue = ""; }
});
