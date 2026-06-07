const latestThreshold = 8;

export function isAtLatest(element) {
    if (!element) {
        return true;
    }

    const distanceFromLatest = element.scrollHeight - element.scrollTop - element.clientHeight;
    return distanceFromLatest <= latestThreshold;
}

export function scrollToLatest(element) {
    if (!element) {
        return;
    }

    element.scrollTop = element.scrollHeight;
}

export function getScrollHeight(element) {
    return element?.scrollHeight ?? 0;
}

export function preservePrependedScroll(element, previousScrollHeight) {
    if (!element) {
        return;
    }

    element.scrollTop += element.scrollHeight - previousScrollHeight;
}
