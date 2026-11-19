"""Phase4O UI fixtures only; no external acquisitions or real member sessions."""
from pathlib import Path
import json
from playwright.sync_api import sync_playwright
r=Path(__file__).resolve().parents[1]
def camel(v):
 if isinstance(v,dict):return {k[0].lower()+k[1:]:camel(x) for k,x in v.items()}
 if isinstance(v,list):return [camel(x) for x in v]
 return v
report=camel(json.loads((r/'docs/phase4o/OperationsReport-Phase4O.json').read_text(encoding='utf-8-sig')))
script=(r/'src/WorkJournal.Web/wwwroot/js/research-data-operations.js').read_text(encoding='utf-8-sig')
with sync_playwright() as pw:
 browser=pw.chromium.launch(channel='msedge',headless=True);page=browser.new_page(viewport={'width':390,'height':844});errors=[];requests=[]
 page.on('pageerror',lambda e:errors.append(str(e)))
 def route(h):
  requests.append(h.request.url)
  if h.request.url.endswith('/fixture'):return h.fulfill(content_type='text/html',body='<section id="phase4o-data" data-admin="true"></section>')
  if '/data/captures' in h.request.url:return h.fulfill(json={'items':[],'totalCount':0})
  return h.fulfill(json=report)
 page.route('http://phase4o.test/**',route);page.goto('http://phase4o.test/fixture');page.add_script_tag(content=script)
 page.get_by_role('heading',name='真實資料取得與向前 PIT 擷取',exact=True).wait_for()
 text=page.locator('#phase4o-data').inner_text()
 assert all(x in text for x in ['3.2.4','新增真實資料：0','Disabled','NOT_RUN','TWSE','TPEx','FinMind','UNKNOWN','ACQUIRE','STORE','RESEARCH','REDISTRIBUTE','43','Bronze','Silver','Gold','Live','Snapshot','Fixture','Blocked'])
 assert page.evaluate('document.documentElement.scrollWidth <= window.innerWidth')
 page.screenshot(path=str(r/'artifacts/phase4o-mobile.png'),full_page=True)
 page.unroute('http://phase4o.test/**');page.route('http://phase4o.test/**',lambda h:h.fulfill(status=401,json={}))
 page.evaluate("document.dispatchEvent(new Event('visibilitychange'))");page.get_by_text('工作階段已結束，請重新登入。',exact=True).wait_for()
 assert 'ACQUIRE' not in page.locator('#phase4o-data').inner_text()
 page.set_content('<section id="phase4o-data" data-admin="false"></section>');before=len(requests);page.add_script_tag(content=script);page.wait_for_timeout(200);assert len(requests)==before
 assert not errors,errors
 browser.close()
print('PASS: Phase4O provider rights, 43-task plan, zero real outcomes, warehouse, 390px layout, expired-session clearing and member no-fetch. Fixture only.')
