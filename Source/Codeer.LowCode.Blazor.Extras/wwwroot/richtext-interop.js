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

export function initEditor(editorElement, toolbarElement, dotNetRef) {
    const editorState = getState(editorElement);
    editorElement.addEventListener('input', () => {
        if (editorState.suppressInput) return;
        dotNetRef.invokeMethodAsync('OnContentChanged', editorElement.innerHTML);
    });
    editorElement.addEventListener('paste', (e) => {
        e.preventDefault();
        const text = e.clipboardData.getData('text/html') || e.clipboardData.getData('text/plain');
        document.execCommand('insertHTML', false, text);
    });
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
    editorElement.innerHTML = html || '';
    ensureLinkTargets(editorElement);
}

export function dispose(editorElement) {
    state.delete(editorElement);
}

// --- Format commands ---

export function execFormatCommand(editorElement, command, value) {
    editorElement.focus();
    if (command === 'formatBlock') {
        applyFormatBlock(editorElement, value || 'div');
    } else {
        document.execCommand(command, false, value || null);
    }
    return editorElement.innerHTML;
}

export function execFormatCommandWithRestore(editorElement, command, value) {
    restoreSelection(editorElement);
    document.execCommand(command, false, value || null);
    saveSelection(editorElement);
    return editorElement.innerHTML;
}

export function clearAllFormatting(editorElement) {
    editorElement.focus();
    document.execCommand('removeFormat', false, null);
    applyFormatBlock(editorElement, 'div');
    return editorElement.innerHTML;
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
    return editorElement.innerHTML;
}

export function cancelLinkHighlight(editorElement) {
    removeHighlight(editorElement);
}
