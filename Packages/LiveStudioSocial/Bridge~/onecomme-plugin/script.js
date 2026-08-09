// Copyright (c) You-Ri, 2026
//
// Settings page for the LiveStudio Social plugin. Talks to the plugin's own REST endpoint, which
// OneComme routes to the plugin's request() handler. Plain DOM on purpose: a settings page with two
// fields does not need a framework, and depending on one OneComme happens to serve would tie this
// page to a version of the host app.

const UID = 'jp.lilium.livestudio.social'
const ENDPOINT = `http://localhost:11180/api/plugins/${UID}`

const form = document.getElementById('form')
const portInput = document.getElementById('port')
const tokenInput = document.getElementById('token')
const status = document.getElementById('status')

function report(message, isError) {
  status.textContent = message
  status.className = isError ? 'error' : ''
}

async function load() {
  try {
    const res = await fetch(ENDPOINT)
    const { response } = await res.json()
    portInput.value = response.port ?? 3003
    tokenInput.value = response.token ?? ''
  } catch (e) {
    report(`読み込みに失敗しました / Failed to load: ${e.message}`, true)
  }
}

form.addEventListener('submit', async (e) => {
  e.preventDefault()
  report('')

  try {
    const res = await fetch(ENDPOINT, {
      method: 'PUT',
      body: JSON.stringify({
        port: Number(portInput.value),
        token: tokenInput.value,
      }),
    })
    const { response } = await res.json()
    // Show what was actually stored rather than what was typed, so a value the plugin corrected
    // (an unparsable port falling back to the default) is visible instead of silently disagreeing.
    portInput.value = response.port
    tokenInput.value = response.token ?? ''
    report('保存しました / Saved')
  } catch (err) {
    report(`保存に失敗しました / Failed to save: ${err.message}`, true)
  }
})

load()
