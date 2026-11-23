/* Tiện ích dùng chung: DOM an toàn, định dạng, nhãn tiếng Việt. */
(function (global) {
  "use strict";

  const FALLBACK_LABELS = {
    categories: {
      Account: "Tài khoản / Đăng nhập",
      Payment: "Thanh toán / Hóa đơn",
      Delivery: "Giao hàng / Đổi trả",
      Other: "Khác"
    },
    priorities: { Low: "Thấp", Medium: "Trung bình", High: "Cao" },
    statuses: { New: "Mới", Assigned: "Đã phân công", InProgress: "Đang xử lý", Resolved: "Đã giải quyết" },
    roles: { Customer: "Khách hàng", Agent: "Nhân viên", Admin: "Quản trị viên" }
  };

  const ICONS = {
    home: '<path d="M3 10.5 12 3l9 7.5"/><path d="M5 9.5V21h14V9.5"/>',
    plus: '<path d="M12 5v14M5 12h14"/>',
    list: '<path d="M8 6h13M8 12h13M8 18h13M3 6h.01M3 12h.01M3 18h.01"/>',
    users: '<path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M23 21v-2a4 4 0 0 0-3-3.87M16 3.13a4 4 0 0 1 0 7.75"/>',
    bell: '<path d="M18 8a6 6 0 0 0-12 0c0 7-3 9-3 9h18s-3-2-3-9"/><path d="M13.7 21a2 2 0 0 1-3.4 0"/>',
    logout: '<path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4"/><path d="M16 17l5-5-5-5M21 12H9"/>',
    search: '<circle cx="11" cy="11" r="7"/><path d="m21 21-4.3-4.3"/>',
    ticket: '<path d="M3 9a3 3 0 0 0 0 6v3a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-3a3 3 0 0 1 0-6V6a2 2 0 0 0-2-2H5a2 2 0 0 0-2 2z"/><path d="M13 5v14"/>',
    clock: '<circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/>',
    chart: '<path d="M3 3v18h18"/><path d="M7 15l4-5 3 3 5-7"/>',
    menu: '<path d="M4 6h16M4 12h16M4 18h16"/>',
    user: '<circle cx="12" cy="8" r="4"/><path d="M4 21a8 8 0 0 1 16 0"/>',
    shield: '<path d="M12 22s8-3 8-10V5l-8-3-8 3v7c0 7 8 10 8 10z"/><path d="m9 12 2 2 4-4"/>',
    check: '<path d="m5 13 4 4L19 7"/>',
    arrowRight: '<path d="M5 12h14M13 6l6 6-6 6"/>',
    refresh: '<path d="M21 12a9 9 0 1 1-3-6.7L21 8"/><path d="M21 3v5h-5"/>',
    x: '<path d="M18 6 6 18M6 6l12 12"/>',
    inbox: '<path d="M4 4h16v16H4z"/><path d="M4 13h4l2 3h4l2-3h4"/>'
  };

  function icon(name, className) {
    const body = ICONS[name] || ICONS.ticket;
    return '<svg class="' + (className || "") + '" viewBox="0 0 24 24" fill="none" stroke="currentColor" ' +
      'stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">' + body + "</svg>";
  }

  function el(tag, props, ...children) {
    const node = document.createElement(tag);
    if (props) {
      for (const [key, value] of Object.entries(props)) {
        if (value === null || value === undefined || value === false) continue;
        if (key === "class") node.className = value;
        else if (key === "text") node.textContent = value;
        else if (key === "html") node.innerHTML = value; // chỉ dùng với chuỗi tin cậy do ứng dụng tạo
        else if (key === "dataset") Object.assign(node.dataset, value);
        else if (key === "style") Object.assign(node.style, value);
        else if (key.startsWith("on") && typeof value === "function") {
          node.addEventListener(key.slice(2).toLowerCase(), value);
        } else if (value === true) node.setAttribute(key, "");
        else node.setAttribute(key, value);
      }
    }
    append(node, children);
    return node;
  }

  function append(parent, children) {
    for (const child of children.flat(Infinity)) {
      if (child === null || child === undefined || child === false) continue;
      parent.appendChild(child instanceof Node ? child : document.createTextNode(String(child)));
    }
  }

  function clear(node) {
    while (node.firstChild) node.removeChild(node.firstChild);
    return node;
  }

  const Meta = {
    data: null,
    async load(force) {
      if (this.data && !force) return this.data;
      this.data = await Api.getMeta();
      return this.data;
    },
    _kind(kind) {
      const map = { category: "categories", priority: "priorities", status: "statuses", role: "roles" };
      return map[kind] || kind;
    },
    label(kind, value) {
      const group = this._kind(kind);
      const list = this.data ? this.data[group] : null;
      if (list) {
        const found = list.find((x) => x.value === value);
        if (found) return found.label;
      }
      return (FALLBACK_LABELS[group] && FALLBACK_LABELS[group][value]) || value || "—";
    },
    options(kind) {
      const group = this._kind(kind);
      if (this.data && this.data[group]) return this.data[group];
      return Object.entries(FALLBACK_LABELS[group] || {}).map(([value, label]) => ({ value, label }));
    }
  };

  function parseDate(value) {
    if (!value) return null;
    let text = String(value);
    if (!/[zZ]|[+-]\d{2}:\d{2}$/.test(text)) text += "Z";
    const date = new Date(text);
    return Number.isNaN(date.getTime()) ? null : date;
  }

  function formatDate(value) {
    const date = parseDate(value);
    if (!date) return "—";
    return new Intl.DateTimeFormat("vi-VN", { day: "2-digit", month: "2-digit", year: "numeric" }).format(date);
  }

  function formatDateTime(value) {
    const date = parseDate(value);
    if (!date) return "—";
    return new Intl.DateTimeFormat("vi-VN", {
      day: "2-digit", month: "2-digit", year: "numeric", hour: "2-digit", minute: "2-digit"
    }).format(date);
  }

  function timeAgo(value) {
    const date = parseDate(value);
    if (!date) return "—";
    const diff = Date.now() - date.getTime();
    const minutes = Math.floor(diff / 60000);
    if (minutes < 1) return "Vừa xong";
    if (minutes < 60) return minutes + " phút trước";
    const hours = Math.floor(minutes / 60);
    if (hours < 24) return hours + " giờ trước";
    const days = Math.floor(hours / 24);
    if (days < 30) return days + " ngày trước";
    return formatDate(value);
  }

  function statusBadge(status) {
    return el("span", {
      class: "status-badge status-" + String(status).toLowerCase(),
      text: Meta.label("status", status)
    });
  }

  function priorityBadge(priority) {
    return el("span", {
      class: "priority-badge priority-" + String(priority).toLowerCase(),
      text: Meta.label("priority", priority)
    });
  }

  function categoryBadge(category) {
    return el("span", { class: "category-badge", text: Meta.label("category", category) });
  }

  function roleBadge(role) {
    return el("span", {
      class: "role-badge-soft role-" + String(role).toLowerCase(),
      text: Meta.label("role", role)
    });
  }

  function setLoading(container, options) {
    const opts = options || {};
    clear(container);
    const rows = opts.rows || 4;
    const wrap = el("div", { class: "p-3" });
    for (let i = 0; i < rows; i++) {
      wrap.appendChild(el("div", { class: "skeleton skeleton-line", style: { width: (90 - i * 8) + "%" } }));
    }
    if (opts.cards) {
      clear(container);
      const grid = el("div", { class: "stat-grid" });
      for (let i = 0; i < 4; i++) grid.appendChild(el("div", { class: "skeleton skeleton-card" }));
      container.appendChild(grid);
      return;
    }
    container.appendChild(wrap);
  }

  function showEmpty(container, options) {
    clear(container);
    const opts = options || {};
    const box = el("div", { class: "empty-state" },
      el("div", { class: "empty-icon", html: icon(opts.icon || "inbox") }),
      el("h3", { text: opts.title || "Chưa có dữ liệu" }),
      opts.message ? el("p", { text: opts.message }) : null,
      opts.actionLabel && opts.actionHref
        ? el("a", { class: "btn btn-primary btn-sm", href: opts.actionHref, text: opts.actionLabel })
        : null
    );
    container.appendChild(box);
  }

  function showAlert(container, message, type) {
    clear(container);
    container.appendChild(el("div", {
      class: "alert alert-" + (type || "danger") + " d-flex align-items-start gap-2",
      role: "alert"
    }, el("span", { text: message })));
  }

  function buttonBusy(button, busy, busyText) {
    if (!button) return;
    if (busy) {
      button.dataset.originalHtml = button.innerHTML;
      button.disabled = true;
      button.innerHTML = '<span class="spinner-border spinner-border-sm me-2" aria-hidden="true"></span>' +
        (busyText || "Đang xử lý...");
    } else {
      button.disabled = false;
      if (button.dataset.originalHtml) button.innerHTML = button.dataset.originalHtml;
      delete button.dataset.originalHtml;
    }
  }

  function getParam(name) {
    return new URLSearchParams(window.location.search).get(name);
  }

  function initials(name) {
    const parts = String(name || "?").trim().split(/\s+/).slice(0, 2);
    return parts.map((x) => x.charAt(0).toUpperCase()).join("") || "?";
  }

  function fieldLabel(field) {
    const map = {
      Status: "Trạng thái",
      AssignedTeamId: "Nhóm xử lý",
      AssignedUserId: "Người xử lý",
      Priority: "Mức ưu tiên",
      Category: "Danh mục",
      Created: "Khởi tạo"
    };
    return map[field] || field;
  }

  function historyValue(field, value) {
    if (!value) return "—";
    if (field === "Status") return Meta.label("status", value);
    if (field === "Priority") return Meta.label("priority", value);
    if (field === "Category") return Meta.label("category", value);
    return value;
  }

  global.UI = {
    el, icon, clear, append, Meta, parseDate, formatDate, formatDateTime, timeAgo,
    statusBadge, priorityBadge, categoryBadge, roleBadge,
    setLoading, showEmpty, showAlert, buttonBusy, getParam, initials, fieldLabel, historyValue
  };
})(window);
