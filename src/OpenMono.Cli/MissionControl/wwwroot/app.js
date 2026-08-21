const $ = (id) => document.getElementById(id);

const state = {
  discovery: null,
  sessions: [],
  activeSessionId: null,
  streaming: false,
  usage: { input: null, output: null, total: null },
};

const els = {
  connectionPill: $("connection-pill"),
  loginBtn: $("login-btn"),
  logoutBtn: $("logout-btn"),
  agentId: $("agent-id"),
  agentStatus: $("agent-status"),
  agentWorkspace: $("agent-workspace"),
  agentUptime: $("agent-uptime"),
  agentVersion: $("agent-version"),
  sessionList: $("session-list"),
  emptyState: $("empty-state"),
  chatShell: $("chat-shell"),
  activeSessionTitle: $("active-session-title"),
  activeSessionMeta: $("active-session-meta"),
  messages: $("messages"),
  composer: $("composer"),
  promptInput: $("prompt-input"),
  sendBtn: $("send-btn"),
  turnStatus: $("turn-status"),
  usageInput: $("usage-input"),
  usageOutput: $("usage-output"),
  usageTotal: $("usage-total"),
  activityLog: $("activity-log"),
  permissionDialog: $("permission-dialog"),
  permissionTool: $("permission-tool"),
  permissionSummary: $("permission-summary"),
  permissionDanger: $("permission-danger"),
  inputDialog: $("input-dialog"),
  inputQuestion: $("input-question"),
  inputAnswer: $("input-answer"),
};

function formatUptime(seconds) {
  const h = Math.floor(seconds / 3600);
  const m = Math.floor((seconds % 3600) / 60);
  const s = seconds % 60;
  if (h > 0) return `${h}h ${m}m`;
  if (m > 0) return `${m}m ${s}s`;
  return `${s}s`;
}

function formatTime(iso) {
  if (!iso) return "—";
  return new Date(iso).toLocaleString();
}

function logActivity(label, detail) {
  const li = document.createElement("li");
  li.innerHTML = `<strong>${escapeHtml(label)}</strong> ${escapeHtml(detail)}`;
  els.activityLog.prepend(li);
  while (els.activityLog.children.length > 40) {
    els.activityLog.lastElementChild?.remove();
  }
}

function escapeHtml(text) {
  return String(text ?? "")
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

function setConnection(ok, label) {
  els.connectionPill.textContent = label;
  els.connectionPill.className = `pill ${ok ? "pill-ok" : "pill-muted"}`;
}

function renderDiscovery() {
  const d = state.discovery;
  if (!d) return;
  els.agentStatus.textContent = d.status ?? "—";
  els.agentWorkspace.textContent = d.host_workspace ?? "—";
  els.agentUptime.textContent = formatUptime(d.uptime_seconds ?? 0);
  els.agentVersion.textContent = d.version ?? "—";
  els.agentId.textContent = d.agent_id ?? "";
  renderAuthControls();
}

function renderUsage() {
  els.usageInput.textContent = state.usage.input ?? "—";
  els.usageOutput.textContent = state.usage.output ?? "—";
  els.usageTotal.textContent = state.usage.total ?? "—";
}

function renderSessions() {
  els.sessionList.replaceChildren();
  if (state.sessions.length === 0) {
    const empty = document.createElement("li");
    empty.className = "muted";
    empty.textContent = "No sessions yet.";
    els.sessionList.append(empty);
    return;
  }

  for (const session of state.sessions) {
    const btn = document.createElement("button");
    btn.type = "button";
    btn.className = "session-item";
    if (session.session_id === state.activeSessionId) btn.classList.add("active");

    const busy = session.busy ? " · busy" : "";
    const pending = session.pending_count > 0 ? ` · ${session.pending_count} pending` : "";
    btn.innerHTML = `
      <div class="session-item-title">
        <span>${escapeHtml(session.session_id)}</span>
        <span>${session.turn_count} turns</span>
      </div>
      <div class="session-item-meta">
        ${escapeHtml(session.model)}${busy}${pending}
      </div>`;
    btn.addEventListener("click", () => selectSession(session.session_id));
    els.sessionList.append(btn);
  }
}

function renderToolCalls(toolCalls) {
  if (!toolCalls?.length) return "";
  const items = toolCalls
    .map(
      (call) => `
        <div class="tool-call ${call.ok ? "ok" : "bad"}">
          <div>${escapeHtml(call.name)} · ${escapeHtml(call.summary)}</div>
          ${call.preview ? `<div class="muted">${escapeHtml(call.preview)}</div>` : ""}
        </div>`
    )
    .join("");
  return `<div class="tool-calls">${items}</div>`;
}

function renderMessages(messages) {
  els.messages.replaceChildren();
  for (const msg of messages) {
    const div = document.createElement("article");
    div.className = `message ${msg.role}`;
    div.innerHTML = escapeHtml(msg.content || "") + renderToolCalls(msg.toolCalls);
    els.messages.append(div);
  }
  els.messages.scrollTop = els.messages.scrollHeight;
}

function setStreaming(active, label = "") {
  state.streaming = active;
  els.sendBtn.disabled = active;
  els.promptInput.disabled = active;
  els.turnStatus.textContent = label;
  setConnection(true, active ? "Turn in progress" : "Connected");
  els.connectionPill.className = `pill ${active ? "pill-busy" : "pill-ok"}`;
}

async function api(path, options = {}) {
  const res = await fetch(path, options);
  if (res.status === 401) {
    const payload = await res.json().catch(() => ({}));
    if (payload.login_url) {
      window.location.href = payload.login_url;
      return null;
    }
  }
  if (!res.ok) {
    const text = await res.text();
    throw new Error(`${res.status} ${text}`);
  }
  if (res.status === 204) return null;
  const contentType = res.headers.get("content-type") ?? "";
  if (contentType.includes("application/json")) return res.json();
  return res.text();
}

function renderAuthControls() {
  const auth = state.discovery?.auth;
  const enabled = !!auth?.enabled;
  els.loginBtn.classList.toggle("hidden", !enabled);
  els.logoutBtn.classList.toggle("hidden", !enabled);
  if (enabled) {
    els.loginBtn.href = auth.login_url ?? "/auth/login";
  }
}

async function refreshDiscovery() {
  state.discovery = await api("/api/v1/discovery");
  renderDiscovery();
  setConnection(true, state.streaming ? "Turn in progress" : "Connected");
}

async function refreshSessions() {
  const data = await api("/api/v1/sessions");
  state.sessions = data.sessions ?? [];
  renderSessions();
}

async function loadSessionMessages(sessionId) {
  const data = await api(`/api/v1/sessions/${sessionId}/messages`);
  renderMessages(data.messages ?? []);
}

async function loadSessionMeta(sessionId) {
  const session = await api(`/api/v1/sessions/${sessionId}`);
  els.activeSessionTitle.textContent = session.session_id;
  els.activeSessionMeta.textContent = `${session.model} · started ${formatTime(session.started_at)} · ${session.turn_count} turns`;
}

async function selectSession(sessionId) {
  state.activeSessionId = sessionId;
  els.emptyState.classList.add("hidden");
  els.chatShell.classList.remove("hidden");
  renderSessions();
  await Promise.all([loadSessionMeta(sessionId), loadSessionMessages(sessionId)]);
}

async function createSession() {
  const data = await api("/api/v1/sessions", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({}),
  });
  logActivity("Session created", data.session_id);
  await refreshSessions();
  await selectSession(data.session_id);
}

