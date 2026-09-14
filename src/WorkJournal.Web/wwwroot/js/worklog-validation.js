// Use ISO date strings for the date input on both client and server.
$.validator.addMethod("workdate", function (value, element, bounds) {
    return this.optional(element) || (/^\d{4}-\d{2}-\d{2}$/.test(value) && value >= bounds[0] && value <= bounds[1]);
});
$.validator.unobtrusive.adapters.add("workdate", ["min", "max"], function (options) {
    options.rules.workdate = [options.params.min, options.params.max];
    options.messages.workdate = options.message;
});


const startTime = document.getElementById("StartTime");
const endTime = document.getElementById("EndTime");
if (startTime && endTime) {
    const validateTimes = () => {
        const paired = Boolean(startTime.value) === Boolean(endTime.value);
        startTime.setCustomValidity(paired ? "" : "請同時填寫開始與結束時間。");
        endTime.setCustomValidity(!paired ? "請同時填寫開始與結束時間。" :
            (startTime.value && endTime.value <= startTime.value ? "結束時間必須晚於開始時間（同一天）。" : ""));
    };
    startTime.addEventListener("input", validateTimes);
    endTime.addEventListener("input", validateTimes);
    validateTimes();
}
