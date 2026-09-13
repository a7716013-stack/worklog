import html
import http.cookiejar
import re
import sys
import urllib.error
import urllib.parse
import urllib.request
import uuid

base = sys.argv[1] if len(sys.argv) > 1 else "http://localhost:5180"
opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
created = []
marker = "smoke-" + uuid.uuid4().hex[:10]

def request(path, data=None):
    payload = urllib.parse.urlencode(data).encode() if data is not None else None
    try:
        with opener.open(base + path, payload, timeout=30) as response:
            return response.status, response.geturl(), response.read().decode()
    except urllib.error.HTTPError as error:
        return error.code, error.geturl(), error.read().decode()

def token(path):
    status, _, body = request(path)
    assert status == 200, (path, status)
    match = re.search(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"', body)
    assert match, "Missing antiforgery token"
    return html.unescape(match.group(1))

def fields():
    return {"WorkDate": "2026-09-13", "Title": marker, "Project": marker,
            "Hours": "1.25", "Status": "1", "Content": "Completed a test task.",
            "NextSteps": "Follow up tomorrow."}

try:
    status, _, body = request("/")
    assert status == 200 and "WORK JOURNAL" in body
    assert request("/lib/bootstrap/dist/css/bootstrap.min.css")[0] == 200
    assert request("/css/site.css")[0] == 200
    assert request("/WorkLogs/Details/2147483647")[0] == 404
    assert request("/WorkLogs/Create", fields())[0] == 400
    for bad, expected in [
        ({"Title": ""}, "請填寫工作項目"),
        ({"Hours": "25"}, "單筆工時"),
        ({"Status": "99"}, "無效"),
        ({"WorkDate": "1999-12-31"}, "日期須介於"),
        ({"Title": "   "}, "請填寫工作項目"),
    ]:
        data = fields() | bad | {"__RequestVerificationToken": token("/WorkLogs/Create")}
        status, url, body = request("/WorkLogs/Create", data)
        assert status == 200 and "/Details/" not in url and expected in html.unescape(body), (bad, status, url)
    for number in range(11):
        data = fields() | {"Title": marker + "-" + str(number),
                            "__RequestVerificationToken": token("/WorkLogs/Create")}
        status, url, body = request("/WorkLogs/Create", data)
        match = re.search(r"/WorkLogs/Details/(\d+)", url)
        assert status == 200 and match, ("create", status, url, body[:200])
        created.append(int(match.group(1)))
    query = "/WorkLogs?Search=" + marker
    status, _, body = request(query)
    assert status == 200
    assert len(re.findall(r'class="entry-title"', body)) == 10
    assert "13.75" in body, "Filtered total hours incorrect"
    _, _, body = request(query + "&Page=2")
    assert len(re.findall(r'class="entry-title"', body)) == 1
    _, _, body = request(query + "&Status=2")
    assert len(re.findall(r'class="entry-title"', body)) == 0
    _, _, body = request(query + "&From=2026-09-14")
    assert len(re.findall(r'class="entry-title"', body)) == 0
    _, _, body = request(query + "&From=2026-10-01&To=2026-09-01")
    assert "開始日期不得晚於結束日期" in html.unescape(body)

    entry = created[0]
    data = fields() | {"Title": marker + "-edited", "Content": '<script>alert("test")</script>',
                       "Status": "2", "Hours": "2.50", "Id": "999999",
                       "CreatedAt": "2000-01-01T00:00:00Z",
                       "__RequestVerificationToken": token(f"/WorkLogs/Edit/{entry}")}
    status, url, body = request(f"/WorkLogs/Edit/{entry}", data)
    assert status == 200 and marker + "-edited" in body
    assert '<script>alert("test")</script>' not in body
    assert "&lt;script&gt;" in body
    _, _, body = request(query + "&Status=2")
    assert len(re.findall(r'class="entry-title"', body)) == 1 and "2.5" in body

    assert request(f"/WorkLogs/Delete/{entry}")[0] == 200
    assert request(f"/WorkLogs/Details/{entry}")[0] == 200, "GET deleted data"
    assert request(f"/WorkLogs/Delete/{entry}", {})[0] == 400
    status, _, _ = request(f"/WorkLogs/Delete/{entry}",
                          {"__RequestVerificationToken": token(f"/WorkLogs/Delete/{entry}")})
    assert status == 200 and request(f"/WorkLogs/Details/{entry}")[0] == 404
    created.remove(entry)
    print("PASS: MVC pages, local assets, create/edit/delete, SQL-backed reads, filters, pagination, totals, validation, CSRF, HTML escaping.")
finally:
    for entry in created:
        request(f"/WorkLogs/Delete/{entry}",
                {"__RequestVerificationToken": token(f"/WorkLogs/Delete/{entry}")})
    print("Test entries cleaned up.")

