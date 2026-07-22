(function () {
    var navCollapsedStorageKey = "cloudshell.navigation.collapsed";
    var themeStorageKey = "cloudshell.theme";

    setNavCollapsed(isCompactViewport() || readStoredNavCollapsed() === true);
    initializeAccountSelectors();

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
        return window.matchMedia && window.matchMedia("(max-width: 900px)").matches;
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
