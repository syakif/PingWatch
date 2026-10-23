/**
 * PingWatch Client Script
 * Handles asynchronous communication with ASP.NET Core REST API
 */

// Determine API base endpoint
const API_BASE = window.location.protocol.startsWith("http")
  ? window.location.origin
  : "http://localhost:5000";

// DOM Elements
const monitorsTbody = document.getElementById("monitorsTbody");
const addMonitorForm = document.getElementById("addMonitorForm");
const monitorNameInput = document.getElementById("monitorName");
const monitorUrlInput = document.getElementById("monitorUrl");
const btnRefresh = document.getElementById("btnRefresh");
const activeCountBadge = document.getElementById("activeCountBadge");
const stationStatus = document.getElementById("stationStatus");
const clockDisplay = document.getElementById("clockDisplay");

// Modal Elements
const historyModal = document.getElementById("historyModal");
const modalTitle = document.getElementById("modalTitle");
const modalSubtitle = document.getElementById("modalSubtitle");
const modalCount = document.getElementById("modalCount");
const historyTbody = document.getElementById("historyTbody");
const modalClose = document.getElementById("modalClose");

// Clock update
function updateClock() {
  const now = new Date();
  clockDisplay.textContent = now.toTimeString().split(" ")[0];
}
setInterval(updateClock, 1000);
updateClock();

/**
 * Fetch and render all monitored sites
 */
async function loadMonitors() {
  try {
    stationStatus.textContent = "QUERYING...";
    const response = await fetch(`${API_BASE}/api/monitors`);
    if (!response.ok) {
      throw new Error(`HTTP Error: ${response.status}`);
    }

    const data = await response.json();
    renderMonitors(data);
    stationStatus.textContent = "CONNECTED";
  } catch (error) {
    console.error("Failed to load monitors:", error);
    stationStatus.textContent = "DISCONNECTED";
    monitorsTbody.innerHTML = `
      <tr>
        <td colspan="8" class="empty-state" style="color: var(--accent-down);">
          <strong>FAILED TO COMMUNICATE WITH PINGWATCH API</strong><br>
          <small>Check if the ASP.NET Core service is running at ${API_BASE}</small>
        </td>
      </tr>
    `;
  }
}

/**
 * Render monitor list rows
 */
function renderMonitors(monitors) {
  activeCountBadge.textContent = `${monitors.length} TARGET${monitors.length === 1 ? "" : "S"}`;

  if (!monitors || monitors.length === 0) {
    monitorsTbody.innerHTML = `
      <tr>
        <td colspan="8" class="empty-state">
          No websites enrolled yet. Enroll your first URL using the form above.
        </td>
      </tr>
    `;
    return;
  }

  monitorsTbody.innerHTML = monitors.map(m => {
    let statusStamp = '<span class="stamp stamp-pending">PENDING</span>';
    if (m.isUp === true) {
      statusStamp = '<span class="stamp stamp-up">● UP</span>';
    } else if (m.isUp === false) {
      statusStamp = '<span class="stamp stamp-down">■ DOWN</span>';
    }

    const statusCode = m.lastStatusCode !== null ? `<code>${m.lastStatusCode}</code>` : "—";
    const latency = m.lastResponseTimeMs !== null ? `${m.lastResponseTimeMs} ms` : "—";
    const lastChecked = m.lastCheckedAt
      ? new Date(m.lastCheckedAt).toISOString().replace("T", " ").substring(0, 19)
      : "Awaiting run...";

    return `
      <tr>
        <td>#${m.id}</td>
        <td><strong>${escapeHtml(m.name)}</strong></td>
        <td>
          <a href="${escapeHtml(m.url)}" target="_blank" style="color: var(--text-primary); text-decoration: underline;">
            ${escapeHtml(m.url)}
          </a>
        </td>
        <td>${statusStamp}</td>
        <td>${statusCode}</td>
        <td>${latency}</td>
        <td><small>${lastChecked}</small></td>
        <td>
          <div class="action-group">
            <button class="btn-retro btn-retro-sm" onclick="triggerManualCheck(${m.id})">
              PING
            </button>
            <button class="btn-retro btn-retro-sm" onclick="openHistory(${m.id}, '${escapeHtml(m.name)}')">
              LOGS
            </button>
            <button class="btn-retro btn-retro-sm btn-danger" onclick="deleteMonitor(${m.id})">
              DEL
            </button>
          </div>
        </td>
      </tr>
    `;
  }).join("");
}

/**
 * Form Submission: Enroll new monitor
 */
