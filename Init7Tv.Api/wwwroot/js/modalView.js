export const modalView = () => {
    return {
        title: "",
        description: "",
        cancelText: "",
        okText: "",
        res: null,
        visible: false,

        show(title, description, okText = "ok", cancelText = "cancel") {
            this.title = title;
            this.description = description;
            this.okText = okText;
            this.cancelText = cancelText;
            this.visible = true;

            return new Promise((res) => {
                this.res = res;
            });
        },

        ok() {
            this.res(true);
            this.visible = false;
        },

        fail() {
            this.res(false);
            this.visible = false;
        }
    }
}