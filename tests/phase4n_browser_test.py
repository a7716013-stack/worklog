"""Presentation fixtures only. No real accounts, cookies, or network providers."""
from pathlib import Path
import json
from playwright.sync_api import sync_playwright
r=Path(__file__).resolve().parents[1]
def camel(v):
 if isinstance(v,dict):return {k[0].lower()+k[1:]:camel(x) for k,x in v.items()}
 if isinstance(v,list):return [camel(x) for x in v]
 return v
report=camel(json.loads((r/'docs/phase4n/OperationsReport-Phase4N.json').read_text(encoding='utf-8-sig')))
script=(r/'src/WorkJournal.Web/wwwroot/js/research-operations.js').read_text(encoding='utf-8-sig')
with sync_playwright() as pw:
 browser=pw.chromium.launch(channel='msedge',headless=True)
 page=browser.new_page(viewport={'width':390,'height':844});errors=[];requests=[]
 page.on('pageerror',lambda e:errors.append(str(e)))
 def route(h):
  requests.append(h.request.url)
  if h.request.url.endswith('/fixture'):return h.fulfill(content_type='text/html',body='<section id="phase4n-operations" data-admin="true"></section>')
  if '/resolution-packets' in h.request.url:return h.fulfill(json={'items':report['packets'],'totalCount':len(report['packets'])})
  return h.fulfill(json=report)
 page.route('http://phase4n.test/**',route);page.goto('http://phase4n.test/fixture');page.add_script_tag(content=script)
 page.get_by_role('heading',name='營運驗收與人工處理',exact=True).wait_for()
 text=page.locator('#phase4n-operations').inner_text()
 assert all(x in text for x in ['3.2.3','Disabled','0/8','BLOCKED_BY_AUTHORITY','NOT_RUN','Verified Live','Verified at Export','Fixture','Blocked','Not Run'])
 assert page.locator('details').count()==43
 page.locator('details').first.locator('summary').click()
 assert 'UNPROVEN' in page.locator('details').first.inner_text()
 assert page.evaluate('document.documentElement.scrollWidth <= window.innerWidth')
 page.screenshot(path=str(r/'artifacts/phase4n-mobile.png'),full_page=True)
 # Session expiry on re-entry must clear old administrative content.
 page.unroute('http://phase4n.test/**');page.route('http://phase4n.test/**',lambda h:h.fulfill(status=401,json={}))
 page.evaluate("document.dispatchEvent(new Event('visibilitychange'))")
 page.get_by_text('工作階段已結束，請重新登入。',exact=True).wait_for()
 assert page.locator('details').count()==0 and 'BLOCKED_BY_AUTHORITY' not in page.locator('#phase4n-operations').inner_text()
 # Even accidental script inclusion on a member page makes no management requests.
 page.set_content('<section id="phase4n-operations" data-admin="false"></section>');before=len(requests);page.add_script_tag(content=script)
 page.wait_for_timeout(200);assert len(requests)==before
 assert not errors,errors
 browser.close()
print('PASS: Phase4N management dashboard, 43 packets, evidence classes, 390px layout, 401 clearing, member no-fetch. Fixture only.')
