import {notify} from './notification.js';

/// hls.js pointed at one playlist, with the handling both players need: a browser that cannot play
/// hls at all, a fatal error part way through, and autoplay being refused.
///
/// Hands back the instance to destroy when the player is done with it, or null when there is
/// nothing to play with.
export function playHls(video, url, {tuning = {}, onFatal} = {}) {
    if (!Hls.isSupported()) {
        notify('Hls is not supported.', 'Playing hls streams is not supported in this browser', 'error');
        return null;
    }

    // Defaults are tuned for adaptive VOD. These are single rendition streams, and low latency
    // mode is on by default while neither playlist carries LL-HLS parts for it to use. How far
    // back to sit is the caller's, because a live channel and a recording want opposite things.
    const hls = new Hls({lowLatencyMode: false, maxBufferLength: 30, ...tuning});

    hls.on(Hls.Events.ERROR, (_, data) => data.fatal && onFatal?.());
    hls.on(Hls.Events.MANIFEST_PARSED, () => startPlaying(video, hls));
    hls.loadSource(url);
    hls.attachMedia(video);

    return hls;
}

function startPlaying(video, hls) {
    video.play().catch(error => {
        if (error.name === 'NotAllowedError') {
            waitForUserToPlay(video, hls);
        } else if (error.name !== 'AbortError') {
            notify('Playback error', 'Something went wrong', 'error');
        }
    });
}

/// Autoplay was refused. Keep the player from pulling segments nobody is watching, which otherwise
/// continues for as long as the page is open.
function waitForUserToPlay(video, hls) {
    hls.stopLoad();

    notify('Press play to start', 'Your browser blocked autoplay for this page.', 'error');

    video.addEventListener('play', () => hls.startLoad(), {once: true});
}
