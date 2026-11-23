/* Quản lý phiên đăng nhập phía trình duyệt. Token chỉ lưu trong localStorage của máy người dùng. */
(function (global) {
  "use strict";

  const TOKEN_KEY = "st_access_token";
  const EXPIRES_KEY = "st_expires_at";
  const USER_KEY = "st_user";

  function setSession(auth) {
    localStorage.setItem(TOKEN_KEY, auth.token);
    localStorage.setItem(EXPIRES_KEY, auth.expiresAt);
    localStorage.setItem(USER_KEY, JSON.stringify(auth.user));
  }

  function getToken() {
    const token = localStorage.getItem(TOKEN_KEY);
    if (!token) return null;
    const expiresAt = localStorage.getItem(EXPIRES_KEY);
    if (expiresAt) {
      const time = new Date(expiresAt).getTime();
      if (!Number.isNaN(time) && time <= Date.now()) {
        clear();
        return null;
      }
    }
    return token;
  }

  function getUser() {
    try {
      const raw = localStorage.getItem(USER_KEY);
      return raw ? JSON.parse(raw) : null;
    } catch {
      return null;
    }
  }

  function isLoggedIn() {
    return Boolean(getToken() && getUser());
  }

  function clear() {
    localStorage.removeItem(TOKEN_KEY);
    localStorage.removeItem(EXPIRES_KEY);
    localStorage.removeItem(USER_KEY);
  }

  function loginRedirectUrl() {
    const current = window.location.pathname.split("/").pop() + window.location.search;
    return "login.html?returnUrl=" + encodeURIComponent(current || "index.html");
  }

  function requireAuth() {
    if (!isLoggedIn()) {
      window.location.replace(loginRedirectUrl());
      return false;
    }
    return true;
  }

  function safeReturnUrl(value, fallback) {
    if (!value) return fallback || "index.html";
    if (/^[a-z]+:/i.test(value) || value.startsWith("//")) return fallback || "index.html";
    return value;
  }

  function logout() {
    if (global.Api) {
      global.Api.logout().catch(function () { /* token đã hết hạn cũng bỏ qua */ });
    }
    clear();
    window.location.replace("login.html");
  }

  function roleLabel(role) {
    const map = { Admin: "Quản trị viên", Agent: "Nhân viên", Customer: "Khách hàng" };
    return map[role] || role || "";
  }

  global.Auth = {
    setSession,
    getToken,
    getUser,
    isLoggedIn,
    clear,
    requireAuth,
    loginRedirectUrl,
    safeReturnUrl,
    logout,
    roleLabel,
    isAdmin: function () { const u = getUser(); return Boolean(u && u.role === "Admin"); },
    isAgent: function () { const u = getUser(); return Boolean(u && u.role === "Agent"); }
  };
})(window);
