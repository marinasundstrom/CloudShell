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

            try {
                var existing = JSON.parse(localStorage.getItem(storageKey) || "{}");
                existing.mode = mode || "system";
                localStorage.setItem(storageKey, JSON.stringify(existing));
            } catch {
                try {
                    localStorage.setItem(storageKey, JSON.stringify({ mode: mode || "system" }));
                } catch {
                }
            }
        }
    };

    window.cloudShellForms = {
        getValue: function (id) {
            var element = document.getElementById(id);
            return element && typeof element.value === "string"
                ? element.value
                : "";
        }
    };
})();
