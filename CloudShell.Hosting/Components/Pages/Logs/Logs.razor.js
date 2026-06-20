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

    for (let attempt = 0; attempt < 3; attempt++) {
        await nextFrame();
        element.scrollTop = element.scrollHeight;
    }

    return isAtLatest(element);
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
