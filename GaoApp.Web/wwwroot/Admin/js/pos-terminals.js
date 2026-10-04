(function () {
    "use strict";
    document.querySelectorAll("[data-revoke-key]").forEach(function (form) {
        form.addEventListener("submit", function (event) {
            if (!window.confirm("Thu hồi khóa thiết bị này? Khóa sẽ không dùng được cho lần đăng nhập tiếp theo.")) {
                event.preventDefault();
            }
        });
    });
    const button = document.getElementById("copyDeviceKey");
    if (!button) return;
    button.addEventListener("click", async function () {
        const input = document.getElementById("createdDeviceKey");
        const status = document.getElementById("copyKeyStatus");
        try {
            await navigator.clipboard.writeText(input.value);
            status.textContent = "Đã sao chép khóa.";
        } catch (_) {
            input.focus();
            input.select();
            status.textContent = "Đã chọn khóa. Nhấn Ctrl+C hoặc giữ để sao chép.";
        }
    });
})();
