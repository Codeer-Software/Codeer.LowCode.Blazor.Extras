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
    if (!s.savedRange) {
        editorElement.focus();
        return;
    }
    editorElement.focus();
    const sel = window.getSelection();
    sel.removeAllRanges();
    sel.addRange(s.savedRange);
}

export function initEditor(editorElement, toolbarElement, dotNetRef) {
    editorElement.addEventListener('input', () => {
        dotNetRef.invokeMethodAsync('OnContentChanged', editorElement.innerHTML);
    });
    editorElement.addEventListener('paste', (e) => {
        e.preventDefault();
        const text = e.clipboardData.getData('text/html') || e.clipboardData.getData('text/plain');
        document.execCommand('insertHTML', false, text);
    });

    // Open links in new tab when clicked in editor
    editorElement.addEventListener('click', (e) => {
        const anchor = e.target.closest('a');
        if (anchor && editorElement.contains(anchor)) {
            e.preventDefault();
            window.open(anchor.href, '_blank', 'noopener,noreferrer');
        }
    });

    // Save selection on every mouseup/keyup inside editor
    editorElement.addEventListener('mouseup', () => saveSelection(editorElement));
    editorElement.addEventListener('keyup', () => saveSelection(editorElement));

    // Save selection on toolbar mousedown, BEFORE focus leaves the editor
    toolbarElement.addEventListener('mousedown', () => saveSelection(editorElement));
}

export function execFormatCommand(editorElement, command, value) {
    editorElement.focus();
    document.execCommand(command, false, value || null);
    return editorElement.innerHTML;
}

export function execFormatCommandWithRestore(editorElement, command, value) {
    restoreSelection(editorElement);
    document.execCommand(command, false, value || null);
    saveSelection(editorElement);
    return editorElement.innerHTML;
}

export function getContent(editorElement) {
    return editorElement.innerHTML;
}

function ensureLinkTargets(editorElement) {
    editorElement.querySelectorAll('a').forEach(a => {
        a.setAttribute('target', '_blank');
        a.setAttribute('rel', 'noopener noreferrer');
    });
}

export function setContent(editorElement, html) {
    editorElement.innerHTML = html || '';
    ensureLinkTargets(editorElement);
}

export function getLinkPopupPosition(editorElement) {
    saveSelection(editorElement);
    const wrapperRect = editorElement.parentElement.getBoundingClientRect();
    const sel = window.getSelection();
    if (sel.rangeCount > 0) {
        const range = sel.getRangeAt(0);
        const rangeRect = range.getBoundingClientRect();
        if (rangeRect.width > 0 || rangeRect.height > 0) {
            return {
                top: rangeRect.bottom - wrapperRect.top + 4,
                left: rangeRect.left - wrapperRect.left
            };
        }
    }
    // Fallback: position below the toolbar
    const editorRect = editorElement.getBoundingClientRect();
    return {
        top: editorRect.top - wrapperRect.top + 4,
        left: 8
    };
}

export function applyLink(editorElement, url) {
    restoreSelection(editorElement);
    document.execCommand('createLink', false, url);
    ensureLinkTargets(editorElement);
    return editorElement.innerHTML;
}

export function clearAllFormatting(editorElement) {
    editorElement.focus();
    document.execCommand('removeFormat', false, null);
    document.execCommand('formatBlock', false, 'div');
    return editorElement.innerHTML;
}

export function dispose(editorElement) {
    state.delete(editorElement);
}
