"use strict";
window.unifiedScorePanel = function (comparison) {
 const box=document.createElement("details"); box.className="panel unified-score";
 const add=(tag,text,parent=box)=>{const e=document.createElement(tag);e.textContent=text;parent.append(e);return e;};
 add("summary","模型比較 · Legacy / Candidate 研究預覽");
 if(!comparison){add("p","舊快照未保存版本化比較；保留當時分數，不以新版回填。");return box;}
 for(const score of [comparison.legacy,comparison.candidate]) {
  add("h4",score.modelVersion);add("p",`分數 ${score.totalScore??"—"} · 可用權重 ${score.availableWeight}/100 · ${score.scoreStatus}`);
  add("p",score.researchStatus); const list=add("ul","");
  for(const c of score.componentScores)add("li",`${c.code}: ${c.points??"—"} / ${c.weight} · ${c.status} · ${c.reason}`,list);
  if(score.blockingReasons.length)add("p","缺少資料／阻擋原因："+score.blockingReasons.join("；"));
  add("small",`公式 ${score.formulaDigest} · 參數 ${score.parameterSetId??"未核准"} · 資料集 ${score.datasetSnapshotId??"無"} · cutoff ${score.dataCutoff??"未記錄"}`);
 }
 add("p",`差異 ${comparison.difference??"不可比較"} · Baseline ${comparison.baselineStatus} · OOS ${comparison.oosStatus} · ${comparison.promotionStatus}`);
 add("p","70／75 分門檻僅屬 Legacy；Candidate 尚未正式驗證，不是推薦或交易訊號。");return box;
};

for(const host of document.querySelectorAll("[data-scoring-routing]"))host.querySelector("button").addEventListener("click",async()=>{
 const status=host.querySelector('[role="status"]');status.textContent="讀取資格中…";
 try{const response=await fetch("/api/v1/scoring/readiness?mode="+encodeURIComponent(host.querySelector("select").value),{credentials:"same-origin"});
 if(!response.ok){status.textContent=({401:"請先登入。",403:"此模式僅供已授權管理者研究。",409:"Candidate 尚未 Promotion，禁止作為主要模型。",429:"請稍後再試。"})[response.status]||"資格服務暫時不可用。";return;}
 const value=await response.json();status.textContent=value.mode+" · "+value.candidate.researchStatus+" · "+value.candidate.blockingReasons.join("；")+" · Baseline "+value.baselineStatus+" / OOS "+value.oosStatus+" / "+value.promotionStatus;
 }catch{status.textContent="無法讀取研究資格，保留 Legacy。";}
});
