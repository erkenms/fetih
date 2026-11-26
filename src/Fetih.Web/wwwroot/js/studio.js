// Studio table editors: clone <template> rows and mark rows checked for deletion.
// Buttons use data-add-row="template-id" (with a container carrying data-rows="template-id"
// and data-next-index="N"). Teleporter destinations use data-add-dest inside a row.

document.addEventListener('click', (e) => {
  const addRow = e.target.closest('[data-add-row]')
  if (addRow) {
    const id = addRow.dataset.addRow
    const tpl = document.getElementById(id)
    const body = document.querySelector(`[data-rows="${id}"]`)
    if (!tpl || !body) return
    const idx = Number(body.dataset.nextIndex || body.children.length)
    body.dataset.nextIndex = String(idx + 1)
    const html = tpl.innerHTML.replaceAll('__INDEX__', String(idx))
    body.insertAdjacentHTML('beforeend', html)
    body.lastElementChild?.querySelector('input, textarea')?.focus()
    return
  }

  const addDest = e.target.closest('[data-add-dest]')
  if (addDest) {
    const row = addDest.closest('tr')
    const box = row?.querySelector('.dest-list')
    const tpl = document.getElementById('dest-template')
    if (!row || !box || !tpl) return
    const rowIdx = row.dataset.rowIndex
    const destIdx = Number(box.dataset.nextDest || box.children.length)
    box.dataset.nextDest = String(destIdx + 1)
    const html = tpl.innerHTML
      .replaceAll('__INDEX__', String(rowIdx))
      .replaceAll('__DEST__', String(destIdx))
    box.insertAdjacentHTML('beforeend', html)
    box.lastElementChild?.querySelector('input')?.focus()
  }
})

document.addEventListener('change', (e) => {
  if (e.target.matches('input[type="checkbox"][data-delete-row]')) {
    e.target.closest('tr')?.classList.toggle('deleted', e.target.checked)
  }
})
