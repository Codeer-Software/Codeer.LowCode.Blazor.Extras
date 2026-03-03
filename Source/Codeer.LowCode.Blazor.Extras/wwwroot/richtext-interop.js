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

export function setContent(editorElement, html) {
    editorElement.innerHTML = html || '';
}

export function insertLink(editorElement) {
    saveSelection(editorElement);
    const url = prompt('URL:');
    if (url) {
        restoreSelection(editorElement);
        document.execCommand('createLink', false, url);
    }
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
