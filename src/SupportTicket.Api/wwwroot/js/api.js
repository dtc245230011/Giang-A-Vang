/* Lớp gọi Support API. Mọi dữ liệu đều lấy từ backend, không giả lập nghiệp vụ. */
(function (global) {
  "use strict";

  const BASE = "/api";

  class ApiError extends Error {
    constructor(message, status, payload) {
      super(message);
      this.name = "ApiError";
      this.status = status || 0;
      this.payload = payload || null;
      this.validation = payload && payload.errors ? payload.errors : null;
    }
  }

  function buildQuery(params) {
    if (!params) return "";
    const search = new URLSearchParams();
    for (const [key, value] of Object.entries(params)) {
      if (value === null || value === undefined || value === "") continue;
      search.append(key, value);
    }
    const text = search.toString();
    return text ? "?" + text : "";
  }

  async function request(path, options) {
    const opts = options || {};
    const headers = { Accept: "application/json" };
    const token = global.Auth ? global.Auth.getToken() : null;
    if (token) headers.Authorization = "Bearer " + token;
    if (opts.body !== undefined) headers["Content-Type"] = "application/json";

    let response;
    try {
      response = await fetch(BASE + path + buildQuery(opts.query), {
        method: opts.method || "GET",
        headers,
        body: opts.body !== undefined ? JSON.stringify(opts.body) : undefined,
        signal: opts.signal
      });
    } catch (error) {
      if (error && error.name === "AbortError") throw error;
      throw new ApiError("Không kết nối được máy chủ. Vui lòng kiểm tra kết nối và thử lại.", 0);
    }

    if (response.status === 401 && !opts.skipAuthRedirect) {
      if (global.Auth) global.Auth.clear();
      const onLogin = window.location.pathname.endsWith("login.html");
      if (!onLogin) {
        window.location.replace(global.Auth ? global.Auth.loginRedirectUrl() : "login.html");
      }
      throw new ApiError("Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.", 401);
    }

    if (response.status === 204) return null;

    const contentType = response.headers.get("content-type") || "";
    let payload = null;
    if (contentType.includes("application/json") || contentType.includes("problem+json")) {
      payload = await response.json().catch(() => null);
    } else {
      const text = await response.text();
      payload = text ? { detail: text } : null;
    }

    if (!response.ok) {
      let message = (payload && (payload.detail || payload.title)) || "Yêu cầu không thành công.";
      if (payload && payload.errors) {
        const messages = Object.values(payload.errors).flat();
        if (messages.length) message = messages[0];
      }
      throw new ApiError(message, response.status, payload);
    }

    return payload;
  }

  const Api = {
    ApiError,
    register: (body) => request("/auth/register", { method: "POST", body, skipAuthRedirect: true }),
    login: (body) => request("/auth/login", { method: "POST", body, skipAuthRedirect: true }),
    logout: () => request("/auth/logout", { method: "POST" }),
    me: () => request("/auth/me"),
    getMeta: () => request("/meta"),
    getDashboard: () => request("/dashboard/summary"),

    listTickets: (params) => request("/tickets", { query: params }),
    getTicket: (id) => request("/tickets/" + encodeURIComponent(id)),
    createTicket: (body) => request("/tickets", { method: "POST", body }),
    changeStatus: (id, body) => request("/tickets/" + encodeURIComponent(id) + "/status", { method: "PATCH", body }),
    assignTicket: (id, body) => request("/tickets/" + encodeURIComponent(id) + "/assignment", { method: "PATCH", body }),
    getHistory: (id) => request("/tickets/" + encodeURIComponent(id) + "/history"),
    ticketsOfUser: (userId, params) => request("/users/" + encodeURIComponent(userId) + "/tickets", { query: params }),

    listUsers: (params) => request("/users", { query: params }),
    createUser: (body) => request("/users", { method: "POST", body }),
    updateUser: (id, body) => request("/users/" + encodeURIComponent(id), { method: "PATCH", body }),
    listAgents: (params) => request("/users/agents", { query: params }),

    listTeams: (params) => request("/teams", { query: params }),
    getTeam: (id) => request("/teams/" + encodeURIComponent(id)),
    createTeam: (body) => request("/teams", { method: "POST", body }),
    updateTeam: (id, body) => request("/teams/" + encodeURIComponent(id), { method: "PATCH", body }),
    addTeamMember: (teamId, userId, body) =>
      request("/teams/" + encodeURIComponent(teamId) + "/members/" + encodeURIComponent(userId), { method: "PUT", body }),
    removeTeamMember: (teamId, userId) =>
      request("/teams/" + encodeURIComponent(teamId) + "/members/" + encodeURIComponent(userId), { method: "DELETE" })
  };

  global.Api = Api;
})(window);
