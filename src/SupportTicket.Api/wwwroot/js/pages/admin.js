(async function () {
  "use strict";

  const el = UI.el;
  const user = await Layout.init({
    active: "admin.html",
    title: "Quản trị hệ thống",
    subtitle: "Tài khoản, vai trò và nhóm xử lý",
    roles: ["Admin"]
  });
  if (!user) return;

  const userModal = bootstrap.Modal.getOrCreateInstance(document.getElementById("user-modal"));
  const teamModal = bootstrap.Modal.getOrCreateInstance(document.getElementById("team-modal"));
  const membersModal = bootstrap.Modal.getOrCreateInstance(document.getElementById("members-modal"));

  const state = {
    users: [],
    teams: [],
    memberTeamId: null,
    memberTeam: null,
    agents: [],
    userSearchTimer: null
  };

  try {
    await UI.Meta.load();
  } catch (error) {
    UI.showAlert(document.getElementById("admin-alert"), "Không tải được danh mục: " + error.message, "warning");
  }

  const roleSelect = document.getElementById("user-role");
  const roleFilter = document.getElementById("user-role-filter");
  UI.Meta.options("role").forEach(function (option) {
    roleSelect.appendChild(el("option", { value: option.value, text: option.label }));
    roleFilter.appendChild(el("option", { value: option.value, text: option.label }));
  });

  /* ---------- Tabs ---------- */
  function showTab(name) {
    const isUsers = name !== "teams";
    document.getElementById("panel-users").classList.toggle("d-none", !isUsers);
    document.getElementById("panel-teams").classList.toggle("d-none", isUsers);
    const usersTab = document.getElementById("tab-users");
    const teamsTab = document.getElementById("tab-teams");
    usersTab.classList.toggle("active", isUsers);
    teamsTab.classList.toggle("active", !isUsers);
    usersTab.setAttribute("aria-selected", String(isUsers));
    teamsTab.setAttribute("aria-selected", String(!isUsers));
    history.replaceState(null, "", "admin.html" + (isUsers ? "" : "#teams"));
  }
  document.getElementById("tab-users").addEventListener("click", function () { showTab("users"); });
  document.getElementById("tab-teams").addEventListener("click", function () { showTab("teams"); });
  if (window.location.hash === "#teams") showTab("teams");

  function alertMain(message, type) {
    UI.showAlert(document.getElementById("admin-alert"), message, type || "danger");
  }

  /* ---------- Users ---------- */
  async function loadUsers() {
    const rows = document.getElementById("user-rows");
    rows.innerHTML = '<tr><td colspan="7" class="table-empty">Đang tải tài khoản...</td></tr>';
    const query = {
      search: document.getElementById("user-search").value.trim() || null,
      role: roleFilter.value || null,
      isActive: document.getElementById("user-active-filter").value || null
    };
    try {
      state.users = await Api.listUsers(query);
      renderUsers();
      UI.clear(document.getElementById("admin-alert"));
    } catch (error) {
      rows.innerHTML = '<tr><td colspan="7" class="table-empty">Không tải được tài khoản.</td></tr>';
      alertMain("Không tải được danh sách tài khoản: " + error.message);
    }
  }

  function renderUsers() {
    const rows = document.getElementById("user-rows");
    UI.clear(rows);
    document.getElementById("user-count").textContent = state.users.length + " tài khoản";

    if (!state.users.length) {
      rows.appendChild(el("tr", null, el("td", { colspan: "7" },
        el("div", { class: "empty-state py-4" },
          el("h3", { text: "Không có tài khoản phù hợp" }),
          el("p", { text: "Thử thay đổi bộ lọc hoặc thêm tài khoản mới." })
        )
      )));
      return;
    }

    state.users.forEach(function (item) {
      const toggleLabel = item.isActive ? "Vô hiệu hóa" : "Kích hoạt";
      const toggleClass = item.isActive ? "btn-soft-danger" : "btn-outline-secondary";
      rows.appendChild(el("tr", null,
        el("td", null,
          el("div", { class: "d-flex align-items-center gap-2" },
            el("div", { class: "avatar", style: { width: "32px", height: "32px", fontSize: ".78rem" }, text: UI.initials(item.fullName) }),
            el("span", { class: "fw-semibold", text: item.fullName })
          )
        ),
        el("td", { class: "small", text: item.email }),
        el("td", null, UI.roleBadge(item.role)),
        el("td", null,
          item.teams.length
            ? el("div", { class: "d-flex flex-wrap gap-1" }, item.teams.map(function (team) {
                return el("span", { class: "category-badge", text: team.code });
              }))
            : el("span", { class: "text-muted-soft small", text: "—" })
        ),
        el("td", null, el("span", {
          class: "status-badge " + (item.isActive ? "status-resolved" : "status-new"),
          text: item.isActive ? "Hoạt động" : "Đã vô hiệu hóa"
        })),
        el("td", { class: "small text-muted-soft", text: UI.formatDate(item.createdAt) }),
        el("td", { class: "text-end" },
          el("div", { class: "d-flex justify-content-end gap-1 flex-wrap" },
            el("button", {
              class: "btn btn-sm btn-outline-secondary", type: "button", text: "Sửa",
              onclick: function () { openUserModal(item); }
            }),
            el("button", {
              class: "btn btn-sm " + toggleClass, type: "button", text: toggleLabel,
              onclick: function () { toggleUser(item); }
            })
          )
        )
      ));
    });
  }

  async function toggleUser(item) {
    const action = item.isActive ? "vô hiệu hóa" : "kích hoạt";
    if (!window.confirm("Bạn chắc chắn muốn " + action + " tài khoản " + item.email + "?")) return;
    try {
      await Api.updateUser(item.id, { isActive: !item.isActive });
      Layout.showToast("Đã cập nhật tài khoản", "Tài khoản " + item.email + " đã được " + action + ".");
      await loadUsers();
      await loadTeams();
    } catch (error) {
      alertMain("Không cập nhật được tài khoản: " + error.message);
    }
  }

  function openUserModal(item) {
    const isEdit = Boolean(item);
    document.getElementById("user-modal-title").textContent = isEdit ? "Cập nhật người dùng" : "Thêm người dùng";
    document.getElementById("user-id").value = isEdit ? item.id : "";
    document.getElementById("user-name").value = isEdit ? item.fullName : "";
    document.getElementById("user-email").value = isEdit ? item.email : "";
    document.getElementById("user-email").disabled = isEdit;
    document.getElementById("user-email-help").textContent = isEdit
      ? "Email đăng nhập không thể thay đổi."
      : "Email dùng để đăng nhập.";
    document.getElementById("user-password").value = "";
    document.getElementById("user-password").required = !isEdit;
    document.getElementById("user-password-label").innerHTML = isEdit
      ? "Mật khẩu mới"
      : 'Mật khẩu <span class="required">*</span>';
    document.getElementById("user-password-help").textContent = isEdit
      ? "Để trống nếu không đổi mật khẩu."
      : "Tối thiểu 8 ký tự.";
    document.getElementById("user-role").value = isEdit ? item.role : "Customer";
    document.getElementById("user-create-only").classList.toggle("d-none", isEdit);
    document.getElementById("user-active-wrap").classList.toggle("d-none", !isEdit);
    document.getElementById("user-active").checked = isEdit ? item.isActive : true;
    document.getElementById("user-team").value = "";
    document.getElementById("user-lead").checked = false;
    UI.clear(document.getElementById("user-form-alert"));
    userModal.show();
  }

  document.getElementById("add-user-button").addEventListener("click", function () { openUserModal(null); });

  document.getElementById("user-form").addEventListener("submit", async function (event) {
    event.preventDefault();
    const alertBox = document.getElementById("user-form-alert");
    UI.clear(alertBox);
    const id = document.getElementById("user-id").value;
    const isEdit = Boolean(id);
    const fullName = document.getElementById("user-name").value.trim();
    const email = document.getElementById("user-email").value.trim();
    const password = document.getElementById("user-password").value;
    const role = document.getElementById("user-role").value;
    const teamId = document.getElementById("user-team").value;
    const isLead = document.getElementById("user-lead").checked;
    const submit = document.getElementById("user-submit");

    if (fullName.length < 2) { UI.showAlert(alertBox, "Họ tên phải từ 2 ký tự.", "danger"); return; }
    if (!isEdit && !email) { UI.showAlert(alertBox, "Vui lòng nhập email.", "danger"); return; }
    if ((!isEdit || password) && password.length < 8) {
      UI.showAlert(alertBox, "Mật khẩu phải từ 8 đến 100 ký tự.", "danger"); return;
    }
    if (!isEdit && teamId && role !== "Agent") {
      UI.showAlert(alertBox, "Chỉ tài khoản Nhân viên mới được thêm vào nhóm.", "danger"); return;
    }

    UI.buttonBusy(submit, true, "Đang lưu...");
    try {
      if (isEdit) {
        await Api.updateUser(id, {
          fullName: fullName,
          role: role,
          isActive: document.getElementById("user-active").checked,
          newPassword: password || null
        });
        Layout.showToast("Đã cập nhật người dùng", "Thông tin tài khoản " + email + " đã được lưu.");
      } else {
        await Api.createUser({
          fullName: fullName,
          email: email,
          password: password,
          role: role,
          teamId: role === "Agent" && teamId ? teamId : null,
          isLead: role === "Agent" && teamId ? isLead : false
        });
        Layout.showToast("Đã thêm người dùng", "Tài khoản " + email + " đã được tạo.");
      }
      userModal.hide();
      await Promise.all([loadUsers(), loadTeams()]);
    } catch (error) {
      UI.showAlert(alertBox, error.message || "Không lưu được người dùng.", "danger");
    } finally {
      UI.buttonBusy(submit, false);
    }
  });

  document.getElementById("user-search").addEventListener("input", function () {
    if (state.userSearchTimer) clearTimeout(state.userSearchTimer);
    state.userSearchTimer = setTimeout(loadUsers, 350);
  });
  roleFilter.addEventListener("change", loadUsers);
  document.getElementById("user-active-filter").addEventListener("change", loadUsers);

  /* ---------- Teams ---------- */
  async function loadTeams() {
    try {
      state.teams = await Api.listTeams({ includeInactive: true });
      populateTeamOptions();
      renderTeams();
    } catch (error) {
      document.getElementById("team-cards").innerHTML = "";
      alertMain("Không tải được nhóm xử lý: " + error.message);
    }
  }

  function populateTeamOptions() {
    const select = document.getElementById("user-team");
    const current = select.value;
    UI.clear(select);
    select.appendChild(el("option", { value: "", text: "— Không thêm vào nhóm —" }));
    state.teams.filter(function (team) { return team.isActive; }).forEach(function (team) {
      select.appendChild(el("option", { value: team.id, text: team.code + " — " + team.name }));
    });
    if (current && state.teams.some(function (x) { return x.id === current; })) select.value = current;
  }

  function renderTeams() {
    const container = document.getElementById("team-cards");
    UI.clear(container);
    if (!state.teams.length) {
      container.appendChild(el("div", { class: "col-12" },
        el("div", { class: "card-panel" }, el("div", { class: "card-panel-body" },
          el("div", { class: "empty-state" },
            el("h3", { text: "Chưa có nhóm xử lý" }),
            el("p", { text: "Thêm nhóm để worker có thể phân công yêu cầu." })
          )
        ))
      ));
      return;
    }

    state.teams.forEach(function (team) {
      container.appendChild(el("div", { class: "col-lg-6" },
        el("div", { class: "card-panel h-100" },
          el("div", { class: "card-panel-body" },
            el("div", { class: "d-flex justify-content-between align-items-start gap-2" },
              el("div", null,
                el("div", { class: "d-flex align-items-center gap-2 mb-1" },
                  el("span", { class: "ticket-number", text: team.code }),
                  el("span", {
                    class: "status-badge " + (team.isActive ? "status-resolved" : "status-new"),
                    text: team.isActive ? "Hoạt động" : "Ngừng hoạt động"
                  })
                ),
                el("h3", { class: "h6 fw-bold mb-1", text: team.name })
              ),
              el("span", { class: "category-badge", text: team.memberCount + " thành viên" })
            ),
            el("p", { class: "text-muted-soft small mb-3", text: team.description || "Chưa có mô tả." }),
            el("div", { class: "d-flex flex-wrap gap-2" },
              el("button", {
                class: "btn btn-sm btn-outline-secondary", type: "button", text: "Thành viên",
                onclick: function () { openMembers(team); }
              }),
              el("button", {
                class: "btn btn-sm btn-outline-secondary", type: "button", text: "Sửa nhóm",
                onclick: function () { openTeamModal(team); }
              })
            )
          )
        )
      ));
    });
  }

  function openTeamModal(team) {
    const isEdit = Boolean(team);
    document.getElementById("team-modal-title").textContent = isEdit ? "Cập nhật nhóm xử lý" : "Thêm nhóm xử lý";
    document.getElementById("team-id").value = isEdit ? team.id : "";
    document.getElementById("team-code").value = isEdit ? team.code : "";
    document.getElementById("team-code").disabled = isEdit;
    document.getElementById("team-code-help").textContent = isEdit
      ? "Mã nhóm không thể thay đổi sau khi tạo."
      : "Ví dụ TECH, ACCT. Không thể đổi sau khi tạo.";
    document.getElementById("team-name").value = isEdit ? team.name : "";
    document.getElementById("team-description").value = isEdit ? team.description || "" : "";
    document.getElementById("team-active-wrap").classList.toggle("d-none", !isEdit);
    document.getElementById("team-active").checked = isEdit ? team.isActive : true;
    UI.clear(document.getElementById("team-form-alert"));
    teamModal.show();
  }

  document.getElementById("add-team-button").addEventListener("click", function () { openTeamModal(null); });

  document.getElementById("team-form").addEventListener("submit", async function (event) {
    event.preventDefault();
    const alertBox = document.getElementById("team-form-alert");
    UI.clear(alertBox);
    const id = document.getElementById("team-id").value;
    const isEdit = Boolean(id);
    const code = document.getElementById("team-code").value.trim().toUpperCase();
    const name = document.getElementById("team-name").value.trim();
    const description = document.getElementById("team-description").value.trim();
    const submit = document.getElementById("team-submit");

    if (!isEdit && !/^[A-Za-z0-9_-]{2,20}$/.test(code)) {
      UI.showAlert(alertBox, "Mã nhóm phải từ 2 đến 20 ký tự, chỉ gồm chữ, số, gạch ngang hoặc gạch dưới.", "danger");
      return;
    }
    if (name.length < 2) { UI.showAlert(alertBox, "Tên nhóm phải từ 2 ký tự.", "danger"); return; }

    UI.buttonBusy(submit, true, "Đang lưu...");
    try {
      if (isEdit) {
        await Api.updateTeam(id, {
          name: name,
          description: description || null,
          isActive: document.getElementById("team-active").checked
        });
        Layout.showToast("Đã cập nhật nhóm", "Nhóm " + code + " đã được lưu.");
      } else {
        await Api.createTeam({ code: code, name: name, description: description || null });
        Layout.showToast("Đã thêm nhóm", "Nhóm " + code + " đã được tạo.");
      }
      teamModal.hide();
      await loadTeams();
      await loadUsers();
    } catch (error) {
      UI.showAlert(alertBox, error.message || "Không lưu được nhóm.", "danger");
    } finally {
      UI.buttonBusy(submit, false);
    }
  });

  /* ---------- Members ---------- */
  async function openMembers(team) {
    state.memberTeamId = team.id;
    state.memberTeam = team;
    document.getElementById("members-modal-title").textContent = "Thành viên nhóm " + team.code + " — " + team.name;
    UI.clear(document.getElementById("members-alert"));
    document.getElementById("member-lead").checked = false;
    document.getElementById("member-rows").innerHTML = '<tr><td colspan="5" class="table-empty">Đang tải...</td></tr>';
    membersModal.show();
    await loadMemberData();
  }

  async function loadMemberData() {
    if (!state.memberTeamId) return;
    const rows = document.getElementById("member-rows");
    try {
      const results = await Promise.all([
        Api.getTeam(state.memberTeamId),
        Api.listAgents()
      ]);
      state.memberTeam = results[0];
      state.agents = results[1];
      renderMembers();
    } catch (error) {
      rows.innerHTML = '<tr><td colspan="5" class="table-empty">Không tải được thành viên.</td></tr>';
      UI.showAlert(document.getElementById("members-alert"), "Không tải được thành viên: " + error.message, "danger");
    }
  }

  function renderMembers() {
    const team = state.memberTeam;
    const rows = document.getElementById("member-rows");
    UI.clear(rows);

    if (!team.members.length) {
      rows.appendChild(el("tr", null, el("td", { colspan: "5" },
        el("div", { class: "empty-state py-3" },
          el("h3", { text: "Nhóm chưa có thành viên" }),
          el("p", { text: "Thêm nhân viên để worker có thể gán người xử lý." })
        )
      )));
    } else {
      team.members.forEach(function (member) {
        rows.appendChild(el("tr", null,
          el("td", null,
            el("div", { class: "d-flex align-items-center gap-2" },
              el("span", { class: "fw-semibold", text: member.fullName }),
              member.isActive ? null : el("span", { class: "badge-inactive status-badge", text: "Đã vô hiệu hóa" })
            )
          ),
          el("td", { class: "small", text: member.email }),
          el("td", null, UI.roleBadge(member.role)),
          el("td", null, member.isLead
            ? el("span", { class: "status-badge status-assigned", text: "Trưởng nhóm" })
            : el("span", { class: "text-muted-soft small", text: "—" })),
          el("td", { class: "text-end" },
            el("div", { class: "d-flex justify-content-end gap-1 flex-wrap" },
              el("button", {
                class: "btn btn-sm btn-outline-secondary", type: "button",
                text: member.isLead ? "Bỏ trưởng nhóm" : "Đặt trưởng nhóm",
                onclick: function () { setLead(member); }
              }),
              el("button", {
                class: "btn btn-sm btn-soft-danger", type: "button", text: "Gỡ khỏi nhóm",
                onclick: function () { removeMember(member); }
              })
            )
          )
        ));
      });
    }

    const select = document.getElementById("member-agent");
    UI.clear(select);
    const memberIds = new Set(team.members.map(function (m) { return m.userId; }));
    const available = state.agents.filter(function (agent) { return !memberIds.has(agent.id); });
    if (!available.length) {
      select.appendChild(el("option", { value: "", text: "Không còn nhân viên để thêm" }));
      select.disabled = true;
      document.getElementById("add-member-button").disabled = true;
    } else {
      select.disabled = false;
      document.getElementById("add-member-button").disabled = false;
      select.appendChild(el("option", { value: "", text: "— Chọn nhân viên —" }));
      available.forEach(function (agent) {
        select.appendChild(el("option", { value: agent.id, text: agent.fullName + " (" + agent.email + ")" }));
      });
    }
  }

  document.getElementById("add-member-button").addEventListener("click", async function () {
    const select = document.getElementById("member-agent");
    const agentId = select.value;
    if (!agentId) {
      UI.showAlert(document.getElementById("members-alert"), "Vui lòng chọn nhân viên cần thêm.", "warning");
      return;
    }
    const button = this;
    UI.buttonBusy(button, true, "Đang thêm...");
    try {
      await Api.addTeamMember(state.memberTeamId, agentId, { isLead: document.getElementById("member-lead").checked });
      UI.clear(document.getElementById("members-alert"));
      document.getElementById("member-lead").checked = false;
      Layout.showToast("Đã thêm thành viên", "Nhân viên đã được thêm vào nhóm " + state.memberTeam.code + ".");
      await loadMemberData();
      await Promise.all([loadTeams(), loadUsers()]);
    } catch (error) {
      UI.showAlert(document.getElementById("members-alert"), error.message || "Không thêm được thành viên.", "danger");
    } finally {
      UI.buttonBusy(button, false);
    }
  });

  async function setLead(member) {
    try {
      await Api.addTeamMember(state.memberTeamId, member.userId, { isLead: !member.isLead });
      Layout.showToast("Đã cập nhật trưởng nhóm", member.fullName + (member.isLead ? " không còn là trưởng nhóm." : " đã trở thành trưởng nhóm."));
      await loadMemberData();
      await Promise.all([loadTeams(), loadUsers()]);
    } catch (error) {
      UI.showAlert(document.getElementById("members-alert"), error.message || "Không cập nhật được trưởng nhóm.", "danger");
    }
  }

  async function removeMember(member) {
    if (!window.confirm("Gỡ " + member.fullName + " khỏi nhóm " + state.memberTeam.code + "?")) return;
    try {
      await Api.removeTeamMember(state.memberTeamId, member.userId);
      Layout.showToast("Đã gỡ thành viên", member.fullName + " đã được gỡ khỏi nhóm.");
      await loadMemberData();
      await Promise.all([loadTeams(), loadUsers()]);
    } catch (error) {
      UI.showAlert(document.getElementById("members-alert"), error.message || "Không gỡ được thành viên.", "danger");
    }
  }

  await Promise.all([loadUsers(), loadTeams()]);
})();
