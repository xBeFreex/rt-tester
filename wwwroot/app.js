const rowsEl = document.getElementById("rows");
const emptyEl = document.getElementById("empty");
const statusEl = document.getElementById("status");

const filterIds = ["severity", "category", "feedType", "agency", "sinceHours"];
filterIds.forEach(id => document.getElementById(id).addEventListener("change", load));

document.querySelectorAll(".quicknav button").forEach(btn => {
  btn.addEventListener("click", () => {
    if (btn.dataset.reset) {
      filterIds.forEach(id => { document.getElementById(id).value = ""; });
      document.getElementById("sinceHours").value = "24";
    } else if (btn.dataset.category) {
      document.getElementById("category").value = btn.dataset.category;
    }
    load();
  });
});

function buildQuery() {
  const params = new URLSearchParams();
  for (const id of filterIds) {
    const val = document.getElementById(id).value.trim();
    if (!val) continue;
    params.set(id, val);
  }
  return params.toString();
}

async function load() {
  try {
    const [findingsRes, healthRes] = await Promise.all([
      fetch(`/api/findings?${buildQuery()}`),
      fetch("/api/health"),
    ]);
    const findings = await findingsRes.json();
    const health = await healthRes.json();
    renderFindings(findings);
    renderStatus(health);
  } catch (e) {
    statusEl.textContent = "Error loading data: " + e;
  }
}

function renderStatus(health) {
  const run = health.lastRun;
  if (!run) {
    statusEl.textContent = "No check run yet";
    return;
  }
  const completed = run.completedAtUtc ? new Date(run.completedAtUtc).toLocaleTimeString() : "in progress";
  let fetchIssues = 0;
  try {
    fetchIssues = JSON.parse(run.feedFetchStatusJson || "[]").filter(f => !f.ok).length;
  } catch {}
  const issueText = fetchIssues > 0 ? ` — ${fetchIssues} feed(s) unreachable` : "";
  statusEl.textContent = `Last check: ${completed}, ${run.findingCount} findings${issueText}`;
}

function td(text) {
  const el = document.createElement("td");
  el.textContent = text ?? "";
  return el;
}

function badgeTd(severity) {
  const el = document.createElement("td");
  const span = document.createElement("span");
  span.className = `badge ${severity}`;
  span.textContent = severity;
  el.appendChild(span);
  return el;
}

function labeledPre(label, text) {
  const wrap = document.createElement("div");
  const strong = document.createElement("strong");
  strong.textContent = label;
  const pre = document.createElement("pre");
  pre.textContent = text ?? "(n/a)";
  wrap.appendChild(strong);
  wrap.appendChild(pre);
  return wrap;
}

function renderFindings(findings) {
  rowsEl.replaceChildren();
  emptyEl.style.display = findings.length === 0 ? "block" : "none";

  for (const f of findings) {
    const tr = document.createElement("tr");
    tr.className = "row";
    tr.appendChild(td(new Date(f.lastSeenUtc).toLocaleString()));
    tr.appendChild(badgeTd(f.severity));
    tr.appendChild(td(f.category));
    tr.appendChild(td(f.feedType));
    tr.appendChild(td(f.agencyId));
    tr.appendChild(td(f.entityKey));
    tr.appendChild(td(f.fieldName));
    tr.appendChild(td(f.likelyValidatorCode));

    const detailTr = document.createElement("tr");
    detailTr.className = "detail-row";
    detailTr.style.display = "none";
    const detailTd = document.createElement("td");
    detailTd.colSpan = 8;

    const explanation = document.createElement("p");
    explanation.textContent = f.explanation;
    detailTd.appendChild(explanation);

    const diffCols = document.createElement("div");
    diffCols.className = "diff-cols";
    diffCols.appendChild(labeledPre("Source", f.sourceValue ?? f.sourceEntityJson));
    diffCols.appendChild(labeledPre("Merged", f.mergedValue ?? f.mergedEntityJson));
    detailTd.appendChild(diffCols);

    detailTr.appendChild(detailTd);

    tr.addEventListener("click", () => {
      detailTr.style.display = detailTr.style.display === "none" ? "table-row" : "none";
    });

    rowsEl.appendChild(tr);
    rowsEl.appendChild(detailTr);
  }
}

load();
setInterval(load, 10000);
