/* Khung giao diện dùng chung: sidebar, topbar, thông báo realtime. */
(function (global) {
  "use strict";

  const el = UI.el;
  let currentUser = null;
  let unread = 0;
  const notifications = [];
  const seenNotifications = new Set();
  let toastContainer = null;

  function navLink(href, label, iconName, active) {
    return el("a", {
      class: "nav-link" + (active === href ? " active" : ""),
      href: href,
      "aria-current": active === href ? "page" : null
    }, el("span", { html: UI.icon(iconName) }), el("span", { text: label }));
  }

  function renderSidebar(user, active, mount) {
    const links = [
      navLink("index.html", "Tổng quan", "home", active),
      navLink("new-ticket.html", "Tạo yêu cầu", "plus", active),
      navLink("tickets.html", user.role === "Customer" ? "Yêu cầu của tôi" : "Danh sách yêu cầu", "list", active)
    ];
    if (user.role === "Admin") {
      links.push(navLink("admin.html", "Quản trị hệ thống", "shield", active));
    }

    const footer = el("div", { class: "sidebar-footer" },
      el("div", { class: "user-chip" },
        el("div", { class: "avatar", text: UI.initials(user.fullName) }),
        el("div", { class: "flex-grow-1 min-w-0" },
          el("div", { class: "name text-truncate", text: user.fullName }),
          el("div", { class: "role", text: Auth.roleLabel(user.role) })
        ),
        el("button", {
          class: "btn btn-sm btn-link text-white-50 p-1",
          type: "button",
          title: "Đăng xuất",
          "aria-label": "Đăng xuất",
          onclick: function () { Auth.logout(); }
        }, el("span", { html: UI.icon("logout") }))
      )
    );

    mount.appendChild(el("div", { class: "sidebar-brand" },
      el("div", { class: "brand-mark", text: "ST" }),
      el("div", null,
        el("div", { class: "brand-name", text: "Support Ticket" }),
        el("div", { class: "brand-sub", text: "Trung tâm hỗ trợ khách hàng" })
      )
    ));
    mount.appendChild(el("div", { class: "sidebar-section", text: "Nghiệp vụ" }));
    const nav = el("nav", { class: "sidebar-nav", "aria-label": "Điều hướng chính" }, links);
    mount.appendChild(nav);

    if (user.role !== "Customer") {
      mount.appendChild(el("div", { class: "sidebar-section", text: "Hướng dẫn" }));
      mount.appendChild(el("div", { class: "px-3 pb-2" },
        el("div", { class: "small text-white-50", style: { lineHeight: "1.55" },
          text: user.role === "Admin"
            ? "Bạn có thể phân công lại yêu cầu trong trang chi tiết và quản lý tài khoản, nhóm xử lý."
            : "Yêu cầu thuộc nhóm của bạn sẽ xuất hiện trong danh sách và được thông báo khi có thay đổi." })
      ));
    }

    mount.appendChild(footer);
  }

  function renderTopbar(title, subtitle, user, mount) {
    const bellButton = el("button", {
      class: "icon-button",
      type: "button",
      "data-bs-toggle": "dropdown",
      "aria-expanded": "false",
      "aria-label": "Thông báo"
    }, el("span", { html: UI.icon("bell") }),
      el("span", { class: "notification-dot d-none", id: "notification-count", text: "0" }));

    const notificationList = el("div", { class: "notification-list", id: "notification-list" },
      el("div", { class: "notification-empty", text: "Chưa có thông báo mới." }));

    const bell = el("div", { class: "dropdown" }, bellButton,
      el("div", { class: "dropdown-menu dropdown-menu-end notification-menu" },
        el("div", { class: "menu-head" },
          el("strong", { text: "Thông báo" }),
          el("button", {
            class: "btn btn-sm btn-link p-0",
            type: "button",
            onclick: function (event) {
              event.preventDefault();
              event.stopPropagation();
              markAllRead();
            }
          }, "Đánh dấu đã đọc")
        ),
        notificationList
      )
    );

    const menuButton = el("button", {
      class: "icon-button mobile-menu",
      type: "button",
      "aria-label": "Mở menu",
      onclick: function () { toggleSidebar(true); }
    }, el("span", { html: UI.icon("menu") }));

    const connectionPill = el("span", {
      class: "connection-pill is-connecting",
      "data-connection-status": "true",
      title: "Trạng thái kết nối realtime"
    }, el("span", { class: "dot" }), el("span", { class: "status-text", text: "Đang kết nối" }));

    mount.appendChild(menuButton);
    mount.appendChild(el("div", { class: "topbar-title" },
      el("h1", { text: title }),
      subtitle ? el("p", { text: subtitle }) : null
    ));
    mount.appendChild(el("div", { class: "topbar-actions" },
      connectionPill,
      el("a", {
        class: "btn btn-primary btn-sm d-none d-md-inline-flex align-items-center gap-1",
        href: "new-ticket.html"
      }, el("span", { html: UI.icon("plus") }), "Tạo yêu cầu"),
      bell
    ));
  }

  function toggleSidebar(open) {
    const sidebar = document.getElementById("sidebar");
    const backdrop = document.getElementById("sidebar-backdrop");
    if (!sidebar) return;
    const shouldOpen = typeof open === "boolean" ? open : !sidebar.classList.contains("show");
    sidebar.classList.toggle("show", shouldOpen);
    if (backdrop) backdrop.classList.toggle("show", shouldOpen);
    document.body.style.overflow = shouldOpen ? "hidden" : "";
  }

  function markAllRead() {
    unread = 0;
    notifications.forEach(function (item) { item.read = true; });
    renderNotifications();
  }

  function markRead(item) {
    if (!item.read) {
      item.read = true;
      unread = Math.max(0, unread - 1);
      renderNotifications();
    }
  }

  function renderNotifications() {
    const badge = document.getElementById("notification-count");
    if (badge) {
      badge.textContent = unread > 9 ? "9+" : String(unread);
      badge.classList.toggle("d-none", unread === 0);
    }

    const list = document.getElementById("notification-list");
    if (!list) return;
    UI.clear(list);
    if (notifications.length === 0) {
      list.appendChild(el("div", { class: "notification-empty", text: "Chưa có thông báo mới." }));
      return;
    }

    notifications.slice(0, 10).forEach(function (item) {
      const link = el("a", {
        class: "notification-item",
        href: "ticket.html?id=" + encodeURIComponent(item.ticketId),
        onclick: function () { markRead(item); }
      },
        el("div", { class: "d-flex align-items-start gap-2" },
          el("div", { class: "flex-grow-1" },
            el("div", { class: "n-title", text: item.title || "Yêu cầu " + item.ticketNumber }),
            el("div", { class: "n-meta" },
              (item.eventType === "TicketAssigned" ? "Phân công • " : "Cập nhật • "),
              UI.Meta.label("status", item.status),
              " • ",
              UI.timeAgo(item.changedAt)
            )
          ),
          item.read ? null : el("span", { class: "badge rounded-pill bg-danger", text: "Mới" })
        )
      );
      list.appendChild(link);
    });
  }

  function addNotification(event) {
    if (currentUser && event.changedByUserId === currentUser.id) return;
    const key = event.ticketId + "|" + event.changedAt + "|" + event.eventType;
    if (seenNotifications.has(key)) return;
    seenNotifications.add(key);

    const item = Object.assign({}, event, { read: false, receivedAt: Date.now() });
    notifications.unshift(item);
    if (notifications.length > 30) notifications.length = 30;
    unread += 1;
    renderNotifications();

    const statusText = UI.Meta.label("status", event.status);
    const isAssigned = event.eventType === "TicketAssigned";
    showToast(
      isAssigned ? "Yêu cầu đã được phân công" : "Cập nhật trạng thái yêu cầu",
      event.ticketNumber + " • " + event.title + " — " + statusText,
      "ticket.html?id=" + encodeURIComponent(event.ticketId)
    );
  }

  function showToast(title, body, link) {
    if (!toastContainer) {
      toastContainer = document.getElementById("toast-container");
      if (!toastContainer) {
        toastContainer = el("div", { class: "toast-container-custom", id: "toast-container", "aria-live": "polite" });
        document.body.appendChild(toastContainer);
      }
    }

    const node = el("div", { class: "app-toast", role: "status" },
      el("div", { class: "d-flex justify-content-between gap-2" },
        el("div", null,
          el("div", { class: "t-title", text: title }),
          el("div", { class: "t-body", text: body }),
          link ? el("a", { href: link, class: "d-inline-block mt-1", text: "Xem chi tiết" }) : null
        ),
        el("button", {
          class: "btn-close btn-sm",
          type: "button",
          "aria-label": "Đóng thông báo",
          onclick: function () { node.remove(); }
        })
      )
    );
    toastContainer.appendChild(node);
    setTimeout(function () { node.remove(); }, 7000);
  }

  function showForbidden(mount, message) {
    UI.clear(mount);
    mount.appendChild(el("div", { class: "card-panel" },
      el("div", { class: "card-panel-body" },
        el("div", { class: "empty-state" },
          el("div", { class: "empty-icon", html: UI.icon("shield") }),
          el("h3", { text: "Không có quyền truy cập" }),
          el("p", { text: message || "Tài khoản của bạn không có quyền sử dụng chức năng này." }),
          el("a", { class: "btn btn-primary btn-sm", href: "index.html", text: "Về trang tổng quan" })
        )
      )
    ));
  }

  async function init(options) {
    const opts = options || {};
    if (!Auth.requireAuth()) return null;

    const user = Auth.getUser();
    currentUser = user;

    const sidebarMount = document.getElementById("sidebar");
    const topbarMount = document.getElementById("topbar");
    const content = document.getElementById("content");
    const backdrop = document.getElementById("sidebar-backdrop");

    if (opts.roles && opts.roles.indexOf(user.role) === -1) {
      if (sidebarMount) sidebarMount.style.display = "none";
      if (topbarMount) topbarMount.style.display = "none";
      sanitizeShell();
      showForbidden(content, "Chức năng này chỉ dành cho " +
        opts.roles.map(function (r) { return Auth.roleLabel(r).toLowerCase(); }).join(" hoặc ") + ".");
      return null;
    }

    sanitizeShell();
    if (sidebarMount) {
      UI.clear(sidebarMount);
      renderSidebar(user, opts.active || "", sidebarMount);
      sidebarMount.setAttribute("aria-label", "Thanh điều hướng");
    }
    if (topbarMount) {
      UI.clear(topbarMount);
      renderTopbar(opts.title || "Tổng quan", opts.subtitle || "", user, topbarMount);
    }
    if (backdrop) {
      backdrop.addEventListener("click", function () { toggleSidebar(false); });
    }

    document.title = (opts.title ? opts.title + " • " : "") + "Hỗ trợ khách hàng";

    try { await UI.Meta.load(); } catch (error) { console.warn("Không tải được danh mục:", error); }

    window.addEventListener("realtime:status", function (event) {
      const detail = event.detail || {};
      const pill = document.querySelector("[data-connection-status]");
      if (pill) {
        pill.classList.remove("is-connected", "is-connecting", "is-offline");
        pill.classList.add("is-" + detail.state);
        const label = pill.querySelector(".status-text");
        if (label) label.textContent = detail.text;
      }
    });

    window.addEventListener("realtime:ticket", function (event) {
      addNotification(event.detail || {});
    });

    window.addEventListener("realtime:reconnected", function () {
      showToast("Đã kết nối lại", "Danh sách và chi tiết đang được tải lại để bảo đảm dữ liệu mới nhất.");
    });

    Realtime.connect();
    return user;
  }

  /* Xóa nội dung tĩnh trong các vùng khung để không hiển thị dữ liệu giả trước khi JS chạy. */
  function sanitizeShell() {
    document.querySelectorAll("[data-static-hint]").forEach(function (node) { node.remove(); });
  }

  global.Layout = { init: init, showToast: showToast, showForbidden: showForbidden, getUser: function () { return currentUser; } };
})(window);
