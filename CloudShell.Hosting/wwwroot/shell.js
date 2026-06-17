(function () {
    var navCollapsedStorageKey = "cloudshell.navigation.collapsed";

    setNavCollapsed(readStoredNavCollapsed() === true);

    window.cloudShellNav = {
        setCollapsed: function (collapsed, persist) {
            setNavCollapsed(collapsed);

            if (persist) {
                writeStoredNavCollapsed(collapsed);
            }
        },
        isCollapsed: function () {
            return readStoredNavCollapsed() === true;
        },
        getStoredCollapsed: function () {
            return readStoredNavCollapsed();
        }
    };

    window.cloudShellTheme = {
        setMode: function (storageKey, mode) {
            if (!storageKey) {
                return;
            }

            var normalizedMode = normalizeThemeMode(mode);

            try {
                var existing = JSON.parse(localStorage.getItem(storageKey) || "{}");
                existing.mode = normalizedMode;
                localStorage.setItem(storageKey, JSON.stringify(existing));
            } catch {
                try {
                    localStorage.setItem(storageKey, JSON.stringify({ mode: normalizedMode }));
                } catch {
                }
            }
        }
    };

    window.cloudShellLayout = {
        scrollToTop: function () {
            window.scrollTo({ top: 0, left: 0, behavior: "instant" });
            document.documentElement.scrollTop = 0;
            document.body.scrollTop = 0;

            document.querySelectorAll(".shell-main, .shell-content").forEach(function (element) {
                element.scrollTop = 0;
                element.scrollLeft = 0;
            });
        }
    };

    function normalizeThemeMode(mode) {
        if (typeof mode !== "string") {
            return null;
        }

        var value = mode.toLowerCase();
        return value === "dark" || value === "light"
            ? value
            : null;
    }

    function readStoredNavCollapsed() {
        try {
            var value = localStorage.getItem(navCollapsedStorageKey);

            if (value === "true") {
                return true;
            }

            if (value === "false") {
                return false;
            }

            return null;
        } catch {
            return null;
        }
    }

    function writeStoredNavCollapsed(collapsed) {
        try {
            localStorage.setItem(navCollapsedStorageKey, collapsed ? "true" : "false");
        } catch {
        }
    }

    function setNavCollapsed(collapsed) {
        document.querySelectorAll(".shell").forEach(function (shell) {
            shell.classList.toggle("nav-collapsed", collapsed);
        });
    }

    window.cloudShellForms = {
        getValue: function (id) {
            var element = document.getElementById(id);
            return element && typeof element.value === "string"
                ? element.value
                : "";
        }
    };

    document.addEventListener("pointerdown", function (event) {
        closeOpenFluentMenus(event.target);
    }, true);

    document.addEventListener("focusin", function (event) {
        closeOpenFluentMenus(event.target);
    }, true);

    document.addEventListener("keydown", function (event) {
        if (event.key === "Escape") {
            closeOpenFluentMenus(null);
        }
    }, true);

    function closeOpenFluentMenus(target) {
        if (target && (
            target.closest("fluent-menu") ||
            target.closest(".fluent-menubutton-container") ||
            target.closest(".action-overflow-button"))) {
            return;
        }

        document.querySelectorAll("fluent-anchored-region, .fluent-overlay").forEach(function (element) {
            element.remove();
        });

        document.querySelectorAll("fluent-button[aria-haspopup='true']").forEach(function (button) {
            button.removeAttribute("aria-expanded");
        });
    }

})();
