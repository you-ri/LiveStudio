// Copyright (c) You-Ri, 2026
//
// OneComme (わんコメ) plugin that forwards comments and gifts to a LiveStudio app running the
// jp.lilium.livestudio.social intake. One plugin covers every platform OneComme itself supports —
// YouTube, Twitch, niconico, TwitCasting, Kick and the rest — because it reads OneComme's already
// normalized comment stream rather than talking to any platform directly.
//
// Plain CommonJS with no dependencies and no build step: copy the folder into OneComme's plugins
// directory and enable it. Node 20 (what OneComme 5.2+ ships) provides the global fetch this uses.

const UID = 'jp.lilium.livestudio.social'

// Largest number of recently seen comment ids kept to suppress duplicates. OneComme delivers new
// comments, but a redelivery would otherwise fire an operation twice; the id is cheap insurance.
const SEEN_LIMIT = 512

// Seconds of silence between repeated delivery-failure logs. Without it, a stream running while the
// LiveStudio app is closed fills OneComme's log with one error per comment.
const FAILURE_LOG_INTERVAL_MS = 30000

// OneComme's service ids where they differ from the well-known values in the LiveStudio contract.
// Anything not listed passes through unchanged — the contract's vocabulary is open, so a platform
// OneComme adds later still arrives with a usable name.
const SERVICE_MAP = {
  niconama: 'niconico',
  twicas: 'twitcasting',
}

// YouTube states the kind of gift outright. Mapping notes:
//   supersticker  -> sticker      (a paid sticker, not a superchat)
//   sponsorgift   -> gift         (someone bought memberships for others)
//   giftreceived  -> gift         (someone was given one)
//   milestonechat -> membership   (a member's anniversary message)
//   subscribe     -> membership   (a new channel member; `subscribe` is kept for Twitch-style subs)
const YOUTUBE_GIFT_TYPES = {
  superchat: 'superchat',
  supersticker: 'sticker',
  sponsorgift: 'gift',
  giftreceived: 'gift',
  milestonechat: 'membership',
  subscribe: 'membership',
}

const plugin = {
  name: 'LiveStudio Social',
  uid: UID,
  version: '0.1.0',
  author: 'You-Ri',
  url: `http://localhost:11180/plugins/${UID}/index.html`,
  permissions: ['comments'],

  defaultState: {
    port: 3003,
    token: '',
  },

  // Ring of recently forwarded comment ids, oldest first, plus a set for the membership test.
  seenIds: [],
  seenSet: new Set(),
  lastFailureLogAt: 0,

  init({ store }) {
    this.store = store
    this.seenIds = []
    this.seenSet = new Set()
    this.lastFailureLogAt = 0
  },

  destroy() {
    this.seenIds = []
    this.seenSet.clear()
  },

  // OneComme delivers comments in batches: subscribe('comments', { comments: [...] }).
  subscribe(type, ...args) {
    if (type !== 'comments') return

    for (const arg of args) {
      if (!arg || !Array.isArray(arg.comments)) continue

      const events = []
      for (const comment of arg.comments) {
        const event = this.toSocialEvent(comment)
        if (event) events.push(event)
      }
      if (events.length > 0) this.send(events)
    }
  },

  /**
   * Maps one OneComme comment onto the LiveStudio social event schema, or returns null when it
   * should not be forwarded (already seen, or a OneComme system message rather than a viewer).
   */
  toSocialEvent(comment) {
    if (!comment || !comment.data) return null
    if (comment.service === 'system') return null

    const d = comment.data
    if (d.id && !this.remember(d.id)) return null

    const service = SERVICE_MAP[comment.service] || comment.service || 'unknown'

    return {
      source: service,
      type: this.resolveType(comment.service, d),
      id: d.id || '',
      user: {
        id: d.userId || '',
        // displayName is the name the viewer chose to show; name is what the platform reported.
        name: d.displayName || d.name || '',
        isModerator: d.isModerator === true,
        // Only YouTube reports membership directly. Twitch says the same thing through a subscriber
        // flag it sends as the string '1'.
        isMember: comment.service === 'twitch' ? d.subscriber === '1' : d.isMember === true,
        isOwner: d.isOwner === true,
      },
      message: d.comment || '',
      ...this.resolveAmount(d),
      timestamp: d.timestamp || undefined,
    }
  },

  resolveType(service, d) {
    // YouTube names the kind of gift itself, and does so even for events that carry no money.
    if (service === 'youtube' && d.giftType) {
      return YOUTUBE_GIFT_TYPES[d.giftType] || 'gift'
    }
    if (!d.hasGift) return 'chat'
    if (service === 'twitch' && d.bits) return 'cheer'
    return 'gift'
  },

  resolveAmount(d) {
    if (typeof d.price === 'number' && isFinite(d.price)) {
      // currency is the modern field; unit is its deprecated predecessor and still carries a symbol
      // on older payloads. Neither is normalized here — the contract leaves that to the consumer.
      return { amount: d.price, currency: d.currency || d.unit || '' }
    }
    if (d.bits) {
      const bits = Number(d.bits)
      if (isFinite(bits)) return { amount: bits, currency: 'bits' }
    }
    return { amount: 0, currency: '' }
  },

  /** Records an id and reports whether it is new. Bounded, oldest evicted first. */
  remember(id) {
    if (this.seenSet.has(id)) return false

    this.seenSet.add(id)
    this.seenIds.push(id)
    if (this.seenIds.length > SEEN_LIMIT) {
      this.seenSet.delete(this.seenIds.shift())
    }
    return true
  },

  send(events) {
    const port = Number(this.store.get('port')) || plugin.defaultState.port
    const token = this.store.get('token') || ''

    const headers = { 'Content-Type': 'application/json' }
    if (token) headers['X-Social-Token'] = token

    // Deliberately not awaited: OneComme is waiting to display these comments, and a slow or absent
    // LiveStudio app must not hold up the stream the viewer is watching.
    fetch(`http://127.0.0.1:${port}/social/events`, {
      method: 'POST',
      headers,
      body: JSON.stringify(events),
    })
      .then((res) => {
        if (!res.ok) this.logFailure(`LiveStudio answered ${res.status}`)
      })
      .catch((e) => {
        this.logFailure(e && e.message ? e.message : String(e))
      })
  },

  // One line per half minute at most. A stream that runs with the app closed would otherwise log a
  // failure for every single comment.
  logFailure(reason) {
    const now = Date.now()
    if (now - this.lastFailureLogAt < FAILURE_LOG_INTERVAL_MS) return

    this.lastFailureLogAt = now
    console.warn(`[LiveStudio Social] Could not deliver events: ${reason}`)
  },

  // Settings page (index.html) reads and writes the stored state through this.
  async request(req) {
    switch (req.method) {
      case 'GET':
        return { code: 200, response: { ...this.store.store } }
      case 'PUT': {
        const data = JSON.parse(req.body)
        this.store.set('port', Number(data.port) || plugin.defaultState.port)
        this.store.set('token', typeof data.token === 'string' ? data.token : '')
        return { code: 200, response: { ...this.store.store } }
      }
      default:
        return { code: 404, response: {} }
    }
  },
}

module.exports = plugin
