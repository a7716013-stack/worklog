(() => {
'use strict';
const app=document.getElementById('stock-research');if(!app)return;
const symbol=app.dataset.symbol, $=id=>document.getElementById(id), num=(v,d=2)=>v==null?'—':Number(v).toLocaleString('zh-TW',{maximumFractionDigits:d}), node=(tag,text,cls)=>{const n=document.createElement(tag);if(text!=null)n.textContent=text;if(cls)n.className=cls;return n;};
const text=(id,value)=>$(id).textContent=value;
async function get(endpoint){const r=await fetch(endpoint+'?symbol='+encodeURIComponent(symbol),{headers:{Accept:'application/json'},signal:AbortSignal.timeout(55000)});if(!r.ok)throw Error('資料暫時無法取得（'+r.status+'），請稍後重試。');return r.json();}
function table(target,headers,rows){target.replaceChildren();const wrap=node('div',null,'table-responsive'),t=node('table',null,'research-data-table'),head=node('thead'),hr=node('tr');headers.forEach(h=>hr.append(node('th',h)));head.append(hr);t.append(head);const body=node('tbody');rows.forEach(row=>{const tr=node('tr');row.forEach(v=>tr.append(node('td',v)));body.append(tr);});t.append(body);wrap.append(t);target.append(wrap);}
function tab(id,focus=false){if(!$('panel-'+id))id='overview';app.querySelectorAll('[role=tab]').forEach(b=>{const active=b.dataset.tab===id;b.setAttribute('aria-selected',String(active));b.tabIndex=active?0:-1;if(active&&focus)b.focus();});app.querySelectorAll('[role=tabpanel]').forEach(p=>p.hidden=p.id!=='panel-'+id);history.replaceState(null,'','#'+id);}
const tabs=[...app.querySelectorAll('[role=tab]')];tabs.forEach((b,i)=>{b.onclick=()=>tab(b.dataset.tab);b.onkeydown=e=>{let n;if(e.key==='ArrowRight')n=(i+1)%tabs.length;if(e.key==='ArrowLeft')n=(i+tabs.length-1)%tabs.length;if(e.key==='Home')n=0;if(e.key==='End')n=tabs.length-1;if(n!=null){e.preventDefault();tab(tabs[n].dataset.tab,true);}};});
app.querySelectorAll('[data-open-tab]').forEach(b=>b.onclick=()=>{tab(b.dataset.openTab,true);$('tab-'+b.dataset.openTab).scrollIntoView({block:'nearest'});});tab(location.hash.slice(1)||'overview');
function drawChart(host,all){
 host.replaceChildren();if(!all.length){host.append(node('p','尚無可用走勢資料。'));return;}
 const controls=node('div',null,'chart-controls'),period=node('select',null,'form-select form-select-sm');period.style.width='auto';period.setAttribute('aria-label','走勢期間');[[1,'1 個月'],[3,'3 個月'],[6,'6 個月'],[12,'1 年']].forEach(([v,l])=>{const o=node('option',l);o.value=v;period.append(o);});period.value='3';controls.append(period);
 const colors={ma5:'#b97316',ma20:'#387cca',ma60:'#865eb2',volumeMA20:'#287c70'},visible={ma5:true,ma20:true,ma60:true,volumeMA20:true};
 Object.keys(colors).forEach(key=>{const label=node('label'),check=node('input');check.type='checkbox';check.checked=true;check.onchange=()=>{visible[key]=check.checked;render();};label.append(check,document.createTextNode(' '+(key==='volumeMA20'?'20日均量':key.toUpperCase())));label.style.color=colors[key];controls.append(label);});
 const frame=node('div'),tip=node('p','移動滑鼠、點選或使用左右方向鍵查看同日價格與成交量。','chart-tooltip');tip.setAttribute('aria-live','polite');host.append(controls,frame,tip);period.onchange=render;
 function render(){
 frame.replaceChildren();const date=new Date(all.at(-1).date+'T00:00:00Z');date.setUTCMonth(date.getUTCMonth()-Number(period.value));const cutoff=date.toISOString().slice(0,10),rows=all.filter(r=>r.date>=cutoff),values=rows.flatMap(r=>[r.close,r.high,r.low,...Object.keys(colors).filter(k=>k!=='volumeMA20'&&visible[k]).map(k=>r[k])]).filter(v=>v!=null&&v>0);
 if(!values.length){frame.append(node('p','所選期間沒有有效價格。'));return;}
 const W=900,H=470,L=74,R=20,top=28,bottom=280,vt=330,vb=420,span=W-L-R;
 let min=Math.min(...values),max=Math.max(...values),pad=(max-min)*.08||max*.01||1;min-=pad;max+=pad;
 const volMax=Math.max(1,...rows.flatMap(r=>[r.volume,visible.volumeMA20?r.volumeMA20:null]).filter(v=>v!=null&&v>=0));
 const x=i=>L+(i+.5)*span/rows.length,y=v=>bottom-(v-min)/(max-min)*(bottom-top),vy=v=>vb-v/volMax*(vb-vt);
 const svg=document.createElementNS('http://www.w3.org/2000/svg','svg');svg.setAttribute('viewBox',`0 0 ${W} ${H}`);svg.setAttribute('tabindex','0');svg.setAttribute('role','img');svg.setAttribute('aria-label',(host.dataset.chart==='candles'?'K線':'股價')+'及MA5、MA20、MA60與成交量，左右鍵檢視各日資料');
 function add(tag,attrs){const n=document.createElementNS(svg.namespaceURI,tag);Object.entries(attrs).forEach(([k,v])=>n.setAttribute(k,v));svg.append(n);return n;}
 function label(tx,ty,value){const n=add('text',{x:tx,y:ty,fill:'#63746a','font-size':13});n.textContent=value;}
 for(let i=0;i<=4;i++){const v=min+(max-min)*i/4;add('line',{x1:L,y1:y(v),x2:W-R,y2:y(v),stroke:'#e3e9e2'});label(4,y(v)+4,num(v));}
 label(4,vt-10,'量（股）');label(4,vt+12,num(volMax,0));label(L,450,rows[0].date);label(W-115,450,rows.at(-1).date);
 const width=Math.max(.6,span/rows.length*.65);
 rows.forEach((r,i)=>{const color=r.close!=null&&r.open!=null?(r.close>=r.open?'#be4c4a':'#26836b'):'#89958d';if(r.volume!=null&&r.volume>=0)add('rect',{x:x(i)-width/2,y:vy(r.volume),width,height:vb-vy(r.volume),fill:color,opacity:.65});
 if(host.dataset.chart==='candles'&&[r.open,r.high,r.low,r.close].every(v=>v!=null&&v>0)&&r.high>=Math.max(r.open,r.close)&&r.low<=Math.min(r.open,r.close)){
 add('line',{x1:x(i),x2:x(i),y1:y(r.high),y2:y(r.low),stroke:color});add('rect',{x:x(i)-width/2,y:Math.min(y(r.open),y(r.close)),width,height:Math.max(1,Math.abs(y(r.open)-y(r.close))),fill:color});}});
 function line(key,color,volume=false){let d='',continuous=false;rows.forEach((r,i)=>{const v=r[key];if(v==null||v<0){continuous=false;return;}d+=(continuous?'L':'M')+x(i)+' '+(volume?vy(v):y(v))+' ';continuous=true;});if(d)add('path',{d,stroke:color,fill:'none','stroke-width':key==='close'?2.4:1.7});}
 if(host.dataset.chart!=='candles')line('close','#244e3a');Object.keys(colors).filter(k=>visible[k]).forEach(k=>line(k,colors[k],k==='volumeMA20'));
 const cross=add('line',{x1:L,x2:L,y1:top,y2:vb,stroke:'#7b827d','stroke-dasharray':'4 3',visibility:'hidden'});let index=rows.length-1;
 function inspect(i){index=Math.max(0,Math.min(rows.length-1,i));const r=rows[index];cross.setAttribute('x1',x(index));cross.setAttribute('x2',x(index));cross.setAttribute('visibility','visible');tip.textContent=`${r.date} ｜ 開 ${num(r.open)} 高 ${num(r.high)} 低 ${num(r.low)} 收 ${num(r.close)} ｜ MA5 ${num(r.ma5)} / MA20 ${num(r.ma20)} / MA60 ${num(r.ma60)} ｜ 量 ${num(r.volume,0)} 股，前20日均量 ${num(r.volumeMA20,0)} 股`;}
 svg.onpointermove=e=>{const rect=svg.getBoundingClientRect();inspect(Math.floor(((e.clientX-rect.left)/rect.width*W-L)/span*rows.length));};svg.onpointerdown=svg.onpointermove;
 svg.onkeydown=e=>{if(e.key==='ArrowLeft'||e.key==='ArrowRight'){e.preventDefault();inspect(index+(e.key==='ArrowLeft'?-1:1));}};frame.append(svg);inspect(index);
 }render();
}
async function loadHistory(){
 $('retry-history').disabled=true;
 try{const data=await get(app.dataset.history);data.chart=data.chart.map(p=>({...p,ma5:p.mA5,ma20:p.mA20,ma60:p.mA60}));const last=data.chart.at(-1),s=data.strategy;
 const ratio=last?.volumeMA20>0&&last.volume!=null?last.volume/last.volumeMA20:null;
 const ratio5=last?.volumeMA5>0&&last.volume!=null?last.volume/last.volumeMA5:null;
 text('volume-summary',`${last?.date??'無日期'}：${ratio==null?'資料不足':ratio>=1.5?'放量':ratio<=.7?'縮量':'量能正常範圍'}。成交量 ${num(last?.volume,0)} 股；前5日均量 ${num(last?.volumeMA5,0)} 股（${num(ratio5)} 倍），前20日均量 ${num(last?.volumeMA20,0)} 股（${num(ratio)} 倍）。`);
 text('flow-summary',data.flows.map(f=>f.name+'近5日 '+(f.days5==null?'資料不足':f.days5>0?'買超 ':f.days5<0?'賣超 ':'持平 ')+num(f.days5,0)+' 股').join('；')+'。截至 '+(last?.date??'—'));
 table($('flow-detail'),['法人','當日','近5日','近20日'],data.flows.map(f=>[f.name,num(f.day,0),num(f.days5,0),num(f.days20,0)]));$('flow-detail').append(node('p','日期：'+(last?.date??'—')+' · '+data.message));
 text('trend-summary',`${last?.date??'—'}：短期${data.shortTrend}、中期${data.mediumTrend}。MA5 ${num(s.mA5)}／MA20 ${num(s.mA20)}／MA60 ${num(s.mA60)}；RSI ${num(s.rsi)}、MACD DIF ${num(s.macd)}／訊號 ${num(s.signal)}。前20日支撐參考 ${num(s.priorLow20)}、壓力參考 ${num(s.priorHigh20)}；量比 ${num(ratio)}。`);
 table($('strategy-detail'),['策略','條件','目前結果'],s.strategies.map(x=>[x.name,x.definition,x.passed==null?'資料不足':x.passed?'符合':'未符合']));$('strategy-detail').prepend(node('p',`波段分數 ${s.score==null?'未完整（'+s.earned+'/'+s.available+'）':s.score+'/100'} · ${s.state} · ${s.date??'—'}`));
 app.querySelectorAll('.stock-chart').forEach(h=>drawChart(h,data.chart));
 }catch(e){['volume-summary','flow-summary','flow-detail','trend-summary','strategy-detail'].forEach(id=>text(id,'走勢／法人資料暫時無法取得，請按「重新載入走勢」。'));app.querySelectorAll('.stock-chart').forEach(h=>h.replaceChildren(node('p','圖表資料暫時無法取得。')));}finally{$('retry-history').disabled=false;}
}
async function loadRadar(){
 $('retry-radar').disabled=true;
 try{const data=await get(app.dataset.radar),s=data.score;
 text('radar-headline',s?(s.score==null?'不完整':s.score+' / 100'):'無資料');text('radar-summary',s?`${s.level} · ${s.quote.date}；已知 ${s.earned} 分／可評權重 ${s.available}。${s.reasons.join('；')} ${s.riskFlags.join('；')}`:data.message);
 $('radar-parts').replaceChildren();(s?.parts??[]).forEach(p=>{const detail=node('details'),summary=node('summary',p.name+'：'+num(p.points,0)+' / '+p.maximum,'research-part');detail.append(summary,node('p',p.explanation));$('radar-parts').append(detail);});
 const events=data.events??[];text('event-summary',events.length?`${events.length} 則來源公告；最近：${events[0].title}`:'來源未提供此標的公告，不代表沒有重大消息。');$('event-detail').replaceChildren();
 events.forEach(e=>{const article=node('article',null,'research-event');article.append(node('h3',e.title),node('p',e.summary),node('p',e.publishedAt+' · '+e.source+' · '+e.category));try{const u=new URL(e.sourceUrl);if(['https:','http:'].includes(u.protocol)){const a=node('a','查看原始公告');a.href=u.href;a.target='_blank';a.rel='noopener noreferrer';article.append(a);}}catch{}$('event-detail').append(article);});
 if(!events.length)$('event-detail').append(node('p','來源未提供此標的公告，不代表沒有重大消息。'));(data.warnings??[]).forEach(w=>$('event-detail').append(node('p',w,'muted')));$('event-detail').append(node('p','來源更新時間：'+data.updatedAt));
 }catch(e){text('radar-headline','暫無資料');text('radar-summary','Radar 資料暂時無法取得，請重試。');text('event-summary','公告來源暫時無法取得。');text('event-detail','公告來源暫時無法取得，請從總覽重新載入。');}finally{$('retry-radar').disabled=false;}
}
async function personal(){if(app.dataset.auth!=='true')return;
 try{const m=await get(app.dataset.membership);const targets={favorite:['☆ 已加入自選','Favorites'],swing:['↗ 查看波段分析','Swing?symbol='+encodeURIComponent(symbol)],tracking:['◎ 查看績效追蹤','RadarTracking?Mine=true&Symbol='+encodeURIComponent(symbol)]};Object.entries(targets).forEach(([key,[label,path]])=>{if(m[key]){const b=app.querySelector(`[data-membership-button="${key}"]`),a=node('a',label,'btn btn-light');a.href='/StockAnalysis/'+path;b.closest('form').replaceWith(a);}});text('membership-status',m.held?'已有此股票的虛擬持倉。':'尚未持有此股票的虛擬部位。');}catch{text('membership-status','個人清單狀態暫時無法取得，可重整頁面重試。');}
 try{const p=await get(app.dataset.performance);const box=$('performance-detail');box.replaceChildren();box.append(node('h3','個人追蹤快照（最近20筆）'));
 const rec=node('div');box.append(rec);table(rec,['快照日期','分數','1D','5D','20D'],p.recommendations.map(x=>[x.tradeDate,num(x.score,0),x.r1==null?'待更新':num(x.r1)+'%',x.r5==null?'待更新':num(x.r5)+'%',x.r20==null?'待更新':num(x.r20)+'%']));if(!p.recommendations.length)rec.append(node('p','尚無個人追蹤。'));box.append(node('p','以上為推薦收盤基準，不等於個人加入後報酬。個人起算績效請見個人追蹤。'));
 box.append(node('p','虛擬交易已實現損益：'+num(p.realizedProfit)+' 元（已扣交易成本）'));box.append(node('h3','虛擬委託（最近20筆）'));const orders=node('div');box.append(orders);table(orders,['日期','方向','股數','狀態'],p.orders.map(x=>[x.date,x.side==='Buy'?'買進':'賣出',num(x.quantity,0),x.status]));if(!p.orders.length)orders.append(node('p','尚無虛擬委託。'));
 }catch{text('performance-detail','個人績效暫時無法取得，可重整頁面或開啟下方明細。');}
}
$('retry-history').onclick=loadHistory;$('retry-radar').onclick=loadRadar;loadHistory();loadRadar();personal();
})();
