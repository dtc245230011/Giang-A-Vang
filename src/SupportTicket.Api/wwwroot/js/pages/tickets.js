(async function () {
  "use strict";

  const el = UI.el;
  const rowsBody = document.getElementById("ticket-rows");
  const alertBox = document.getElementById("list-alert");
  const pageInfo = document.getElementById("page-info");
  const pager = document.getElementById("pager");
  const filterForm = document.getElementById("filter-form");
  const searchInput = document.getElementById("search");
  const statusSelect = document.getElementById("filter-status");
  const prioritySelect = document.getElementById("filter-priority");
  const categorySelect = document.getElementById("filter-category");
  const teamSelect = document.getElementById("filter-team");
  const unassignedCheck = document.getElementById("filter-unassigned");
  const pageSizeSelect = document.getElementById("page-size");
  const params = new URLSearchParams(window.location.search);

  let state = {
    page: Math.max(1, Number(params.get("page")) || 1),
    pageSize: [10, 20, 50].indexOf(Number(params.get("pageSize"))) >= 0 ? Number(params.get("pageSize")) : 20,
    search: params.get("search") || "",
    status: params.get("status") || "",
    priority: params.get("priority") || "",
    category: params.get("category") || "",
    teamId: params.get("teamId") || "",
    unassigned: params.get("unassigned") === "true"
  };
  let teams = [];
  let requestSerial = 0;
  let reloadTimer = null;

  const user = await Layout.init({
    active: "tickets.html",
    title: "Danh sách yêu cầu",
    subtitle: "Tìm kiếm, lọc và theo dõi tiến trình"
  });
  if (!user) return;

  searchInput.value = state.search;
  unassignedCheck.checked = state.unassigned;
  pageSizeSelect.value = String(state.pageSize);
  document.getElementById("list-heading").textContent =
    user.role === "Customer" ? "Yêu cầu của tôi" : user.role === "Admin" ? "Tất cả yêu cầu" : "Yêu cầu tôi có thể xử lý";

  const isStaff = user.role === "Admin" || user.role === "Agent";
  if (!isStaff) {
    document.getElementById("team-filter-wrap").classList.add("d-none");
    document.getElementById("unassigned-wrap").classList.add("d-none");
  }

  try {
    await UI.Meta.load();
    UI.Meta.options("status").forEach(function (o) {
      statusSelect.appendChild(el("option", { value: o.value, text: o.label }));
    });
    UI.Meta.options("priority").forEach(function (o) {
      prioritySelect.appendChild(el("option", { value: o.value, text: o.label }));
    });
    UI.Meta.options("category").forEach(function (o) {
      categorySelect.appendChild(el("option", { value: o.value, text: o.label }));
    });
    if (isStaff) {
      teams = await Api.listTeams();
      teams.forEach(function (team) {
        teamSelect.appendChild(el("option", { value: team.id, text: team.code + " — " + team.name }));
      });
    }
    statusSelect.value = state.status;
    prioritySelect.value = state.priority;
    categorySelect.value = state.category;
    teamSelect.value = state.teamId;
  } catch (error) {
    UI.showAlert(alertBox, "Không tải được danh mục/bộ lọc: " + error.message, "warning");
  }

  function syncUrl() {
    const query = new URLSearchParams();
    if (state.page > 1) query.set("page", state.page);
    if (state.pageSize !== 20) query.set("pageSize", state.pageSize);
    if (state.search) query.set("search", state.search);
    if (state.status) query.set("status", state.status);
    if (state.priority) query.set("priority", state.priority);
    if (state.category) query.set("category", state.category);
    if (state.teamId) query.set("teamId", state.teamId);
    if (state.unassigned) query.set("unassigned", "true");
    const text = query.toString();
    history.replaceState(null, "", "tickets.html" + (text ? "?" + text : ""));
  }

  function readFilters() {
    state.search = searchInput.value.trim();
    state.status = statusSelect.value;
    state.priority = prioritySelect.value;
    state.category = categorySelect.value;
    state.teamId = teamSelect.value;
    state.unassigned = unassignedCheck.checked;
  }

  function loadingRows() {
    UI.clear(rowsBody);
    for (let i = 0; i < 5; i++) {
      const tr = el("tr");
      for (let c = 0; c < 7; c++) {
        tr.appendChild(el("td", null, el("div", { class: "skeleton skeleton-line", style: { width: (60 + ((i + c) % 35)) + "%" } })));
      }
      rowsBody.appendChild(tr);
    }
    pageInfo.textContent = "Đang tải...";
    UI.clear(pager);
  }

  function renderRows(items) {
    UI.clear(rowsBody);
    if (!items.length) {
      rowsBody.appendChild(el("tr", null,
        el("td", { colspan: "7" },
          el("div", { class: "empty-state" },
            el("div", { class: "empty-icon", html: UI.icon("inbox") }),
            el("h3", { text: "Không tìm thấy yêu cầu" }),
            el("p", { text: "Thử thay đổi từ khóa hoặc bộ lọc. Nếu chưa có yêu cầu, bạn có thể tạo yêu cầu mới." }),
            el("a", { class: "btn btn-primary btn-sm", href: "new-ticket.html", text: "Tạo yêu cầu" })
          )
        )
      ));
      return;
    }

    items.forEach(function (ticket) {
      const href = "ticket.html?id=" + encodeURIComponent(ticket.id);
      const tr = el("tr", {
        class: "ticket-row",
        tabindex: "0",
        role: "link",
        "aria-label": "Mở yêu cầu " + ticket.ticketNumber,
        onclick: function (event) {
          if (event.target.closest("a")) return;
          window.location.href = href;
        },
        onkeydown: function (event) {
          if (event.key === "Enter" || event.key === " ") {
            event.preventDefault();
            window.location.href = href;
          }
        }
      },
        el("td", null, el("a", { class: "ticket-number", href: href, text: ticket.ticketNumber })),
        el("td", null,
          el("div", { class: "ticket-title text-truncate", title: ticket.title, text: ticket.title }),
          el("div", { class: "small text-muted-soft", text: UI.Meta.label("category", ticket.category) })
        ),
        el("td", null, UI.statusBadge(ticket.status)),
        el("td", null, UI.priorityBadge(ticket.priority)),
        el("td", null,
          el("div", { class: "small fw-semibold", text: ticket.requester ? ticket.requester.fullName : "—" }),
          el("div", { class: "small text-muted-soft", text: ticket.requester ? ticket.requester.email : "" })
        ),
        el("td", null,
          ticket.assignedTeam
            ? el("div", null,
                el("div", { class: "small fw-semibold", text: ticket.assignedTeam.name }),
                el("div", { class: "small text-muted-soft", text: ticket.assignedUser ? ticket.assignedUser.fullName : "Chưa gán nhân viên" }))
            : el("span", { class: "text-muted-soft small", text: "Chưa phân công" })
        ),
        el("td", null,
          el("div", { class: "small", text: UI.timeAgo(ticket.updatedAt) }),
          el("div", { class: "small text-muted-soft", text: UI.formatDateTime(ticket.updatedAt) })
        )
      );
      rowsBody.appendChild(tr);
    });
  }

  function renderPager(data) {
    UI.clear(pager);
    const totalPages = data.totalPages || 0;
    if (totalPages <= 1) return;

    function pageButton(label, page, disabled, active) {
      return el("button", {
        class: "btn btn-sm " + (active ? "btn-navy" : "btn-outline-secondary"),
        type: "button",
        disabled: disabled || active,
        text: label,
        "aria-label": "Trang " + page,
        onclick: function () { state.page = page; load(); }
      });
    }

    pager.appendChild(pageButton("‹", state.page - 1, state.page <= 1));
    const start = Math.max(1, state.page - 2);
    const end = Math.min(totalPages, start + 4);
    for (let page = start; page <= end; page++) {
      pager.appendChild(pageButton(String(page), page, false, page === state.page));
    }
    pager.appendChild(pageButton("›", state.page + 1, state.page >= totalPages));
  }

  async function load() {
    const serial = ++requestSerial;
    readFilters();
    syncUrl();
    loadingRows();
    UI.clear(alertBox);

    const query = {
      page: state.page,
      pageSize: state.pageSize,
      search: state.search || null,
      status: state.status || null,
      priority: state.priority || null,
      category: state.category || null,
      teamId: state.teamId || null,
      unassigned: state.unassigned ? true : null
    };

    try {
      const data = await Api.listTickets(query);
      if (serial !== requestSerial) return;
      renderRows(data.items || []);
      const from = data.totalItems === 0 ? 0 : (data.page - 1) * data.pageSize + 1;
      const to = Math.min(data.totalItems, data.page * data.pageSize);
      pageInfo.textContent = data.totalItems === 0
        ? "Không có yêu cầu"
        : "Hiển thị " + from + "–" + to + " trong " + data.totalItems + " yêu cầu";
      document.getElementById("list-subtitle").textContent =
        data.totalItems + " yêu cầu phù hợp với bộ lọc hiện tại";
      document.getElementById("filter-summary").textContent =
        (state.search ? "Từ khóa: “" + state.search + "” • " : "") + "Tổng: " + data.totalItems;
      if (state.page > data.totalPages && data.totalPages > 0) {
        state.page = data.totalPages;
        load();
        return;
      }
      renderPager(data);
    } catch (error) {
      if (serial !== requestSerial) return;
      UI.clear(rowsBody);
      rowsBody.appendChild(el("tr", null, el("td", { colspan: "7", class: "table-empty", text: "Không tải được dữ liệu." })));
      pageInfo.textContent = "—";
      UI.clear(pager);
      UI.clear(alertBox);
      alertBox.appendChild(el("div", { class: "alert alert-danger d-flex flex-wrap align-items-center gap-2", role: "alert" },
        el("span", { text: error.message || "Không tải được danh sách yêu cầu." }),
        el("button", {
          class: "btn btn-sm btn-outline-secondary",
          type: "button",
          text: "Thử lại",
          onclick: function () { load(); }
        })
      ));
    }
  }

  function applyFilterFromControls() {
    state.page = 1;
    load();
  }

  let searchTimer = null;
  searchInput.addEventListener("input", function () {
    if (searchTimer) clearTimeout(searchTimer);
    searchTimer = setTimeout(applyFilterFromControls, 450);
  });
  statusSelect.addEventListener("change", applyFilterFromControls);
  prioritySelect.addEventListener("change", applyFilterFromControls);
  categorySelect.addEventListener("change", applyFilterFromControls);
  teamSelect.addEventListener("change", applyFilterFromControls);
  unassignedCheck.addEventListener("change", applyFilterFromControls);
  pageSizeSelect.addEventListener("change", function () {
    state.pageSize = Number(pageSizeSelect.value);
    state.page = 1;
    load();
  });
  filterForm.addEventListener("submit", function (event) {
    event.preventDefault();
    applyFilterFromControls();
  });
  document.getElementById("clear-filters").addEventListener("click", function () {
    searchInput.value = "";
    statusSelect.value = "";
    prioritySelect.value = "";
    categorySelect.value = "";
    teamSelect.value = "";
    unassignedCheck.checked = false;
    state.page = 1;
    load();
  });

  window.addEventListener("realtime:ticket", function () {
    if (reloadTimer) clearTimeout(reloadTimer);
    reloadTimer = setTimeout(load, 700);
  });
  window.addEventListener("realtime:reconnected", load);

  await load();
})();
