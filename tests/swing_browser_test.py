"""Read-only analysis plus isolated browser-local watchlist checks. No shared data changed."""
import sys,json
from pathlib import Path
from playwright.sync_api import sync_playwright,expect
base=sys.argv[1] if len(sys.argv)>1 else "http://localhost:5180"
with sync_playwright() as p:
    browser=p.chromium.launch(channel="msedge",headless=True)
    context=browser.new_context(viewport={"width":1440,"height":1000})
    page=context.new_page()
    errors=[]
    page.on("pageerror",lambda error:errors.append(str(error)))
    page.goto(base+"/StockAnalysis/Swing",wait_until="networkidle")
    expect(page.locator("#swing-empty")).to_be_visible()
    expect(page.locator(".swing-card")).to_have_count(0)
    def search(q):
        page.locator("#swing-query").fill(q)
        page.get_by_role("button",name="搜尋股票",exact=True).click()
        expect(page.locator("#swing-search-results .swing-search-row")).not_to_have_count(0,timeout=60000)
    search("台積電")
    expect(page.locator(".swing-card")).to_have_count(0)
    page.locator("#swing-search-results").get_by_role("button",name="加入追蹤").first.click()
    card=page.locator('.swing-card[data-symbol="2330"]')
    expect(card.get_by_role("button",name="詳細分析")).to_be_enabled(timeout=90000)
    expect(page.locator("#swing-search-results").get_by_role("button",name="已追蹤")).to_be_disabled()
    card.get_by_role("button",name="詳細分析").click()
    expect(page.locator("#swing-detail-title")).to_contain_text("2330 台積電")
    expect(page.locator("#swing-score-breakdown .swing-rule")).to_have_count(11)
    assert page.locator("#swing-chart").evaluate("(c)=>c.width>0 && c.height>0")
    page.get_by_role("button",name="執行回測",exact=True).click()
    expect(page.get_by_role("button",name="執行回測",exact=True)).to_be_enabled(timeout=180000)
    expect(page.locator("#swing-backtest-result")).to_contain_text("實際期間")
    assert "2021" in page.locator("#swing-backtest-result").inner_text()
    search("006208")
    page.locator("#swing-search-results").get_by_role("button",name="加入追蹤").first.click()
    etf=page.locator('.swing-card[data-symbol="006208"]')
    expect(etf.get_by_role("button",name="詳細分析")).to_be_enabled(timeout=90000)
    etf.get_by_role("button",name="詳細分析").click()
    expect(page.locator("#swing-detail-title")).to_contain_text("006208")
    expect(page.locator("#swing-backtest-result")).to_be_empty()
    page.reload(wait_until="networkidle")
    expect(page.locator(".swing-card")).to_have_count(2)
    expect(card.get_by_role("button",name="詳細分析")).to_be_enabled(timeout=90000)
    card.get_by_role("button",name="詳細分析").click()
    page.screenshot(path="artifacts/swing-desktop.png",full_page=True)
    page.set_viewport_size({"width":390,"height":844})
    page.wait_for_timeout(400)
    assert page.evaluate("document.documentElement.scrollWidth<=innerWidth"),"Mobile overflow"
    page.screenshot(path="artifacts/swing-mobile.png",full_page=True)
    page.locator("#swing-filter").select_option(label="優先研究")
    # Filtering must preserve stored watchlist.
    assert len(json.loads(page.evaluate("localStorage.getItem('workjournal.swing.watchlist.v1')")))==2
    page.locator("#swing-filter").select_option("")
    etf.get_by_role("button",name="移除追蹤").click()
    expect(page.locator(".swing-card")).to_have_count(1)
    card.get_by_role("button",name="移除追蹤").click()
    expect(page.locator("#swing-empty")).to_be_visible()
    expect(page.locator("#swing-detail")).to_be_hidden()
    page.reload(wait_until="networkidle")
    expect(page.locator(".swing-card")).to_have_count(0)
    assert not errors,errors
    assert page.request.get(base+"/StockAnalysis/SwingData?symbol=bad").status==400
    assert page.request.get(base+"/StockAnalysis/SwingSearch?q="+"a"*31).status==400
    context.close()
    browser.close()
print("PASS: name/code search, explicit follow, duplicates, persistence, detail switching, backtest, filter, remove, mobile and invalid input.")
