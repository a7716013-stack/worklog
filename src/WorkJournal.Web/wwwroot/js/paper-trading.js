(() => {
    "use strict";
    const form = document.getElementById("paper-order-form");
    const type = document.getElementById("Order_OrderType");
    const limit = document.getElementById("Order_LimitPrice");
    function update() {
        const isLimit = type.value === "Limit" || type.value === "1";
        limit.disabled = !isLimit;
        limit.required = isLimit;
        if (!isLimit) limit.value = "";
    }
    type.addEventListener("change", update);
    update();
    form.addEventListener("submit", () => { form.querySelector("button[type=submit]").disabled = true; });
    document.getElementById("paper-reset-form").addEventListener("submit", event => {
        if (!window.confirm("確定清除共用虛擬帳戶的全部交易資料？此操作無法復原。")) event.preventDefault();
    });
})();
