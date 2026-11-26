// The only script every page loads, beside htmx. It wires three HTML attributes:
//   <button data-dialog="id">     opens <dialog id="id">; <dialog class="open"> opens on load
//   <form data-confirm="Sure?">   asks with confirm() before it posts
//   .picker list buttons          fill the picker's hidden input (see Pages/Shared/Pickers)

document.addEventListener('click', (e) => {
  document.querySelectorAll('.picker-list').forEach((list) => {
    if (!list.parentElement.contains(e.target)) list.replaceChildren()
  })
  const opener = e.target.closest('[data-dialog]')
  if (opener) {
    document.getElementById(opener.dataset.dialog)?.showModal()
    return
  }
  if (e.target.closest('[data-close]')) {
    e.target.closest('dialog')?.close()
    return
  }
  const option = e.target.closest('.picker-list button[data-value]')
  if (option) {
    const picker = option.closest('.picker')
    picker.querySelector('input[type=hidden]').value = option.dataset.value
    picker.querySelector('input[type=search]').value = option.dataset.label
    option.parentElement.replaceChildren()
  }
})

document.addEventListener('submit', (e) => {
  const message = e.target.dataset.confirm
  if (message && !confirm(message)) e.preventDefault()
})

document.querySelectorAll('dialog.open').forEach((d) => d.showModal())
