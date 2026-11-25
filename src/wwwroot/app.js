const $ = id => document.getElementById(id);
const labels = {
  'passed': 'Passed', 'wrong-answer': 'Wrong answer', 'runtime-error': 'Runtime error',
  'time-limit': 'Time limit exceeded', 'compile-error': 'Compile error', 'not-run': 'Not run'
};
let editor, problem, revision = 0, seconds = 0, loading = true, dirty = false, saveTimer = 0, queue = Promise.resolve();

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
  trace: $('trace').value, seconds, revision
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

async function loadList() {
  const list = await api('');
  const marks = { solved: '✓ ', started: '• ', new: '' };
  $('problem').replaceChildren();
  for (const lesson of [...new Set(list.map(p => p.lesson))]) {
    const group = document.createElement('optgroup');
    group.label = lesson;
    list.filter(p => p.lesson === lesson).forEach(p => group.append(new Option(`${marks[p.status]}${p.title} (${p.id})`, p.id)));
    $('problem').append(group);
  }
  if (problem) $('problem').value = problem.id;
  return list;
}

async function load(id) {
  loading = true;
  problem = await api('/' + id);
  revision = problem.work.revision;
  seconds = problem.work.seconds;
  $('problem').value = id;
  $('description').textContent = problem.description;
  $('traceLabel').textContent = problem.tracePrompt;
  $('time').value = problem.work.time;
  $('space').value = problem.work.space;
  $('trace').value = problem.work.trace;
  editor.setValue(problem.work.code);
  monaco.editor.setModelMarkers(editor.getModel(), 'judge', []);
  $('results').replaceChildren();
  $('saved').textContent = '';
  localStorage.setItem('lastProblem', id);
  showTimer();
  dirty = false;
  loading = false;
}

function element(tag, className, text) {
  const e = document.createElement(tag);
  e.className = className;
  e.textContent = text;
  return e;
}

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
  const count = `${result.passed} of ${result.tests.length} tests passed`;
  box.append(element('div', 'summary ' + (good ? 'good' : 'bad'),
    good ? `${count}.${submit ? ' Solved! Type "review" in the chat.' : ' Submit to run the edge cases.'}` : `${labels[result.status]}. ${count}.`));
  result.tests.forEach(t => {
    const kind = t.status === 'passed' ? 'good' : t.status === 'not-run' ? 'skip' : 'bad';
    const card = element('div', 'test ' + kind, `${labels[t.status]}: ${t.name}${t.status === 'passed' ? ` (${t.ms} ms)` : ''}`);
    let details = '';
    if (t.status !== 'passed' && t.status !== 'not-run') {
      details = `Input:    ${t.input}\nExpected: ${t.expected}`;
      if (t.actual !== null) details += `\nActual:   ${t.actual}`;
      if (t.error) details += `\n${t.error}`;
    }
    if (t.output) details += `${details ? '\n' : ''}Output:\n${t.output}`;
    if (details) card.append(element('pre', '', details));
    box.append(card);
  });
}

async function run(submit) {
  if (submit && (!$('time').value.trim() || !$('space').value.trim() || !$('trace').value.trim()))
    return message('Fill in both complexities and your trace before submitting.');
  const buttons = [$('run'), $('submit'), $('problem')];
  buttons.forEach(b => b.disabled = true);
  message('Running…', 'skip');
  clearTimeout(saveTimer);
  try {
    const response = await enqueue(() => {
      dirty = false;
      return api(`/${problem.id}/run`, { method: 'POST', body: JSON.stringify({ work: work(), submit }) });
    });
    if (!dirty) $('saved').textContent = 'Saved';
    showResult(response.result, submit);
    await loadList();
  } catch (e) {
    dirty = true;
    message(e.message);
  } finally {
    buttons.forEach(b => b.disabled = false);
  }
}

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
  $('problem').onchange = async e => {
    const id = e.target.value;
    try { await save(); await load(id); }
    catch (err) { $('problem').value = problem.id; message(err.message); }
  };
  setInterval(() => { if (!loading && !document.hidden) { seconds++; showTimer(); } }, 1000);
  addEventListener('beforeunload', e => { if (dirty) { save().catch(() => {}); e.preventDefault(); } });

  try {
    const list = await loadList();
    const last = Number(localStorage.getItem('lastProblem'));
    await load(list.some(p => p.id === last) ? last : list[0].id);
  } catch (e) {
    message(e.message);
  }
});
