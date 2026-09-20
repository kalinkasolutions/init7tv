/// The confirm dialog, which is a question and the promise of an answer.
export function modalStore() {
    return {
        title: '',
        description: '',
        visible: false,
        answer: null,

        show(title, description) {
            // a second question while one is open leaves the first waiting for ever
            this.answer?.(false);

            this.title = title;
            this.description = description;
            this.visible = true;

            return new Promise(resolve => (this.answer = resolve));
        },

        confirm() {
            this.close(true);
        },

        cancel() {
            this.close(false);
        },

        close(answer) {
            this.visible = false;
            this.answer?.(answer);
            this.answer = null;
        }
    };
}
