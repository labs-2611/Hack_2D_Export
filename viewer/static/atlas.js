import {isSmoothPolyline,tracePolyline} from "/curve-render.js";
import {pixelsPerModelMeter,displayedScaleDenominator} from "/view-scale.js";
import {drawAnnotationText} from "/annotation-text.js";
import {composeElementOverlay,sha256Hex} from "/element-overlay.js";
const $=id=>document.getElementById(id),feet=.3048;
const canvas=$("drawing"),ctx=canvas.getContext("2d");
let manifest,centerX=0,centerY=0,scale=1,drag=null,frame=0;
const views=new Map(),baseViews=new Map(),modes=new Map(),hiddenCategories=new Set();
let elementOverlay=null,overlayEnabled=true,overlayBytes=0;
const editPalette=["#bd00c7","#006fd6"],editColors=new Map();
const state={text:true,patterns:true,smooth:true,trueViewId:null,screenPpi:96,selectedKey:null,highlightFamily:"",highlightColor:"#e34b27"};
function worldToScreen(x,y){return [(x-centerX)*scale+canvas.clientWidth/2,(centerY-y)*scale+canvas.clientHeight/2]}
function screenToWorld(x,y){return [centerX+(x-canvas.clientWidth/2)/scale,centerY-(y-canvas.clientHeight/2)/scale]}
function schedule(){if(!frame)frame=requestAnimationFrame(()=>{frame=0;draw()})}
function primitivePoints(p){const [kind,d]=p;
  if(kind.endsWith(":Line")&&d.length===6)return [[d[0]*feet,d[1]*feet],[d[3]*feet,d[4]*feet]];
  if(kind==="tessellatedPolyline"||kind==="tessellatedFilledPolyline"||kind==="tessellatedLine"||kind==="polyline"){
    const points=[];for(let i=0;i<d.length;i+=3)points.push([d[i]*feet,d[i+1]*feet]);return points;
  }
  if((kind.endsWith(":Arc")&&d.length===12)||(kind.endsWith(":Ellipse")&&d.length===13)){
    const ellipse=kind.endsWith(":Ellipse"),rx=d[9],ry=ellipse?d[10]:rx,a0=ellipse?d[11]:d[10],a1=ellipse?d[12]:d[11];
    const n=Math.max(12,Math.ceil(Math.abs(a1-a0)*50)),points=[];
    for(let i=0;i<=n;i++){const a=a0+(a1-a0)*i/n;points.push([(d[0]+d[3]*rx*Math.cos(a)+d[6]*ry*Math.sin(a))*feet,(d[1]+d[4]*rx*Math.cos(a)+d[7]*ry*Math.sin(a))*feet])}return points;
  }
  return [];
}
function rgb(c){return Array.isArray(c)?`rgb(${c[0]},${c[1]},${c[2]})`:"#000"}
function lineWidth(raw){return Math.max(state.trueViewId ? .25 : .65,Math.min(5,(raw||.00025)*12*state.screenPpi))}
function dashPattern(pattern,width){
  if(!pattern?.segments?.length)return [];
  const out=[];
  for(const s of pattern.segments){let px=s.type==="Dot"?Math.max(1,width):s.lengthFeet*12*state.screenPpi;
    out.push(Math.max(.8,Math.min(200,px)))}
  return out.length%2===0?out:[];
}
function prepare(data){
  const patternMap=new Map(data.patterns.map(p=>[`${p.sourceDocument}\u0000${p.id}`,p]));
  const ops=[];let minX=Infinity,minY=Infinity,maxX=-Infinity,maxY=-Infinity;
  for(const e of data.elements){e._ops=[];let ex0=Infinity,ey0=Infinity,ex1=-Infinity,ey1=-Infinity;
    for(const p of e.p){const text=p[0]==="text",d=p[1];let op;
      if(text){const x=d[0]*feet,y=d[1]*feet;op={text:p[3],x,y,angle:Math.atan2(-d[4],d[3]),height:d[10]*feet,textWidth:d[9]*feet,textStyle:p[8]||null};ex0=Math.min(ex0,x);ey0=Math.min(ey0,y);ex1=Math.max(ex1,x);ey1=Math.max(ey1,y)}
      else{const points=primitivePoints(p);if(points.length<2)continue;op={points,smooth:p[0]==="tessellatedPolyline"&&isSmoothPolyline(points)};
        for(const [x,y] of points){ex0=Math.min(ex0,x);ey0=Math.min(ey0,y);ex1=Math.max(ex1,x);ey1=Math.max(ey1,y)}}
      op.color=rgb(p[2]);op.widthRaw=p[6];op.linePattern=patternMap.get(`${e.s}\u0000${p[7]}`);op.width=lineWidth(op.widthRaw);op.dash=dashPattern(op.linePattern,op.width);
      e._ops.push(op);ops.push({e,op,sequence:p[4]});
    }
    e._bounds={minX:ex0,minY:ey0,maxX:ex1,maxY:ey1};
    if(Number.isFinite(ex0)){minX=Math.min(minX,ex0);minY=Math.min(minY,ey0);maxX=Math.max(maxX,ex1);maxY=Math.max(maxY,ey1)}
  }
  ops.sort((a,b)=>a.sequence-b.sequence);
  return {ops,bounds:{minX,minY,maxX,maxY},data};
}
async function loadView(id){
  if(views.has(id))return views.get(id);
  const response=await fetch(`/atlas-view/${id}.json`);if(!response.ok)throw Error(`View ${id}: HTTP ${response.status}`);
  const bytes=await response.arrayBuffer(),base=JSON.parse(new TextDecoder().decode(bytes));
  if(elementOverlay?.viewId===id){const actual=await sha256Hex(bytes);if(actual!==elementOverlay.baseSha256)throw Error(`Second export base hash mismatch for view ${id}`)}
  baseViews.set(id,base);
  const prepared=prepare(overlayEnabled&&elementOverlay?.viewId===id?composeElementOverlay(base,elementOverlay):base);
  views.set(id,prepared);updateFamilyOptions();return prepared;
}
function refreshOverlay(){for(const [id,base] of baseViews){views.set(id,prepare(overlayEnabled&&elementOverlay?.viewId===id?composeElementOverlay(base,elementOverlay):base))}schedule()}
function zoomToChanges(){if(!elementOverlay)return;const view=views.get(elementOverlay.viewId);if(!view)return;
  const keys=new Set(elementOverlay.changes.map(change=>change.key));let minX=Infinity,minY=Infinity,maxX=-Infinity,maxY=-Infinity;
  for(const e of view.data.elements){if(!keys.has(e.key))continue;const b=e._bounds;minX=Math.min(minX,b.minX);minY=Math.min(minY,b.minY);maxX=Math.max(maxX,b.maxX);maxY=Math.max(maxY,b.maxY)}
  if(!Number.isFinite(minX))return;centerX=(minX+maxX)/2;centerY=(minY+maxY)/2;scale=Math.min(canvas.clientWidth/Math.max(1,(maxX-minX)*4),canvas.clientHeight/Math.max(1,(maxY-minY)*4));
  if(state.trueViewId){state.trueViewId=null;restyleViews()}schedule();
}
function active(){return manifest.views.filter(v=>modes.get(v.id)!=="hidden"&&views.has(v.id))}
function restyleViews(){for(const view of views.values())for(const {op} of view.ops){op.width=lineWidth(op.widthRaw);op.dash=dashPattern(op.linePattern,op.width)}}
function fit(){if(state.trueViewId){state.trueViewId=null;restyleViews()}let minX=Infinity,minY=Infinity,maxX=-Infinity,maxY=-Infinity;for(const v of active()){
  const b=views.get(v.id).bounds;minX=Math.min(minX,b.minX);minY=Math.min(minY,b.minY);maxX=Math.max(maxX,b.maxX);maxY=Math.max(maxY,b.maxY)}
  if(!Number.isFinite(minX)||maxX<=minX||maxY<=minY)return;centerX=(minX+maxX)/2;centerY=(minY+maxY)/2;scale=Math.min(canvas.clientWidth/((maxX-minX)*1.08),canvas.clientHeight/((maxY-minY)*1.08));schedule();
}
function highlighted(e){return state.selectedKey?e.key===state.selectedKey:!!state.highlightFamily&&e.f===state.highlightFamily}
function updateFamilyOptions(){const select=$("highlightFamily"),value=state.highlightFamily,names=new Set();
  for(const view of manifest.views)for(const name of Object.keys(view.families||{}))names.add(name);
  select.replaceChildren(new Option("Click an element",""),...[...names].sort().map(name=>new Option(name,name)));
  if(names.has(value))select.value=value;else{state.highlightFamily="";select.value=""}
}
function setTrueScale(id=Number($("scaleView").value)){const view=manifest.views.find(v=>v.id===id);if(!view)return;
  const b=views.get(id)?.bounds;if(!b)return;centerX=(b.minX+b.maxX)/2;centerY=(b.minY+b.maxY)/2;
  scale=pixelsPerModelMeter(state.screenPpi,view.scale);state.trueViewId=id;restyleViews();schedule();
}
async function showTrueView(){const id=Number($("scaleView").value);for(const v of manifest.views)modes.set(v.id,v.id===id?"full":"hidden");
  hiddenCategories.clear();document.querySelectorAll(".category input").forEach(input=>input.checked=true);
  state.text=true;state.patterns=true;$("showText").checked=true;$("linePatterns").checked=true;
  renderLayers();try{await loadView(id);setTrueScale(id)}catch(error){$("status").textContent=error.message}
}
function renderable(e,mode){return !hiddenCategories.has(e.c)&&(mode==="full"||mode==="annotations"&&e.k==="Annotation"||mode==="model"&&e.k!=="Annotation")}
function draw(){if(!manifest)return;const w=canvas.clientWidth,h=canvas.clientHeight;ctx.clearRect(0,0,w,h);let primitives=0,elements=0,highlights=0;
  for(const v of active()){const {ops}=views.get(v.id),mode=modes.get(v.id),seen=new Set(),visibility=new Map();let activeStyle=null;
    const flush=()=>{if(activeStyle!==null){ctx.stroke();activeStyle=null}};
    for(const {e,op} of ops){let visible=visibility.get(e);
      if(visible===undefined){const b=e._bounds,p0=worldToScreen(b.minX,b.maxY),p1=worldToScreen(b.maxX,b.minY);
        visible=renderable(e,mode)&&!(p1[0]<-30||p0[0]>w+30||p1[1]<-30||p0[1]>h+30);visibility.set(e,visible)}
      if(!visible||op.text&&!state.text)continue;
      if(!seen.has(e)&&highlighted(e))highlights++;
      seen.add(e);primitives++;
      const edited=overlayEnabled&&v.id===elementOverlay?.viewId?editColors.get(e.key):null;
      const color=highlighted(e)?state.highlightColor:edited||op.color;
      const width=highlighted(e)?Math.max(1.5,op.width):edited?Math.max(2.5,op.width):op.width;
      if(op.text){flush();const [screenX,screenY]=worldToScreen(op.x,op.y);
        ctx.fillStyle=color;drawAnnotationText(ctx,{...op,screenX,screenY},scale);continue}
      const dash=state.patterns?op.dash:[],style=`${color}/${width}/${dash.join(",")}`;
      if(activeStyle!==style){flush();activeStyle=style;ctx.strokeStyle=color;ctx.lineWidth=width;ctx.setLineDash(dash);ctx.beginPath()}
      tracePolyline(ctx,op.points,worldToScreen,state.smooth&&op.smooth);
    }flush();elements+=seen.size;
  }
  ctx.setLineDash([]);$("counts").textContent=`${active().length} views · ${elements.toLocaleString()} graphics · ${primitives.toLocaleString()} primitives`;
  const native=manifest.views.find(v=>v.id===state.trueViewId),denominator=displayedScaleDenominator(state.screenPpi,scale);
  $("scaleReadout").textContent=native?`${native.name} · native 1:${native.scale} · display 1:${denominator}`:`Display scale about 1:${denominator} · fit/zoom mode`;
  $("drawingTitle").textContent=native?`${native.name} · true view scale 1:${native.scale}`:`Drawing views · display about 1:${denominator}`;
  $("highlightStatus").textContent=state.selectedKey||state.highlightFamily?`${highlights} visible graphics highlighted`:"No highlight";
}
function resize(){const dpr=Math.min(devicePixelRatio||1,2),w=canvas.clientWidth,h=canvas.clientHeight;canvas.width=Math.round(w*dpr);canvas.height=Math.round(h*dpr);ctx.setTransform(dpr,0,0,dpr,0,0);schedule()}
function zoom(factor,px=canvas.clientWidth/2,py=canvas.clientHeight/2){const before=screenToWorld(px,py);scale=Math.max(.2,Math.min(8000,scale*factor));const after=screenToWorld(px,py);centerX+=before[0]-after[0];centerY+=before[1]-after[1];if(state.trueViewId){state.trueViewId=null;restyleViews()}schedule()}
function renderLayers(){
  const box=$("layers");box.replaceChildren();for(const v of manifest.views){const row=document.createElement("div");row.className="layer";
    const title=document.createElement("strong");title.textContent=v.name;const note=document.createElement("small");note.textContent=`${v.type} · 1:${v.scale} · ${v.elements} graphics · ${v.annotationPrimitives} annotations`;
    const select=document.createElement("select");for(const [value,label] of [["hidden","Hidden"],["full","Full drawing"],["model","Model only"],["annotations","Annotations only"]])select.add(new Option(label,value));
    select.value=modes.get(v.id);select.onchange=async()=>{modes.set(v.id,select.value);if(select.value!=="hidden"){try{await loadView(v.id)}catch(error){$("status").textContent=error.message;return}}fit();schedule()};
    row.append(title,note,select);box.append(row)}
}
function renderControls(){
  renderLayers();$("scaleView").replaceChildren(...manifest.views.map(v=>new Option(`${v.name} · ${v.type} · 1:${v.scale}`,v.id)));
  const preferred=manifest.views.find(v=>v.type==="FloorPlan"&&v.name==="L4")||manifest.views[0];$("scaleView").value=String(preferred.id);
  const counts=new Map();for(const v of manifest.views)for(const [name,n] of Object.entries(v.categories))counts.set(name,(counts.get(name)||0)+n);
  const cats=$("categories");cats.replaceChildren();for(const [name,n] of [...counts].sort((a,b)=>a[0].localeCompare(b[0]))){const row=document.createElement("label");row.className="category";const input=document.createElement("input");input.type="checkbox";input.checked=true;input.onchange=()=>{input.checked?hiddenCategories.delete(name):hiddenCategories.add(name);schedule()};const label=document.createElement("span");label.textContent=name;const count=document.createElement("small");count.textContent=n.toLocaleString();row.append(input,label,count);cats.append(row)}
}
canvas.addEventListener("wheel",event=>{event.preventDefault();const r=canvas.getBoundingClientRect();zoom(event.deltaY<0?1.3:1/1.3,event.clientX-r.left,event.clientY-r.top)},{passive:false});
canvas.addEventListener("pointerdown",event=>{canvas.setPointerCapture(event.pointerId);drag={x:event.clientX,y:event.clientY,cx:centerX,cy:centerY,moved:false}});
canvas.addEventListener("pointermove",event=>{if(!drag)return;const dx=event.clientX-drag.x,dy=event.clientY-drag.y;if(Math.abs(dx)+Math.abs(dy)>3)drag.moved=true;centerX=drag.cx-dx/scale;centerY=drag.cy+dy/scale;schedule()});
canvas.addEventListener("pointerup",event=>{if(!drag)return;if(!drag.moved){const r=canvas.getBoundingClientRect(),x=event.clientX-r.left,y=event.clientY-r.top;let best=12,hit=null;
    for(const v of active())for(const {e,op} of views.get(v.id).ops){if(!renderable(e,modes.get(v.id)))continue;
      if(op.text){if(!state.text)continue;const p=worldToScreen(op.x,op.y),dist=Math.hypot(x-p[0],y-p[1]);if(dist<best){best=dist;hit={v,e}}continue}
      const pts=op.points;for(let i=1;i<pts.length;i++){const a=worldToScreen(...pts[i-1]),b=worldToScreen(...pts[i]),dx=b[0]-a[0],dy=b[1]-a[1],t=Math.max(0,Math.min(1,((x-a[0])*dx+(y-a[1])*dy)/(dx*dx+dy*dy||1))),dist=Math.hypot(x-a[0]-t*dx,y-a[1]-t*dy);if(dist<best){best=dist;hit={v,e}}}}
    state.selectedKey=hit?.e.key??null;state.highlightFamily="";$("highlightFamily").value="";
    $("inspect").textContent=hit?`${hit.v.name} · ${hit.e.c} · ${hit.e.f||hit.e.t||""} · Revit element ${hit.e.r} · ${hit.e.k}`:"Click a line or annotation to highlight its Revit element.";schedule()}
  drag=null});
