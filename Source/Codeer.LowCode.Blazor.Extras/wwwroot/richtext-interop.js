import { sanitize } from './safe-html.js';

const state = new WeakMap();

function getState(editorElement) {
    if (!state.has(editorElement)) {
        state.set(editorElement, { savedRange: null });
    }
    return state.get(editorElement);
}

function saveSelection(editorElement) {
    const sel = window.getSelection();
    if (sel.rangeCount > 0) {
        const range = sel.getRangeAt(0);
        if (editorElement.contains(range.commonAncestorContainer)) {
            getState(editorElement).savedRange = range.cloneRange();
        }
    }
}

function restoreSelection(editorElement) {
    const s = getState(editorElement);
    editorElement.focus();
    if (!s.savedRange) return;
    const sel = window.getSelection();
    sel.removeAllRanges();
    sel.addRange(s.savedRange);
}

// --- Editor lifecycle ---

export function initEditor(editorElement, toolbarElement, dotNetRef, options) {
    const editorState = getState(editorElement);
    editorState.dotNetRef = dotNetRef;
    editorState.mention = !!(options && options.mention);
    editorElement.addEventListener('input', (e) => {
        if (editorState.suppressInput) return;
        dotNetRef.invokeMethodAsync('OnContentChanged', contentHtml(editorElement));
        // 変換中は判定しない (確定した後の compositionend で判定する)
        if (!e.isComposing) checkMention(editorElement);
    });
    editorElement.addEventListener('paste', (e) => {
        e.preventDefault();
        // 貼り付ける HTML は消毒する。HTML の無い貼り付けは文字として入れる
        const html = e.clipboardData.getData('text/html');
        if (html) document.execCommand('insertHTML', false, sanitize(html, 'richtext'));
        else document.execCommand('insertText', false, e.clipboardData.getData('text/plain'));
        markMentions(editorElement);
    });
    if (editorState.mention) {
        editorElement.addEventListener('compositionend', () => checkMention(editorElement));
        // 候補が出ている間の ↑ ↓ Enter Tab Esc は候補の操作 (改行・フォーカス移動をさせない)
        editorElement.addEventListener('keydown', (e) => {
            if (!editorState.mentionVisible || e.isComposing || e.keyCode === 229) return;
            if (!['ArrowUp', 'ArrowDown', 'Enter', 'Tab', 'Escape'].includes(e.key)) return;
            e.preventDefault();
            e.stopPropagation();
            if (e.key === 'Escape') {
                // 同じ @ では出し直さない (別の @ を打てばまた出る)
                const m = editorState.mentionRange;
                editorState.mentionDismissed = m ? { node: m.node, start: m.start } : null;
                closeMention(editorElement);
                return;
            }
            dotNetRef.invokeMethodAsync('OnMentionKey', e.key);
        });
        // カーソルを動かしたら判定し直す (@ の外へ出たら閉じる)
        editorElement.addEventListener('keyup', (e) => {
            if (['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(e.key)) checkMention(editorElement);
        });
        editorElement.addEventListener('mouseup', () => checkMention(editorElement));
        editorElement.addEventListener('blur', () => closeMention(editorElement));
        // 候補のクリックで編集欄からフォーカスを外さない (外れると blur で候補が閉じ、クリックが届かない)。候補は編集欄の枠の中に描かれる
        if (editorElement.parentElement) {
            editorElement.parentElement.addEventListener('mousedown', (e) => {
                if (e.target instanceof Element && e.target.closest('.richtext-mention-popup')) e.preventDefault();
            });
        }
    }
    editorElement.addEventListener('click', (e) => {
        const anchor = e.target.closest('a');
        if (anchor && editorElement.contains(anchor)) {
            e.preventDefault();
            window.open(anchor.href, '_blank', 'noopener,noreferrer');
        }
    });
    editorElement.addEventListener('mouseup', () => saveSelection(editorElement));
    editorElement.addEventListener('keyup', () => saveSelection(editorElement));
    toolbarElement.addEventListener('mousedown', () => saveSelection(editorElement));
}

export function setContent(editorElement, html) {
    closeMention(editorElement);
    editorElement.innerHTML = sanitize(html, 'richtext');
    ensureLinkTargets(editorElement);
    markMentions(editorElement);
}

export function dispose(editorElement) {
    state.delete(editorElement);
}

// --- Value (HTML) ---

// メンションの部品は編集中だけ 1 つの塊にする (途中にカーソルを入れない・Backspace で丸ごと消える)
function markMentions(editorElement) {
    editorElement.querySelectorAll('span.mention').forEach(e => e.setAttribute('contenteditable', 'false'));
}

// 値として渡す HTML。編集中だけの印 (contenteditable) は残さない
function contentHtml(editorElement) {
    markMentions(editorElement);
    const clone = editorElement.cloneNode(true);
    clone.querySelectorAll('[contenteditable]').forEach(e => e.removeAttribute('contenteditable'));
    return clone.innerHTML;
}

// --- Format commands ---

export function execFormatCommand(editorElement, command, value) {
    editorElement.focus();
    if (command === 'formatBlock') {
        applyFormatBlock(editorElement, value || 'div');
    } else {
        document.execCommand(command, false, value || null);
    }
    return contentHtml(editorElement);
}

export function execFormatCommandWithRestore(editorElement, command, value) {
    restoreSelection(editorElement);
    document.execCommand(command, false, value || null);
    saveSelection(editorElement);
    return contentHtml(editorElement);
}

export function clearAllFormatting(editorElement) {
    editorElement.focus();
    document.execCommand('removeFormat', false, null);
    applyFormatBlock(editorElement, 'div');
    return contentHtml(editorElement);
}

// --- Mention ---

const MENTION_MARKS = ['@', '＠'];
const SPACE = /[\s 　]/;
// RichTextField.MaxMentionTextLength と同じ (超えた文字では候補を出さない)
const MAX_MENTION_QUERY = 50;

// カーソルの直前が「行頭か空白の後の @ + 空白を含まない文字」なら、その @ の位置と打った文字を返す
function findMentionQuery(editorElement) {
    const sel = window.getSelection();
    if (!sel.rangeCount || !sel.isCollapsed) return null;
    const node = sel.anchorNode;
    const offset = sel.anchorOffset;
    if (!node || node.nodeType !== Node.TEXT_NODE || !editorElement.contains(node)) return null;
    if (node.parentElement && node.parentElement.closest('span.mention')) return null;
    const before = node.data.slice(0, offset);
    const start = Math.max(...MENTION_MARKS.map(m => before.lastIndexOf(m)));
    if (start < 0) return null;
    const query = before.slice(start + 1);
    if (SPACE.test(query) || query.length > MAX_MENTION_QUERY) return null;
    // メールアドレスのように文字の直後の @ では出さない
    const prev = start > 0 ? before[start - 1] : previousChar(node);
    if (prev !== null && !SPACE.test(prev)) return null;
    return { node, start, end: offset, query };
}

// テキストノードの直前の文字 (ブラウザが文字を別のノードに分けていることがある)。行頭・塊の直後は null
function previousChar(node) {
    const prev = node.previousSibling;
    if (prev && prev.nodeType === Node.TEXT_NODE && prev.data.length > 0) return prev.data[prev.data.length - 1];
    return null;
}

function checkMention(editorElement) {
    const s = getState(editorElement);
    if (!s.mention || !s.dotNetRef) return;
    const m = findMentionQuery(editorElement);
    const dismissed = s.mentionDismissed;
    if (!m || (dismissed && dismissed.node === m.node && dismissed.start === m.start)) {
        if (!m) s.mentionDismissed = null;
        closeMention(editorElement);
        return;
    }
    s.mentionDismissed = null;
    s.mentionRange = m;
    s.mentionOpen = true;
    // 候補は画面に固定で出す (編集欄の枠は overflow: hidden なので、枠の中に置くと下端で切れる)
    const range = document.createRange();
    range.setStart(m.node, m.start);
    range.setEnd(m.node, Math.min(m.start + 1, m.node.length));
    const rect = range.getBoundingClientRect();
    s.dotNetRef.invokeMethodAsync('OnMentionQuery', m.query, rect.bottom + 4, rect.left);
}

function closeMention(editorElement) {
    const s = getState(editorElement);
    s.mentionVisible = false;
    s.mentionRange = null;
    if (!s.mentionOpen) return;
    s.mentionOpen = false;
    if (s.dotNetRef) s.dotNetRef.invokeMethodAsync('OnMentionClose');
}

// .NET が候補を出しているか (出ていなければ Enter などは普段どおり)
export function setMentionVisible(editorElement, visible) {
    getState(editorElement).mentionVisible = !!visible;
}

// 打った @文字 を部品に置き換え、後ろに空白を 1 つ入れてカーソルをその後ろへ。値の HTML を返す (置き換える場所が無くなっていれば null)
export function insertMention(editorElement, id, label, isAll) {
    const s = getState(editorElement);
    const m = findMentionQuery(editorElement) || s.mentionRange;
    s.mentionVisible = false;
    s.mentionOpen = false;
    s.mentionRange = null;
    if (!m || !m.node.isConnected || !editorElement.contains(m.node)) return null;

    const range = document.createRange();
    range.setStart(m.node, m.start);
    range.setEnd(m.node, Math.min(m.end, m.node.length));
    range.deleteContents();

    const span = document.createElement('span');
    span.className = 'mention';
    if (isAll) span.setAttribute('data-mention-all', '');
    else span.setAttribute('data-mention-id', id);
    span.setAttribute('contenteditable', 'false');
    span.textContent = '@' + label;
    const space = document.createTextNode(' ');
    range.insertNode(space);
    range.insertNode(span);

    const caret = document.createRange();
    caret.setStart(space, 1);
    caret.collapse(true);
    const sel = window.getSelection();
    sel.removeAllRanges();
    sel.addRange(caret);
    editorElement.focus();
    return contentHtml(editorElement);
}

// --- formatBlock (custom implementation to preserve empty lines) ---

const BLOCK_TAGS = new Set(['DIV', 'P', 'H1', 'H2', 'H3', 'H4', 'H5', 'H6', 'BLOCKQUOTE', 'PRE', 'LI']);

function getBlockParent(node, editorElement) {
    let cur = node.nodeType === Node.TEXT_NODE ? node.parentNode : node;
    while (cur && cur !== editorElement) {
        if (BLOCK_TAGS.has(cur.tagName)) return cur;
        cur = cur.parentNode;
    }
    return null;
}

function isEmptyBlock(el) {
    const html = el.innerHTML;
    return html === '' || html === '<br>' || html.trim() === '';
}

function collectSelectedBlocks(editorElement, range) {
    const blocks = [];
    const seen = new Set();

    const walker = document.createTreeWalker(
        editorElement,
        NodeFilter.SHOW_TEXT | NodeFilter.SHOW_ELEMENT,
        { acceptNode: (node) => range.intersectsNode(node) ? NodeFilter.FILTER_ACCEPT : NodeFilter.FILTER_SKIP }
    );

    for (let node = walker.nextNode(); node; node = walker.nextNode()) {
        const block = getBlockParent(node, editorElement);
        if (block && !seen.has(block)) {
            seen.add(block);
            blocks.push(block);
        } else if (!block && node.nodeType === Node.TEXT_NODE && node.parentNode === editorElement && !seen.has(node)) {
            seen.add(node);
            blocks.push(node);
        }
    }

    for (let child = editorElement.firstChild; child; child = child.nextSibling) {
        if (child.nodeType === Node.ELEMENT_NODE && !seen.has(child) && isEmptyBlock(child) && range.intersectsNode(child)) {
            seen.add(child);
            blocks.push(child);
        }
    }

    blocks.sort((a, b) => {
        const pos = a.compareDocumentPosition(b);
        if (pos & Node.DOCUMENT_POSITION_FOLLOWING) return -1;
        if (pos & Node.DOCUMENT_POSITION_PRECEDING) return 1;
        return 0;
    });

    return blocks;
}

function replaceBlockTag(block, tagUpper) {
    if (block.nodeType === Node.TEXT_NODE) {
        const newEl = document.createElement(tagUpper);
        block.parentNode.insertBefore(newEl, block);
        newEl.appendChild(block);
        return newEl;
    }
    if (block.tagName === tagUpper) return block;

    const newEl = document.createElement(tagUpper);
    while (block.firstChild) {
        newEl.appendChild(block.firstChild);
    }
    if (newEl.innerHTML === '' || newEl.innerHTML.trim() === '') {
        newEl.innerHTML = '<br>';
    }
    block.parentNode.insertBefore(newEl, block);
    block.parentNode.removeChild(block);
    return newEl;
}

function applyFormatBlock(editorElement, tag) {
    const sel = window.getSelection();
    if (!sel.rangeCount) return;

    const blocks = collectSelectedBlocks(editorElement, sel.getRangeAt(0));
    if (blocks.length === 0) {
        document.execCommand('formatBlock', false, tag);
        return;
    }

    const tagUpper = tag.toUpperCase();
    const newBlocks = blocks.map(block => replaceBlockTag(block, tagUpper));

    const first = newBlocks[0];
    const last = newBlocks[newBlocks.length - 1];
    const newRange = document.createRange();
    newRange.setStart(first, 0);
    newRange.setEnd(last, last.childNodes.length);
    sel.removeAllRanges();
    sel.addRange(newRange);
}

// --- Link handling ---

function ensureLinkTargets(editorElement) {
    editorElement.querySelectorAll('a').forEach(a => {
        a.setAttribute('target', '_blank');
        a.setAttribute('rel', 'noopener noreferrer');
    });
}

export function getLinkPopupPosition(editorElement) {
    const wrapperRect = editorElement.parentElement.getBoundingClientRect();
    const s = getState(editorElement);

    let top, left;
    if (s.savedRange) {
        const rangeRect = s.savedRange.getBoundingClientRect();
        if (rangeRect.width > 0 || rangeRect.height > 0) {
            top = rangeRect.bottom - wrapperRect.top + 4;
            left = rangeRect.left - wrapperRect.left;
        }
    }
    if (top === undefined) {
        const editorRect = editorElement.getBoundingClientRect();
        top = editorRect.top - wrapperRect.top + 4;
        left = 8;
    }

    if (s.savedRange && !s.savedRange.collapsed) {
        try {
            s.suppressInput = true;
            const mark = document.createElement('span');
            mark.style.backgroundColor = '#b3d4fc';
            mark.setAttribute('data-link-highlight', '');
            const contents = s.savedRange.extractContents();
            mark.appendChild(contents);
            s.savedRange.insertNode(mark);
            s.highlightMark = mark;
            const newRange = document.createRange();
            newRange.selectNodeContents(mark);
            s.savedRange = newRange;
        } catch (e) {
            // ignore – highlight is cosmetic
        } finally {
            s.suppressInput = false;
        }
    }

    return { top, left };
}

function removeHighlight(editorElement) {
    const s = getState(editorElement);
    if (!s.highlightMark) return;
    s.suppressInput = true;
    try {
        const mark = s.highlightMark;
        const parent = mark.parentNode;
        const firstChild = mark.firstChild;
        const lastChild = mark.lastChild;
        while (mark.firstChild) {
            parent.insertBefore(mark.firstChild, mark);
        }
        parent.removeChild(mark);
        if (firstChild && lastChild) {
            const range = document.createRange();
            range.setStartBefore(firstChild);
            range.setEndAfter(lastChild);
            s.savedRange = range;
        }
    } catch (e) {
        // ignore
    }
    s.highlightMark = null;
    s.suppressInput = false;
}

export function applyLink(editorElement, url) {
    removeHighlight(editorElement);
    restoreSelection(editorElement);
    document.execCommand('createLink', false, url);
    ensureLinkTargets(editorElement);
    return contentHtml(editorElement);
}

export function cancelLinkHighlight(editorElement) {
    removeHighlight(editorElement);
}