async function deleteActiveSession() {
  if (!state.activeSessionId) return;
  if (!confirm("Delete this session?")) return;
  await api(`/api/v1/sessions/${state.activeSessionId}`, { method: "DELETE" });
  logActivity("Session deleted", state.activeSessionId);
  state.activeSessionId = null;
  els.chatShell.classList.add("hidden");
  els.emptyState.classList.remove("hidden");
  await refreshSessions();
}

async function abortTurn() {
  if (!state.activeSessionId) return;
  await api(`/api/v1/sessions/${state.activeSessionId}/turn`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ abort: true }),
  });
  logActivity("Turn aborted", state.activeSessionId);
  setStreaming(false);
  await refreshSessions();
}

function appendStreamingAssistant() {
  const div = document.createElement("article");
  div.className = "message assistant streaming";
  div.dataset.streaming = "true";
  div.textContent = "";
  els.messages.append(div);
  els.messages.scrollTop = els.messages.scrollHeight;
  return div;
}

function appendThinking(text) {
  const div = document.createElement("article");
  div.className = "message system";
  div.textContent = text;
  els.messages.append(div);
}

function appendToolEvent(kind, payload) {
  const div = document.createElement("article");
  div.className = "message system";
  if (kind === "start") {
    div.textContent = `▶ ${payload.name}: ${payload.summary}`;
  } else {
    const status = payload.ok ? "done" : "failed";
    div.textContent = `■ ${payload.name} ${status} (${Math.round(payload.duration_ms)}ms)`;
  }
  els.messages.append(div);
  els.messages.scrollTop = els.messages.scrollHeight;
  logActivity(kind === "start" ? "Tool start" : "Tool end", payload.name);
}

async function askPermission(payload) {
  els.permissionTool.textContent = payload.tool;
  els.permissionSummary.textContent = payload.summary;
  els.permissionDanger.classList.toggle("hidden", !payload.dangerous);
  const result = await new Promise((resolve) => {
    const form = els.permissionDialog.querySelector("form");
    const onClose = () => {
      els.permissionDialog.removeEventListener("close", onClose);
      resolve(els.permissionDialog.returnValue === "allow");
    };
    els.permissionDialog.addEventListener("close", onClose);
    els.permissionDialog.showModal();
  });
  return result;
}

async function askUserInput(payload) {
  els.inputQuestion.textContent = payload.question;
  els.inputAnswer.value = "";
  const result = await new Promise((resolve) => {
    const onClose = () => {
      els.inputDialog.removeEventListener("close", onClose);
      resolve(
        els.inputDialog.returnValue === "submit" ? els.inputAnswer.value : null
      );
    };
    els.inputDialog.addEventListener("close", onClose);
    els.inputDialog.showModal();
  });
  return result;
}

