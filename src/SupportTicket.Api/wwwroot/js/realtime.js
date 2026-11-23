/* Kết nối SignalR: xác thực bằng JWT, tự kết nối lại, phát sự kiện cho trang đang mở. */
(function (global) {
  "use strict";

  let connection = null;
  let starting = false;
  let retryTimer = null;
  const lastEventAt = new Map();

  function setStatus(state, text) {
    document.querySelectorAll("[data-connection-status]").forEach(function (node) {
      node.classList.remove("is-connected", "is-connecting", "is-offline");
      node.classList.add("is-" + state);
      const label = node.querySelector(".status-text");
      if (label) label.textContent = text;
    });
    window.dispatchEvent(new CustomEvent("realtime:status", { detail: { state: state, text: text } }));
  }

  function dispatchTicketEvent(eventName, payload) {
    if (!payload) return;
    const time = UI.parseDate(payload.changedAt);
    const stamp = time ? time.getTime() : Date.now();
    const previous = lastEventAt.get(payload.ticketId) || 0;
    if (stamp < previous) return; // sự kiện cũ không được làm giao diện quay về trạng thái cũ
    lastEventAt.set(payload.ticketId, stamp);
    window.dispatchEvent(new CustomEvent("realtime:ticket", {
      detail: Object.assign({}, payload, { eventType: eventName })
    }));
  }

  function scheduleRetry() {
    if (retryTimer) return;
    retryTimer = setTimeout(function () {
      retryTimer = null;
      start();
    }, 10000);
  }

  async function start() {
    if (!Auth.isLoggedIn()) {
      setStatus("offline", "Chưa đăng nhập");
      return;
    }
    if (starting || (connection && connection.state !== signalR.HubConnectionState.Disconnected)) return;
    if (!global.signalR) {
      setStatus("offline", "Thiếu SignalR client");
      return;
    }

    starting = true;
    setStatus("connecting", "Đang kết nối");
    try {
      connection = new signalR.HubConnectionBuilder()
        .withUrl("/hubs/notifications", {
          accessTokenFactory: function () { return Auth.getToken() || ""; }
        })
        .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
        .configureLogging(signalR.LogLevel.Warning)
        .build();

      connection.on("TicketAssigned", function (payload) { dispatchTicketEvent("TicketAssigned", payload); });
      connection.on("TicketStatusChanged", function (payload) { dispatchTicketEvent("TicketStatusChanged", payload); });

      connection.onreconnecting(function () {
        setStatus("connecting", "Đang kết nối lại");
      });

      connection.onreconnected(function () {
        setStatus("connected", "Đã kết nối");
        window.dispatchEvent(new CustomEvent("realtime:reconnected"));
      });

      connection.onclose(function () {
        setStatus("offline", "Mất kết nối");
        connection = null;
        if (Auth.isLoggedIn()) scheduleRetry();
      });

      await connection.start();
      setStatus("connected", "Đã kết nối");
    } catch (error) {
      connection = null;
      setStatus("offline", "Mất kết nối");
      if (error && /401|unauthorized/i.test(String(error.message))) {
        Auth.clear();
        window.location.replace(Auth.loginRedirectUrl());
        return;
      }
      scheduleRetry();
    } finally {
      starting = false;
    }
  }

  async function stop() {
    if (retryTimer) { clearTimeout(retryTimer); retryTimer = null; }
    if (connection) {
      try { await connection.stop(); } catch { /* bỏ qua */ }
      connection = null;
    }
    setStatus("offline", "Mất kết nối");
  }

  global.Realtime = { connect: start, stop: stop, isConnected: function () { return Boolean(connection && connection.state === signalR.HubConnectionState.Connected); } };
})(window);
