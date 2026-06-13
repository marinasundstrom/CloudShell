(function () {
    window.cloudShellNav = {
        setCollapsed: function (collapsed) {
            document.documentElement.classList.toggle("nav-collapsed", collapsed);
            document.querySelectorAll(".shell").forEach(function (shell) {
                shell.classList.toggle("nav-collapsed", collapsed);
            });
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

    function normalizeThemeMode(mode) {
        if (typeof mode !== "string") {
            return null;
        }

        var value = mode.toLowerCase();
        return value === "dark" || value === "light"
            ? value
            : null;
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
