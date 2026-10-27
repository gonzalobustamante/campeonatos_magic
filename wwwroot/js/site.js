document.querySelectorAll('form[data-confirm]').forEach(form => {
  form.addEventListener('submit', event => { if (!window.confirm(form.dataset.confirm)) event.preventDefault(); });
});
const rows = [...document.querySelectorAll('[data-seat]')];
if (rows.length) {
  const preview = document.getElementById('score-preview');
  function update() {
    const selected = rows.filter(r => r.querySelector('.seat-toggle').checked);
    const selectedIds = new Set(selected.map(r => r.dataset.playerId));
    for (const row of rows) {
      const active = selectedIds.has(row.dataset.playerId);
      row.classList.toggle('seat-active', active);
      const outcome = row.querySelector('.outcome-select');
      outcome.disabled = !active;
      const killer = row.querySelector('.killer-select');
      if (outcome.value !== 'Eliminated') killer.value = '';
      killer.disabled = !active || outcome.value !== 'Eliminated';
      for (const option of killer.options) {
        option.hidden = !!option.value && !selectedIds.has(option.value);
        option.disabled = !!option.value && !selectedIds.has(option.value);
      }
      if (killer.value && !selectedIds.has(killer.value)) killer.value = '';
    }
    const winners = selected.filter(r => r.querySelector('.outcome-select').value === 'Winner').length;
    const survivors = selected.filter(r => r.querySelector('.outcome-select').value === 'Survived').length;
    for (const row of rows) {
      let points = 0;
      const outcome = row.querySelector('.outcome-select').value;
      if (outcome === 'Winner') points += 3;
      if (!winners && outcome === 'Survived') points += 1;
      points += selected.filter(r => r.querySelector('.killer-select').value === row.dataset.playerId).length;
      row.querySelector('output').textContent = selectedIds.has(row.dataset.playerId) ? '+' + points : '—';
    }
    preview.textContent = selected.length < 2 || selected.length > 8 ? 'Selecciona entre 2 y 8 participantes.' :
      winners > 1 ? 'Solo puede haber un ganador.' :
      winners && survivors ? 'Si hay ganador, los demás deben figurar como eliminados o retirados.' :
      selected.length + ' participantes · ' + (winners ? 'Mesa con ganador' : 'Mesa sin ganador') + ' · Puntaje calculado automáticamente';
  }
  rows.forEach(r => r.addEventListener('change', update));
  update();
}
