/*
 * Kiểm thử end-to-end thật:
 * - Đăng nhập khách hàng, nhân viên, khách hàng khác.
 * - Kết nối SignalR bằng JWT.
 * - Tạo ticket qua Support API.
 * - Chờ Assignment Worker đọc RabbitMQ và phân công.
 * - Kiểm tra thông báo realtime đúng người, nhân viên cập nhật trạng thái,
 *   khách hàng nhận thông báo, khách hàng khác không nhận.
 *
 * Yêu cầu: API và Worker đang chạy, SQL Server/RabbitMQ healthy.
 */
import * as signalR from "@microsoft/signalr";

const API = process.env.API_URL || "http://localhost:5026";
const PASSWORD = process.env.DEMO_PASSWORD;
const MANAGEMENT = process.env.RABBITMQ_MANAGEMENT_URL || "http://localhost:15672";
const MQ_USER = process.env.RABBITMQ_USER || "";
const MQ_PASS = process.env.RABBITMQ_PASSWORD || "";

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

function assert(condition, message) {
  if (!condition) throw new Error("ASSERT: " + message);
}

async function request(path, options = {}) {
  const headers = { Accept: "application/json" };
  if (options.token) headers.Authorization = "Bearer " + options.token;
  if (options.body !== undefined) headers["Content-Type"] = "application/json";

  const response = await fetch(API + path, {
    method: options.method || "GET",
    headers,
    body: options.body !== undefined ? JSON.stringify(options.body) : undefined
  });

  if (response.status === 204) return null;
  const text = await response.text();
  const body = text ? JSON.parse(text) : null;
  if (!response.ok) {
    throw new Error(`HTTP ${response.status} ${path}: ${body?.detail || body?.title || text}`);
  }
  return body;
}

async function login(email) {
  return request("/api/auth/login", {
    method: "POST",
    body: { email, password: PASSWORD }
  });
}

async function waitFor(predicate, timeoutMs, label) {
  const started = Date.now();
  while (Date.now() - started < timeoutMs) {
    const value = predicate();
    if (value) return value;
    await sleep(150);
  }
  throw new Error("Timeout sau " + timeoutMs + "ms: " + label);
}

async function connect(label, token, events) {
  const connection = new signalR.HubConnectionBuilder()
    .withUrl(API + "/hubs/notifications", {
      accessTokenFactory: () => token
    })
    .configureLogging(signalR.LogLevel.Warning)
    .build();

  for (const eventName of ["TicketAssigned", "TicketStatusChanged"]) {
    connection.on(eventName, (payload) => {
      events.push({ eventName, ...payload, receivedAt: Date.now() });
      console.log(`  [SignalR:${label}] ${eventName} ${payload.ticketNumber} status=${payload.status}`);
    });
  }

  connection.onreconnecting(() => console.log(`  [SignalR:${label}] đang kết nối lại...`));
  connection.onreconnected(() => console.log(`  [SignalR:${label}] đã kết nối lại.`));

  await connection.start();
  console.log(`  [SignalR:${label}] connected (${connection.connectionId})`);
  return connection;
}

async function waitHealthy() {
  for (let attempt = 1; attempt <= 120; attempt++) {
    try {
      const response = await fetch(API + "/health");
      if (response.ok) {
        console.log("API health:", (await response.text()).trim());
        return;
      }
    } catch {
      // API chưa sẵn sàng.
    }
    await sleep(500);
  }
  throw new Error("API không sẵn sàng tại " + API);
}

async function queueDepths() {
  if (!MQ_USER || !MQ_PASS) return null;
  const auth = Buffer.from(MQ_USER + ":" + MQ_PASS).toString("base64");
  try {
    const response = await fetch(MANAGEMENT + "/api/queues", {
      headers: { Authorization: "Basic " + auth }
    });
    if (!response.ok) return null;
    const queues = await response.json();
    return queues
      .filter((q) => q.name.startsWith("assignment-worker") || q.name.startsWith("notification-service"))
      .map((q) => ({ name: q.name, messages: q.messages, consumers: q.consumers, state: q.state }))
      .sort((a, b) => a.name.localeCompare(b.name));
  } catch {
    return null;
  }
}

