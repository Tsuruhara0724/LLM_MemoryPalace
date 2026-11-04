"use strict";
const assert = require("node:assert/strict");
const fs = require("node:fs");
const vm = require("node:vm");
const { webcrypto } = require("node:crypto");
const html = fs.readFileSync(process.argv[2], "utf8");
const dataText = html.match(/<script id="offlineData" type="application\/json">([\s\S]*?)<\/script>/)[1];
const publicData = JSON.parse(dataText);
const code = html.match(/<script>\r?\n([\s\S]*?)\r?\n<\/script>/)[1];
let downloadBlob;
let downloadName;
class Element {
  constructor() { this.hidden = true; this.disabled = false; this.value = ""; this.textContent = ""; this.dataset = {}; this.style = {}; this.children = []; this.events = {}; this.classList = { toggle() {} }; }
  addEventListener(type, handler) { this.events[type] = handler; }
  setAttribute() {}
  focus() {}
  replaceChildren() { this.children = []; }
  append(child) { this.children.push(child); }
  querySelectorAll() { return this.children; }
  set src(value) { assert.ok(value.startsWith('data:image/')); this.onload(); }
  click() { downloadName = this.download; }
  remove() {}
}
const elements = Object.fromEntries([...html.matchAll(/id="([^"]+)"/g)].map((match) => [match[1], new Element()]));
elements.offlineData.textContent = dataText;
const steps = ['baseline', 'story', 'matching', 'post'].map((stage) => { const item = new Element(); item.dataset.stage = stage; return item; });
const context = vm.createContext({ document: {
  getElementById: (id) => { assert.ok(elements[id], 'Missing ID: ' + id); return elements[id]; },
  createElement: () => new Element(), body: new Element(),
  querySelectorAll: (selector) => selector === '[data-stage]' ? steps : elements.choiceOptions.children,
}, window: { addEventListener() {}, scrollTo() {}, setTimeout: (fn) => fn(), location: { search: '' } },
  crypto: webcrypto, Blob, URLSearchParams, Uint8Array, Uint32Array, Intl, Date, performance: { now: () => 2000 },
  URL: { createObjectURL: (blob) => { downloadBlob = blob; return 'blob:test'; }, revokeObjectURL() {} },
  setTimeout() {},
  fetch: () => { throw new Error('Unexpected network request'); },
});
vm.runInContext(code, context);
async function event(id, type = 'click') { await elements[id].events[type]({ preventDefault() {} }); await new Promise(setImmediate); }
(async () => {
  elements.lookupId.value = publicData.participantId;
  await event('lookupForm', 'submit');
  await event('beginManualButton');
  for (let i = 0; i < publicData.words.length; i++) await event('manualUnknownButton');
  assert.equal(elements.storyView.hidden, false);
  assert.equal(elements.storyText.textContent, publicData.storyText);
  await event('finishStoryButton');
  for (let i = 0; i < publicData.words.length; i++) {
    await elements.choiceOptions.children[0].events.click();
    await event('choiceNextButton');
  }
  await event('beginRetestButton');
  for (let i = 0; i < publicData.words.length; i++) { elements.manualAnswer.value = 'test'; await event('manualForm', 'submit'); }
  assert.equal(elements.resultView.hidden, false);
  assert.equal(elements.manualScoreValue.textContent, 'Recorded');
  await event('offlineDownload');
  assert.ok(downloadName.endsWith('.json'));
  const returned = JSON.parse(await downloadBlob.text());
  assert.equal(returned.status, 'complete');
  assert.equal(returned.protocolVersion, 'offline-delayed-story-spatial-typed-v1');
  assert.equal(returned.baselineResponses.length, publicData.words.length);
  assert.equal(returned.matchingResponses.length, publicData.words.length);
  assert.equal(returned.postResponses.length, publicData.words.length);
  assert.equal(returned.packageId, publicData.packageId);
  assert.ok(!JSON.stringify(returned).includes('expectedMeaning'));
  console.log('PASS: complete offline flow and JSON download; no network, no answer key, no fabricated scores');
})().catch((error) => { console.error(error); process.exitCode = 1; });
