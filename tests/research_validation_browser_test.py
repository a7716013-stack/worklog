"""Presentation-only browser checks against reviewed release DTO fixtures; no production authentication bypass."""
from pathlib import Path
import json,re
from urllib.parse import urlparse,unquote,parse_qs
from playwright.sync_api import sync_playwright
r=Path(__file__).resolve().parents[1]
def camel(v):
 if isinstance(v,dict):return {k[0].lower()+k[1:]:camel(x) for k,x in v.items()}
 if isinstance(v,list):return [camel(x) for x in v]
 return v
p=camel(json.loads((r/'docs/phase4i-state-3.2.2.json').read_text(encoding='utf-8-sig')));v=p['validation']
j=camel(json.loads((r/'docs/phase4j-state-3.2.2.json').read_text(encoding='utf-8-sig')))
etl=camel(json.loads((r/'docs/etl-state-3.2.2.json').read_text(encoding='utf-8-sig')))
ops=camel(json.loads((r/'docs/EtlOperationsReport-3.2.2.json').read_text(encoding='utf-8-sig')))
q=[dict(id=c['domain']+':'+c['claimId'],claim=c,applicability='UNPROVEN_FOR_HISTORICAL_SCOPE') for c in v['claims']]
markup=(r/'src/WorkJournal.Web/Views/StockAnalysis/Validation.cshtml').read_text(encoding='utf-8-sig')
markup=re.sub(r'@\{.*?\}','',markup,flags=re.S).split('@section Scripts')[0]
requests=[]
def route(handler):
 path=unquote(urlparse(handler.request.url).path);requests.append(path)
 if path=='/ResearchValidation':return handler.fulfill(content_type='text/html',body='<html><body>'+markup+'</body></html>')
 if path.endswith('/data/providers'):body=dict(items=etl['providers'],totalCount=len(etl['providers']))
 elif path.endswith('/etl/operations'):body=ops
 elif path.endswith('/etl/health'):body=dict(verifiedAt=etl['verifiedAt'],realJobs=2,testJobs=0,failedJobs=0,quarantinedJobs=0)
 elif path.endswith('/etl/jobs'):body=dict(items=etl['jobs'],totalCount=len(etl['jobs']))
 elif '/etl/jobs/' in path:
  job=next(x for x in etl['jobs'] if x['jobId'] in path);body=dict(quality=job['quality']) if path.endswith('/quality') else job
 elif '/etl/batches/' in path:body=next(x['lineage'] for x in etl['jobs'] if x['jobId'] in path)
 elif path.endswith('eligibility-summary'):body=dict(status='BLOCKED',dataset=v['dataset'],components=v['components'])
 elif path.endswith('/provenance'):body=v['provenance']
 elif path.endswith('/readiness'):body=dict(counts=p['counts'],builds=j['builds'])
 elif path.endswith('/validation-summary'):body=dict(trend=p['trend'])
 elif path.endswith('/replay-status'):body=dict(replay=p['replay'])
 elif path.endswith('/closure-queue'):body=dict(claims=dict(items=q,totalCount=len(q)))
 elif path.endswith('/evidence'):body=dict(requirement=next(c for c in q if c['id'] in path))
 elif path.endswith('/sources'):body=dict(sources=dict(items=j['sources'],totalCount=len(j['sources'])),verifiedAt=j['generatedAt'])
 elif path.endswith('/acquisition-plan'):
  query=parse_qs(urlparse(handler.request.url).query);page=int(query.get('page',['1'])[0]);size=int(query.get('pageSize',['20'])[0]);body=dict(tasks=dict(items=j['plan'][(page-1)*size:page*size],totalCount=len(j['plan'])))
 elif '/acquisition-attempts/' in path:body=dict(attempts=[a for a in j['attempts'] if a['taskId']==path.split('/')[-1]])
 elif path.endswith('/coverage'):body=dict(observations=dict(items=v['observations'],totalCount=len(v['observations'])))
 else:raise AssertionError('Unexpected request '+path)
 handler.fulfill(content_type='application/json',body=json.dumps(body))
with sync_playwright() as pw:
 browser=pw.chromium.launch(channel='msedge',headless=True)
 page=browser.new_page();page.route('http://phase4i.test/**',route)
 page.goto('http://phase4i.test/ResearchValidation')
 for name in ['research-validation.js','research-phase4i.js','research-phase4j.js','research-etl.js']:page.add_script_tag(content=(r/'src/WorkJournal.Web/wwwroot/js'/name).read_text(encoding='utf-8-sig'))
 page.wait_for_function("document.querySelectorAll('#validation-claims tr').length === 20")
 page.wait_for_function("document.querySelectorAll('#validation-pit tr').length === 1")
 assert '價格 20' in page.locator('#validation-counts').inner_text() and '法人 1' in page.locator('#validation-counts').inner_text()
 assert page.locator('#validation-components tr').count()==6
 assert '未證明' in page.locator('#validation-pit').inner_text()
 page.locator('#validation-claims button').first.click()
 page.wait_for_function("document.querySelector('#validation-claim-detail').textContent.includes('requirement')")
 assert page.locator('#validation-pit-more').is_hidden()
 page.wait_for_function("document.querySelectorAll('#supply-builds tr').length === 6")
 assert page.locator('#supply-sources tr').count()==13
 assert page.locator('#supply-plan tr').count()==20
 page.locator('#supply-more').click()
 page.wait_for_function("document.querySelectorAll('#supply-plan tr').length === 40")
 page.locator('#supply-plan button').first.click()
 page.wait_for_function("document.querySelector('#supply-attempts').textContent.includes('history')")
 assert '未建置' in page.locator('#supply-builds').inner_text()
 page.wait_for_function("document.querySelectorAll('#etl-jobs tr').length === 2")
 page.locator('#etl-jobs button').first.click()
 page.wait_for_function("document.querySelector('#etl-detail').textContent.includes('rawDigest')")
 assert '真實工作 2' in page.locator('#etl-status').inner_text()
 page.get_by_text('排程、證據結案與研究準備',exact=True).wait_for()
 assert page.get_by_text('證據結案工作（43）',exact=True).count()==1
 assert page.get_by_text('資料集研究準備（6）',exact=True).count()==1
 page.screenshot(path=str(r/'artifacts/phase3220-validation-browser.png'),full_page=True)
 page.unroute('http://phase4i.test/**');page.route('http://phase4i.test/api/**',lambda h:h.fulfill(status=401,content_type='application/json',body='{}'))
 page.add_script_tag(content=(r/'src/WorkJournal.Web/wwwroot/js/research-phase4i.js').read_text(encoding='utf-8-sig'))
 page.wait_for_function("document.querySelector('#validation-counts').textContent.includes('請重新登入')")
 page.add_script_tag(content=(r/'src/WorkJournal.Web/wwwroot/js/research-etl.js').read_text(encoding='utf-8-sig'))
 page.wait_for_function("!document.querySelector('#etl-operations') && document.querySelector('#etl-status').textContent.includes('請重新登入')")
 browser.close()
print('PASS: 21/20/1 counts, 6 components, 20 claims, PIT missing time, evidence interaction, paging boundary, expired session; API fixture presentation only.')
