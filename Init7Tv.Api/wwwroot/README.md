# How the page is put together

One document, no build step, Alpine.js. Four views live in `index.html` at once and the address says
which one is showing. Everything below follows from that.

## The six rules

**1. The address says which view.** `$store.view` reads the fragment and nothing else writes it.
The header's links are ordinary links, so the back button, a bookmark and a reload all still mean
something.

**2. A hidden view behaves as if it were gone.** No streaming, no polling, no open connections, no
playing video — and it catches up when it is come back to. `whileShowing()` is how that is said, and
every view that does ongoing work uses it.

**3. Anything two components share is a store.** A component reads the store, writes the store, and
reacts to it with `$watch`. No component reaches into another, and nothing is dispatched between
them — the one exception is `notify()`, which is a message to the viewer rather than to a component,
and nothing waits for an answer.

**4. A component is a view of a store, not an owner of state.** It holds what only it cares about —
a search box, a spinner, which row is highlighted — and derives everything else with a getter.

**5. Talking to the server goes through `requestHandler.js`.** It reports failures itself, and
returns `null` when it has, so callers check for `null` and never for a status.

**6. Nothing polls. The server says when something changed.** A view that has to keep up opens an
`EventSource` while it is showing — the player for its stream, the recordings list for its
recordings. The event carries the news and nothing else: the client then reads the list through the
same endpoint it would have used anyway, which keeps one place deciding what a given user may see,
and a browser that reconnects is told to look again straight away.

The dashboard is the exception, and for a reason: it is the one view that wants the payload itself,
box-wide, for admins only, which is what its SignalR hub already pushes.

## The shape of a component

```js
export const thingView = () => ({
    // state this component alone cares about
    field: null,

    init() { },      // watches and subscriptions; Alpine cleans $watch up on its own
    destroy() { },   // only for things Alpine cannot clean up, like an EventSource

    // actions, then getters
});
```

Single quotes, `() => ({...})`, and elements are reached with `x-ref`, never `getElementById`.

## Where things live

| | |
|---|---|
| `index.html` | the one document: the header, the four views, the modal and the toasts |
| `js/index.js` | the only bootstrap: it creates the stores and registers the components |
| `js/stores/` | everything shared between components |
| `js/views/` | one file per component, mirroring `partial/` |
| `partial/` | the markup for each component |
| `js/whileShowing.js` | rule 2 |
| `/api/streaming/events`, `/api/recording/events` | rule 6 |
| `js/requestHandler.js`, `notification.js`, `loadPartial.js`, `epgCache.js` | the small shared pieces |

## The stores

| | |
|---|---|
| `view` | which of the four views the address says |
| `channels` | the channel list, and which channel the page points at |
| `menu` | whether the channel drawer is open, on a screen too narrow to show it beside the page |
| `tabs` | which of the four recording tabs is showing, and their counts |
| `filter` | how the recording lists are narrowed, kept across tabs |
| `recordings` | what the recording lists have to know about each other |
| `modal` | the confirm dialog, which is a promise |
