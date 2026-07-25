(function () {
    var navCollapsedStorageKey = "cloudshell.navigation.collapsed";
    var themeStorageKey = "cloudshell.theme";
    var compactViewportQuery = window.matchMedia &&
        window.matchMedia("(max-width: 56.25rem)");

    setNavCollapsed(isCompactViewport() || readStoredNavCollapsed() === true);
    initializeAccountSelectors();

    if (compactViewportQuery) {
        compactViewportQuery.addEventListener("change", syncDrawerAccessibility);
    }

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
        },
        isCompactViewport: function () {
            return isCompactViewport();
        },
        focusToggle: function () {
            var toggle = document.getElementById("navmenu-toggle");
            if (toggle) {
                toggle.focus();
            }
        }
    };

    window.cloudShellTheme = {
        setMode: function (storageKey, mode) {
            if (!storageKey) {
                return;
            }

            writeStoredThemeMode(storageKey, mode);
            applyThemeMode(mode);
        },
        applyMode: function (mode) {
            applyThemeMode(mode);
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

    window.cloudShellFocus = {
        focusById: function (id) {
            var element = document.getElementById(id);
            if (element) {
                element.focus();
            }
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

    function readStoredThemeMode(storageKey) {
        try {
            var storedTheme = JSON.parse(localStorage.getItem(storageKey) || "{}");
            return normalizeThemeMode(storedTheme.mode);
        } catch {
            return null;
        }
    }

    function writeStoredThemeMode(storageKey, mode) {
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

    function applyThemeMode(mode) {
        var effectiveMode = normalizeThemeMode(mode) || getSystemThemeMode();
        document.body.dataset.theme = effectiveMode;
        document.documentElement.style.colorScheme = effectiveMode;

        document.querySelectorAll("fluent-design-theme").forEach(function (theme) {
            theme.setAttribute("mode", effectiveMode);
        });
    }

    function getSystemThemeMode() {
        return window.matchMedia &&
            window.matchMedia("(prefers-color-scheme: dark)").matches
                ? "dark"
                : "light";
    }

    function initializeAccountSelectors() {
        initializeAccountLanguageSelectors();
        initializeAccountThemeSelectors();
    }

    function initializeAccountLanguageSelectors() {
        document.querySelectorAll("[data-cloudshell-language-select]").forEach(function (select) {
            if (select.dataset.cloudshellInitialized === "true") {
                return;
            }

            select.dataset.cloudshellInitialized = "true";
            select.addEventListener("change", function () {
                var culture = select.value || select.getAttribute("value");
                if (!culture) {
                    return;
                }

                var returnUrl = window.location.pathname + window.location.search;
                var target = "/localization/set?culture=" +
                    encodeURIComponent(culture) +
                    "&returnUrl=" +
                    encodeURIComponent(returnUrl);
                window.location.assign(target);
            });
        });
    }

    function initializeAccountThemeSelectors() {
        document.querySelectorAll("[data-cloudshell-theme-select]").forEach(function (select) {
            if (select.dataset.cloudshellInitialized === "true") {
                return;
            }

            select.dataset.cloudshellInitialized = "true";
            var storedMode = readStoredThemeMode(themeStorageKey) || "system";
            setFluentSelectValue(select, storedMode);
            whenFluentSelectDefined(function () {
                setFluentSelectValue(select, storedMode);
            });

            select.addEventListener("change", function () {
                var mode = select.value || select.getAttribute("value") || "system";
                setFluentSelectValue(select, mode);
                writeStoredThemeMode(themeStorageKey, mode);
                applyThemeMode(mode);
            });
        });
    }

    function whenFluentSelectDefined(callback) {
        if (!window.customElements || !customElements.whenDefined) {
            callback();
            return;
        }

        customElements.whenDefined("fluent-select").then(callback);
    }

    function setFluentSelectValue(select, value) {
        select.value = value;
        select.setAttribute("value", value);
        select.querySelectorAll("fluent-option").forEach(function (option) {
            if (option.getAttribute("value") === value) {
                option.setAttribute("selected", "");
            } else {
                option.removeAttribute("selected");
            }
        });
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

    function isCompactViewport() {
        return compactViewportQuery && compactViewportQuery.matches;
    }

    function setNavCollapsed(collapsed) {
        document.querySelectorAll(".shell").forEach(function (shell) {
            shell.classList.toggle("nav-collapsed", collapsed);
        });

        syncDrawerAccessibility();
    }

    function syncDrawerAccessibility() {
        document.querySelectorAll(".shell").forEach(function (shell) {
            var sidebar = shell.querySelector(".shell-sidebar");
            var main = shell.querySelector(".shell-main");
            var isOpenDrawer = isCompactViewport() &&
                !shell.classList.contains("nav-collapsed");

            if (sidebar) {
                if (isOpenDrawer) {
                    sidebar.setAttribute("role", "dialog");
                    sidebar.setAttribute("aria-modal", "true");
                } else {
                    sidebar.removeAttribute("role");
                    sidebar.removeAttribute("aria-modal");
                }
            }

            if (main) {
                main.inert = isOpenDrawer;
            }
        });
    }

    function getOpenDrawer() {
        if (!isCompactViewport()) {
            return null;
        }

        return document.querySelector(".shell:not(.nav-collapsed) .shell-sidebar");
    }

    function getDrawerFocusableElements(drawer) {
        return Array.from(drawer.querySelectorAll(
            "a[href], button:not([disabled]), input:not([disabled]), " +
            "select:not([disabled]), textarea:not([disabled]), " +
            "[tabindex]:not([tabindex='-1']), fluent-anchor, fluent-button, fluent-nav-item"))
            .filter(function (element) {
                return !element.hasAttribute("disabled") &&
                    element.getAttribute("aria-hidden") !== "true" &&
                    element.getClientRects().length > 0;
            });
    }

    function trapDrawerFocus(event) {
        if (event.key !== "Tab") {
            return;
        }

        var drawer = getOpenDrawer();
        if (!drawer) {
            return;
        }

        var focusableElements = getDrawerFocusableElements(drawer);
        if (focusableElements.length === 0) {
            event.preventDefault();
            return;
        }

        var first = focusableElements[0];
        var last = focusableElements[focusableElements.length - 1];
        var activeElement = document.activeElement;

        if (!drawer.contains(activeElement)) {
            event.preventDefault();
            first.focus();
        } else if (event.shiftKey && activeElement === first) {
            event.preventDefault();
            last.focus();
        } else if (!event.shiftKey && activeElement === last) {
            event.preventDefault();
            first.focus();
        }
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
        trapDrawerFocus(event);

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
