
"""Local integration coverage for calendar persistence, validation and browser rendering."""
import runpy
from pathlib import Path
from playwright.sync_api import sync_playwright
s = runpy.run_path(str(Path(__file__).with_name("smoke_test.py")))
request, token, fields = s["request"], s["token"], s["fields"]
base, marker = s["base"], s["marker"]
created = []
try:
    for i in range(12):
        data = fields() | {"WorkDate": "2026-09-14", "StartTime": "09:00", "EndTime": "10:30", "Color": str(i % 6)}
        data["__RequestVerificationToken"] = token("/WorkLogs/Create")
        status, url, body = request("/WorkLogs/Create", data)
        assert status == 200 and "/Details/" in url, (status, url)
        created.append(url.replace(base, ""))
    for extra in [{"StartTime":"12:00","EndTime":"11:00"}, {"StartTime":"09:00","EndTime":""}, {"StartTime":"09:00","EndTime":"10:00","Color":"99"}]:
        data = fields() | extra
        data["__RequestVerificationToken"] = token("/WorkLogs/Create")
        status,url,body = request("/WorkLogs/Create",data)
        assert "/Details/" not in url and "validation-summary-errors" in body
    path = created[0].replace("/Details/", "/Edit/")
    data = fields() | {"WorkDate":"2026-10-02", "StartTime":"13:15","EndTime":"15:45","Color":"4"}
    data["__RequestVerificationToken"] = token(path)
    assert "/Details/" in request(path,data)[1]
    with sync_playwright() as p:
        browser=p.chromium.launch(channel="msedge",headless=True)
        page=browser.new_page(viewport={"width":1440,"height":1000})
        page.goto(base + "/?CalendarMonth=2026-09-01&Search=" + marker, wait_until="networkidle")
        assert page.locator("#calendar a.calendar-event").count() == 11, "Calendar must include rows beyond pagination"
        assert page.locator("#calendar .event-time").first.inner_text() == "09:00–10:30"
        assert page.locator("#calendar a.color-1").count() == 2
        page.locator("#calendar").screenshot(path="artifacts/calendar-desktop.png")
        page.get_by_role("link",name="下一個月",exact=True).click()
        assert page.locator("#calendar a.calendar-event").count() == 1
        assert page.locator("#calendar a.color-4 .event-time").inner_text() == "13:15–15:45"
        page.locator("#calendar a.calendar-event").click()
        page.get_by_role("link",name="編輯日誌",exact=True).click()
        assert page.locator("#StartTime").input_value() == "13:15"
        assert page.locator("#Color").input_value() == "4"
        page.locator("#EndTime").fill("12:00")
        assert not page.locator("#EndTime").evaluate("(e) => e.checkValidity()")
        page.goto(base + "/?CalendarMonth=2026-09-01&Search=" + marker, wait_until="networkidle")
        page.set_viewport_size({"width":390,"height":844})
        assert page.evaluate("document.documentElement.scrollWidth <= innerWidth"), "Mobile page must not overflow"
        page.locator("#calendar").screenshot(path="artifacts/calendar-mobile.png")
        browser.close()
    print("PASS: calendar all-month records, six colors, edit persistence, month navigation, time validation, mobile layout.")
finally:
    for path in created:
        path=path.replace("/Details/","/Delete/")
        request(path, {"__RequestVerificationToken":token(path)})
    print("Calendar test entries cleaned up.")
