(async function () {
  "use strict";

  const el = UI.el;
  const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
  const id = UI.getParam("id");
  const statusModal = document.getElementById("status-modal");
  const modal = window.bootstrap ? bootstrap.Modal.getOrCreateInstance(statusModal) : null;
  const statusForm = document.getElementById("status-form");
  const statusTarget = document.getElementById("status-target");
  const statusNote = document.getElementById("status-note");
  const statusConfirm = document.getElementById("status-confirm");
  const statusError = document.getElementById("status-error");

  const state = {
    id: id,
    detail: null,
    history: [],
    teams: [],
    pendingStatus: null,
    assignmentDirty: false,
    loading: false,
    refreshTimer: null
  };

  const user = await Layout.init({
    active: "tickets.html",
    title: "Chi tiết yêu cầu",
    subtitle: "Thông tin, lịch sử và thao tác xử lý"
  });
  if (!user) return;

  if (!id || !GUID.test(id)) {
    showFatal("Mã yêu cầu không hợp lệ. Vui lòng mở yêu cầu từ danh sách.");
    return;
  }

  function showFatal(message) {
    document.getElementById("detail-loading").classList.add("d-none");
    document.getElementById("detail-view").classList.add("d-none");
    const box = document.getElementById("detail-alert");
    UI.clear(box);
    box.appendChild(el("div", { class: "alert alert-danger d-flex flex-wrap align-items-center gap-2", role: "alert" },
      el("span", { text: message }),
      el("a", { class: "btn btn-sm btn-outline-secondary", href: "tickets.html", text: "Về danh sách" })
    ));
  }

  function teamLabel(code) {
    if (!code) return "—";
    const team = state.teams.find(function (x) { return x.code === code; });
    return team ? code + " — " + team.name : code;
  }

  function metaRow(label, value) {
    return el("div", { class: "meta-row" },
      el("span", { class: "label", text: label }),
      value instanceof Node ? el("span", { class: "value" }, value) : el("span", { class: "value", text: value || "—" })
    );
  }

  function renderHeader() {
    const detail = state.detail;
    document.getElementById("detail-number").textContent = detail.ticketNumber;
    document.getElementById("detail-title").textContent = detail.title;

    const badges = document.getElementById("detail-badges");
    UI.clear(badges);
    badges.appendChild(UI.statusBadge(detail.status));
    badges.appendChild(UI.priorityBadge(detail.priority));
    badges.appendChild(UI.categoryBadge(detail.category));

    const subtitle = document.getElementById("detail-subtitle");
    UI.clear(subtitle);
    subtitle.appendChild(el("span", { text: "Người gửi: " + detail.requester.fullName }));
    subtitle.appendChild(el("span", { text: "Tạo lúc " + UI.formatDateTime(detail.createdAt) }));
    subtitle.appendChild(el("span", { text: "Cập nhật " + UI.timeAgo(detail.updatedAt) }));

    document.getElementById("detail-description").textContent = detail.description;

    const meta = document.getElementById("detail-meta");
    UI.clear(meta);
    meta.appendChild(metaRow("Người gửi", detail.requester.fullName));
    meta.appendChild(metaRow("Email", detail.requester.email));
    meta.appendChild(metaRow("Danh mục", UI.Meta.label("category", detail.category)));
    meta.appendChild(metaRow("Mức ưu tiên", UI.Meta.label("priority", detail.priority)));
    meta.appendChild(metaRow("Nhóm xử lý", detail.assignedTeam ? detail.assignedTeam.name : "Chưa phân công"));
    meta.appendChild(metaRow("Người xử lý", detail.assignedUser ? detail.assignedUser.fullName : "Chưa gán"));
    meta.appendChild(metaRow("Ngày tạo", UI.formatDateTime(detail.createdAt)));
    meta.appendChild(metaRow("Cập nhật cuối", UI.formatDateTime(detail.updatedAt)));
    if (detail.resolvedAt) meta.appendChild(metaRow("Hoàn thành lúc", UI.formatDateTime(detail.resolvedAt)));

    document.title = detail.ticketNumber + " • Hỗ trợ khách hàng";
  }

  function historyValue(field, value) {
    if (!value) return "—";
    if (field === "Status") return UI.Meta.label("status", value);
    if (field === "Priority") return UI.Meta.label("priority", value);
    if (field === "Category") return UI.Meta.label("category", value);
    if (field === "AssignedTeamId") return teamLabel(value);
    return value;
  }

  function renderHistory() {
    const list = document.getElementById("history-list");
    UI.clear(list);
    document.getElementById("history-count").textContent = state.history.length + " bản ghi";

    if (!state.history.length) {
      list.appendChild(el("div", { class: "text-muted-soft small", text: "Chưa có lịch sử thay đổi." }));
      return;
    }

    const timeline = el("div", { class: "timeline" });
    state.history.forEach(function (item) {
      const isSystem = !item.changedByUserId;
      const changeText = item.oldValue && item.newValue
        ? historyValue(item.fieldChanged, item.oldValue) + " → " + historyValue(item.fieldChanged, item.newValue)
        : historyValue(item.fieldChanged, item.newValue || item.oldValue);

      timeline.appendChild(el("div", { class: "timeline-item" + (isSystem ? " is-system" : "") },
        el("div", { class: "timeline-title", text: UI.fieldLabel(item.fieldChanged) }),
        el("div", { class: "timeline-change", text: changeText }),
        item.note ? el("div", { class: "timeline-note", text: item.note }) : null,
        el("div", { class: "timeline-meta mt-1" },
          (item.changedByName || "Hệ thống") + " • " + UI.formatDateTime(item.changedAt) + " • " + UI.timeAgo(item.changedAt))
      ));
    });
    list.appendChild(timeline);
  }

  function renderActions() {
    const area = document.getElementById("action-area");
    UI.clear(area);
    const detail = state.detail;
    const stack = el("div", { class: "action-stack" });

    if (detail.status === "Resolved") {
      stack.appendChild(el("div", { class: "alert alert-success mb-0" },
        el("strong", { text: "Yêu cầu đã hoàn thành." }),
        el("div", { class: "small mt-1", text: "Không thể thay đổi trạng thái hoặc phân công sau khi hoàn thành." })
      ));
      area.appendChild(stack);
      return;
    }

    if (detail.allowedNextStatuses.length) {
      detail.allowedNextStatuses.forEach(function (status) {
        const isResolve = status === "Resolved";
        stack.appendChild(el("button", {
          class: "btn " + (isResolve ? "btn-success" : "btn-primary"),
          type: "button",
          onclick: function () { openStatusModal(status); }
        }, isResolve ? "Đánh dấu hoàn thành" : "Bắt đầu xử lý"));
      });
    }

    if (detail.canAssign) {
      stack.appendChild(buildAssignmentForm());
    }

    if (!detail.allowedNextStatuses.length && !detail.canAssign) {
      if (detail.status === "New") {
        stack.appendChild(el("div", { class: "info-box" },
          "Yêu cầu đang chờ hệ thống phân công. Bạn sẽ nhận thông báo ngay khi có nhóm xử lý."));
      } else if (detail.status === "Assigned" || detail.status === "InProgress") {
        stack.appendChild(el("div", { class: "info-box" },
          "Chỉ nhân viên được giao hoặc thành viên nhóm xử lý mới có thể cập nhật trạng thái yêu cầu này."));
      } else {
        stack.appendChild(el("div", { class: "info-box", text: "Không có thao tác khả dụng cho trạng thái hiện tại." }));
      }
    }

    area.appendChild(stack);
  }

  function buildAssignmentForm() {
    const detail = state.detail;
    const form = el("form", { class: "mt-2 pt-3 border-top" });
    const errorBox = el("div", { "aria-live": "polite" });

    const teamSelect = el("select", { class: "form-select form-select-sm", id: "assign-team", required: true });
    state.teams.filter(function (team) { return team.isActive; }).forEach(function (team) {
      teamSelect.appendChild(el("option", { value: team.id, text: team.code + " — " + team.name }));
    });
    if (detail.assignedTeam) teamSelect.value = detail.assignedTeam.id;

    const agentSelect = el("select", { class: "form-select form-select-sm", id: "assign-agent", disabled: true });
    agentSelect.appendChild(el("option", { value: "", text: "Đang tải nhân viên..." }));

    const note = el("textarea", {
      class: "form-control form-control-sm", id: "assign-note", rows: "2", maxlength: "500",
      placeholder: "Ghi chú phân công (không bắt buộc)"
    });

    const saveButton = el("button", { class: "btn btn-navy w-100", type: "submit" },
      detail.assignedTeam ? "Lưu phân công" : "Giao yêu cầu");

    form.appendChild(el("div", { class: "small-label mb-2", text: detail.assignedTeam ? "Phân công lại" : "Phân công yêu cầu" }));
    form.appendChild(errorBox);
    form.appendChild(el("label", { class: "form-label", for: "assign-team", text: "Nhóm xử lý" }));
    form.appendChild(teamSelect);
    form.appendChild(el("label", { class: "form-label mt-2", for: "assign-agent", text: "Nhân viên xử lý" }));
    form.appendChild(agentSelect);
    form.appendChild(el("label", { class: "form-label mt-2", for: "assign-note", text: "Ghi chú" }));
    form.appendChild(note);
    form.appendChild(el("div", { class: "form-text mb-2", text: "Chỉ nhân viên thuộc nhóm mới được chọn. Có thể để trống nhân viên." }));
    form.appendChild(saveButton);

    async function loadAgents(selectedUserId) {
      agentSelect.disabled = true;
      UI.clear(agentSelect);
      agentSelect.appendChild(el("option", { value: "", text: "Đang tải..." }));
      try {
        const agents = await Api.listAgents({ teamId: teamSelect.value });
        UI.clear(agentSelect);
        agentSelect.appendChild(el("option", { value: "", text: "— Chưa gán nhân viên —" }));
        agents.forEach(function (agent) {
          agentSelect.appendChild(el("option", {
            value: agent.id,
            text: agent.fullName + " (" + agent.email + ")"
          }));
        });
        if (selectedUserId && agents.some(function (x) { return x.id === selectedUserId; })) {
          agentSelect.value = selectedUserId;
        }
        agentSelect.disabled = false;
      } catch (error) {
        UI.clear(agentSelect);
        agentSelect.appendChild(el("option", { value: "", text: "Không tải được nhân viên" }));
        UI.showAlert(errorBox, error.message, "danger");
      }
    }

    loadAgents(detail.assignedUser ? detail.assignedUser.id : null);

    teamSelect.addEventListener("change", function () {
      state.assignmentDirty = true;
      loadAgents(null);
    });
    agentSelect.addEventListener("change", function () { state.assignmentDirty = true; });
    note.addEventListener("input", function () { state.assignmentDirty = Boolean(note.value); });

    form.addEventListener("submit", async function (event) {
      event.preventDefault();
      UI.clear(errorBox);
      if (!teamSelect.value) {
        UI.showAlert(errorBox, "Vui lòng chọn nhóm xử lý.", "danger");
        return;
      }

      UI.buttonBusy(saveButton, true, "Đang lưu...");
      try {
        await Api.assignTicket(state.id, {
          teamId: teamSelect.value,
          assignedUserId: agentSelect.value || null,
          note: note.value.trim() || null,
          rowVersion: state.detail.rowVersion
        });
        state.assignmentDirty = false;
        Layout.showToast("Đã cập nhật phân công", "Yêu cầu " + state.detail.ticketNumber + " đã được phân công.");
        await loadAll(true);
      } catch (error) {
        UI.showAlert(errorBox, error.message || "Không thể phân công yêu cầu.", "danger");
        if (error.status === 409) {
          UI.showAlert(errorBox, error.message + " Dữ liệu sẽ được tải lại.", "warning");
          await loadAll(true);
        }
      } finally {
        UI.buttonBusy(saveButton, false);
      }
    });

    return form;
  }

  function openStatusModal(status) {
    state.pendingStatus = status;
    document.getElementById("status-ticket-number").textContent = state.detail.ticketNumber;
    statusTarget.textContent = UI.Meta.label("status", status);
    statusNote.value = "";
    UI.clear(statusError);
    statusConfirm.className = "btn " + (status === "Resolved" ? "btn-success" : "btn-primary");
    statusConfirm.textContent = status === "Resolved" ? "Xác nhận hoàn thành" : "Xác nhận bắt đầu";
    if (modal) modal.show();
  }

  statusForm.addEventListener("submit", async function (event) {
    event.preventDefault();
    if (!state.pendingStatus || !state.detail) return;
    UI.clear(statusError);
    UI.buttonBusy(statusConfirm, true, "Đang cập nhật...");
    try {
      await Api.changeStatus(state.id, {
        status: state.pendingStatus,
        note: statusNote.value.trim() || null,
        rowVersion: state.detail.rowVersion
      });
      if (modal) modal.hide();
      Layout.showToast("Đã cập nhật trạng thái", "Yêu cầu " + state.detail.ticketNumber + " chuyển sang " +
        UI.Meta.label("status", state.pendingStatus) + ".");
      await loadAll(true);
    } catch (error) {
      if (error.status === 409) {
        if (modal) modal.hide();
        const pending = document.getElementById("pending-refresh");
        UI.clear(pending);
        pending.classList.remove("d-none");
        pending.appendChild(el("div", { class: "alert alert-warning d-flex flex-wrap align-items-center gap-2" },
          el("span", { text: error.message + " Dữ liệu mới nhất đã được tải lại." })
        ));
        await loadAll(true);
      } else {
        UI.showAlert(statusError, error.message || "Không thể cập nhật trạng thái.", "danger");
      }
    } finally {
      UI.buttonBusy(statusConfirm, false);
    }
  });

  if (statusModal) {
    statusModal.addEventListener("hidden.bs.modal", function () {
      state.pendingStatus = null;
      UI.clear(statusError);
    });
  }

  function renderAll() {
    document.getElementById("detail-loading").classList.add("d-none");
    document.getElementById("detail-view").classList.remove("d-none");
    renderHeader();
    renderHistory();
    renderActions();
  }

  function showPendingRefresh(message) {
    const box = document.getElementById("pending-refresh");
    UI.clear(box);
    box.classList.remove("d-none");
    box.appendChild(el("div", { class: "alert alert-info d-flex flex-wrap align-items-center gap-2" },
      el("span", { text: message || "Yêu cầu vừa có cập nhật mới. Tải lại để xem phiên bản mới nhất." }),
      el("button", {
        class: "btn btn-sm btn-primary", type: "button", text: "Tải lại",
        onclick: function () {
          state.assignmentDirty = false;
          if (modal) modal.hide();
          loadAll(false);
          box.classList.add("d-none");
        }
      })
    ));
  }

  async function loadAll(silent) {
    if (state.loading) return;
    state.loading = true;
    if (!silent) document.getElementById("detail-loading").classList.remove("d-none");
    try {
      const results = await Promise.all([
        Api.getTicket(state.id),
        Api.getHistory(state.id),
        Api.listTeams().catch(function () { return []; })
      ]);
      state.detail = results[0];
      state.history = results[1] || [];
      state.teams = results[2] || [];
      UI.clear(document.getElementById("detail-alert"));
      renderAll();
    } catch (error) {
      if (error.status === 404) {
        showFatal("Không tìm thấy yêu cầu này hoặc yêu cầu đã bị xóa khỏi phạm vi truy cập.");
      } else if (error.status === 403) {
        showFatal("Bạn không có quyền xem yêu cầu này.");
      } else {
        document.getElementById("detail-loading").classList.add("d-none");
        const box = document.getElementById("detail-alert");
        UI.clear(box);
        box.appendChild(el("div", { class: "alert alert-danger d-flex flex-wrap align-items-center gap-2" },
          el("span", { text: error.message || "Không tải được chi tiết yêu cầu." }),
          el("button", { class: "btn btn-sm btn-outline-secondary", type: "button", text: "Thử lại", onclick: function () { loadAll(false); } })
        ));
      }
    } finally {
      state.loading = false;
    }
  }

  document.getElementById("refresh-button").addEventListener("click", function () {
    state.assignmentDirty = false;
    const pending = document.getElementById("pending-refresh");
    pending.classList.add("d-none");
    loadAll(false);
  });

  window.addEventListener("realtime:ticket", function (event) {
    const detail = event.detail;
    if (!state.detail || detail.ticketId !== state.id) return;

    const eventTime = (UI.parseDate(detail.changedAt) || new Date(0)).getTime();
    const currentTime = (UI.parseDate(state.detail.updatedAt) || new Date(0)).getTime();
    if (eventTime < currentTime) return;

    const modalOpen = statusModal.classList.contains("show");
    if (state.assignmentDirty || modalOpen) {
      showPendingRefresh("Yêu cầu vừa có cập nhật mới trong khi bạn đang thao tác. Tải lại để xem dữ liệu mới nhất.");
      return;
    }

    if (state.refreshTimer) clearTimeout(state.refreshTimer);
    state.refreshTimer = setTimeout(function () { loadAll(true); }, 400);
  });

  window.addEventListener("realtime:reconnected", function () { loadAll(true); });

  await loadAll(false);
})();
