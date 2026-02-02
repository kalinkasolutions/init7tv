export const modalView = () => {
    return {
        title: "",
        description: "",
        res: null,
        visible: false,

        show(title, description) {
            this.title = title;
            this.description = description;
            this.visible = true;

            return new Promise((res) => {
                this.res = res;
            });
        },

        modalConfirm() {
            this.visible = false;
            this.res(true);
        },

        modalFail() {
            this.visible = false;
            this.res(false);
        }
    }
}