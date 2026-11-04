"use strict";

// Code-level DOM fixture: no browser dependencies or participant records.
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");
const web = path.join(__dirname, "../web");
const html = fs.readFileSync(path.join(web, "index.html"), "utf8");
assert.doesNotMatch(html, /protocol-explanation|First type the English meanings without reviewing your story/);
class Element {
  constructor() {
    this.hidden = true;
    this.disabled = false;
    this.value = "";
    this.textContent = "";
    this.dataset = {};
    this.style = {};
    this.children = [];
    this.events = {};
    this.classList = { toggle() {} };
  }
  addEventListener(type, handler) { this.events[type] = handler; }
  setAttribute() {}
  focus() {}
  replaceChildren() { this.children = []; }
  append(child) { this.children.push(child); }
  querySelectorAll() { return this.children; }
  set src(value) { this.imageUrl = value; this.onload(); }
}
const elements = Object.fromEntries([...html.matchAll(/id="([^"]+)"/g)].map((match) => [match[1], new Element()]));
const steps = ["recall", "baseline", "story", "matching", "post"].map((stage) => {
  const element = new Element();
  element.dataset.stage = stage;
  return element;
});
const requests = [];
let nowMs = 0;
let nextIntervalId = 1;
const intervals = new Map();
class TestDate extends Date { static now() { return nowMs; } }
const inviteMode = process.argv.includes("--invite");
let removedFragment = false;
const replies = [
  { protocolVersion: "delayed-story-spatial-typed-retest-v3", attemptId: "synthetic", participantId: "P-DEMO",
    sessionId: "demo", wordCount: 1, daysSinceStudy: 8, manualQuestions: [{ questionId: "b", word: "rueda" }] },
  { nextPhase: "story", storyText: "Synthetic original story" },
  { nextPhase: "matching", matchingQuestions: [{ questionId: "m", furnitureLabel: "Desk",
    snapshotUrl: "/synthetic.png", options: [{ optionId: "o", label: "rueda" }] }] },
  { nextPhase: "post_manual", manualQuestions: [{ questionId: "p", word: "rueda" }] },
  { nextPhase: "complete", baselineManualScore: 1, matchingScore: 1, postManualScore: 1, wordCount: 1, resultId: "demo-result" },
];
if (inviteMode) replies.unshift({ ok: true, sessionId: "demo" });
const context = vm.createContext({
  document: {
    getElementById: (id) => { assert.ok(elements[id], "Unknown DOM ID: " + id); return elements[id]; },
    querySelectorAll: (selector) => selector === "[data-stage]" ? steps : elements.choiceOptions.children,
    createElement: () => new Element(),
  },
  window: { scrollTo() {}, setTimeout: (fn) => fn(),
    setInterval: (fn) => { const id = nextIntervalId++; intervals.set(id, fn); return id; },
    clearInterval: (id) => intervals.delete(id),
    location: { hash: inviteMode ? "#access=synthetic-secret" : "", pathname: "/", search: "?id=P-DEMO" },
    history: { replaceState: (_, __, url) => { assert.equal(url, "/?id=P-DEMO"); removedFragment = true; } } },
  performance: { now: () => 1000 }, URLSearchParams, Intl, Date: TestDate,
  fetch: async (route, options) => {
    if (route === "/api/access") assert.equal(options.headers.Authorization, "Bearer synthetic-secret");
    requests.push({ route, body: JSON.parse(options.body) });
    const reply = replies.shift();
    assert.ok(reply, "Unexpected request: " + route);
    return { ok: true, json: async () => reply };
  },
});
vm.runInContext(fs.readFileSync(path.join(web, "app.js"), "utf8"), context);
async function event(id, type = "click") {
  await elements[id].events[type]({ preventDefault() {} });
  await new Promise(setImmediate);
}
function advanceClock(ms) {
  nowMs += ms;
  for (const tick of [...intervals.values()]) tick();
}
(async () => {
  elements.lookupId.value = "P-DEMO";
  await event("lookupForm", "submit");
  assert.match(html, /story you created or selected one week ago/);
  assert.match(html, /VR room layout/);
  assert.match(html, /where the furniture was placed/);
  assert.match(html, /order in which you walked through the room/);
  assert.match(html, /which Spanish word belonged to each furniture item/);
  await event("startRecallButton");
  assert.equal(elements.recallView.hidden, false);
  assert.equal(elements.recallTimer.textContent, "03:00");
  assert.equal(elements.beginManualButton.disabled, true);
  await event("beginManualButton");
  assert.equal(elements.testView.hidden, true, "First word test must stay locked during recall");
  advanceClock(179000);
  assert.equal(elements.recallTimer.textContent, "00:01");
  assert.equal(elements.beginManualButton.disabled, true);
  advanceClock(1000);
  assert.equal(elements.recallTimer.textContent, "00:00");
  assert.equal(elements.beginManualButton.disabled, false);
  assert.equal(intervals.size, 0);
  await event("beginManualButton");
  assert.equal(elements.testView.hidden, false);
  elements.manualAnswer.value = "wheel";
  await event("manualForm", "submit");
  assert.equal(elements.storyView.hidden, false, "First typed test must go directly to story");
  assert.equal(elements.storyText.textContent, "Synthetic original story");
  await event("finishStoryButton");
  assert.equal(elements.manualForm.hidden, true);
  assert.equal(elements.choiceOptions.children[0].textContent, "rueda");
  await elements.choiceOptions.children[0].events.click();
  await event("choiceNextButton");
  assert.equal(elements.transitionView.hidden, false);
  await event("beginRetestButton");
  assert.equal(elements.choiceForm.hidden, true);
  elements.manualAnswer.value = "wheel";
  await event("manualForm", "submit");
  assert.equal(elements.resultView.hidden, false, "Second typed test must finish immediately");
  for (const id of ["manualScoreValue", "matchingScoreValue", "postManualScoreValue"]) {
    assert.equal(elements[id], undefined, "Scores must not appear on the result page");
  }
  assert.equal(elements.choiceScoreValue, undefined);
  assert.equal(elements.postChoiceScoreValue, undefined);
  const testRequests = inviteMode ? requests.slice(1) : requests;
  if (inviteMode) { assert.equal(requests[0].route, "/api/access"); assert.ok(removedFragment); }
  assert.deepEqual(testRequests.map((request) => request.route), ["/api/start", "/api/manual", "/api/story", "/api/matching", "/api/manual"]);
  assert.equal(testRequests[1].body.stage, "baseline");
  assert.equal(testRequests[4].body.stage, "post");
  assert.equal(replies.length, 0);
  console.log("PASS: 3-minute recall -> typed baseline -> story -> Spanish furniture matching -> typed retest -> no displayed scores");
})().catch((error) => { console.error(error); process.exitCode = 1; });
