(() => {
  const rows = [...document.querySelectorAll('[data-appointment-row]')];
  const search = document.querySelector('[data-appointment-search]');
  const status = document.querySelector('[data-status-filter]');
  const type = document.querySelector('[data-type-filter]');
  const normalize = text => String(text || '').toLocaleLowerCase('tr-TR').replace(/\s+/g, ' ').trim();
  const filter = () => {
    const query = normalize(search?.value); let visible = 0;
    rows.forEach(row => {
      const haystack = normalize(row.dataset.search);
      const match = (!query || haystack.includes(query) || (/^[+\d\s()-]+$/.test(query) && haystack.replace(/[^0-9]/g, '').includes(query.replace(/[^0-9]/g, '')))) && (!status?.value || row.dataset.status === status.value) && (!type?.value || row.dataset.type === type.value);
      row.hidden = !match; if (match) visible++;
    });
    const empty = document.querySelector('[data-appointments-empty]'); if (empty) empty.hidden = visible !== 0;
    const count = document.querySelector('[data-filter-count]'); if (count) count.textContent = visible === rows.length ? `${visible} randevu` : `${rows.length} randevudan ${visible} gösteriliyor`;
  };
  search?.addEventListener('input', filter); status?.addEventListener('change', filter); type?.addEventListener('change', filter);
  document.querySelector('[data-reset-filters]')?.addEventListener('click', () => { search.value = ''; status.value = ''; type.value = ''; filter(); });
  const more = document.querySelector('[data-more-filters]');
  more?.addEventListener('click', () => { const panel = document.getElementById('appointment-filters'); panel.hidden = !panel.hidden; more.setAttribute('aria-expanded', String(!panel.hidden)); });
  const table = document.querySelector('[data-appointments-table]');
  const density = document.querySelector('[data-density-toggle]');
  const setDensity = compact => { table?.classList.toggle('is-compact', compact); density?.setAttribute('aria-pressed', String(compact)); density?.setAttribute('aria-label', compact ? 'Ferah tabloyu aç' : 'Kompakt tabloyu aç'); };
  try { setDensity(localStorage.getItem('voxcrm.tableCompact') === 'true'); } catch {}
  density?.addEventListener('click', () => { const compact = !table.classList.contains('is-compact'); setDensity(compact); try { localStorage.setItem('voxcrm.tableCompact', String(compact)); } catch {} });
  document.querySelectorAll('[data-task-check]').forEach(check => check.addEventListener('change', () => { check.setAttribute('aria-busy', 'true'); check.form.requestSubmit(); check.disabled = true; }));
})();
