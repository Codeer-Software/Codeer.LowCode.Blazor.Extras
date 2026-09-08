// MarkdownField のツールバー操作。textarea の選択範囲に記法を差し込み、新しい全文を返す。
// 言語依存の文言は入れない (空選択なら記号だけ入れてカーソルを間に置く)。

function lineRange(value, start, end) {
  const lineStart = value.lastIndexOf('\n', start - 1) + 1;
  let lineEnd = value.indexOf('\n', end);
  if (lineEnd < 0) lineEnd = value.length;
  return { lineStart, lineEnd };
}

function wrap(value, start, end, before, after) {
  const selected = value.slice(start, end);
  // すでに囲まれていれば外す
  if (start >= before.length && value.slice(start - before.length, start) === before && value.slice(end, end + after.length) === after) {
    return { text: value.slice(0, start - before.length) + selected + value.slice(end + after.length), start: start - before.length, end: end - before.length };
  }
  const text = value.slice(0, start) + before + selected + after + value.slice(end);
  return { text, start: start + before.length, end: start + before.length + selected.length };
}

function prefixLines(value, start, end, prefixOf) {
  const { lineStart, lineEnd } = lineRange(value, start, end);
  const lines = value.slice(lineStart, lineEnd).split('\n');
  const prefixes = lines.map((_, i) => prefixOf(i));
  const allPrefixed = lines.every((l, i) => l.startsWith(prefixes[i]) || (l.length === 0 && lines.length > 1));
  const replaced = lines.map((l, i) => allPrefixed ? l.replace(prefixes[i], '') : (l.length === 0 && lines.length > 1 ? l : prefixes[i] + l)).join('\n');
  const text = value.slice(0, lineStart) + replaced + value.slice(lineEnd);
  return { text, start: lineStart, end: lineStart + replaced.length };
}

function cycleHeading(value, start, end) {
  const { lineStart, lineEnd } = lineRange(value, start, start);
  const line = value.slice(lineStart, lineEnd);
  const m = /^(#{1,6})\s/.exec(line);
  let replaced;
  if (!m) replaced = '# ' + line;
  else if (m[1].length >= 3) replaced = line.slice(m[0].length);
  else replaced = '#' + line;
  const text = value.slice(0, lineStart) + replaced + value.slice(lineEnd);
  const pos = lineStart + replaced.length;
  return { text, start: pos, end: pos };
}

// ブロック要素 (表・コードブロック) をカーソル行の前に挿入する。前後を空行で区切らないと
// 隣の段落と結合して表として認識されないので、必ず空行を挟む
function insertBlock(value, start, end, block) {
  const { lineStart } = lineRange(value, start, start);
  let before = value.slice(0, lineStart);
  let after = value.slice(lineStart);
  if (before.length > 0 && !before.endsWith('\n\n')) before += before.endsWith('\n') ? '\n' : '\n\n';
  if (after.length > 0 && !after.startsWith('\n\n')) after = (after.startsWith('\n') ? '\n' : '\n\n') + after;
  const text = before + block + after;
  const pos = before.length + block.length;
  return { text, start: pos, end: pos };
}

// ===== 高さの自動伸長 =====
// 通常の行では中身に合わせて textarea を伸ばす。FillAvailable や行 Height で外から高さが決まっているときは
// 伸ばさず、flex の stretch に任せて内部スクロールにする。
// 判定: textarea の高さを 0 にしても親 (.markdown-body) の高さが変わらなければ「外から決まっている」。
const attached = new WeakMap();

function autoSize(textarea) {
  const body = textarea.closest('.markdown-body');
  if (!body) return;
  const before = body.clientHeight;
  // CSS の min-height (最小 6 行) が効いていると 0 に縮まず判定できないので、判定の間だけ外す
  textarea.style.minHeight = '0px';
  textarea.style.height = '0px';
  const constrained = body.clientHeight >= before - 1 && before > 0;
  textarea.style.minHeight = '';
  if (constrained) {
    textarea.style.height = '';
    return;
  }
  const style = getComputedStyle(textarea);
  const border = parseFloat(style.borderTopWidth) + parseFloat(style.borderBottomWidth);
  textarea.style.height = (textarea.scrollHeight + border) + 'px';
}

export function attach(textarea) {
  if (!textarea) return;
  if (!attached.has(textarea)) {
    const onInput = () => autoSize(textarea);
    textarea.addEventListener('input', onInput);
    attached.set(textarea, onInput);
  }
  autoSize(textarea);
}

export function detach(textarea) {
  const h = textarea && attached.get(textarea);
  if (!h) return;
  textarea.removeEventListener('input', h);
  attached.delete(textarea);
}

export function applyAction(textarea, action) {
  if (!textarea) return null;
  const value = textarea.value;
  const start = textarea.selectionStart ?? value.length;
  const end = textarea.selectionEnd ?? start;
  const selected = value.slice(start, end);
  let r;
  switch (action) {
    case 'bold': r = wrap(value, start, end, '**', '**'); break;
    case 'italic': r = wrap(value, start, end, '*', '*'); break;
    case 'strike': r = wrap(value, start, end, '~~', '~~'); break;
    case 'code':
      r = selected.includes('\n')
        ? { text: value.slice(0, start) + '```\n' + selected + '\n```' + value.slice(end), start: start + 4, end: start + 4 + selected.length }
        : wrap(value, start, end, '`', '`');
      break;
    case 'codeblock':
      r = selected.length > 0
        ? { text: value.slice(0, start) + '```\n' + selected + '\n```' + value.slice(end), start: start + 4, end: start + 4 + selected.length }
        : insertBlock(value, start, end, '```\n\n```');
      if (selected.length === 0) { r.start = r.end = r.end - 4; }
      break;
    case 'heading': r = cycleHeading(value, start, end); break;
    case 'ul': r = prefixLines(value, start, end, () => '- '); break;
    case 'ol': r = prefixLines(value, start, end, i => (i + 1) + '. '); break;
    case 'task': r = prefixLines(value, start, end, () => '- [ ] '); break;
    case 'quote': r = prefixLines(value, start, end, () => '> '); break;
    case 'link':
      if (/^https?:\/\/\S+$/.test(selected)) {
        const text = value.slice(0, start) + '[](' + selected + ')' + value.slice(end);
        r = { text, start: start + 1, end: start + 1 };
      } else {
        const text = value.slice(0, start) + '[' + selected + '](https://)' + value.slice(end);
        const urlStart = start + 1 + selected.length + 2;
        r = { text, start: urlStart, end: urlStart + 8 };
      }
      break;
    case 'table':
      r = insertBlock(value, start, end, '| A | B | C |\n|---|---|---|\n|   |   |   |\n|   |   |   |');
      break;
    default: return null;
  }
  textarea.value = r.text;
  textarea.focus();
  textarea.setSelectionRange(r.start, r.end);
  // Blazor 側の oninput を発火させて編集中テキストを同期する
  textarea.dispatchEvent(new Event('input', { bubbles: true }));
  return r.text;
}
