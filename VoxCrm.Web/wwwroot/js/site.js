(() => {
  const wireSecurityActions = (root = document) => {
    root.querySelectorAll("form[data-confirm]").forEach((form) => {
      if (form.dataset.confirmWired === "true") return;
      form.dataset.confirmWired = "true";
      form.addEventListener("submit", (event) => {
        if (!window.confirm(form.dataset.confirm || "Bu işlemi onaylıyor musunuz?")) event.preventDefault();
      });
    });
    root.querySelectorAll("[data-submit-on-change]").forEach((element) => {
      if (element.dataset.submitWired === "true") return;
      element.dataset.submitWired = "true";
      element.addEventListener("change", () => element.form?.requestSubmit());
    });
    root.querySelectorAll("[data-copy-target]").forEach((button) => {
      if (button.dataset.copyWired === "true") return;
      button.dataset.copyWired = "true";
      button.addEventListener("click", async () => {
        const target = document.querySelector(button.dataset.copyTarget);
        if (target) await navigator.clipboard.writeText(target.value || target.textContent || "");
      });
    });
  };

  wireSecurityActions();
  const debounce = (fn, delay = 300) => {
    let timer;
    return (...args) => {
      window.clearTimeout(timer);
      timer = window.setTimeout(() => fn(...args), delay);
    };
  };

  // ── Auto-search: fetch partial table, no full page reload ──────────────────
  document.querySelectorAll("[data-auto-search]").forEach((input) => {
    const form = input.closest("form");
    if (!form) return;

    const tableWrapper = document.querySelector("[data-search-table]");

    // If there's a partial-capable table container, use fetch approach
    if (tableWrapper) {
      const partialUrl = tableWrapper.dataset.searchTable; // e.g. "/Patient?partial=true"

      const doSearch = debounce(async () => {
        const value = input.value;
        const url = new URL(partialUrl, window.location.origin);
        url.searchParams.set("search", value);

        // Update browser URL without reload
        const historyUrl = new URL(form.action || window.location.href, window.location.origin);
        historyUrl.searchParams.set("search", value);
        if (!value) historyUrl.searchParams.delete("search");
        history.replaceState(null, "", historyUrl.toString());

        try {
          const res = await fetch(url.toString(), {
            headers: { "X-Requested-With": "XMLHttpRequest" }
          });
          if (!res.ok) return;
          const html = await res.text();
          tableWrapper.innerHTML = html;
          // Re-initialize delete confirm buttons inside the fresh HTML
          wireSecurityActions(tableWrapper);
        } catch {
          // silently ignore network errors
        }
      }, 500);

      input.addEventListener("input", doSearch);

      // Also keep clear button visible/hidden
      const clearBtn = form.querySelector("[data-search-clear]");
      if (clearBtn) {
        input.addEventListener("input", () => {
          clearBtn.hidden = !input.value;
        });
      }
    } else {
      // Fallback: classic form submit with increased debounce
      let lastValue = input.value;
      input.addEventListener("input", debounce(() => {
        if (input.value === lastValue) return;
        lastValue = input.value;
        form.requestSubmit();
      }, 700));
    }
  });

  // ── Search-select widget ───────────────────────────────────────────────────
  const endpointByType = {
    patient: "/Patient/Search",
    owner: "/PetOwner/Search"
  };

  document.querySelectorAll("select[data-search-select]").forEach((select) => {
    const type = select.dataset.searchSelect;
    const endpoint = endpointByType[type];
    if (!endpoint) return;

    const originalName = select.name;
    const selectedOption = select.selectedOptions[0];
    const selectedValue = select.value;
    const selectedText = selectedOption && selectedOption.value ? selectedOption.textContent.trim() : "";

    select.removeAttribute("name");
    select.removeAttribute("required");
    select.classList.add("search-select-native");

    const wrapper = document.createElement("div");
    wrapper.className = "search-select";
    select.parentNode.insertBefore(wrapper, select);
    wrapper.appendChild(select);

    const hidden = document.createElement("input");
    hidden.type = "hidden";
    hidden.name = originalName;
    hidden.value = selectedValue;

    const input = document.createElement("input");
    input.type = "search";
    input.className = "form-control search-select-input";
    input.placeholder = select.dataset.placeholder || "Yazmaya başlayın...";
    input.autocomplete = "off";
    input.value = selectedText;
    if (select.dataset.required === "true") input.required = true;

    const results = document.createElement("div");
    results.className = "search-select-results";
    results.hidden = true;

    wrapper.append(hidden, input, results);

    const setSelection = (item) => {
      hidden.value = item.id;
      input.value = item.label;
      input.setCustomValidity("");
      results.hidden = true;

      let option = Array.from(select.options).find((candidate) => candidate.value === item.id);
      if (!option) {
        option = new Option(item.label, item.id, true, true);
        select.appendChild(option);
      }
      select.value = item.id;
      select.dispatchEvent(new Event("change", { bubbles: true }));
    };

    const render = (items) => {
      results.innerHTML = "";
      if (!items.length) {
        const empty = document.createElement("div");
        empty.className = "search-select-empty";
        empty.textContent = "Sonuç bulunamadı";
        results.appendChild(empty);
        results.hidden = false;
        return;
      }

      items.forEach((item) => {
        const button = document.createElement("button");
        button.type = "button";
        button.className = "search-select-option";
        button.innerHTML = `
          <span class="search-select-label">${escapeHtml(item.label)}</span>
          ${item.meta ? `<span class="search-select-meta">${escapeHtml(item.meta)}</span>` : ""}
          ${item.detail ? `<span class="search-select-detail">${escapeHtml(item.detail)}</span>` : ""}
        `;
        button.addEventListener("click", () => setSelection(item));
        results.appendChild(button);
      });
      results.hidden = false;
    };

    const search = debounce(async () => {
      const query = input.value.trim();
      hidden.value = "";
      select.value = "";
      if (select.dataset.required === "true") input.setCustomValidity("Listeden bir kayıt seçin.");

      const response = await fetch(`${endpoint}?q=${encodeURIComponent(query)}`, {
        headers: { "Accept": "application/json" }
      });
      if (!response.ok) return;
      render(await response.json());
    }, 250);

    input.addEventListener("input", search);
    input.addEventListener("focus", () => {
      if (!results.hidden && results.childElementCount) return;
      search();
    });
    input.addEventListener("blur", () => {
      window.setTimeout(() => {
        if (!hidden.value && input.value.trim()) {
          input.setCustomValidity("Listeden bir kayıt seçin.");
        }
        results.hidden = true;
      }, 160);
    });
  });

  document.addEventListener("click", (event) => {
    document.querySelectorAll(".search-select-results").forEach((results) => {
      if (!results.parentElement.contains(event.target)) results.hidden = true;
    });
  });

  function escapeHtml(value) {
    return String(value)
      .replaceAll("&", "&amp;")
      .replaceAll("<", "&lt;")
      .replaceAll(">", "&gt;")
      .replaceAll('"', "&quot;")
      .replaceAll("'", "&#039;");
  }
})();
