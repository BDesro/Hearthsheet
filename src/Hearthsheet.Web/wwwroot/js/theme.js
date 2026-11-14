// Theme: loaded by index.html before Blazor so the first paint is already in the right theme (the CSP forbids inline
// scripts). The choice is "system", "light" or "dark", kept per device; the effective theme goes on <html data-theme>.
(function () {
    "use strict";
    var KEY = "hearthsheet.theme";
    var media = window.matchMedia ? window.matchMedia("(prefers-color-scheme: dark)") : null;

    function read() {
        try {
            var value = localStorage.getItem(KEY);
            return value === "light" || value === "dark" ? value : "system";
        } catch (e) {
            return "system"; // storage blocked (private window, site data off)
        }
    }

    function apply() {
        var choice = read();
        var dark = choice === "dark" || (choice === "system" && media !== null && media.matches);
        document.documentElement.dataset.theme = dark ? "dark" : "light";
    }

    window.hearthsheetTheme = {
        get: read,
        set: function (choice) {
            try {
                if (choice === "light" || choice === "dark") localStorage.setItem(KEY, choice);
                else localStorage.removeItem(KEY);
            } catch (e) {
                // Not saved; still applies for this page.
                document.documentElement.dataset.theme = choice === "dark" ? "dark" : "light";
                return;
            }
            apply();
        }
    };

    if (media !== null && media.addEventListener) media.addEventListener("change", apply);
    apply();
})();
