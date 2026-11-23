(async function () {
  "use strict";

  const el = UI.el;
  let reloadTimer = null;
  let hasData = false;

  const roleSubtitle = {
    Admin: "Toàn bộ yêu cầu, phân công và hoạt động của hệ thống.",
    Agent: "Các yêu cầu thuộc nhóm xử lý và yêu cầu được giao cho bạn.",
    Customer: "Theo dõi tiến trình các yêu cầu bạn đã gửi."
  };

  const user = await Layout.init({
    active: "index.html",
    title: "Tổng quan",
    subtitle: "Số liệu thực từ hệ thống"
  });
  if (!user) return;

  document.getElementById("hero-role").textContent = Auth.roleLabel(user.role);
  const hour = new Date().getHours();
  const greeting = hour < 11 ? "Chào buổi sáng" : hour < 18 ? "Chào buổi chiều" : "Chào buổi tối";
  document.getElementById("hero-greeting").textContent = greeting + ", " + user.fullName;
  document.getElementById("hero-sub").textContent = roleSubtitle[user.role] || roleSubtitle.Customer;

  function statCard(iconName, color, value, label, hint) {
    return el("div", { class: "stat-card" },
      el("div", { class: "stat-icon " + (color || ""), html: UI.icon(iconName) }),
      el("div", { class: "min-w-0" },
        el("div", { class: "stat-value", text: value }),
        el("div", { class: "stat-label", text: label }),
        hint ? el("div", { class: "text-muted-soft", style: { fontSize: ".72rem", marginTop: ".15rem" }, text: hint }) : null
      )
    );
  }

  function renderStats(data) {
    const grid = document.getElementById("stat-grid");
    UI.clear(grid);
    grid.removeAttribute("aria-busy");
    grid.appendChild(statCard("ticket", "", data.total, "Tổng yêu cầu", user.role === "Customer" ? "Yêu cầu bạn đã gửi" : "Trong phạm vi quyền xem"));
    grid.appendChild(statCard("clock", "amber", data.new, "Yêu cầu mới", data.unassigned + " chưa phân công"));
    grid.appendChild(statCard("chart", "blue", data.inProgress, "Đang xử lý", data.assigned + " đã phân công"));
    grid.appendChild(statCard("check", "green", data.resolvedToday, "Hoàn thành hôm nay", data.resolved + " tổng đã giải quyết"));
  }

  function renderRecent(items) {
    const container = document.getElementById("recent-list");
    UI.clear(container);
    if (!items.length) {
      UI.showEmpty(container, {
        title: "Chưa có yêu cầu nào",
        message: "Khi có yêu cầu hỗ trợ, thông tin sẽ xuất hiện tại đây.",
        actionLabel: "Tạo yêu cầu đầu tiên",
        actionHref: "new-ticket.html"
      });
      return;
    }

    items.forEach(function (ticket) {
      const link = el("a", {
        class: "d-block text-decoration-none text-reset ticket-row",
        href: "ticket.html?id=" + encodeURIComponent(ticket.id),
        style: { borderBottom: "1px solid #eef3f8" }
      },
        el("div", { class: "d-flex align-items-start gap-3 p-3" },
          el("div", { class: "flex-grow-1 min-w-0" },
            el("div", { class: "d-flex align-items-center gap-2 flex-wrap mb-1" },
              el("span", { class: "ticket-number", text: ticket.ticketNumber }),
              UI.statusBadge(ticket.status),
              UI.priorityBadge(ticket.priority)
            ),
            el("div", { class: "fw-semibold text-truncate", text: ticket.title }),
            el("div", { class: "text-muted-soft", style: { fontSize: ".76rem" },
              text: (ticket.requester ? ticket.requester.fullName : "—") +
                " • " + (ticket.assignedTeam ? ticket.assignedTeam.name : "Chưa phân công") +
                " • " + UI.timeAgo(ticket.updatedAt) })
          ),
          el("div", { class: "text-end flex-shrink-0" },
            el("span", { class: "category-badge", text: UI.Meta.label("category", ticket.category) })
          )
        )
      );
      container.appendChild(link);
    });
  }

  function renderBars(containerId, counts) {
    const container = document.getElementById(containerId);
    UI.clear(container);
    const max = Math.max(1, ...counts.map(function (x) { return x.count; }));
    counts.forEach(function (entry) {
      const label = containerId === "category-chart"
        ? UI.Meta.label("category", entry.key)
        : UI.Meta.label("priority", entry.key);
      container.appendChild(el("div", { class: "bar-row" },
        el("div", { class: "bar-label text-truncate", title: label, text: label }),
        el("div", { class: "bar-track" },
          el("div", { class: "bar-fill", style: { width: Math.round((entry.count / max) * 100) + "%" } })
        ),
        el("div", { class: "bar-value", text: String(entry.count) })
      ));
    });
  }

  function debounceReload() {
    if (reloadTimer) clearTimeout(reloadTimer);
    reloadTimer = setTimeout(function () { load(true); }, 700);
  }

  async function load(silent) {
    if (!silent) {
      document.getElementById("stat-grid").setAttribute("aria-busy", "true");
    }
    try {
      const data = await Api.getDashboard();
      renderStats(data);
      renderRecent(data.recentTickets || []);
      renderBars("category-chart", data.byCategory || []);
      renderBars("priority-chart", data.byPriority || []);
      hasData = true;
      UI.clear(document.getElementById("dashboard-alert"));
    } catch (error) {
      if (!hasData) {
        document.getElementById("stat-grid").removeAttribute("aria-busy");
        UI.showEmpty(document.getElementById("recent-list"), {
          title: "Không tải được số liệu",
          message: error.message,
          actionLabel: "Thử lại",
          actionHref: "index.html"
        });
      }
      UI.showAlert(document.getElementById("dashboard-alert"),
        "Không tải được số liệu tổng quan: " + (error.message || "Lỗi không xác định"), "danger");
    }
  }

  window.addEventListener("realtime:ticket", debounceReload);
  window.addEventListener("realtime:reconnected", function () { load(true); });

  await load(false);
})();
