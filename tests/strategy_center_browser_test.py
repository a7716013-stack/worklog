import json,os,time,urllib.request
from pathlib import Path
from playwright.sync_api import sync_playwright
base=os.environ.get('STRATEGY_TEST_URL','http://localhost:5195')
report=[]
with sync_playwright() as p:
    browser=p.chromium.launch(channel='msedge',headless=True)
    context=browser.new_context(ignore_https_errors=True)
    page=context.new_page()
    errors=[]
    page.on('pageerror',lambda e:errors.append(str(e)))
    page.route('**/StockAnalysis/MarketPulse*',lambda r:r.fulfill(json={'market':'all','indices':[],'summary':None,'heat':None,'industries':[],'complete':False,'warnings':['測試來源未提供市場環境']}))
    for width in [1440,768,390]:
        page.set_viewport_size({'width':width,'height':1000})
        response=page.goto(base+'/StockAnalysis/Strategies',wait_until='networkidle')
        assert response.status==200,(response.status,page.locator('body').inner_text()[:800])
        assert page.get_by_role('heading',name='策略研究中心',exact=True).count()==1
        assert page.evaluate('document.documentElement.scrollWidth <= innerWidth+1'),width
        page.screenshot(path=f'artifacts/strategy151-{width}.png',full_page=True)
        report.append({'width':width,'status':'PASS','overflow':False})
    signal=page.locator('a[href*="/StrategySignal/"]').first
    if not signal.count(): signal=page.locator('a[href*="StrategySignal?id="]').first
    if signal.count():
        signal.click();page.wait_for_load_state('networkidle')
        assert page.get_by_role('heading',name='訊號依據').count()==1
        assert page.locator('a[href*="strategySignalId="]').count()>0
        assert page.evaluate('document.documentElement.scrollWidth <= innerWidth+1')
        report.append({'detail':'PASS'})
    page.goto(base+'/StockAnalysis/Strategies?tab=performance&basis=open&group=heat',wait_until='networkidle')
    assert page.get_by_role('heading',name='策略績效比較',exact=False).count()==1
    assert page.evaluate('document.documentElement.scrollWidth <= innerWidth+1')
    report.append({'performance':'PASS','pageErrors':errors})
    assert not errors,errors
    browser.close()
Path('artifacts/strategy151-browser.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(report,ensure_ascii=False))