addMonitorForm.addEventListener("submit", async (e) => {
  e.preventDefault();

  const name = monitorNameInput.value.trim();
  const url = monitorUrlInput.value.trim();

  if (!name || !url) return;

  try {
    const btnSubmit = document.getElementById("btnSubmit");
    btnSubmit.disabled = true;
    btnSubmit.textContent = "ENROLLING...";

    const response = await fetch(`${API_BASE}/api/monitors`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ name, url })
    });

    if (!response.ok) {
      const err = await response.json();
      throw new Error(err.message || "Failed to add target.");
    }

    addMonitorForm.reset();
    btnSubmit.textContent = "[ ENROLL TARGET ]";
    btnSubmit.disabled = false;

    // Refresh list (initial check starts automatically)
    await loadMonitors();
    setTimeout(loadMonitors, 1500);
  } catch (error) {
    alert(`Error: ${error.message}`);
    const btnSubmit = document.getElementById("btnSubmit");
    btnSubmit.textContent = "[ ENROLL TARGET ]";
    btnSubmit.disabled = false;
  }
});

/**
 * Trigger an on-demand check for a single monitor
 */
async function triggerManualCheck(id) {
  try {
    stationStatus.textContent = `PINGING #${id}...`;
    const response = await fetch(`${API_BASE}/api/monitors/${id}/check`, {
      method: "POST"
    });

    if (!response.ok) {
      throw new Error("Manual ping failed");
    }

    await loadMonitors();
  } catch (error) {
    alert(`Failed to ping monitor #${id}: ${error.message}`);
  }
}

/**
 * Open Check History Modal
 */
async function openHistory(id, name) {
  modalTitle.textContent = `TELEMETRY LOGS // ${name}`;
  modalSubtitle.textContent = `Target ID: #${id}`;
  modalCount.textContent = "Loading...";
  historyTbody.innerHTML = `<tr><td colspan="6" class="empty-state">Loading history...</td></tr>`;
  historyModal.style.display = "block";

  try {
    const response = await fetch(`${API_BASE}/api/monitors/${id}/checks?limit=50`);
    if (!response.ok) throw new Error("Could not retrieve check history.");

    const checks = await response.json();
    modalCount.textContent = `${checks.length} recent checks`;

    if (checks.length === 0) {
      historyTbody.innerHTML = `<tr><td colspan="6" class="empty-state">No check history recorded yet.</td></tr>`;
      return;
    }

    historyTbody.innerHTML = checks.map(c => {
      const stamp = c.isSuccess
        ? '<span class="stamp stamp-up">● UP</span>'
        : '<span class="stamp stamp-down">■ DOWN</span>';
      const timestamp = new Date(c.checkedAt).toISOString().replace("T", " ").substring(0, 19);
      const code = c.statusCode ? `<code>${c.statusCode}</code>` : "—";
      const errorMsg = c.errorMessage ? escapeHtml(c.errorMessage) : "—";

      return `
        <tr>
          <td>#${c.id}</td>
          <td><small>${timestamp}</small></td>
          <td>${code}</td>
          <td>${c.responseTimeMs} ms</td>
          <td>${stamp}</td>
          <td><small style="color: ${c.isSuccess ? 'inherit' : 'var(--accent-down)'}">${errorMsg}</small></td>
        </tr>
      `;
    }).join("");
  } catch (error) {
    historyTbody.innerHTML = `<tr><td colspan="6" class="empty-state" style="color: var(--accent-down);">${error.message}</td></tr>`;
  }
}

/**
 * Close modal
 */
modalClose.addEventListener("click", () => {
  historyModal.style.display = "none";
});

window.addEventListener("click", (e) => {
  if (e.target === historyModal) {
    historyModal.style.display = "none";
  }
});

/**
 * Delete a monitor
 */
async function deleteMonitor(id) {
  if (!confirm(`Are you sure you want to remove monitor #${id}? All history will be deleted.`)) {
    return;
  }

  try {
    const response = await fetch(`${API_BASE}/api/monitors/${id}`, {
      method: "DELETE"
    });

    if (!response.ok) {
      throw new Error("Failed to delete monitor.");
    }

    await loadMonitors();
  } catch (error) {
    alert(error.message);
  }
}

// Helper to escape HTML characters
function escapeHtml(str) {
  if (!str) return "";
  return str.replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;")
            .replace(/'/g, "&#039;");
}

// Refresh button listener
btnRefresh.addEventListener("click", loadMonitors);

// Auto-refresh watchlist every 15 seconds
setInterval(loadMonitors, 15000);

// Initial load
loadMonitors();
