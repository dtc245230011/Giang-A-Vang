(async function () {
  "use strict";

  const user = await Layout.init({
    active: "new-ticket.html",
    title: "Tạo yêu cầu hỗ trợ",
    subtitle: "Gửi yêu cầu đến đúng nhóm xử lý"
  });
  if (!user) return;

  const form = document.getElementById("ticket-form");
  const titleInput = document.getElementById("title");
  const categorySelect = document.getElementById("category");
  const prioritySelect = document.getElementById("priority");
  const descriptionInput = document.getElementById("description");
  const submitButton = document.getElementById("submit-button");
  const alertBox = document.getElementById("form-alert");
  const successPanel = document.getElementById("success-panel");
  let createdTicket = null;

  try {
    await UI.Meta.load();
    UI.Meta.options("category").forEach(function (option) {
      categorySelect.appendChild(UI.el("option", { value: option.value, text: option.label }));
    });
    UI.Meta.options("priority").forEach(function (option) {
      prioritySelect.appendChild(UI.el("option", { value: option.value, text: option.label }));
    });
    prioritySelect.value = "Medium";
  } catch (error) {
    UI.showAlert(alertBox, "Không tải được danh mục: " + error.message, "warning");
  }

  function updateCount(input, targetId, max) {
    document.getElementById(targetId).textContent = input.value.length + "/" + max;
  }

  titleInput.addEventListener("input", function () {
    updateCount(titleInput, "title-count", 200);
    titleInput.classList.remove("is-invalid");
  });

  descriptionInput.addEventListener("input", function () {
    updateCount(descriptionInput, "description-count", 4000);
    descriptionInput.classList.remove("is-invalid");
  });

  categorySelect.addEventListener("change", function () { categorySelect.classList.remove("is-invalid"); });

  function validate() {
    const title = titleInput.value.trim();
    const description = descriptionInput.value.trim();
    const titleInvalid = title.length < 5 || title.length > 200;
    const descriptionInvalid = description.length < 10 || description.length > 4000;
    const categoryInvalid = !categorySelect.value;

    titleInput.classList.toggle("is-invalid", titleInvalid);
    descriptionInput.classList.toggle("is-invalid", descriptionInvalid);
    categorySelect.classList.toggle("is-invalid", categoryInvalid);

    if (titleInvalid) return { ok: false, message: "Tiêu đề phải từ 5 đến 200 ký tự." };
    if (categoryInvalid) return { ok: false, message: "Vui lòng chọn danh mục cho yêu cầu." };
    if (descriptionInvalid) return { ok: false, message: "Nội dung phải từ 10 đến 4000 ký tự." };
    return { ok: true };
  }

  form.addEventListener("submit", async function (event) {
    event.preventDefault();
    UI.clear(alertBox);
    const validation = validate();
    if (!validation.ok) {
      UI.showAlert(alertBox, validation.message, "danger");
      return;
    }

    UI.buttonBusy(submitButton, true, "Đang gửi...");
    try {
      createdTicket = await Api.createTicket({
        title: titleInput.value.trim(),
        description: descriptionInput.value.trim(),
        category: categorySelect.value,
        priority: prioritySelect.value || null
      });

      document.getElementById("success-number").textContent = createdTicket.ticketNumber;
      const statusNode = document.getElementById("success-status");
      UI.clear(statusNode);
      statusNode.appendChild(UI.statusBadge(createdTicket.status));
      document.getElementById("success-link").href = "ticket.html?id=" + encodeURIComponent(createdTicket.id);
      successPanel.classList.remove("d-none");
      form.classList.add("d-none");
      successPanel.scrollIntoView({ behavior: "smooth", block: "center" });
      successPanel.setAttribute("tabindex", "-1");
      successPanel.focus({ preventScroll: true });
    } catch (error) {
      UI.showAlert(alertBox, error.message || "Không thể gửi yêu cầu.", "danger");
      if (error.validation) {
        if (error.validation.Title) titleInput.classList.add("is-invalid");
        if (error.validation.Description) descriptionInput.classList.add("is-invalid");
        if (error.validation.Category) categorySelect.classList.add("is-invalid");
      }
    } finally {
      UI.buttonBusy(submitButton, false);
    }
  });

  form.addEventListener("reset", function () {
    setTimeout(function () {
      updateCount(titleInput, "title-count", 200);
      updateCount(descriptionInput, "description-count", 4000);
      prioritySelect.value = "Medium";
      UI.clear(alertBox);
    }, 0);
  });

  document.getElementById("another-button").addEventListener("click", function () {
    createdTicket = null;
    form.reset();
    successPanel.classList.add("d-none");
    form.classList.remove("d-none");
    titleInput.focus();
  });

  form.addEventListener("keydown", function (event) {
    if ((event.ctrlKey || event.metaKey) && event.key === "Enter") {
      event.preventDefault();
      form.requestSubmit();
    }
  });

  window.addEventListener("realtime:ticket", function (event) {
    if (!createdTicket || event.detail.ticketId !== createdTicket.id) return;
    const statusNode = document.getElementById("success-status");
    UI.clear(statusNode);
    statusNode.appendChild(UI.statusBadge(event.detail.status));
  });
})();