async function main() {
  if (!PASSWORD) {
    throw new Error("Thiếu biến môi trường DEMO_PASSWORD (mật khẩu tài khoản demo trên máy bạn).");
  }

  console.log("== 1. Kiểm tra API ==");
  await waitHealthy();

  console.log("\n== 2. Đăng nhập các vai trò ==");
  const customer = await login("customer@support.local");
  const customer2 = await login("customer2@support.local");
  const agent = await login("agent.tech@support.local");
  console.log("  customer:", customer.user.fullName, customer.user.role);
  console.log("  customer2:", customer2.user.fullName, customer2.user.role);
  console.log("  agent.tech:", agent.user.fullName, agent.user.role);

  const customerEvents = [];
  const customer2Events = [];
  const agentEvents = [];

  console.log("\n== 3. Kết nối SignalR ==");
  const connections = [];
  try {
    connections.push(await connect("customer", customer.token, customerEvents));
    connections.push(await connect("customer2", customer2.token, customer2Events));
    connections.push(await connect("agent.tech", agent.token, agentEvents));

    console.log("\n== 4. Khách hàng tạo ticket ==");
    const created = await request("/api/tickets", {
      method: "POST",
      token: customer.token,
      body: {
        title: "E2E: Không đăng nhập được sau khi đổi mật khẩu",
        description:
          "Kiểm thử end-to-end thật qua RabbitMQ, Assignment Worker và SignalR. " +
          "Yêu cầu thuộc danh mục tài khoản để kiểm tra phân công về nhóm Kỹ thuật.",
        category: "Account",
        priority: "High"
      }
    });
    console.log("  Đã tạo:", created.ticketNumber, "status=" + created.status, "id=" + created.id);
    assert(created.status === "New", "Ticket mới phải ở trạng thái New");
    assert(created.assignedTeam === null, "Ticket mới chưa có nhóm xử lý");
    assert(created.ticketNumber.startsWith("TK-"), "Mã ticket phải có tiền tố TK-");

    console.log("\n== 5. Chờ Assignment Worker phân công qua RabbitMQ ==");
    const customerAssigned = await waitFor(
      () => customerEvents.find((e) => e.eventName === "TicketAssigned" && e.ticketId === created.id),
      25000,
      "khách hàng nhận TicketAssigned"
    );
    const agentAssigned = await waitFor(
      () => agentEvents.find((e) => e.eventName === "TicketAssigned" && e.ticketId === created.id),
      15000,
      "nhân viên nhận TicketAssigned"
    );
    assert(customerAssigned.status === "Assigned", "Sự kiện phân công phải có status Assigned");
    assert(customerAssigned.teamName === "Kỹ thuật", "Ticket Account phải giao nhóm Kỹ thuật");
    assert(customerAssigned.assignedUserId, "Worker phải gán trưởng nhóm Kỹ thuật");
    assert(agentAssigned.assignedUserId === customerAssigned.assignedUserId, "Nhân viên được gán phải nhận thông báo");
    console.log("  Phân công:", customerAssigned.teamName, "/", customerAssigned.assignedUserName);

    await sleep(1800);
    const wrongCustomerEvents = customer2Events.filter((e) => e.ticketId === created.id);
    assert(wrongCustomerEvents.length === 0, "Khách hàng khác không được nhận thông báo của ticket này");
    console.log("  customer2 không nhận thông báo: OK");

    const detail = await request("/api/tickets/" + created.id, { token: agent.token });
    assert(detail.status === "Assigned", "Ticket phải ở trạng thái Assigned sau khi worker xử lý");
    assert(detail.assignedTeam.code === "TECH", "API phải trả nhóm TECH");
    assert(detail.canChangeStatus, "Nhân viên được giao phải có quyền đổi trạng thái");

    console.log("\n== 6. Nhân viên bắt đầu xử lý ==");
    const started = await request(`/api/tickets/${created.id}/status`, {
      method: "PATCH",
      token: agent.token,
      body: { status: "InProgress", note: "Nhân viên bắt đầu xử lý (E2E).", rowVersion: detail.rowVersion }
    });
    assert(started.status === "InProgress", "API phải trả InProgress");

    const customerStarted = await waitFor(
      () => customerEvents.find(
        (e) => e.eventName === "TicketStatusChanged" && e.ticketId === created.id && e.status === "InProgress"
      ),
      15000,
      "khách hàng nhận TicketStatusChanged InProgress"
    );
    assert(customerStarted.newStatus === "InProgress", "Sự kiện phải báo InProgress");
    console.log("  Khách hàng nhận cập nhật InProgress: OK");

    await sleep(1200);
    const selfNotifications = agentEvents.filter(
      (e) => e.eventName === "TicketStatusChanged" && e.ticketId === created.id
    );
    assert(selfNotifications.length === 0, "Người thực hiện thay đổi không nhận thông báo thừa");
    console.log("  Nhân viên thực hiện không nhận lại thông báo của chính mình: OK");

    console.log("\n== 7. Nhân viên hoàn thành yêu cầu ==");
    const resolved = await request(`/api/tickets/${created.id}/status`, {
      method: "PATCH",
      token: agent.token,
      body: { status: "Resolved", note: "Đã hướng dẫn khách hàng (E2E).", rowVersion: started.rowVersion }
    });
    assert(resolved.status === "Resolved", "API phải trả Resolved");
    assert(resolved.resolvedAt, "ResolvedAt phải có giá trị");

    const customerResolved = await waitFor(
      () => customerEvents.find(
        (e) => e.eventName === "TicketStatusChanged" && e.ticketId === created.id && e.status === "Resolved"
      ),
      15000,
      "khách hàng nhận TicketStatusChanged Resolved"
    );
    assert(customerResolved.newStatus === "Resolved", "Sự kiện phải báo Resolved");
    console.log("  Khách hàng nhận cập nhật Resolved: OK");

    console.log("\n== 8. Kiểm tra dữ liệu cuối cùng ==");
    const finalDetail = await request("/api/tickets/" + created.id, { token: customer.token });
    const history = await request(`/api/tickets/${created.id}/history`, { token: customer.token });
    assert(finalDetail.status === "Resolved", "Ticket cuối phải Resolved");
    assert(history.length >= 6, "Lịch sử phải có ít nhất 6 bản ghi, thực tế " + history.length);
    const fields = history.map((h) => `${h.fieldChanged}:${h.oldValue || "∅"}→${h.newValue || "∅"}`);
    console.log("  Lịch sử (" + history.length + "):");
    fields.forEach((f) => console.log("   -", f));

    const customerList = await request("/api/tickets?search=" + encodeURIComponent(created.ticketNumber), {
      token: customer.token
    });
    assert(customerList.items.some((t) => t.id === created.id), "Ticket phải có trong danh sách của khách hàng");

    const queues = await queueDepths();
    if (queues) {
      console.log("\n== 9. Hàng đợi RabbitMQ ==");
      queues.forEach((q) => console.log(`  ${q.name}: messages=${q.messages}, consumers=${q.consumers}, state=${q.state}`));
    }

    console.log("\nE2E PASSED: tạo ticket → RabbitMQ → Worker phân công → SignalR → InProgress → Resolved.");
  } finally {
    for (const connection of connections) {
      try {
        await connection.stop();
      } catch {
        // Bỏ qua lỗi đóng kết nối.
      }
    }
  }
}

main().catch((error) => {
  console.error("\nE2E FAILED:", error.message);
  if (error.stack) console.error(error.stack);
  process.exitCode = 1;
});