async function consumeSse(response, handlers) {
  const reader = response.body.getReader();
  const decoder = new TextDecoder();
  let buffer = "";

  while (true) {
    const { value, done } = await reader.read();
    if (done) break;
    buffer += decoder.decode(value, { stream: true });

    let splitAt;
    while ((splitAt = buffer.indexOf("\n\n")) >= 0) {
      const block = buffer.slice(0, splitAt);
      buffer = buffer.slice(splitAt + 2);
      const event = parseSseBlock(block);
      if (event) await handlers(event);
    }
  }
}

function parseSseBlock(block) {
  let eventName = "message";
  const dataLines = [];
  for (const line of block.split("\n")) {
    if (line.startsWith("event:")) eventName = line.slice(6).trim();
    else if (line.startsWith("data:")) dataLines.push(line.slice(5).trim());
  }
  if (dataLines.length === 0) return null;
  try {
    return { event: eventName, data: JSON.parse(dataLines.join("\n")) };
  } catch {
    return { event: eventName, data: dataLines.join("\n") };
  }
}

async function postTurn(body) {
  const response = await fetch(`/api/v1/sessions/${state.activeSessionId}/turn`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      Accept: "text/event-stream",
    },
    body: JSON.stringify(body),
  });

  if (!response.ok) {
    const text = await response.text();
    throw new Error(`${response.status} ${text}`);
  }

  if (response.status === 204) return;

  let assistantEl = null;
  await consumeSse(response, async ({ event, data }) => {
    switch (event) {
      case "text_delta":
        if (!assistantEl) assistantEl = appendStreamingAssistant();
        assistantEl.textContent += data.content ?? "";
        els.messages.scrollTop = els.messages.scrollHeight;
        break;
      case "thinking_delta":
        appendThinking(data.content ?? "");
        break;
      case "tool_start":
        appendToolEvent("start", data);
        break;
      case "tool_end":
        appendToolEvent("end", data);
        break;
      case "tool_result_preview":
        logActivity("Tool preview", data.preview ?? "");
        break;
      case "usage":
        state.usage = {
          input: data.input_tokens,
          output: data.output_tokens,
          total: data.total_tokens,
        };
        renderUsage();
        break;
      case "compaction":
        logActivity("Compaction", `${data.messages_compressed} messages`);
        break;
      case "permission_request": {
        setStreaming(true, "Waiting for permission…");
        const allow = await askPermission(data);
        await postTurn({
          permission: { id: data.id, decision: allow ? "allow" : "deny" },
        });
        break;
      }
      case "user_input_request": {
        setStreaming(true, "Waiting for input…");
        const answer = await askUserInput(data);
        if (answer === null) {
          await abortTurn();
        } else {
          await postTurn({ user_input: { id: data.id, value: answer } });
        }
        break;
      }
      case "error":
        appendThinking(`Error: ${data.message ?? "unknown"}`);
        break;
      case "done":
        assistantEl?.classList.remove("streaming");
        break;
      default:
        break;
    }
  });
}

async function sendMessage(text) {
  if (!state.activeSessionId || !text.trim()) return;
  const userDiv = document.createElement("article");
  userDiv.className = "message user";
  userDiv.textContent = text.trim();
  els.messages.append(userDiv);
  els.messages.scrollTop = els.messages.scrollHeight;

  setStreaming(true, "Agent working…");
  try {
    await postTurn({ message: text.trim() });
    await Promise.all([refreshSessions(), loadSessionMessages(state.activeSessionId)]);
  } catch (err) {
    appendThinking(`Error: ${err.message}`);
    logActivity("Turn failed", err.message);
  } finally {
    setStreaming(false);
  }
}

els.composer.addEventListener("submit", async (event) => {
  event.preventDefault();
  const text = els.promptInput.value;
  els.promptInput.value = "";
  await sendMessage(text);
});

$("refresh-btn").addEventListener("click", async () => {
  try {
    await Promise.all([refreshDiscovery(), refreshSessions()]);
    if (state.activeSessionId) {
      await loadSessionMeta(state.activeSessionId);
      await loadSessionMessages(state.activeSessionId);
    }
  } catch (err) {
    setConnection(false, "Offline");
    logActivity("Refresh failed", err.message);
  }
});

$("new-session-btn").addEventListener("click", () => createSession().catch(showError));
$("delete-session-btn").addEventListener("click", () => deleteActiveSession().catch(showError));
$("abort-btn").addEventListener("click", () => abortTurn().catch(showError));
els.logoutBtn.addEventListener("click", () => {
  window.location.href = "/auth/logout";
});

function showError(err) {
  logActivity("Error", err.message);
  appendThinking(`Error: ${err.message}`);
}

async function boot() {
  try {
    await refreshDiscovery();
    await refreshSessions();
    setConnection(true, "Connected");
    logActivity("Mission Control", "Connected to agent");
  } catch (err) {
    setConnection(false, "Offline");
    els.agentStatus.textContent = "unreachable";
    logActivity("Boot failed", err.message);
  }
}

boot();
setInterval(() => refreshSessions().catch(() => {}), 5000);