$("fit").onclick=fit;$("zoomIn").onclick=()=>zoom(1.6);$("zoomOut").onclick=()=>zoom(1/1.6);
$("trueView").onclick=showTrueView;
$("screenPpi").onchange=()=>{state.screenPpi=Math.max(30,Math.min(400,Number($("screenPpi").value)||96));$("screenPpi").value=String(state.screenPpi);$("rulerBar").style.width=`${state.screenPpi}px`;if(state.trueViewId)setTrueScale(state.trueViewId);else{restyleViews();schedule()}};
$("highlightFamily").onchange=()=>{state.highlightFamily=$("highlightFamily").value;state.selectedKey=null;schedule()};
$("highlightColor").oninput=()=>{state.highlightColor=$("highlightColor").value;schedule()};
$("clearHighlight").onclick=()=>{state.selectedKey=null;state.highlightFamily="";$("highlightFamily").value="";$("inspect").textContent="Click a line or annotation to highlight its Revit element.";schedule()};
$("showText").onchange=event=>{state.text=event.target.checked;schedule()};$("linePatterns").onchange=event=>{state.patterns=event.target.checked;schedule()};$("smoothCurves").onchange=event=>{state.smooth=event.target.checked;schedule()};
$("allCategories").onclick=()=>{hiddenCategories.clear();document.querySelectorAll(".category input").forEach(x=>x.checked=true);schedule()};
$("noCategories").onclick=()=>{document.querySelectorAll(".category input").forEach(x=>{x.checked=false;hiddenCategories.add(x.parentElement.querySelector("span").textContent)});schedule()};
$("useElementOverlay").onchange=event=>{overlayEnabled=event.target.checked;$("overlayLegend").hidden=!overlayEnabled;refreshOverlay()};
$("zoomChanges").onclick=async()=>{if(!elementOverlay)return;const id=elementOverlay.viewId;
  for(const v of manifest.views)modes.set(v.id,v.id===id?"full":"hidden");renderLayers();
  try{await loadView(id);zoomToChanges()}catch(error){$("status").textContent=error.message}
};
new ResizeObserver(resize).observe(canvas);
$("rulerBar").style.width=`${state.screenPpi}px`;
try{const response=await fetch("/atlas-manifest.json");if(!response.ok)throw Error(`Manifest HTTP ${response.status}`);manifest=await response.json();
  const overlayResponse=await fetch("/atlas-overlay.json");
  if(overlayResponse.ok){const bytes=await overlayResponse.arrayBuffer();overlayBytes=bytes.byteLength;elementOverlay=JSON.parse(new TextDecoder().decode(bytes));
    const origin=elementOverlay.sourceKind==="local-v2dg-revision"?"Local V2DG revision":"Element revision";
    $("overlayStatus").textContent=`${origin}. ${elementOverlay.changes.length} changed IDs (${elementOverlay.changes.map(x=>x.element.r).join(", ")}); ${overlayBytes.toLocaleString()} bytes in second layer.`;
    const legend=$("overlayLegend");legend.replaceChildren();
    elementOverlay.changes.forEach((change,index)=>{const color=editPalette[index%editPalette.length];editColors.set(change.key,color);
      const row=document.createElement("div"),swatch=document.createElement("span"),label=document.createElement("span");
      row.className="editLegendRow";swatch.className="editSwatch";swatch.style.backgroundColor=color;
      label.textContent=`Revit ${change.element.r} · ${change.element.t||change.element.c}`;row.append(swatch,label);legend.append(row);
    });
  }else{$("overlayStatus").textContent="No second export available.";$("useElementOverlay").disabled=true;$("zoomChanges").disabled=true}
  const first=manifest.views.find(v=>v.type==="FloorPlan"&&v.name==="L4")||manifest.views.find(v=>v.type==="FloorPlan")||manifest.views[0];for(const v of manifest.views)modes.set(v.id,v.id===first.id?"full":"hidden");
  renderControls();await loadView(first.id);$("status").textContent=`${manifest.views.length} exported views`;fit();schedule();
}catch(error){$("status").textContent=`Load failed: ${error.message}`;console.error(error)}
