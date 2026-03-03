export function initTaskBoard(element) {
    element.addEventListener('pointerdown', (e) => {
        if (e.target.closest('input, button, select, textarea, a, [contenteditable], .form-control, .form-select, .btn')) {
            const card = e.target.closest('.taskboard-card');
            if (card) {
                card.setAttribute('draggable', 'false');
                const restore = () => {
                    card.setAttribute('draggable', 'true');
                    document.removeEventListener('pointerup', restore, true);
                    document.removeEventListener('pointercancel', restore, true);
                };
                document.addEventListener('pointerup', restore, true);
                document.addEventListener('pointercancel', restore, true);
            }
        }
    }, true);
}
