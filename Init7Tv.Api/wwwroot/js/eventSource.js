/// Server-sent events that come back after the server has gone away.
///
/// The browser reconnects a stream that dropped mid-flight, but not one that was answered with an
/// error status: a 502 while the server restarts closes it for good. The page then sits there
/// looking live while nothing arrives, which is worse than saying nothing, because everything on it
/// is as stale as the moment the socket died.
///
/// So a closed one is opened again, backing off up to half a minute so a server that stays down is
/// asked about rather than hammered. Reconnecting is enough on its own: the first thing either
/// endpoint says is "look again".
export function subscribe(url, handlers) {
    const firstDelay = 1000;
    const longestDelay = 30000;

    let delay = firstDelay;
    let source = null;
    let retry = null;
    let closed = false;

    const open = () => {
        const opening = source = new EventSource(url);

        // the server is answering again, so the next drop starts over from a short wait
        opening.addEventListener('open', () => (delay = firstDelay));

        for (const [name, handler] of Object.entries(handlers)) {
            opening.addEventListener(name, handler);
        }

        opening.addEventListener('error', () => {
            // anything else means the browser is retrying by itself
            if (closed || opening.readyState !== EventSource.CLOSED) {
                return;
            }

            clearTimeout(retry);
            retry = setTimeout(open, delay);
            delay = Math.min(delay * 2, longestDelay);
        });
    };

    open();

    return {
        close() {
            closed = true;
            clearTimeout(retry);
            source?.close();
        }
    };
}
