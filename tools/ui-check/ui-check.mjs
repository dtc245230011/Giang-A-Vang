/*
 * Kiểm thử giao diện thật bằng Chromium:
 * đăng nhập 3 vai trò, tạo ticket, xem dashboard/quản trị, nhân viên cập nhật trạng thái,
 * khách hàng nhận cập nhật realtime, kiểm tra responsive và lưu ảnh chụp.
 */
import { chromium } from "playwright";
import path from "node:path";
import fs from "node:fs";

const BASE = process.env.BASE_URL || "http://localhost:5026";
const PASSWORD = process.env.DEMO_PASSWORD;
const SHOTS = path.resolve(process.cwd(), "../../docs/screenshots");

if (!PASSWORD) {
  throw new Error("Thiếu DEMO_PASSWORD.");
}
fs.mkdirSync(SHOTS, { recursive: true });

const errors = [];
function watch(page, label) {
  page.on("pageerror", (error) => errors.push(`${label} pageerror: ${error.message}`));
  page.on("console", (message) => {
    if (message.type() === "error") errors.push(`${label} console: ${message.text()}`);
  });
}

async function login(page, email) {
  await page.goto(BASE + "/login.html", { waitUntil: "networkidle" });
  await page.fill("#login-email", email);
  await page.fill("#login-password", PASSWORD);
  await Promise.all([
    page.waitForURL("**/index.html", { timeout: 15000 }),
    page.click("#login-submit")
  ]);
  await page.waitForSelector("#stat-grid .stat-card", { timeout: 15000 });
  await page.waitForSelector("[data-connection-status].is-connected", { timeout: 15000 });
}

async function expectText(page, selector, text, timeout = 20000) {
  await page.waitForFunction(
    ({ selector, text }) => {
      const node = document.querySelector(selector);
      return node && node.textContent.includes(text);
    },
    { selector, text },
    { timeout }
  );
}

const browser = await chromium.launch({ headless: true });
const customerContext = await browser.newContext({ viewport: { width: 1440, height: 960 } });
const adminContext = await browser.newContext({ viewport: { width: 1440, height: 960 } });
const agentContext = await browser.newContext({ viewport: { width: 1280, height: 900 } });

