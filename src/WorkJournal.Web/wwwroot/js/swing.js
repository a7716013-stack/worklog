(() => {
"use strict";
const app=document.getElementById("swing-app"); if(!app)return;
const $=id=>document.getElementById(id);
const key="workjournal.swing.watchlist.v1";
const data=new Map(), failures=new Map(), pending=new Set();
let tracked=[], searchResults=[], searchAbort, active=null, run=0;
const number=(v,d=2)=>v==null?"—":Number(v).toLocaleString("zh-TW",{minimumFractionDigits:d,maximumFractionDigits:d});
const el=(tag,text,cls)=>{const x=document.createElement(tag);if(text!=null)x.textContent=text;if(cls)x.className=cls;return x;};
function button(text,action,cls="btn btn-light btn-sm"){const b=el("button",text,cls);b.type="button";b.addEventListener("click",action);return b;}
function valid(s){return s && typeof s.symbol==="string" && /^[0-9]{4}[0-9A-Z]{0,2}$/.test(s.symbol) && typeof s.name==="string";}
function load(){
 try {const value=JSON.parse(localStorage.getItem(key)||"[]");return Array.isArray(value)?value.filter(valid).filter((v,i,a)=>a.findIndex(x=>x.symbol===v.symbol)===i).slice(0,20):[];}
 catch { $("swing-status").textContent="無法讀取追蹤清單，請確認瀏覽器允許網站儲存資料。";return []; }
}
function save(next){
 try{localStorage.setItem(key,JSON.stringify(next));tracked=next;return true;}
 catch{$("swing-status").textContent="無法儲存追蹤清單，請確認瀏覽器儲存空間與設定。";return false;}
}
async function request(base,param,signal){
 const url=new URL(base,location.origin);Object.entries(param).forEach(([k,v])=>url.searchParams.set(k,v));
 const response=await fetch(url,{signal,headers:{Accept:"application/json"}});
 let json;try{json=await response.json();}catch{throw new Error("服務未回傳有效資料，請稍後重試。");}
 if(!response.ok)throw new Error(json.message||"資料服務暫時無法使用。");return json;
}
function add(stock){
 if(tracked.some(x=>x.symbol===stock.symbol))return;
 if(tracked.length>=20){$("swing-search-status").textContent="最多追蹤 20 檔，請先移除不需追蹤的股票。";return;}
 if(save([...tracked,stock])){renderSearch();render();refresh();}
}
function remove(symbol){
 if(save(tracked.filter(x=>x.symbol!==symbol))){
  data.delete(symbol);failures.delete(symbol);
  if(active===symbol){active=null;$("swing-detail").hidden=true;}
  renderSearch();render();
 }
}
function renderSearch(){
 const host=$("swing-search-results");host.replaceChildren();
 for(const stock of searchResults){
  const row=el("div",null,"swing-search-row");
  const label=el("div");label.append(el("strong",stock.symbol+" "+stock.name),el("small",(stock.market==="twse"?"上市":"上櫃")+(stock.isEtf?" · ETF":""),"muted d-block"));
  const has=tracked.some(x=>x.symbol===stock.symbol);
  const b=button(has?"已追蹤":"加入追蹤",()=>add(stock),has?"btn btn-light btn-sm":"btn btn-forest btn-sm");b.disabled=has;
  row.append(label,b);host.append(row);
 }
}
$("swing-search-form").addEventListener("submit",async e=>{
 e.preventDefault();searchAbort?.abort();searchAbort=new AbortController();
 const q=$("swing-query").value.trim();
 searchResults=[];renderSearch();
 if(!q){$("swing-search-status").textContent="請輸入股票代號或名稱。";return;}
 $("swing-search-status").textContent="搜尋中…";
 try{searchResults=await request(app.dataset.searchUrl,{q},searchAbort.signal);renderSearch();$("swing-search-status").textContent=searchResults.length?"搜尋結果（最多 12 筆）":"查無符合的上市櫃股票。";}
 catch(error){if(error.name!=="AbortError")$("swing-search-status").textContent=error.message;}
});
function metric(label,value){const box=el("div");box.append(el("dt",label),el("dd",value));return box;}
function stateClass(state){return state==="優先研究"?"strong":state==="風險升高"?"risk":state==="資料不足"?"missing":"observe";}
function render(){
 const list=tracked.map(s=>({stock:s,info:data.get(s.symbol)})).sort((a,b)=>(b.info?.summary.score??-1)-(a.info?.summary.score??-1)||a.stock.symbol.localeCompare(b.stock.symbol));
 const summaries=list.map(x=>x.info?.summary).filter(Boolean);
 $("swing-count").textContent=tracked.length;
 for(const [id,state] of [["swing-strong","優先研究"],["swing-observe","持續觀察"],["swing-risk","風險升高"]]) $(id).textContent=summaries.length?String(summaries.filter(s=>s.state===state).length):"—";
 $("swing-empty").hidden=tracked.length>0;
 const host=$("swing-cards");host.replaceChildren();
 let visible=0;
 for(const [index,{stock,info}] of list.entries()){
  const s=info?.summary;
  if($("swing-filter").value && (s?.state??"資料不足")!==$("swing-filter").value)continue;
  visible++;
  const card=el("article",null,"panel swing-card");card.dataset.symbol=stock.symbol;
  const head=el("div",null,"swing-card-head");
  const title=el("div");title.append(el("p","#"+(index+1)+" · "+stock.symbol,"eyebrow"),el("h3",stock.name));
  const score=el("div",null,"swing-score");score.append(el("strong",s?.score==null?"—":s.score),el("small","/ 100"));
  head.append(title,score);card.append(head);
  if(info){
   card.append(el("span",s.state,"swing-state "+stateClass(s.state)));
   card.append(el("p","行情 "+(s.date||"—")+" · 法人 "+(s.flowDate||"—"),"muted mt-3"));
   if(s.date && (Date.now()-Date.parse(s.date+"T16:00:00+08:00"))/86400000>4)card.append(el("p","行情距今已超過 4 個日曆日，請確認是否停牌或來源尚未更新。","swing-warning"));
   if(s.score==null)card.append(el("p","完整評分資料不足；已知得分 "+s.earned+"，可評項目 "+s.available+"/100，未補值。","swing-warning"));
   const metrics=el("dl",null,"swing-card-metrics");
   metrics.append(metric("收盤價",number(s.close)),metric("漲跌幅",number(s.changePercent)+"%"),metric("RSI14",number(s.rsi)),metric("成交量比",number(s.volumeRatio)+" 倍"));
   card.append(metrics);
   const tags=el("div",null,"swing-tags");
   for(const strategy of s.strategies.filter(x=>x.passed===true).slice(0,3))tags.append(el("span",strategy.name));
   if(!tags.children.length)tags.append(el("span","尚無符合的策略組合"));
   card.append(tags);
  }else card.append(el("p",failures.get(stock.symbol)||"正在載入日行情與法人資料…","muted mt-3"));
  const actions=el("div",null,"swing-card-actions");
  const detail=button("詳細分析",()=>showDetail(stock.symbol),"btn btn-forest btn-sm");detail.disabled=!info;
  actions.append(detail,button("移除追蹤",()=>remove(stock.symbol)));
  if(failures.has(stock.symbol))actions.append(button("重試",()=>{failures.delete(stock.symbol);refresh();}));
  card.append(actions);host.append(card);
 }
 if(tracked.length && !visible)host.append(el("p","目前沒有符合此狀態的追蹤股票。","muted"));
 $("swing-status").textContent=tracked.length?"已載入 "+summaries.length+"/"+tracked.length+" 檔"+(pending.size?" · 更新中…":"")+(failures.size?" · 部分資料載入失敗，可個別重試":""):"";
}
async function refresh(force=false){
 if(force){for(const s of tracked){data.delete(s.symbol);failures.delete(s.symbol);}}
 const queue=tracked.filter(s=>!data.has(s.symbol)&&!pending.has(s.symbol));
 async function worker(){
  while(queue.length){
   const stock=queue.shift();if(!tracked.some(x=>x.symbol===stock.symbol)||pending.has(stock.symbol)||data.has(stock.symbol))continue;
   pending.add(stock.symbol);render();
   try{const info=await request(app.dataset.analysisUrl,{symbol:stock.symbol});if(tracked.some(x=>x.symbol===stock.symbol)){data.set(stock.symbol,info);failures.delete(stock.symbol);}}
   catch(e){if(tracked.some(x=>x.symbol===stock.symbol))failures.set(stock.symbol,e.message);}
   finally{pending.delete(stock.symbol);render();}
  }
 }
 await Promise.all([worker(),worker()]);
 if(active&&data.has(active))showDetail(active,false);
}
$("swing-filter").addEventListener("change",render);
$("swing-refresh").addEventListener("click",()=>{if(!pending.size)refresh(true);});
$("swing-close-detail").addEventListener("click",()=>{$("swing-detail").hidden=true;active=null;run++;});
function showDetail(symbol,scroll=true){
 const info=data.get(symbol);if(!info)return;
 if(active!==symbol){run++;$("swing-backtest-result").replaceChildren();$("swing-run-backtest").disabled=false;}
 active=symbol;const s=info.summary;
 $("swing-detail").hidden=false;
 $("swing-detail-title").textContent=info.stock.symbol+" "+info.stock.name+" · "+s.state;
 $("swing-detail-note").textContent=info.message+" 行情："+(s.date||"—")+"；法人："+(s.flowDate||"—")+"。";
 const score=$("swing-score-breakdown");score.replaceChildren();
 for(const r of s.rules){
  const row=el("div",null,"swing-rule");
  row.append(el("span",(r.passed==null?"— ":r.passed?"✓ ":"✕ ")+r.label),
   el("strong",r.passed==null?"資料不足":(r.passed?(r.points>0?"+":"")+r.points:"0")+" 分",r.points<0&&r.passed?"swing-negative":""));
  score.append(row);
 }
 const strategies=$("swing-strategies");strategies.replaceChildren();
 for(const strategy of s.strategies){
  const d=el("details",null,"swing-strategy");d.append(el("summary",(strategy.passed==null?"— ":strategy.passed?"✓ ":"✕ ")+strategy.name),el("p",strategy.definition,"muted"));strategies.append(d);
 }
 const metrics=$("swing-metrics");metrics.replaceChildren();
 for(const [label,value] of [["MA5",s.mA5],["MA10",s.mA10],["MA20",s.mA20],["MA60",s.mA60],["RSI14",s.rsi],["MACD DIF",s.macd],["MACD 訊號線",s.signal],["前 20 日高點",s.priorHigh20],["前 20 日低點",s.priorLow20],["前 20 日均量（張）",s.averageVolume==null?null:s.averageVolume/1000],["外資近 5 日（張）",s.foreign5==null?null:s.foreign5/1000],["投信近 5 日（張）",s.trust5==null?null:s.trust5/1000],["外資近 20 日（張）",s.foreign20==null?null:s.foreign20/1000],["投信近 20 日（張）",s.trust20==null?null:s.trust20/1000],["外資連買（最多 20 日）",s.foreignStreak],["投信連買（最多 20 日）",s.trustStreak]])metrics.append(metric(label,number(value)));
 drawChart(info.chart);
 if(scroll)$("swing-detail").scrollIntoView({behavior:"smooth",block:"start"});
}
function drawChart(points){
 const canvas=$("swing-chart"),ratio=window.devicePixelRatio||1,width=Math.max(canvas.clientWidth,280),height=520;
 canvas.width=width*ratio;canvas.height=height*ratio;
 const c=canvas.getContext("2d");c.scale(ratio,ratio);c.clearRect(0,0,width,height);
 c.font="11px Segoe UI";c.fillStyle="#748079";
 if(!points.length){c.fillText("尚無可用圖表資料",20,40);return;}
 const left=50,right=12,plot=width-left-right,step=plot/points.length;
 const x=i=>left+(i+.5)*step;
 function panel(top,h,values,label,minOverride,maxOverride){
  const v=values.filter(a=>a!=null&&Number.isFinite(a));
  let min=minOverride??(v.length?Math.min(...v):0),max=maxOverride??(v.length?Math.max(...v):1);
  if(max===min){max+=1;min-=1;}
  const y=value=>top+h-(value-min)/(max-min)*h;
  c.fillStyle="#748079";c.fillText(label,left,top-8);
  for(let i=0;i<=2;i++){let val=min+(max-min)*i/2;let yy=y(val);c.strokeStyle="#e3e8e1";c.beginPath();c.moveTo(left,yy);c.lineTo(width-right,yy);c.stroke();c.fillText(number(val,1),0,yy+4);}
  return y;
 }
 function line(key,color,y){c.strokeStyle=color;c.lineWidth=1.3;c.beginPath();let started=false;points.forEach((p,i)=>{if(p[key]==null){started=false;return;}if(!started){c.moveTo(x(i),y(p[key]));started=true;}else c.lineTo(x(i),y(p[key]));});c.stroke();}
 const prices=points.flatMap(p=>[p.high,p.low,p.mA5,p.mA10,p.mA20,p.mA60]);
 const y=panel(24,200,prices,"K 線 / MA");
 points.forEach((p,i)=>{if([p.open,p.close,p.high,p.low].some(v=>v==null))return;c.strokeStyle=c.fillStyle=p.close>=p.open?"#c45b4f":"#328563";c.beginPath();c.moveTo(x(i),y(p.high));c.lineTo(x(i),y(p.low));c.stroke();c.fillRect(x(i)-Math.max(1,step*.55)/2,Math.min(y(p.open),y(p.close)),Math.max(1,step*.55),Math.max(1,Math.abs(y(p.open)-y(p.close))));});
 [["mA5","#c98b32"],["mA10","#9765ab"],["mA20","#417bc1"],["mA60","#7d8c87"]].forEach(([k,col])=>line(k,col,y));
 const ry=panel(260,60,[],"RSI14",0,100);line("rsi","#9765ab",ry);
 const my=panel(353,65,points.flatMap(p=>[p.macd,p.signal,p.histogram,0]),"MACD / DIF / Signal");
 points.forEach((p,i)=>{if(p.histogram==null)return;c.fillStyle=p.histogram>=0?"#c45b4f":"#328563";c.fillRect(x(i)-step*.3,Math.min(my(0),my(p.histogram)),Math.max(1,step*.6),Math.max(1,Math.abs(my(p.histogram)-my(0))));});
 line("macd","#c98b32",my);line("signal","#417bc1",my);
 const vy=panel(450,42,points.map(p=>p.volume==null?null:p.volume/1000),"成交量（張）",0);
 points.forEach((p,i)=>{if(p.volume==null)return;c.fillStyle="#789b86";c.fillRect(x(i)-step*.3,vy(p.volume/1000),Math.max(1,step*.6),492-vy(p.volume/1000));});
 c.fillStyle="#748079";c.fillText(points[0].date,left,515);c.fillText(points.at(-1).date,Math.max(left,width-85),515);
 const display=i=>{const p=points[Math.max(0,Math.min(points.length-1,i))];$("swing-chart-value").textContent=p.date+" 開 "+number(p.open)+" 高 "+number(p.high)+" 低 "+number(p.low)+" 收 "+number(p.close)+" · RSI "+number(p.rsi)+" · MACD "+number(p.macd);};
 canvas.onpointermove=e=>display(Math.floor((e.offsetX-left)/step));canvas.tabIndex=0;
 let cursor=points.length-1;
 canvas.onkeydown=e=>{if(e.key==="ArrowLeft"||e.key==="ArrowRight"){e.preventDefault();cursor=Math.max(0,Math.min(points.length-1,cursor+(e.key==="ArrowLeft"?-1:1)));display(cursor);}};
 display(cursor);
}
new ResizeObserver(()=>{if(active&&data.has(active)&&!$("swing-detail").hidden)drawChart(data.get(active).chart);}).observe($("swing-chart"));
$("swing-run-backtest").addEventListener("click",async()=>{
 if(!active)return;const symbol=active,sequence=++run,host=$("swing-backtest-result"),b=$("swing-run-backtest");
 b.disabled=true;host.textContent="正在讀取近 5 年資料並計算，請稍候…";
 try{
  const r=await request(app.dataset.backtestUrl,{symbol});
  if(sequence!==run||active!==symbol)return;
  host.replaceChildren(el("p",r.message,"mt-3"));
  if(r.from)host.append(el("p","實際期間 "+r.from+" ～ "+r.to+" · "+r.observations+" 交易日 · 可評 "+r.scoredDays+" 日／資料不足 "+r.missingDays+" 日","muted"));
  if(r.totalReturn!=null){
   const stats=el("dl",null,"swing-metrics");
   for(const [label,v] of [["已平倉筆數",r.trades.length],["勝率（%）",r.winRate],["平均每筆報酬（%）",r.averageReturn],["含期末持倉總報酬（%）",r.totalReturn],["最大回撤（%）",r.maxDrawdown],["期末未平倉筆數",r.openPositions]])stats.append(metric(label,number(v)));
   host.append(stats);
   if(r.trades.length){
    const wrapper=el("div",null,"table-responsive");const table=el("table",null,"table text-nowrap");
    const head=el("tr");["進場日","出場日","進場價","出場價","報酬（%）","出場原因"].forEach(x=>head.append(el("th",x)));
    const thead=el("thead");thead.append(head);const body=el("tbody");
    for(const t of r.trades.slice().reverse()){const row=el("tr");[t.entry,t.exit,number(t.entryPrice),number(t.exitPrice),number(t.returnPercent),t.reason].forEach(v=>row.append(el("td",v)));body.append(row);}
    table.append(thead,body);wrapper.append(table);host.append(wrapper);
   }
  }
 }catch(e){if(sequence===run)host.textContent=e.message;}
 finally{if(sequence===run)b.disabled=false;}
});
window.addEventListener("storage",e=>{if(e.key===key){tracked=load();if(active&&!tracked.some(x=>x.symbol===active)){active=null;$("swing-detail").hidden=true;}renderSearch();render();refresh();}});
tracked=load();render();refresh();
})();
