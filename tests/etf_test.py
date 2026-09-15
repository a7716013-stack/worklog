"""Read-only ETF/stock panel regression against a running local site."""
import sys
from playwright.sync_api import sync_playwright
base = sys.argv[1] if len(sys.argv)>1 else "http://localhost:5180"
with sync_playwright() as p:
    browser=p.chromium.launch(channel="msedge",headless=True)
    page=browser.new_page(viewport={"width":1280,"height":900})
    errors=[]
    page.on("pageerror",lambda error: errors.append(str(error)))
    for symbol,etf in [("006208",True),("2330",False),("0050",True)]:
        page.goto(base+"/StockAnalysis?symbol="+symbol,wait_until="networkidle",timeout=120000)
        assert page.locator("#etf-heading").count()==int(etf)
        assert page.locator("#fundamentals-heading").count()==int(not etf)
        assert page.locator('[data-metric="ma20"]').inner_text()!="—"
        if etf:
            assert page.locator('[data-metric="nav"]').inner_text()!="—"
            assert page.locator('[data-metric="cash-dividend"]').inner_text()!="—"
        if symbol=="006208":
            assert page.get_by_role("cell",name="台積電",exact=True).count()==1
            page.screenshot(path="artifacts/etf-desktop.png",full_page=True)
            page.set_viewport_size({"width":390,"height":844})
            assert page.evaluate("document.documentElement.scrollWidth <= innerWidth"),"Mobile horizontal overflow"
            page.screenshot(path="artifacts/etf-mobile.png",full_page=True)
            page.set_viewport_size({"width":1280,"height":900})
    assert not errors, errors
    browser.close()
print("PASS: ETF/stock panel switch, live data, holdings, technicals and mobile layout.")
