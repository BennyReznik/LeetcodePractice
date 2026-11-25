const $ = id => document.getElementById(id);
const labels = {
  'passed': 'Passed', 'wrong-answer': 'Wrong answer', 'runtime-error': 'Runtime error',
  'time-limit': 'Time limit exceeded', 'compile-error': 'Compile error', 'not-run': 'Not run'
};
let editor, problem, revision = 0, seconds = 0, loading = true, dirty = false, saveTimer = 0, queue = Promise.resolve();

function today() {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

async function api(path, options = {}) {
  let response;
  try {
    response = await fetch('/api/problems' + path, { headers: { 'Content-Type': 'application/json' }, ...options });
  } catch {
    throw new Error('The app is not responding. Check that Docker is running.');
  }
  const body = await response.json().catch(() => ({}));
  if (body.revision !== undefined) revision = body.revision;
  if (response.status === 409) throw new Error('This problem was changed in another tab. Copy your code if you need it, then reload the page.');
  if (!response.ok) throw new Error(body.error || `Something went wrong (${response.status}).`);
  return body;
}

const work = () => ({
  code: editor.getValue(), time: $('time').value, space: $('space').value,
  trace: $('trace').value, seconds, revision, rows: readRows(), custom: readCustoms()
});

// Saves and runs go one at a time, so each uses the latest revision.
function enqueue(action) {
  const result = queue.then(action);
  queue = result.catch(() => {});
  return result;
}

function changed() {
  if (loading) return;
  dirty = true;
  $('saved').textContent = 'Saving…';
  clearTimeout(saveTimer);
  saveTimer = setTimeout(() => save().catch(() => {}), 700);
}

function save() {
  clearTimeout(saveTimer);
  return enqueue(async () => {
    if (!dirty) return;
    dirty = false;
    try {
      await api(`/${problem.id}/work`, { method: 'PUT', body: JSON.stringify(work()) });
      if (!dirty) $('saved').textContent = 'Saved';
    } catch (e) {
      dirty = true;
      $('saved').textContent = e.message;
      throw e;
    }
  });
}

function showTimer() {
  $('timer').textContent = `${String(Math.floor(seconds / 60)).padStart(2, '0')}:${String(seconds % 60).padStart(2, '0')}`;
}

function element(tag, className, text) {
  const e = document.createElement(tag);
  e.className = className;
  e.textContent = text;
  return e;
}

// Trace table and own tests

function field(tag, label, value, placeholder = '') {
  const e = document.createElement(tag);
  e.value = value;
  e.placeholder = placeholder;
  e.setAttribute('aria-label', label);
  e.addEventListener('input', changed);
  return e;
}

function removeButton(label, target) {
  const b = element('button', 'remove', '×');
  b.setAttribute('aria-label', label);
  b.onclick = () => { target.remove(); changed(); };
  return b;
}

function addRow(values = []) {
  const row = document.createElement('tr');
  problem.traceColumns.forEach((name, i) => {
    const input = field('input', name, values[i] || '');
    input.addEventListener('keydown', e => {
      if (e.key !== 'Enter') return;
      e.preventDefault();
      if (!row.nextElementSibling) addRow();
      row.nextElementSibling.querySelector('input').focus();
    });
    const cell = document.createElement('td');
    cell.append(input);
    row.append(cell);
  });
  const last = document.createElement('td');
  last.append(removeButton('Remove row', row));
  row.append(last);
  $('rows').tBodies[0].append(row);
}

function showRows(rows) {
  const head = document.createElement('tr');
  problem.traceColumns.forEach(name => head.append(element('th', '', name)));
  head.append(element('th', '', ''));
  const thead = document.createElement('thead');
  thead.append(head);
  $('rows').replaceChildren(thead, document.createElement('tbody'));
  const count = Math.max(rows.length, problem.traceRows);
  for (let i = 0; i < count; i++) addRow(rows[i]);
}

const readRows = () => [...$('rows').tBodies[0].rows]
  .map(r => [...r.querySelectorAll('input')].map(i => i.value))
  .filter(r => r.some(v => v.trim()));

const fullRows = () => readRows().filter(r => r.every(v => v.trim())).length;

function addCustom(test = { input: '', expected: '' }) {
  const box = element('div', 'custom', '');
  box.append(
    field('textarea', 'Input', test.input, problem.exampleInput),
    field('textarea', 'Predicted answer', test.expected, problem.exampleAnswer),
    removeButton('Remove test', box));
  $('customs').append(box);
}

const readCustoms = () => [...$('customs').children].map(box => {
  const [input, expected] = box.querySelectorAll('textarea');
  return { input: input.value, expected: expected.value };
}).filter(t => t.input.trim() || t.expected.trim());

// Loading problems and reviews

async function loadList() {
  const list = await api('?today=' + today());
  const marks = { due: '↻ ', solved: '✓ ', started: '• ', new: '' };
  $('problem').replaceChildren();
  for (const lesson of [...new Set(list.map(p => p.lesson))]) {
    const group = document.createElement('optgroup');
    group.label = lesson;
    list.filter(p => p.lesson === lesson).forEach(p => group.append(new Option(`${marks[p.status]}${p.title} (${p.id})`, p.id)));
    $('problem').append(group);
  }
  if (problem) $('problem').value = problem.id;
  const due = list.filter(p => p.status === 'due');
  $('due').hidden = due.length === 0;
  $('due').replaceChildren(element('span', '', 'Due for review:'), ...due.map(p => {
    const b = element('button', '', p.title);
    b.onclick = () => choose(p.id);
    return b;
  }));
  return list;
}

async function load(id) {
  loading = true;
  try {
    let next = await api('/' + id);
    // A due problem opens blank. The server keeps the old solution in "attempts".
    if (next.review?.due && next.review.due <= today() && !next.review.started) {
      await api(`/${id}/start-review`, { method: 'POST', body: JSON.stringify({ revision: next.work.revision, today: today() }) });
      next = await api('/' + id);
    }
    problem = next;
    revision = problem.work.revision;
    seconds = problem.work.seconds;
    $('problem').value = id;
    $('description').textContent = problem.description;
    $('traceLabel').textContent = `${problem.tracePrompt} (at least ${problem.traceRows} rows)`;
    $('time').value = problem.work.time;
    $('space').value = problem.work.space;
    $('trace').value = problem.work.trace;
    showRows(problem.work.rows || []);
    $('customs').replaceChildren();
    (problem.work.custom || []).forEach(t => addCustom(t));
    $('ownTests').open = (problem.work.custom || []).length > 0;
    editor.setValue(problem.work.code);
    monaco.editor.setModelMarkers(editor.getModel(), 'judge', []);
    $('results').replaceChildren(reviewNote());
    $('saved').textContent = '';
    localStorage.setItem('lastProblem', id);
    showTimer();
    dirty = false;
  } finally {
    loading = !problem;
  }
}

function reviewNote() {
  const review = problem.review;
  if (!review) return element('div', 'skip', '');
  if (!review.due) return element('div', 'skip', 'Both reviews are done.');
  if (review.started) return element('div', 'summary skip', 'Review in progress. Your old solution is saved in attempts. Submit a passing solution to finish.');
  return element('div', 'skip', 'Next review: ' + review.due);
}

async function choose(id) {
  try { await save(); await load(id); }
  catch (e) {
    if (problem) $('problem').value = problem.id;
    message(e.message);
  }
}

// Running

function message(text, className = 'bad') {
  $('results').replaceChildren(element('div', 'summary ' + className, text));
}

function showResult(result, submit) {
  const box = $('results');
  box.replaceChildren();
  monaco.editor.setModelMarkers(editor.getModel(), 'judge', result.diagnostics.map(d => ({
    startLineNumber: d.line, endLineNumber: d.line, startColumn: d.column, endColumn: d.column + 1,
    message: d.message, severity: monaco.MarkerSeverity.Error
  })));
  if (result.status === 'compile-error') {
    box.append(element('div', 'summary bad', 'Compile error'));
    result.diagnostics.forEach(d => {
      const b = element('button', 'link', `Line ${d.line}: ${d.message}`);
      b.onclick = () => { editor.setPosition({ lineNumber: d.line, column: d.column }); editor.revealLineInCenter(d.line); editor.focus(); };
      box.append(b);
    });
    return;
  }
  const good = result.status === 'passed';
  const count = `${result.passed} of ${result.total} tests passed`;
  box.append(element('div', 'summary ' + (good ? 'good' : 'bad'),
    good ? `${count}.${submit ? ' Solved! Type "review" in the chat.' : ' Submit also runs the edge cases.'}` : `${labels[result.status]}. ${count}.`));
  if (submit && good) box.append(reviewNote());
  if (result.tests.some(t => t.custom && t.status === 'wrong-answer'))
    box.append(element('div', 'summary bad', 'A result differs from your prediction. Find out which one is wrong.'));
  result.tests.forEach(t => {
    const kind = t.status === 'passed' ? 'good' : t.status === 'not-run' ? 'skip' : 'bad';
    const label = t.custom && t.status === 'wrong-answer' ? 'Differs from your prediction' : labels[t.status];
    const card = element('div', 'test ' + kind, `${label}: ${t.name}${t.status === 'passed' ? ` (${t.ms} ms)` : ''}`);
    let details = '';
    if (t.status !== 'passed' && t.status !== 'not-run') {
      details = `Input:     ${t.input}\n${t.custom ? 'Predicted:' : 'Expected: '} ${t.expected}`;
      if (t.actual !== null) details += `\nActual:    ${t.actual}`;
      if (t.error) details += `\n${t.error}`;
    }
    if (t.output) details += `${details ? '\n' : ''}Output:\n${t.output}`;
    if (details) card.append(element('pre', '', details));
    box.append(card);
  });
}

async function run(submit) {
  if (loading) return;
  if (submit && (!$('time').value.trim() || !$('space').value.trim()))
    return message('Fill in both complexities before submitting.');
  if (submit && fullRows() < problem.traceRows)
    return message(`Fill in at least ${problem.traceRows} complete trace rows before submitting. Use - where a value does not apply.`);
  const buttons = [$('run'), $('submit'), $('problem'), $('reset'), ...$('due').querySelectorAll('button')];
  buttons.forEach(b => b.disabled = true);
  message('Running…', 'skip');
  clearTimeout(saveTimer);
  try {
    const response = await enqueue(() => {
      dirty = false;
      return api(`/${problem.id}/run`, { method: 'POST', body: JSON.stringify({ work: work(), submit, today: today() }) });
    });
    if (!dirty) $('saved').textContent = 'Saved';
    problem.review = response.review;
    showResult(response.result, submit);
    await loadList();
  } catch (e) {
    dirty = true;
    message(e.message);
  } finally {
    buttons.forEach(b => b.disabled = false);
    $('results').scrollIntoView({ block: 'nearest' });
  }
}

// Editor

const types = 'Array ArgumentException Console Dictionary HashSet IDictionary IEnumerable IList KeyValuePair LinkedList List Math PriorityQueue Queue SortedDictionary SortedSet Stack StringBuilder'.split(' ');
const keywords = 'bool break char continue double else false for foreach if int long new null public private return static string throw true var void while'.split(' ');
const members = 'Add Append Clear Contains ContainsKey Count Dequeue Enqueue IsDigit IsLetter IsLetterOrDigit Join Keys Length Max MaxValue Min MinValue Peek Pop Push Remove Reverse Sort Split Substring Sum ToArray ToCharArray ToList ToLower ToLowerInvariant ToString TryAdd TryGetValue Values'.split(' ');

require.config({ paths: { vs: 'vs' } });
require(['vs/editor/editor.main'], async () => {
  monaco.languages.registerCompletionItemProvider('csharp', {
    triggerCharacters: ['.'],
    provideCompletionItems(model, position) {
      const word = model.getWordUntilPosition(position);
      const range = { startLineNumber: position.lineNumber, endLineNumber: position.lineNumber, startColumn: word.startColumn, endColumn: word.endColumn };
      const afterDot = model.getLineContent(position.lineNumber)[word.startColumn - 2] === '.';
      const kinds = monaco.languages.CompletionItemKind;
      const make = (list, kind) => list.map(label => ({ label, kind, insertText: label, range }));
      if (afterDot) return { suggestions: make(members, kinds.Method) };
      const names = [...new Set(model.getValue().match(/[A-Za-z_]\w{2,}/g) || [])]
        .filter(n => n !== word.word && !types.includes(n) && !keywords.includes(n) && !members.includes(n));
      return { suggestions: [...make(types, kinds.Class), ...make(keywords, kinds.Keyword), ...make(names, kinds.Variable)] };
    }
  });

  editor = monaco.editor.create($('editor'), {
    language: 'csharp', theme: 'vs-dark', automaticLayout: true, minimap: { enabled: false },
    fontSize: 14, tabSize: 4, insertSpaces: true, scrollBeyondLastLine: false, wordBasedSuggestions: 'off',
    autoClosingBrackets: 'always', autoIndent: 'full', fixedOverflowWidgets: true
  });

  // Close "<" only after a type name, so "i < n" is left alone.
  editor.onDidType(text => {
    const p = editor.getPosition(), model = editor.getModel(), line = model.getLineContent(p.lineNumber);
    const before = line.slice(0, p.column - 1);
    if (text === '<' && types.some(t => new RegExp(`\\b${t}<$`).test(before))) {
      editor.executeEdits('generic', [{ range: new monaco.Range(p.lineNumber, p.column, p.lineNumber, p.column), text: '>' }]);
      editor.setPosition(p);
    } else if (text === '>' && line[p.column - 1] === '>' && line.split('>').length > line.split('<').length) {
      editor.executeEdits('generic', [{ range: new monaco.Range(p.lineNumber, p.column, p.lineNumber, p.column + 1), text: '' }]);
    }
  });
  editor.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.Enter, () => run(false));
  editor.onDidChangeModelContent(() => {
    // Bracket closing needs up-to-date tokens; Monaco updates them in the background, too late for fast typing.
    const model = editor.getModel();
    model.tokenization?.forceTokenization?.(model.getLineCount());
    changed();
  });
  ['time', 'space', 'trace'].forEach(id => $(id).addEventListener('input', changed));

  $('run').onclick = () => run(false);
  $('submit').onclick = () => run(true);
  $('addRow').onclick = () => { addRow(); $('rows').tBodies[0].lastElementChild.querySelector('input').focus(); };
  $('addCustom').onclick = () => { addCustom(); $('customs').lastElementChild.querySelector('textarea').focus(); };
  $('restart').onclick = () => { seconds = 0; showTimer(); changed(); };
  $('suggest').checked = localStorage.getItem('suggest') !== 'off';
  const applySuggest = () => {
    const on = $('suggest').checked;
    localStorage.setItem('suggest', on ? 'on' : 'off');
    editor.updateOptions({ quickSuggestions: on, suggestOnTriggerCharacters: on });
  };
  $('suggest').onchange = applySuggest;
  applySuggest();
  $('reset').onclick = () => {
    const b = $('reset');
    if (b.textContent === 'Confirm reset') {
      editor.executeEdits('reset', [{ range: editor.getModel().getFullModelRange(), text: problem.starter }]); // Ctrl+Z undoes this
      b.textContent = 'Reset code';
    } else {
      b.textContent = 'Confirm reset';
      setTimeout(() => b.textContent = 'Reset code', 3000);
    }
  };
  $('problem').onchange = e => choose(e.target.value);
  setInterval(() => { if (!loading && !document.hidden) { seconds++; showTimer(); } }, 1000);
  addEventListener('focus', () => { if (!loading) loadList().catch(() => {}); }); // picks up reviews that became due
  addEventListener('beforeunload', e => { if (dirty) { save().catch(() => {}); e.preventDefault(); } });

  try {
    const list = await loadList();
    const last = Number(localStorage.getItem('lastProblem'));
    const remembered = list.find(p => p.id === last);
    // Reviews start only when chosen, not because the problem was the last one open.
    const first = remembered && remembered.status !== 'due' ? remembered : list.find(p => p.status !== 'due') || list[0];
    await load(first.id);
  } catch (e) {
    message(e.message);
  }
});
