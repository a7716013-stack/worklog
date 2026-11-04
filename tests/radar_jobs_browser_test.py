from pathlib import Path
import json, sys
from playwright.sync_api import sync_playwright

root=Path(__file__).resolve().parents[1]
base=sys.argv[1] if len(sys.argv)>1 else 'http://localhost:5203'
quote={'stock':{'symbol':'2330','name':'測試股票','market':'twse','isEtf':False},'date':'2026-11-03','close':100,'change':1,'changePercent':1,'volume':1000000}
snapshot={'tradeDate':'2026-11-03','dates':['2026-11-03'],'market':'all','quotes':[quote],'analysis':[],'events':[],'warnings':[],'updatedAt':'2026-11-03T06:00:00Z'}
analysis={'quote':quote,'score':None,'earned':20,'available':40,'level':'評分不完整','parts':[],'reasons':[],'riskFlags':[],'events':[],'technical':{},'averageVolume5':1000000,'averageVolume20':1000000,'volumeRatio5':1,'volumeRatio20':1,'volumeGrowth20':0,'foreignNet':None,'macdState':'資料不足'}
with sync_playwright() as p:
    browser=p.chromium.launch(channel='msedge',headless=True)
    page=browser.new_page(viewport={'width':1440,'height':1000})
    errors=[];console=[];page.on('pageerror',lambda e:errors.append(str(e)));page.on('console',lambda e:console.append(e.text) if e.type=='error' else None)
    page.on('requestfailed',lambda r:console.append(r.url+': '+str(r.failure)))
    polls=[0];mode=['COMPLETED'];posts=[]
    page.route('**/api/v1/market/radar/snapshot*',lambda r:r.fulfill(json=snapshot))
    page.route('**/StockAnalysis/RadarIndustries',lambda r:r.fulfill(json={'2330':'半導體'}))
    def jobs(route):
        if route.request.method=='POST':
            posts.append(route.request.headers);polls[0]=0
            route.fulfill(status=202,json={'jobId':'fixture','status':'QUEUED','completed':0,'total':1,'result':None})
        else:
            polls[0]+=1
            status='RUNNING' if polls[0]==1 else mode[0]
            route.fulfill(json={'jobId':'fixture','status':status,'completed':1,'total':1,'result':{**snapshot,'analysis':[analysis]},'errorCode':None if status=='COMPLETED' else 'ANALYSIS_TIMEOUT'})
    page.route('**/api/v1/market/radar/jobs**',jobs)
    page.goto(base+'/StockAnalysis/MarketRadar')
    try: page.get_by_text('並非最終推薦排行',exact=False).wait_for(timeout=10000)
    except Exception:
        print(json.dumps({'url':page.url,'title':page.title(),'errors':errors,'console':console,'status':page.locator('#radar-status').all_text_contents(),'scripts':page.locator('script[src]').evaluate_all('(nodes)=>nodes.map(n=>n.src)'),'posts':len(posts)},ensure_ascii=True))
        raise
    page.locator('#radar-results tr[data-symbol="2330"]').wait_for()
    assert '並非最終推薦排行' in page.locator('#radar-results').inner_text()
    page.wait_for_function("document.querySelector('#radar-status').textContent.startsWith('已完成')")
    assert '評分不完整' in page.locator('#radar-results').inner_text()
    assert posts and posts[0].get('requestverificationtoken')
    mode[0]='TIMED_OUT';page.locator('#radar-refresh').click()
    page.wait_for_function("document.querySelector('#radar-status').textContent.includes('分析逾時')")
    assert '並非最終推薦排行' in page.locator('#radar-results').inner_text()
    page.set_viewport_size({'width':390,'height':844})
    assert page.evaluate('document.documentElement.scrollWidth<=innerWidth+1')
    assert not errors,errors
    page.screenshot(path=str(root/'artifacts/radar3110-mobile.png'),full_page=True)
    browser.close()
print('PASS: API page, CSRF, progressive results, partial label, completed/missing scores, timeout, mobile, no JavaScript errors')
