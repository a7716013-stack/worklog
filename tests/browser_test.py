from pathlib import Path
import re
import uuid
import sys
from playwright.sync_api import sync_playwright

root = Path(__file__).resolve().parents[1]
artifacts = root / "artifacts"
artifacts.mkdir(exist_ok=True)
marker = "browser-" + uuid.uuid4().hex[:10]
base = sys.argv[1].rstrip("/") if len(sys.argv) > 1 else "http://localhost:5180"
entry_url = None
with sync_playwright() as playwright:
    browser = playwright.chromium.launch(channel="msedge", headless=True)
    page = browser.new_page(viewport={"width": 1440, "height": 1050}, locale="zh-TW")
    errors = []
    page.on("pageerror", lambda error: errors.append(str(error)))
    try:
        page.goto(base, wait_until="networkidle")
        page.get_by_role("link", name="記錄今天的工作").click()
        page.locator("#Title").fill(marker)
        page.locator("#WorkDate").fill("2026-09-13")
        page.locator("#Project").fill("網站開發")
        page.locator("#Hours").fill("1.25")
        page.locator("#Content").fill("確認工作日誌的表單、日期驗證與響應式版面。")
        page.locator("#NextSteps").fill("開始記錄每天的工作進展。")
        page.wait_for_function('typeof jQuery !== "undefined" && typeof jQuery.fn.valid === "function" && jQuery("form").data("validator")')
        assert page.evaluate('jQuery("form").valid()'), "Valid date rejected by client validation"
        page.get_by_role("button", name="建立日誌").click()
        page.wait_for_url(re.compile(r".*/WorkLogs/Details/\d+"))
        entry_url = page.url
        page.get_by_role("link", name="編輯日誌", exact=True).click()
        page.locator("#Hours").fill("2.5")
        page.get_by_role("button", name="儲存變更").click()
        page.wait_for_url(entry_url)
        assert "2.5 小時" in page.locator(".detail-meta").inner_text()
        page.goto(base, wait_until="networkidle")
        page.screenshot(path=str(artifacts / "desktop.png"), full_page=True)
        page.set_viewport_size({"width": 390, "height": 844})
        assert page.evaluate("document.documentElement.scrollWidth <= window.innerWidth"), "Page overflows mobile viewport"
        page.screenshot(path=str(artifacts / "mobile.png"), full_page=True)
        page.goto(base + "/WorkLogs/Create", wait_until="networkidle")
        page.locator("#WorkDate").fill("1999-12-31")
        page.locator("#Title").fill("Invalid date test")
        page.locator("#Content").fill("Validation test")
        assert not page.evaluate('jQuery("form").valid()'), "Out-of-range date was accepted"
        assert not errors, errors
        print("PASS: Edge create/edit, client date validation, desktop/mobile layout, no JavaScript errors.")
    finally:
        if entry_url:
            page.goto(entry_url.replace("/Details/", "/Delete/"))
            page.get_by_role("button", name="確認刪除").click()
            page.wait_for_url(base + "/")
        browser.close()
