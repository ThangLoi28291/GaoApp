(function () {
    "use strict";

    const form = document.getElementById("loginForm");
    const btnLogin = document.getElementById("btnLogin");
    const passwordInput = document.getElementById("PasswordInput");
    const btnTogglePassword = document.getElementById("btnTogglePassword");

    // Hiện / ẩn mật khẩu
    if (passwordInput && btnTogglePassword) {
        btnTogglePassword.addEventListener("click", function () {
            const isPassword = passwordInput.type === "password";

            passwordInput.type = isPassword ? "text" : "password";
            btnTogglePassword.textContent = isPassword ? "Ẩn" : "Hiện";
        });
    }

    // Chống bấm đăng nhập nhiều lần
    if (form && btnLogin) {
        form.addEventListener("submit", function () {
            btnLogin.disabled = true;
            btnLogin.textContent = "Đang đăng nhập...";
        });
    }
})();