try {
  console.log("== 0. Đăng ký công khai, kiểm tra vai trò và đăng xuất ==");
  const registerContext = await browser.newContext({ viewport: { width: 1280, height: 900 } });
  const registerPage = await registerContext.newPage();
  watch(registerPage, "register");
  const registrationEmail = `ui_${Date.now()}@example.com`;
  await registerPage.goto(BASE + "/login.html", { waitUntil: "networkidle" });
  await registerPage.click("#tab-register");
  await registerPage.fill("#register-name", "Khách Hàng Đăng Ký UI");
  await registerPage.fill("#register-email", registrationEmail);
  await registerPage.fill("#register-password", PASSWORD);
  await registerPage.fill("#register-confirm", PASSWORD);
  await Promise.all([
    registerPage.waitForURL("**/index.html", { timeout: 15000 }),
    registerPage.click("#register-submit")
  ]);
  await registerPage.waitForSelector("#stat-grid .stat-card", { timeout: 15000 });
  const roleText = await registerPage.locator("#hero-role").textContent();
  if (!roleText.includes("Khách hàng")) throw new Error("Tài khoản đăng ký phải có vai trò Khách hàng.");
  await registerPage.screenshot({ path: path.join(SHOTS, "00-register-customer.png"), fullPage: true });
  await registerPage.click('.sidebar-footer button[aria-label="Đăng xuất"]');
  await registerPage.waitForURL("**/login.html", { timeout: 15000 });
  await registerContext.close();
  console.log("  đăng ký tạo đúng vai trò Khách hàng, đăng xuất quay về trang đăng nhập.");

  console.log("\n== 1. Khách hàng đăng nhập và xem dashboard ==");
  const customerPage = await customerContext.newPage();
  watch(customerPage, "customer");
  await login(customerPage, "customer@support.local");
  const statCards = await customerPage.locator("#stat-grid .stat-card").count();
  if (statCards !== 4) throw new Error("Dashboard phải có 4 thẻ số liệu, thực tế " + statCards);
  await customerPage.screenshot({ path: path.join(SHOTS, "01-dashboard-customer.png"), fullPage: true });
  console.log("  dashboard hiển thị 4 thẻ số liệu, SignalR đã kết nối.");

  console.log("\n== 2. Khách hàng tạo ticket qua giao diện ==");
  await customerPage.goto(BASE + "/new-ticket.html", { waitUntil: "networkidle" });
  await customerPage.waitForFunction(() => document.querySelectorAll("#category option").length > 1);
  const title = "Kiểm thử giao diện " + Date.now().toString().slice(-6);
  await customerPage.fill("#title", title);
  await customerPage.selectOption("#category", "Account");
  await customerPage.selectOption("#priority", "High");
  await customerPage.fill(
    "#description",
    "Yêu cầu được tạo hoàn toàn qua giao diện trình duyệt để kiểm tra luồng thật từ form đến API, RabbitMQ và SignalR."
  );
  await customerPage.click("#submit-button");
  await customerPage.waitForSelector("#success-panel:not(.d-none)", { timeout: 15000 });
  const ticketNumber = (await customerPage.textContent("#success-number")).trim();
  await customerPage.screenshot({ path: path.join(SHOTS, "02-ticket-created.png"), fullPage: true });
  console.log("  tạo thành công:", ticketNumber);

  await customerPage.click("#success-link");
  await customerPage.waitForURL("**/ticket.html?id=*");
  await customerPage.waitForSelector("#detail-view:not(.d-none)", { timeout: 15000 });
  const ticketUrl = customerPage.url();
  await expectText(customerPage, "#detail-badges", "Đã phân công", 25000);
  console.log("  chi tiết hiển thị trạng thái Đã phân công (worker đã xử lý).");
  await customerPage.screenshot({ path: path.join(SHOTS, "03-ticket-detail-assigned.png"), fullPage: true });

  console.log("\n== 3. Admin xem trang quản trị ==");
  const adminPage = await adminContext.newPage();
  watch(adminPage, "admin");
  await login(adminPage, "admin@support.local");
  await adminPage.goto(BASE + "/admin.html", { waitUntil: "networkidle" });
  await adminPage.waitForSelector("#user-rows tr", { timeout: 15000 });
  await adminPage.waitForFunction(() => {
    const text = document.querySelector("#user-rows")?.textContent || "";
    return text.includes("admin@support.local");
  });
  await adminPage.screenshot({ path: path.join(SHOTS, "04-admin-users.png"), fullPage: true });
  await adminPage.click("#tab-teams");
  await adminPage.waitForSelector("#team-cards .card-panel", { timeout: 15000 });
  const teamCards = await adminPage.locator("#team-cards .card-panel").count();
  if (teamCards < 4) throw new Error("Phải hiển thị ít nhất 4 nhóm xử lý.");

  const newTeamCode = "UI" + Date.now().toString().slice(-5);
  await adminPage.click("#add-team-button");
  await adminPage.waitForSelector("#team-modal.show", { timeout: 5000 });
  await adminPage.fill("#team-code", newTeamCode);
  await adminPage.fill("#team-name", "Nhóm kiểm thử giao diện " + newTeamCode);
  await adminPage.fill("#team-description", "Nhóm được tạo qua giao diện quản trị trong kiểm thử tự động.");
  await adminPage.click("#team-submit");
  await adminPage.waitForFunction(
    (code) => document.querySelector("#team-cards")?.textContent.includes(code),
    newTeamCode,
    { timeout: 15000 }
  );

  const newTeamCard = adminPage.locator("#team-cards .card-panel", { hasText: newTeamCode });
  await newTeamCard.locator("button:has-text('Thành viên')").click();
  await adminPage.waitForSelector("#members-modal.show", { timeout: 5000 });
  await adminPage.waitForSelector("#member-rows tr", { timeout: 10000 });
  await adminPage.screenshot({ path: path.join(SHOTS, "05b-admin-team-members.png"), fullPage: false });
  await adminPage.click("#members-modal .modal-footer button");
  await adminPage.waitForFunction(() => !document.querySelector("#members-modal")?.classList.contains("show"));
  await adminPage.screenshot({ path: path.join(SHOTS, "05-admin-teams.png"), fullPage: true });
  console.log("  admin thấy danh sách tài khoản, " + teamCards + " nhóm và tạo được nhóm " + newTeamCode + ".");

  console.log("\n== 4. Nhân viên bắt đầu xử lý ticket ==");
  const agentPage = await agentContext.newPage();
  watch(agentPage, "agent");
  await login(agentPage, "agent.tech@support.local");
  await agentPage.goto(ticketUrl, { waitUntil: "networkidle" });
  await agentPage.waitForSelector("#detail-view:not(.d-none)", { timeout: 15000 });
  await agentPage.click("button:has-text('Bắt đầu xử lý')");
  await agentPage.waitForSelector("#status-modal.show", { timeout: 5000 });
  await agentPage.fill("#status-note", "Nhân viên bắt đầu xử lý qua giao diện.");
  await agentPage.click("#status-confirm");
  await expectText(agentPage, "#detail-badges", "Đang xử lý", 15000);
  await agentPage.screenshot({ path: path.join(SHOTS, "06-agent-in-progress.png"), fullPage: true });
  console.log("  nhân viên chuyển ticket sang Đang xử lý.");

  console.log("\n== 5. Khách hàng nhận cập nhật realtime ==");
  await customerPage.waitForFunction(
    () => document.querySelector("#detail-badges")?.textContent.includes("Đang xử lý"),
    { timeout: 15000 }
  );
  await customerPage.waitForSelector(".app-toast", { timeout: 10000 });
  const toastText = await customerPage.locator(".app-toast").first().textContent();
  if (!toastText.includes(ticketNumber)) throw new Error("Toast không chứa mã ticket: " + toastText);
  const unread = await customerPage.locator("#notification-count").textContent();
  if (!unread || unread.trim() === "0") throw new Error("Chuông thông báo phải có số chưa đọc.");
  await customerPage.screenshot({ path: path.join(SHOTS, "07-customer-realtime.png"), fullPage: true });
  console.log("  khách hàng nhận toast + chuông thông báo, trạng thái tự cập nhật:", toastText.trim().slice(0, 80));

  console.log("\n== 6. Nhân viên hoàn thành, khách hàng thấy kết quả ==");
  await agentPage.click("button:has-text('Đánh dấu hoàn thành')");
  await agentPage.waitForSelector("#status-modal.show", { timeout: 5000 });
  await agentPage.fill("#status-note", "Đã hướng dẫn khách hàng qua giao diện.");
  await agentPage.click("#status-confirm");
  await expectText(agentPage, "#detail-badges", "Đã giải quyết", 15000);
  await customerPage.waitForFunction(
    () => document.querySelector("#detail-badges")?.textContent.includes("Đã giải quyết"),
    { timeout: 15000 }
  );
  await customerPage.waitForTimeout(500);
  const timelineItems = await customerPage.locator("#history-list .timeline-item").count();
  if (timelineItems < 6) throw new Error("Timeline phải có ít nhất 6 mục, thực tế " + timelineItems);
  await customerPage.screenshot({ path: path.join(SHOTS, "08-ticket-resolved-timeline.png"), fullPage: true });
  console.log("  timeline có " + timelineItems + " mục; khách hàng thấy trạng thái Đã giải quyết.");

  console.log("\n== 7. Tìm kiếm ticket trên giao diện danh sách ==");
  await customerPage.goto(BASE + "/tickets.html", { waitUntil: "networkidle" });
  await customerPage.waitForSelector("#ticket-rows tr", { timeout: 15000 });
  await customerPage.fill("#search", ticketNumber);
  await customerPage.waitForFunction(
    (number) => {
      const rows = document.querySelectorAll("#ticket-rows tr");
      return rows.length === 1 && rows[0].textContent.includes(number);
    },
    ticketNumber,
    { timeout: 15000 }
  );
  await customerPage.screenshot({ path: path.join(SHOTS, "11-ticket-search.png"), fullPage: true });
  console.log("  tìm kiếm theo mã " + ticketNumber + " trả đúng 1 kết quả.");

  console.log("\n== 8. Kiểm tra responsive trên điện thoại ==");
  const mobileContext = await browser.newContext({
    viewport: { width: 390, height: 844 },
    storageState: await customerContext.storageState()
  });
  const mobilePage = await mobileContext.newPage();
  watch(mobilePage, "mobile");
  await mobilePage.goto(BASE + "/index.html", { waitUntil: "networkidle" });
  await mobilePage.waitForSelector("#stat-grid .stat-card", { timeout: 15000 });
  await mobilePage.screenshot({ path: path.join(SHOTS, "09-mobile-dashboard.png"), fullPage: true });
  await mobilePage.click(".mobile-menu");
  await mobilePage.waitForSelector("#sidebar.show", { timeout: 5000 });
  await mobilePage.screenshot({ path: path.join(SHOTS, "10-mobile-sidebar.png"), fullPage: false });
  await mobileContext.close();
  console.log("  sidebar mobile mở/đóng được, bố cục 390px hiển thị đúng.");

  if (errors.length) {
    throw new Error("Có lỗi JavaScript trên trình duyệt:\n" + errors.join("\n"));
  }

  console.log("\nUI CHECK PASSED: đăng nhập, tạo ticket, dashboard, admin, realtime, trạng thái, responsive.");
} finally {
  await browser.close();
}
