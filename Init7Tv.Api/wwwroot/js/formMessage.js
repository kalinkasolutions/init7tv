// Renders the ?error / ?reset hints that the auth endpoints redirect with.
(() => {
    const params = new URLSearchParams(window.location.search);
    const element = document.getElementById("message");
    if (!element) {
        return;
    }

    const error = params.get("error");
    const reset = params.get("reset");

    if (error) {
        // textContent, never innerHTML: this string comes from the query string
        element.textContent = error === "invalid" ? "Wrong username or password." : error;
        element.classList.add("error");
    } else if (reset === "success") {
        element.textContent = "Your password has been reset, you can log in now.";
        element.classList.add("success");
    } else {
        return;
    }

    element.hidden = false;
})();
