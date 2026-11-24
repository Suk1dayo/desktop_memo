(function () {
  const state = {
    token: localStorage.getItem("dm_token") || "",
    tasks: [],
    stickyNotes: [],
    ai: {},
    activeTab: "quadrants",
    editingTaskId: null,
    extractedTasks: [],
    extractImages: [],
    modalAttachments: [],
    modalNewUploads: [],
    statusTimer: null
  };

  const tabTitles = {
    quadrants: "四象限矩阵",
    schedule: "日程 & DDL",
    extract: "微信聊天 AI 提取",
    notes: "独立便签",
    settings: "AI 与设置"
  };

  const qNames = {
    0: "未分类",
    1: "Q1 重要且紧急",
    2: "Q2 重要不紧急",
    3: "Q3 紧急不重要",
    4: "Q4 不重要不紧急"
  };

  const el = (id) => document.getElementById(id);

  function attachmentUrl(fileName) {
    return "/api/attachments/" + encodeURIComponent(fileName) + "?token=" + encodeURIComponent(state.token || "");
  }

  function showWebImagePreview(titleText, srcUrl) {
    el("imgPreviewTitle").textContent = "🖼 " + (titleText || "图片预览");
    el("imgPreviewEl").src = srcUrl;
    el("imgPreviewModal").classList.remove("hidden");
  }

  function compressImageFileToDataUrl(file) {
    return new Promise((resolve, reject) => {
      const reader = new FileReader();
      reader.onerror = () => reject(new Error("读取图片失败"));
      reader.onload = () => {
        const img = new Image();
        img.onerror = () => reject(new Error("无法解码该图片"));
        img.onload = () => {
          const maxEdge = 1600;
          let w = img.naturalWidth || img.width;
          let h = img.naturalHeight || img.height;
          if (w > maxEdge || h > maxEdge) {
            const ratio = maxEdge / Math.max(w, h);
            w = Math.max(1, Math.round(w * ratio));
            h = Math.max(1, Math.round(h * ratio));
          }
          const canvas = document.createElement("canvas");
          canvas.width = w;
          canvas.height = h;
          const ctx = canvas.getContext("2d");
          ctx.fillStyle = "#ffffff";
          ctx.fillRect(0, 0, w, h);
          ctx.drawImage(img, 0, 0, w, h);
          resolve(canvas.toDataURL("image/jpeg", 0.85));
        };
        img.src = reader.result;
      };
      reader.readAsDataURL(file);
    });
  }

  function renderExtractImageStrip() {
    const strip = el("extractImgStrip");
    strip.innerHTML = "";
    if (!state.extractImages || state.extractImages.length === 0) {
      strip.classList.add("hidden");
      return;
    }
    strip.classList.remove("hidden");
    state.extractImages.forEach((item, idx) => {
      const box = document.createElement("div");
      box.className = "att-thumb";
      const img = document.createElement("img");
      img.src = item.dataUrl;
      img.title = item.name || "点击预览";
      img.onclick = () => showWebImagePreview(item.name || "待识别截图", item.dataUrl);
      const del = document.createElement("button");
      del.type = "button";
      del.className = "att-del";
      del.textContent = "✕";
      del.onclick = () => {
        state.extractImages.splice(idx, 1);
        renderExtractImageStrip();
      };
      box.appendChild(img);
      box.appendChild(del);
      strip.appendChild(box);
    });
  }

  function renderModalAttachments() {
    const strip = el("mAttStrip");
    strip.innerHTML = "";
    state.modalAttachments.forEach((fname, idx) => {
      const url = attachmentUrl(fname);
      const box = document.createElement("div");
      box.className = "att-thumb";
      const img = document.createElement("img");
      img.src = url;
      img.title = fname;
      img.onclick = () => showWebImagePreview(fname, url);
      const del = document.createElement("button");
      del.type = "button";
      del.className = "att-del";
      del.textContent = "✕";
      del.onclick = () => {
        state.modalAttachments.splice(idx, 1);
        renderModalAttachments();
      };
      box.appendChild(img);
      box.appendChild(del);
      strip.appendChild(box);
    });
    state.modalNewUploads.forEach((item, idx) => {
      const box = document.createElement("div");
      box.className = "att-thumb";
      const img = document.createElement("img");
      img.src = item.dataUrl;
      img.title = item.name || "待上传图片";
      img.onclick = () => showWebImagePreview(item.name || "待上传图片", item.dataUrl);
      const del = document.createElement("button");
      del.type = "button";
      del.className = "att-del";
      del.textContent = "✕";
      del.onclick = () => {
        state.modalNewUploads.splice(idx, 1);
        renderModalAttachments();
      };
      box.appendChild(img);
      box.appendChild(del);
      strip.appendChild(box);
    });
  }

  function showBanner(msg, isErr) {
    const b = el("statusToast");
    if (state.statusTimer) clearTimeout(state.statusTimer);
    b.classList.remove("with-undo");
    b.textContent = msg;
    b.style.borderColor = isErr ? "#EF4444" : "var(--border)";
    b.style.color = isErr ? "#DC2626" : "var(--text)";
    b.classList.remove("hidden");
    state.statusTimer = setTimeout(() => b.classList.add("hidden"), 4500);
  }

  function showUndoBanner(taskSnapshot) {
    const b = el("statusToast");
    if (state.statusTimer) clearTimeout(state.statusTimer);
    b.innerHTML = "";
    b.style.borderColor = "var(--border)";
    b.style.color = "var(--text)";
    b.classList.add("with-undo");

    const title = (taskSnapshot.Title || "未命名日程").trim();
    const shortTitle = title.length > 18 ? title.slice(0, 18) + "…" : title;

    const text = document.createElement("span");
    text.textContent = "已删除「" + shortTitle + "」";
    b.appendChild(text);

    const actions = document.createElement("div");
    actions.className = "status-undo-actions";

    const undoBtn = document.createElement("button");
    undoBtn.type = "button";
    undoBtn.className = "btn-accent btn-undo";
    undoBtn.textContent = "↶ 撤回";
    undoBtn.onclick = async () => {
      undoBtn.disabled = true;
      if (state.statusTimer) clearTimeout(state.statusTimer);
      try {
        await api("/api/tasks", {
          method: "POST",
          body: JSON.stringify({
            title: taskSnapshot.Title || "",
            notes: taskSnapshot.Notes || "",
            quadrant: taskSnapshot.Quadrant || 0,
            noteId: taskSnapshot.NoteId || "",
            deadlineIso: taskSnapshot.DeadlineIso || "",
            remindAtIso: taskSnapshot.RemindAtIso || ""
          })
        });
        b.classList.add("hidden");
        b.classList.remove("with-undo");
        await loadState();
        showBanner("✓ 已撤回删除「" + shortTitle + "」");
      } catch (err) {
        undoBtn.disabled = false;
        showBanner("⚠ 撤回失败：" + err.message, true);
      }
    };
    actions.appendChild(undoBtn);

    const closeBtn = document.createElement("button");
    closeBtn.type = "button";
    closeBtn.className = "icon-btn";
    closeBtn.textContent = "✕";
    closeBtn.title = "关闭";
    closeBtn.onclick = () => {
      if (state.statusTimer) clearTimeout(state.statusTimer);
      b.classList.add("hidden");
      b.classList.remove("with-undo");
    };
    actions.appendChild(closeBtn);

    b.appendChild(actions);
    b.classList.remove("hidden");
    state.statusTimer = setTimeout(() => {
      b.classList.add("hidden");
      b.classList.remove("with-undo");
    }, 6000);
  }

  async function api(path, opts) {
    const headers = Object.assign({ "Content-Type": "application/json" }, (opts && opts.headers) || {});
    if (state.token) headers["Authorization"] = "Bearer " + state.token;
    const res = await fetch(path, Object.assign({}, opts || {}, { headers }));
    if (res.status === 401) {
      state.token = "";
      localStorage.removeItem("dm_token");
      showLogin();
      throw new Error("未登录或口令已失效");
    }
    const data = await res.json().catch(() => ({}));
    if (!res.ok) throw new Error(data.error || ("HTTP " + res.status));
    return data;
  }

  function showLogin() {
    el("loginView").classList.remove("hidden");
    el("appShell").classList.add("hidden");
    el("loginPassword").focus();
  }

  function showApp() {
    el("loginView").classList.add("hidden");
    el("appShell").classList.remove("hidden");
  }

  function formatDdlBadge(ddlIso, completed) {
    if (!ddlIso) return null;
    const target = new Date(ddlIso.replace(" ", "T"));
    if (isNaN(target.getTime())) return null;
    const now = new Date();
    const diffMs = target - now;
    const label = ddlIso.slice(5, 16);
    if (completed) return { text: "已完成 · " + label, cls: "normal" };
    if (diffMs < 0) {
      const mins = Math.max(1, Math.ceil(-diffMs / 60000));
      const txt = mins < 60 ? `已逾期 ${mins}分钟` : (mins < 1440 ? `已逾期 ${Math.floor(mins / 60)}小时` : `已逾期 ${Math.floor(mins / 1440)}天`);
      return { text: `${txt} (${label})`, cls: "urgent" };
    }
    const hrs = diffMs / 3600000;
    if (hrs < 3) return { text: `剩 ${Math.max(1, Math.ceil(diffMs / 60000))}分 (${label})`, cls: "urgent" };
    if (hrs < 24) return { text: `剩 ${Math.floor(hrs)}小时 (${label})`, cls: "soon" };
    return { text: `${label} (剩${Math.ceil(hrs / 24)}天)`, cls: "normal" };
  }

  function sortTasks(list) {
    const mode = el("sortSelect").value || "ai";
    return list.slice().sort((a, b) => {
      if (a.Completed !== b.Completed) return a.Completed ? 1 : -1;
      if (mode === "ai" && a.AiScore !== b.AiScore) return (b.AiScore || 0) - (a.AiScore || 0);
      if ((mode === "ai" || mode === "quadrant") && a.Quadrant !== b.Quadrant) {
        const qa = a.Quadrant === 0 ? 9 : a.Quadrant;
        const qb = b.Quadrant === 0 ? 9 : b.Quadrant;
        return qa - qb;
      }
      const da = a.DeadlineIso ? new Date(a.DeadlineIso.replace(" ", "T")).getTime() : Number.MAX_SAFE_INTEGER;
      const db = b.DeadlineIso ? new Date(b.DeadlineIso.replace(" ", "T")).getTime() : Number.MAX_SAFE_INTEGER;
      return da - db;
    });
  }

  function createTaskCard(t, showQuadPill) {
    const card = document.createElement("div");
    card.className = "task-card" + (t.Completed ? " done" : "");

    const cb = document.createElement("input");
    cb.type = "checkbox";
    cb.className = "task-check";
    cb.checked = !!t.Completed;
    cb.onchange = async () => {
      await api("/api/tasks/" + t.Id, { method: "PUT", body: JSON.stringify({ completed: cb.checked }) });
      await loadState();
    };
    card.appendChild(cb);

    const main = document.createElement("div");
    main.className = "task-main";

    const title = document.createElement("div");
    title.className = "task-title";
    title.textContent = t.Title;
    main.appendChild(title);

    if (t.Notes) {
      const notes = document.createElement("div");
      notes.className = "task-notes";
      notes.textContent = t.Notes;
      main.appendChild(notes);
    }

    const badges = document.createElement("div");
    badges.className = "badge-row";
    if (showQuadPill) {
      const qp = document.createElement("span");
      qp.className = "pill";
      qp.textContent = qNames[t.Quadrant] || "未分类";
      badges.appendChild(qp);
    }
    const ddl = formatDdlBadge(t.DeadlineIso, t.Completed);
    if (ddl) {
      const dp = document.createElement("span");
      dp.className = "pill " + ddl.cls;
      dp.textContent = "DDL: " + ddl.text;
      badges.appendChild(dp);
    }
    if (t.AiScore > 0) {
      const ap = document.createElement("span");
      ap.className = "pill ai";
      ap.textContent = `AI ${t.AiScore}分` + (t.AiReason ? ` · ${t.AiReason}` : "");
      badges.appendChild(ap);
    }
    if (Array.isArray(t.AttachmentFiles) && t.AttachmentFiles.length > 0) {
      const attBtn = document.createElement("button");
      attBtn.type = "button";
      attBtn.className = "pill att-pill";
      attBtn.textContent = `🖼 ${t.AttachmentFiles.length}`;
      attBtn.title = "点击查看附件图片（编辑弹窗可查看全部）";
      attBtn.onclick = (ev) => {
        ev.stopPropagation();
        const first = t.AttachmentFiles[0];
        showWebImagePreview(first, attachmentUrl(first));
      };
      badges.appendChild(attBtn);
    }
    if (badges.children.length > 0) main.appendChild(badges);
    card.appendChild(main);

    const actions = document.createElement("div");
    actions.className = "task-actions";

    const editBtn = document.createElement("button");
    editBtn.className = "icon-btn";
    editBtn.textContent = "✎";
    editBtn.title = "编辑";
    editBtn.onclick = () => openTaskModal(t);
    actions.appendChild(editBtn);

    const delBtn = document.createElement("button");
    delBtn.className = "icon-btn";
    delBtn.textContent = "×";
    delBtn.title = "删除";
    delBtn.onclick = async () => {
      const titleText = (t.Title || "未命名日程").trim();
      if (!window.confirm("确定要删除日程「" + titleText + "」吗？")) return;
      const snapshot = {
        Title: t.Title || "",
        Notes: t.Notes || "",
        Quadrant: t.Quadrant || 0,
        NoteId: t.NoteId || "",
        DeadlineIso: t.DeadlineIso || "",
        RemindAtIso: t.RemindAtIso || ""
      };
      try {
        await api("/api/tasks/" + t.Id, { method: "DELETE" });
        await loadState();
        showUndoBanner(snapshot);
      } catch (err) {
        showBanner("⚠ 删除失败：" + err.message, true);
      }
    };
    actions.appendChild(delBtn);

    card.appendChild(actions);
    return card;
  }

  function renderAll() {
    const pending = state.tasks.filter((t) => !t.Completed).length;
    el("summaryMeta").textContent = `${pending} 条待办 · ${state.stickyNotes.length} 张便签`;

    // Quadrants
    const quadGrid = el("quadGrid");
    quadGrid.innerHTML = "";
    for (let q = 1; q <= 4; q++) {
      const box = document.createElement("div");
      box.className = "quad-card";
      const qTasks = sortTasks(state.tasks.filter((t) => !t.Completed && t.Quadrant === q));
      const head = document.createElement("div");
      head.className = "quad-head";
      head.innerHTML = `<span class="quad-title q${q}-dot">${qNames[q]} (${qTasks.length})</span>`;
      const addQ = document.createElement("button");
      addQ.className = "icon-btn";
      addQ.textContent = "+";
      addQ.onclick = () => openTaskModal({ Quadrant: q });
      head.appendChild(addQ);
      box.appendChild(head);

      const list = document.createElement("div");
      list.className = "task-stack";
      if (qTasks.length === 0) {
        const empty = document.createElement("div");
        empty.className = "muted";
        empty.textContent = "暂无此象限待办";
        list.appendChild(empty);
      } else {
        qTasks.forEach((t) => list.appendChild(createTaskCard(t, false)));
      }
      box.appendChild(list);
      quadGrid.appendChild(box);
    }

    // Schedule list
    const kw = (el("searchInput").value || "").trim().toLowerCase();
    const fVal = el("filterSelect").value;
    let filtered = state.tasks.filter((t) => {
      if (fVal === "pending" && t.Completed) return false;
      if (fVal === "completed" && !t.Completed) return false;
      if (fVal === "unclassified" && (t.Completed || t.Quadrant !== 0)) return false;
      if (fVal === "urgent") {
        const d = formatDdlBadge(t.DeadlineIso, t.Completed);
        if (t.Completed || !d || (d.cls !== "urgent" && d.cls !== "soon")) return false;
      }
      if (kw) {
        const hay = ((t.Title || "") + " " + (t.Notes || "") + " " + (t.AiReason || "")).toLowerCase();
        if (!hay.includes(kw)) return false;
      }
      return true;
    });
    filtered = sortTasks(filtered);
    const sList = el("scheduleList");
    sList.innerHTML = "";
    if (filtered.length === 0) {
      sList.innerHTML = `<div class="muted">当前没有匹配的日程。</div>`;
    } else {
      filtered.forEach((t) => sList.appendChild(createTaskCard(t, true)));
    }

    // Sticky Notes
    const nGrid = el("notesGrid");
    nGrid.innerHTML = "";
    state.stickyNotes.forEach((n) => {
      const nc = document.createElement("div");
      nc.className = "note-card";
      const noteTasks = state.tasks.filter((t) => t.NoteId === n.Id);
      nc.innerHTML = `
        <div class="quad-head">
          <strong>${escapeHtml(n.Title || "便签")}</strong>
          <button class="icon-btn" data-del-note="${n.Id}">删除</button>
        </div>
        ${n.ShowScratchpad && n.Content ? `<div class="muted">${escapeHtml(n.Content)}</div>` : ""}
      `;
      const stack = document.createElement("div");
      stack.className = "task-stack";
      if (noteTasks.length === 0) {
        stack.innerHTML = `<span class="muted">暂无便签待办</span>`;
      } else {
        noteTasks.forEach((t) => stack.appendChild(createTaskCard(t, true)));
      }
      nc.appendChild(stack);
      nc.querySelector("[data-del-note]").onclick = async () => {
        if (!confirm("确定删除此便签吗？")) return;
        await api("/api/notes/" + n.Id, { method: "DELETE" });
        await loadState();
      };
      nGrid.appendChild(nc);
    });

    // Settings inputs
    el("aiPresetSelect").value = state.ai.preset || "deepseek";
    el("aiBaseUrlInput").value = state.ai.baseUrl || "";
    el("aiModelInput").value = state.ai.model || "";
    el("aiCustomInput").value = state.ai.customInstruction || "";
    el("keyStatusHint").textContent = state.ai.hasApiKey ? "(服务器已配置 Key)" : "(尚未设置 Key)";
  }

  function escapeHtml(s) {
    return (s || "").replace(/[&<>"]/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", "\"": "&quot;" }[c]));
  }

  async function loadState() {
    const data = await api("/api/state");
    state.tasks = data.tasks || [];
    state.stickyNotes = data.stickyNotes || [];
    state.ai = data.ai || {};
    renderAll();
  }

  function openTaskModal(task) {
    state.editingTaskId = task && task.Id ? task.Id : null;
    el("modalTitle").textContent = state.editingTaskId ? "编辑任务" : "新建任务";
    el("mTitle").value = (task && task.Title) || "";
    el("mNotes").value = (task && task.Notes) || "";
    el("mQuad").value = task && task.Quadrant !== undefined ? String(task.Quadrant) : "1";
    el("mDdl").value = task && task.DeadlineIso ? task.DeadlineIso.replace(" ", "T").slice(0, 16) : "";
    el("mRem").value = task && task.RemindAtIso ? task.RemindAtIso.replace(" ", "T").slice(0, 16) : "";

    const noteSel = el("mNoteId");
    noteSel.innerHTML = `<option value="">全局日程</option>`;
    state.stickyNotes.forEach((n) => {
      if (n.FilterMode === "custom") {
        const opt = document.createElement("option");
        opt.value = n.Id;
        opt.textContent = "便签：" + n.Title;
        noteSel.appendChild(opt);
      }
    });
    noteSel.value = (task && task.NoteId) || "";
    state.modalAttachments = Array.isArray(task && task.AttachmentFiles) ? task.AttachmentFiles.slice() : [];
    state.modalNewUploads = [];
    renderModalAttachments();
    el("taskModal").classList.remove("hidden");
    el("mTitle").focus();
  }

  // Event bindings
  document.querySelectorAll(".nav-btn").forEach((btn) => {
    btn.onclick = () => {
      const tab = btn.dataset.tab;
      state.activeTab = tab;
      document.querySelectorAll(".nav-btn").forEach((b) => b.classList.toggle("active", b === btn));
      document.querySelectorAll(".tab-panel").forEach((p) => p.classList.toggle("active", p.id === "tab-" + tab));
      el("pageTitle").textContent = tabTitles[tab] || "";
    };
  });

  el("loginForm").onsubmit = async (e) => {
    e.preventDefault();
    el("loginError").classList.add("hidden");
    try {
      const res = await api("/api/login", {
        method: "POST",
        body: JSON.stringify({ password: el("loginPassword").value })
      });
      state.token = res.token;
      localStorage.setItem("dm_token", res.token);
      showApp();
      await loadState();
    } catch (err) {
      el("loginError").textContent = err.message;
      el("loginError").classList.remove("hidden");
    }
  };

  el("logoutBtn").onclick = () => {
    state.token = "";
    localStorage.removeItem("dm_token");
    showLogin();
  };

  el("themeToggleBtn").onclick = () => {
    const dark = document.body.classList.toggle("theme-dark");
    document.body.classList.toggle("theme-light", !dark);
    localStorage.setItem("dm_web_theme", dark ? "dark" : "light");
  };
  if (localStorage.getItem("dm_web_theme") === "dark") {
    document.body.classList.remove("theme-light");
    document.body.classList.add("theme-dark");
  }

  el("openNewTaskBtn").onclick = () => openTaskModal({ Quadrant: 1 });
  el("mCancelBtn").onclick = () => el("taskModal").classList.add("hidden");
  el("imgPreviewCloseBtn").onclick = () => el("imgPreviewModal").classList.add("hidden");
  el("imgPreviewModal").onclick = (ev) => {
    if (ev.target === el("imgPreviewModal")) el("imgPreviewModal").classList.add("hidden");
  };

  el("mAddImgBtn").onclick = () => el("mImgInput").click();
  el("mImgInput").onchange = async (ev) => {
    const files = Array.from(ev.target.files || []);
    for (const f of files) {
      if (!f.type.startsWith("image/")) continue;
      const dataUrl = await compressImageFileToDataUrl(f);
      state.modalNewUploads.push({ name: f.name, dataUrl });
    }
    ev.target.value = "";
    renderModalAttachments();
  };

  el("mSaveBtn").onclick = async () => {
    const title = el("mTitle").value.trim();
    if (!title) return;
    const btn = el("mSaveBtn");
    btn.disabled = true;
    try {
      const finalAttachments = state.modalAttachments.slice();
      for (const item of state.modalNewUploads) {
        const up = await api("/api/attachments", {
          method: "POST",
          body: JSON.stringify({ dataUrl: item.dataUrl })
        });
        if (up && up.fileName && finalAttachments.indexOf(up.fileName) < 0) {
          finalAttachments.push(up.fileName);
        }
      }
      const payload = {
        title,
        notes: el("mNotes").value.trim(),
        quadrant: parseInt(el("mQuad").value, 10),
        noteId: el("mNoteId").value,
        deadlineIso: el("mDdl").value ? el("mDdl").value.replace("T", " ") : "",
        remindAtIso: el("mRem").value ? el("mRem").value.replace("T", " ") : "",
        attachmentFiles: finalAttachments
      };
      if (state.editingTaskId) {
        await api("/api/tasks/" + state.editingTaskId, { method: "PUT", body: JSON.stringify(payload) });
      } else {
        await api("/api/tasks", { method: "POST", body: JSON.stringify(payload) });
      }
      el("taskModal").classList.add("hidden");
      await loadState();
    } catch (err) {
      showBanner("⚠ 保存失败：" + err.message, true);
    } finally {
      btn.disabled = false;
    }
  };

  el("searchInput").oninput = renderAll;
  el("filterSelect").onchange = renderAll;
  el("sortSelect").onchange = renderAll;

  el("aiSortBtn").onclick = async () => {
    el("aiSortBtn").disabled = true;
    showBanner("正在调用 AI 评估所有任务的四象限与优先级...");
    try {
      const res = await api("/api/ai/sort", { method: "POST", body: "{}" });
      showBanner(`✓ AI 已完成 ${res.updatedCount} 条任务的智能排序`);
      await loadState();
    } catch (err) {
      showBanner("⚠ " + err.message, true);
    } finally {
      el("aiSortBtn").disabled = false;
    }
  };

  el("addExtractImgBtn").onclick = () => el("extractImgInput").click();
  el("extractImgInput").onchange = async (ev) => {
    const files = Array.from(ev.target.files || []);
    for (const f of files) {
      if (state.extractImages.length >= 4) break;
      if (!f.type.startsWith("image/")) continue;
      const dataUrl = await compressImageFileToDataUrl(f);
      state.extractImages.push({ name: f.name, dataUrl });
    }
    ev.target.value = "";
    renderExtractImageStrip();
  };

  document.addEventListener("paste", async (ev) => {
    const items = (ev.clipboardData && ev.clipboardData.items) ? Array.from(ev.clipboardData.items) : [];
    const imgItems = items.filter((it) => it.kind === "file" && it.type && it.type.startsWith("image/"));
    if (imgItems.length === 0) return;

    const modalVisible = !el("taskModal").classList.contains("hidden");
    if (modalVisible) {
      ev.preventDefault();
      for (const it of imgItems) {
        const f = it.getAsFile();
        if (!f) continue;
        const dataUrl = await compressImageFileToDataUrl(f);
        state.modalNewUploads.push({ name: f.name || "剪贴板截图.jpg", dataUrl });
      }
      renderModalAttachments();
      return;
    }

    if (state.activeTab === "extract") {
      ev.preventDefault();
      for (const it of imgItems) {
        if (state.extractImages.length >= 4) break;
        const f = it.getAsFile();
        if (!f) continue;
        const dataUrl = await compressImageFileToDataUrl(f);
        state.extractImages.push({ name: f.name || "剪贴板截图.jpg", dataUrl });
      }
      renderExtractImageStrip();
    }
  });

  el("runExtractBtn").onclick = async () => {
    const text = el("chatExtractInput").value.trim();
    const images = state.extractImages.map((x) => x.dataUrl);
    if (!text && images.length === 0) {
      el("extractStatus").textContent = "请先输入聊天文本或添加至少一张截图";
      return;
    }
    el("runExtractBtn").disabled = true;
    el("extractStatus").textContent = images.length > 0 ? `正在调用 AI 图文识图 (${images.length}张图)...` : "正在解析聊天文本...";
    try {
      const res = await api("/api/ai/parse-chat", { method: "POST", body: JSON.stringify({ text, images }) });
      state.extractedTasks = res.tasks || [];
      renderExtractPreview();
      el("extractStatus").textContent = `✓ 已识别 ${state.extractedTasks.length} 条任务，请勾选确认`;
    } catch (err) {
      el("extractStatus").textContent = "⚠ " + err.message;
    } finally {
      el("runExtractBtn").disabled = false;
    }
  };

  function renderExtractPreview() {
    const list = el("extractPreviewList");
    list.innerHTML = "";
    if (state.extractedTasks.length === 0) {
      el("extractConfirmBar").classList.add("hidden");
      return;
    }
    el("extractConfirmBar").classList.remove("hidden");
    state.extractedTasks.forEach((item, idx) => {
      const row = document.createElement("div");
      row.className = "task-card extract-card";
      row.innerHTML = `
        <input type="checkbox" class="task-check" checked data-idx="${idx}" />
        <div class="task-main">
          <input type="text" value="${escapeHtml(item.Title)}" data-title="${idx}" />
          <div class="extract-meta-row">
            <select data-quad="${idx}" style="width:150px;">
              <option value="0" ${item.Quadrant === 0 ? "selected" : ""}>未分类</option>
              <option value="1" ${item.Quadrant === 1 ? "selected" : ""}>Q1 重要紧急</option>
              <option value="2" ${item.Quadrant === 2 ? "selected" : ""}>Q2 重要不紧急</option>
              <option value="3" ${item.Quadrant === 3 ? "selected" : ""}>Q3 紧急不重要</option>
              <option value="4" ${item.Quadrant === 4 ? "selected" : ""}>Q4 不重要不紧急</option>
            </select>
            <input type="text" placeholder="DDL yyyy-MM-dd HH:mm" value="${escapeHtml(item.DeadlineIso || "")}" data-ddl="${idx}" style="width:180px;" />
            ${item.Reason ? `<span class="pill ai extract-reason" title="${escapeHtml(item.Reason)}">${escapeHtml(item.Reason)}</span>` : ""}
          </div>
        </div>
      `;
      list.appendChild(row);
    });
  }

  el("confirmExtractBtn").onclick = async () => {
    const list = el("extractPreviewList");
    const chosen = [];
    state.extractedTasks.forEach((item, idx) => {
      const cb = list.querySelector(`[data-idx="${idx}"]`);
      if (!cb || !cb.checked) return;
      const titleInput = list.querySelector(`[data-title="${idx}"]`);
      const quadSelect = list.querySelector(`[data-quad="${idx}"]`);
      const ddlInput = list.querySelector(`[data-ddl="${idx}"]`);
      const title = titleInput ? titleInput.value.trim() : "";
      if (!title) return;
      chosen.push({
        title,
        notes: item.Notes || "",
        quadrant: quadSelect ? parseInt(quadSelect.value, 10) : 0,
        deadlineIso: ddlInput ? ddlInput.value.trim() : "",
        remindAtIso: item.RemindAtIso || "",
        reason: item.Reason || "",
        aiReason: item.Reason || ""
      });
    });
    if (chosen.length === 0) {
      el("extractStatus").textContent = "请至少勾选一条有效任务";
      return;
    }

    const btn = el("confirmExtractBtn");
    btn.disabled = true;
    el("extractStatus").textContent = "正在添加选中任务...";
    try {
      const attachedNames = [];
      if (el("extractAttachChk") && el("extractAttachChk").checked && state.extractImages.length > 0) {
        for (const item of state.extractImages) {
          const up = await api("/api/attachments", {
            method: "POST",
            body: JSON.stringify({ dataUrl: item.dataUrl })
          });
          if (up && up.fileName) attachedNames.push(up.fileName);
        }
      }
      if (attachedNames.length > 0) {
        chosen.forEach((c) => { c.attachmentFiles = attachedNames.slice(); });
      }
      try {
        await api("/api/tasks/batch", { method: "POST", body: JSON.stringify({ tasks: chosen }) });
      } catch (batchErr) {
        const msg = String((batchErr && batchErr.message) || "");
        if (msg.indexOf("缺少 tasks 数组") >= 0 || msg.indexOf("404") >= 0) {
          for (let i = 0; i < chosen.length; i++) {
            await api("/api/tasks", { method: "POST", body: JSON.stringify(chosen[i]) });
          }
        } else {
          throw batchErr;
        }
      }
      state.extractedTasks = [];
      state.extractImages = [];
      renderExtractImageStrip();
      el("chatExtractInput").value = "";
      el("extractPreviewList").innerHTML = "";
      el("extractConfirmBar").classList.add("hidden");
      el("extractStatus").textContent = `✓ 已添加 ${chosen.length} 条任务`;
      showBanner(`✓ 已添加 ${chosen.length} 条提取任务`);
      await loadState();
    } catch (err) {
      el("extractStatus").textContent = "⚠ 添加失败：" + err.message;
      showBanner("⚠ 添加失败：" + err.message, true);
    } finally {
      btn.disabled = false;
    }
  };

  el("newNoteBtn").onclick = async () => {
    const title = prompt("请输入新便签标题：", "桌面便签");
    if (!title) return;
    await api("/api/notes", { method: "POST", body: JSON.stringify({ title }) });
    await loadState();
  };

  el("aiPresetSelect").onchange = () => {
    const presets = {
      deepseek_flash: ["https://api.deepseek.com/v1", "deepseek-flash"],
      deepseek: ["https://api.deepseek.com/v1", "deepseek-chat"],
      deepseek_reasoner: ["https://api.deepseek.com/v1", "deepseek-reasoner"],
      openai: ["https://api.openai.com/v1", "gpt-4o-mini"],
      siliconflow: ["https://api.siliconflow.cn/v1", "deepseek-ai/DeepSeek-V3"],
      qwen: ["https://dashscope.aliyuncs.com/compatible-mode/v1", "qwen-plus"]
    };
    const p = presets[el("aiPresetSelect").value];
    if (p) {
      el("aiBaseUrlInput").value = p[0];
      el("aiModelInput").value = p[1];
    }
  };

  el("saveAiConfigBtn").onclick = async () => {
    await api("/api/config/ai", {
      method: "POST",
      body: JSON.stringify({
        preset: el("aiPresetSelect").value,
        baseUrl: el("aiBaseUrlInput").value,
        model: el("aiModelInput").value,
        apiKey: el("aiKeyInput").value,
        customInstruction: el("aiCustomInput").value
      })
    });
    el("aiKeyInput").value = "";
    el("aiSaveStatus").textContent = "✓ 已保存到服务器";
    await loadState();
  };

  if (state.token) {
    showApp();
    loadState().catch(() => {});
  } else {
    showLogin();
  }
})();
