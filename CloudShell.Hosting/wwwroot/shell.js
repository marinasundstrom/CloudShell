(function () {
    window.cloudShellNav = {
        setCollapsed: function (collapsed) {
            document.documentElement.classList.toggle("nav-collapsed", collapsed);
            document.querySelectorAll(".shell").forEach(function (shell) {
                shell.classList.toggle("nav-collapsed", collapsed);
            });
        }
    };
})();
