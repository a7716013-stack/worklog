"""Run only against PaperTradingChecks --serve: isolated SQL and deterministic quotes."""
import sys
from pathlib import Path
from playwright.sync_api import sync_playwright, expect
base = (sys.argv[1] if len(sys.argv) > 1 else "http://localhost:5187").rstrip("/")
artifacts = Path(__file__).resolve().parents[1] / "artifacts"
artifacts.mkdir(exist_ok=True)
with sync_playwright() as p:
    browser = p.chromium.launch(channel="msedge", headless=True)
    context = browser.new_context(viewport={"width": 1440, "height": 1000}, locale="zh-TW")
    page = context.new_page()
    errors = []
    page.on("pageerror", lambda error: errors.append(str(error)))
    response = page.goto(base + "/StockAnalysis/PaperTrading", wait_until="networkidle")
    assert response.headers.get("x-papertrading-fixture") == "isolated-sql", "Refuse destructive checks outside isolated fixture"
    expect(page.get_by_role("heading", name="虛擬交易", exact=True)).to_be_visible()
    expect(page.locator(".subheading")).to_contain_text("模擬交易，不會送出真實證券委託")
    expect(page.locator("#paper-initial")).to_have_text("1,000,000.00")
    nav = page.get_by_role("navigation", name="網站切換", exact=True)
    expect(nav.get_by_role("link")).to_have_count(2)
    expect(nav.get_by_role("link", name="股票分析")).to_have_attribute("href", "/StockAnalysis")
    tabs = page.get_by_role("navigation", name="分析介面切換")
    expect(tabs.locator('[aria-current="page"]')).to_have_text("虛擬交易")
    def form_data():
        return page.locator("#paper-order-form").evaluate("f => Object.fromEntries(new FormData(f))")
    def submit(side="Buy", quantity="1000", kind="Market", limit=None):
        page.locator("#Order_StockId").fill("2330")
        page.locator("#Order_Side").select_option(side)
        page.locator("#Order_OrderType").select_option(kind)
        page.locator("#Order_Quantity").fill(quantity)
        if limit is not None: page.locator("#Order_LimitPrice").fill(limit)
        page.get_by_role("button", name="送出虛擬委託", exact=True).click()
        page.wait_for_url("**/StockAnalysis/PaperTrading?symbol=2330")
        page.wait_for_load_state("networkidle")
    def reset():
        page.locator("#paper-reset-form").locator("..").locator("summary").click()
        page.locator('[name="confirmReset"]').check()
        page.once("dialog", lambda dialog: dialog.accept())
        page.get_by_role("button", name="確認重設虛擬帳戶").click()
        page.wait_for_load_state("networkidle")
        expect(page.locator("#paper-cash")).to_have_text("1,000,000.00")
    try:
        # Every mutation is protected even before model validation.
        for route in ["PlacePaperOrder", "CancelPaperOrder", "RefreshPaperOrders", "ResetPaperAccount"]:
            assert context.request.post(base + "/StockAnalysis/" + route, form={}).status == 400, route
        # Browser cannot supply its own price, account or status. Replay uses the same request ID.
        data = form_data() | {"Order.StockId": "2330", "Order.Quantity": "1000", "Order.Side": "Buy", "Order.OrderType": "Market",
                             "FilledPrice": "1", "Order.FilledPrice": "1", "Order.AccountId": "999", "Order.Status": "Filled"}
        assert context.request.post(base + "/StockAnalysis/PlacePaperOrder", form=data).status == 200
        assert context.request.post(base + "/StockAnalysis/PlacePaperOrder", form=data).status == 200
        page.reload(wait_until="networkidle")
        expect(page.locator("#paper-trades tbody tr")).to_have_count(1)
        expect(page.locator("#paper-cash")).to_have_text("899,857.50")
        expect(page.locator("#paper-orders tbody tr").first).to_have_attribute("data-status", "Filled")
        submit(quantity="1000")
        expect(page.locator("#paper-positions .paper-quantity")).to_have_text("2,000")
        submit(side="Sell", quantity="500")
        expect(page.locator("#paper-positions .paper-quantity")).to_have_text("1,500")
        expect(page.locator("#paper-trades tbody tr")).to_have_count(3)
        submit(quantity="1", kind="Limit", limit="95")
        expect(page.locator("#paper-orders tbody tr").first).to_have_attribute("data-status", "Pending")
        page.get_by_role("button", name="檢查待成交委託", exact=True).click()
        page.wait_for_load_state("networkidle")
        expect(page.locator("#paper-orders tbody tr").first).to_have_attribute("data-status", "Pending")
        page.locator("#paper-orders tbody tr").first.get_by_role("button", name="取消", exact=True).click()
        page.wait_for_load_state("networkidle")
        expect(page.locator("#paper-orders tbody tr").first).to_have_attribute("data-status", "Cancelled")
        page.reload(wait_until="networkidle")
        expect(page.locator("#paper-positions .paper-quantity")).to_have_text("1,500")
        expect(page.locator("#paper-trades tbody tr")).to_have_count(3)
        page.screenshot(path=str(artifacts / "paper-desktop.png"), full_page=True)
        page.set_viewport_size({"width":390,"height":844})
        assert page.evaluate("document.documentElement.scrollWidth <= innerWidth"), "Mobile overflow"
        page.screenshot(path=str(artifacts / "paper-mobile.png"), full_page=True)
        page.goto(base + "/StockAnalysis?symbol=2330", wait_until="networkidle")
        page.get_by_role("link", name="虛擬買進", exact=True).click()
        expect(page.locator("#Order_StockId")).to_have_value("2330")
        page.goto(base + "/StockAnalysis/Swing", wait_until="networkidle")
        page.locator("#swing-query").fill("2330")
        page.get_by_role("button",name="搜尋股票",exact=True).click()
        page.get_by_role("button",name="加入追蹤",exact=True).click()
        page.get_by_role("button",name="詳細分析",exact=True).click()
        page.locator("#swing-paper-order").click()
        expect(page.locator("#Order_StockId")).to_have_value("2330")
        # Invalid enum and quantities remain on a usable form and do not mutate SQL.
        data = form_data() | {"Order.StockId":"2330", "Order.Quantity":"0", "Order.Side":"Buy", "Order.OrderType":"Market"}
        response = context.request.post(base + "/StockAnalysis/PlacePaperOrder", form=data)
        assert response.status==200 and "validation-summary-errors" in response.text()
        assert not errors, errors
        print("PASS: paper UI, CSRF, price tampering, idempotency, buy/add/sell, pending/cancel, SQL persistence, entry links and 390x844 layout.")
    finally:
        page.goto(base + "/StockAnalysis/PaperTrading", wait_until="networkidle")
        reset()
        expect(page.locator("#paper-empty")).to_be_visible()
        context.close()
        browser.close()
