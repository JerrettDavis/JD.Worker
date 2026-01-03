(() => {
    const storageKey = "jd.worker.theme";
    const root = document.documentElement;

    const normalize = (mode) => {
        if (mode === "light" || mode === "dark") {
            return mode;
        }
        return "system";
    };

    const applyTheme = (mode) => {
        const normalized = normalize(mode);
        if (normalized === "system") {
            root.removeAttribute("data-theme");
            return;
        }

        root.setAttribute("data-theme", normalized);
    };

    const setTheme = (mode) => {
        const normalized = normalize(mode);
        try {
            localStorage.setItem(storageKey, normalized);
        } catch (e) {
        }
        applyTheme(normalized);
    };

    const getTheme = () => {
        try {
            return normalize(localStorage.getItem(storageKey));
        } catch (e) {
            return "system";
        }
    };

    const init = () => {
        applyTheme(getTheme());

        if (window.matchMedia) {
            const media = window.matchMedia("(prefers-color-scheme: dark)");
            const handler = () => {
                if (getTheme() === "system") {
                    applyTheme("system");
                }
            };

            if (media.addEventListener) {
                media.addEventListener("change", handler);
            } else if (media.addListener) {
                media.addListener(handler);
            }
        }
    };

    window.themeManager = {
        getTheme,
        setTheme,
        applyTheme,
        init
    };

    init();
})();
