/// A view that is hidden behaves as if it were gone.
///
/// Everything is on one page, so a view that has been left is still there, still built, still able
/// to stream, poll and hold a connection open for a picture nobody can see. None of that is worth
/// doing, so it stops on the way out and starts again on the way back in.
///
/// `enter` runs every time the view is shown, including straight away if it already is, so it has
/// to be safe to run more than once.
export function whileShowing(component, view, {enter, leave}) {
    component.$watch('$store.view.current', current => (current === view ? enter?.() : leave?.()));

    if (component.$store.view.is(view)) {
        enter?.();
    }
}
