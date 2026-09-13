// Use ISO date strings for the date input on both client and server.
$.validator.addMethod("workdate", function (value, element, bounds) {
    return this.optional(element) || (/^\d{4}-\d{2}-\d{2}$/.test(value) && value >= bounds[0] && value <= bounds[1]);
});
$.validator.unobtrusive.adapters.add("workdate", ["min", "max"], function (options) {
    options.rules.workdate = [options.params.min, options.params.max];
    options.messages.workdate = options.message;
});

