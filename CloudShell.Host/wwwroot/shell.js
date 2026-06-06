(function () {
    const storageKey = "cloudshell.navCollapsed";

    function readCollapsed() {
        try {
            return localStorage.getItem(storageKey) === "True";
        } catch {
            return false;
        }
    }

    function writeCollapsed(collapsed) {
        try {
            localStorage.setItem(storageKey, collapsed ? "True" : "False");
        } catch {
        }
    }

    function applyState(shell, collapsed) {
        shell.classList.toggle("nav-collapsed", collapsed);

        const toggle = shell.querySelector("[data-nav-toggle]");
        if (toggle) {
            toggle.setAttribute("aria-expanded", String(!collapsed));
        }
    }

    function initializeShell(shell) {
        if (shell.dataset.navToggleReady === "true") {
            applyState(shell, readCollapsed());
            return;
        }

        shell.dataset.navToggleReady = "true";
        applyState(shell, readCollapsed());

        const toggle = shell.querySelector("[data-nav-toggle]");
        if (!toggle) {
            return;
        }

        toggle.addEventListener("click", function () {
            const collapsed = !shell.classList.contains("nav-collapsed");
            applyState(shell, collapsed);
            writeCollapsed(collapsed);
        });
    }

    function initialize() {
        document.querySelectorAll(".shell").forEach(initializeShell);
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", initialize);
    } else {
        initialize();
    }

    document.addEventListener("enhancedload", initialize);
})();
