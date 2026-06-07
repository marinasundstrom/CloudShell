const latestThreshold = 8;

export function isAtLatest(element) {
    if (!element) {
        return true;
    }

    const distanceFromLatest = element.scrollHeight - element.scrollTop - element.clientHeight;
    return distanceFromLatest <= latestThreshold;
}

export async function scrollToLatest(element) {
    if (!element || !element.isConnected) {
        return false;
    }

    await nextFrame();
    element.scrollTop = element.scrollHeight;
    await nextFrame();
    element.scrollTop = element.scrollHeight;
    return true;
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

function nextFrame() {
    return new Promise(resolve => requestAnimationFrame(resolve));
}
