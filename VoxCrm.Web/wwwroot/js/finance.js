(() => {
  const mode = document.querySelectorAll('[name="CustomerMode"]');
  const owner = document.getElementById('PetOwnerId');
  const patient = document.getElementById('PatientId');
  const name = document.getElementById('CustomerName');
  const filterPatients = () => {
    if (!patient || !owner) return;
    let validSelection = !patient.value;
    [...patient.options].forEach(o => {
      const allowed = !o.value || o.dataset.owner === owner.value;
      o.hidden = !allowed; o.disabled = !allowed;
      if (allowed && o.selected) validSelection = true;
    });
    if (!validSelection) patient.value = '';
  };
  const update = () => {
    const manual = document.querySelector('[name="CustomerMode"]:checked')?.value === 'manual';
    document.querySelector('[data-finance-registered]')?.classList.toggle('d-none', manual);
    document.querySelector('[data-finance-manual]')?.classList.toggle('d-none', !manual);
    if (owner) { owner.disabled = manual; owner.required = !manual; }
    if (patient) patient.disabled = manual;
    if (name) { name.disabled = !manual; name.required = manual; }
    filterPatients();
  };
  mode.forEach(x => x.addEventListener('change', update)); owner?.addEventListener('change', filterPatients); update();
})();
