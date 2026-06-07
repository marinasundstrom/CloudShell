(function () {
    window.cloudShellNav = {
        readCollapsed: function (storageKey) {
            try {
                return localStorage.getItem(storageKey);
            } catch {
                return null;
            }
        },
        setCollapsed: function (collapsed, storageKey) {
            document.documentElement.classList.toggle("nav-collapsed", collapsed);
            document.querySelectorAll(".shell").forEach(function (shell) {
                shell.classList.toggle("nav-collapsed", collapsed);
            });

            try {
                localStorage.setItem(storageKey, collapsed ? "true" : "false");
            } catch {
            }
        }
    };
})();
