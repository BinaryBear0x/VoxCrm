(() => {
  const form = document.getElementById('vaccinationBatchForm'); if (!form) return;
  const rows = [...form.querySelectorAll('[data-batch-row]')];
  const date = document.getElementById('applicationDate');
  const button = form.querySelector('[data-batch-submit]'); let submitting = false;
  const update = () => {
    let count = 0;
    rows.forEach(row => {
      const checked = row.querySelector('[data-batch-selected]').checked;
      if (checked) count++;
      row.querySelector('[data-batch-code]').disabled = !checked;
      row.classList.toggle('table-primary', checked);
      const next = new Date(`${date.value}T12:00:00Z`);
      next.setUTCDate(next.getUTCDate() + Number(row.dataset.days));
      row.querySelector('[data-batch-next]').textContent = checked && !Number.isNaN(next.getTime()) ? next.toLocaleDateString('tr-TR', {timeZone:'UTC'}) : '—';
    });
    form.querySelector('[data-batch-count]').textContent = `${count} seçili`;
    button.textContent = count ? `Seçili ${count} uygulamayı kaydet` : 'Önce yapılan uygulamaları seçin';
    button.disabled = !count || submitting;
  };
  rows.forEach(row => row.querySelector('[data-batch-selected]').addEventListener('change', update)); date.addEventListener('change', update);
  form.addEventListener('submit', event => { if (submitting) {event.preventDefault(); return;} submitting=true;button.disabled=true;button.textContent='Uygulamalar kaydediliyor…'; });
  update();
})();
