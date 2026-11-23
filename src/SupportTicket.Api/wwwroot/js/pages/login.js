(function () {
  "use strict";

  const loginPanel = document.getElementById("login-panel");
  const registerPanel = document.getElementById("register-panel");
  const tabLogin = document.getElementById("tab-login");
  const tabRegister = document.getElementById("tab-register");
  const alertBox = document.getElementById("auth-alert");
  const returnUrl = Auth.safeReturnUrl(UI.getParam("returnUrl"), "index.html");

  if (Auth.isLoggedIn()) {
    window.location.replace(returnUrl);
    return;
  }

  function switchTab(name) {
    const isLogin = name === "login";
    loginPanel.classList.toggle("d-none", !isLogin);
    registerPanel.classList.toggle("d-none", isLogin);
    tabLogin.classList.toggle("active", isLogin);
    tabRegister.classList.toggle("active", !isLogin);
    tabLogin.setAttribute("aria-selected", String(isLogin));
    tabRegister.setAttribute("aria-selected", String(!isLogin));
    UI.clear(alertBox);
    const first = (isLogin ? document.getElementById("login-email") : document.getElementById("register-name"));
    if (first) first.focus();
  }

  tabLogin.addEventListener("click", function () { switchTab("login"); });
  tabRegister.addEventListener("click", function () { switchTab("register"); });

  document.querySelectorAll("[data-toggle-password]").forEach(function (button) {
    button.addEventListener("click", function () {
      const input = document.getElementById(button.dataset.togglePassword);
      if (!input) return;
      const show = input.type === "password";
      input.type = show ? "text" : "password";
      button.textContent = show ? "Ẩn" : "Hiện";
      button.setAttribute("aria-label", show ? "Ẩn mật khẩu" : "Hiện mật khẩu");
    });
  });

  function showError(message) {
    UI.showAlert(alertBox, message, "danger");
    alertBox.scrollIntoView({ behavior: "smooth", block: "nearest" });
  }

  function markInvalid(input, invalid) {
    if (!input) return;
    input.classList.toggle("is-invalid", invalid);
  }

  document.getElementById("login-form").addEventListener("submit", async function (event) {
    event.preventDefault();
    const email = document.getElementById("login-email");
    const password = document.getElementById("login-password");
    const button = document.getElementById("login-submit");

    markInvalid(email, !email.value.trim() || !email.checkValidity());
    markInvalid(password, !password.value);
    if (email.classList.contains("is-invalid") || password.classList.contains("is-invalid")) {
      showError("Vui lòng nhập email và mật khẩu hợp lệ.");
      return;
    }

    UI.clear(alertBox);
    UI.buttonBusy(button, true, "Đang đăng nhập...");
    try {
      const auth = await Api.login({ email: email.value.trim(), password: password.value });
      Auth.setSession(auth);
      window.location.replace(returnUrl);
    } catch (error) {
      showError(error.message || "Đăng nhập thất bại.");
      UI.buttonBusy(button, false);
    }
  });

  document.getElementById("register-form").addEventListener("submit", async function (event) {
    event.preventDefault();
    const name = document.getElementById("register-name");
    const email = document.getElementById("register-email");
    const password = document.getElementById("register-password");
    const confirm = document.getElementById("register-confirm");
    const button = document.getElementById("register-submit");

    markInvalid(name, name.value.trim().length < 2);
    markInvalid(email, !email.value.trim() || !email.checkValidity());
    markInvalid(password, password.value.length < 8);
    markInvalid(confirm, confirm.value !== password.value || !confirm.value);

    if (document.querySelector("#register-form .is-invalid")) {
      showError("Vui lòng kiểm tra lại thông tin đăng ký.");
      return;
    }

    UI.clear(alertBox);
    UI.buttonBusy(button, true, "Đang tạo tài khoản...");
    try {
      const auth = await Api.register({
        fullName: name.value.trim(),
        email: email.value.trim(),
        password: password.value
      });
      Auth.setSession(auth);
      window.location.replace("index.html");
    } catch (error) {
      showError(error.message || "Không thể tạo tài khoản.");
      UI.buttonBusy(button, false);
    }
  });

  document.getElementById("login-email").focus();
})();
