export function getTimelineWidth(element) {
    const scroll = element.querySelector('.gantt-timeline-scroll');
    if (!scroll) return 0;
    return scroll.clientWidth;
}

const observerMap = new WeakMap();

export function observeResize(element, dotNetRef) {
    if (observerMap.has(element)) return;
    const scroll = element.querySelector('.gantt-timeline-scroll');
    if (!scroll) return;

    const observer = new ResizeObserver(() => {
        const width = scroll.clientWidth;
        dotNetRef.invokeMethodAsync('OnContainerResized', width);
    });
    observer.observe(scroll);
    observerMap.set(element, observer);
}

export function disposeResize(element) {
    const observer = observerMap.get(element);
    if (observer) {
        observer.disconnect();
        observerMap.delete(element);
    }
}
