export function annotationLines(text){
  const lines=String(text).split(/\r\n|\r|\n|\v/);
  while(lines.length>1&&lines.at(-1)==="")lines.pop();
  return lines;
}

export function drawAnnotationText(ctx,op,scale){
  const style=op.textStyle||{},lines=annotationLines(op.text);
  if(!lines.length)return;
  const size=Math.max(.1,Math.min(2048,op.height*scale));
  const font=style.fontName||"Arial",weight=style.isBold?"bold ":"";
  const horizontal={Left:"left",Center:"center",Right:"right"}[style.horizontalAlignment]||"left";
  const vertical={Top:"top",Middle:"middle",Bottom:"bottom"}[style.verticalAlignment]||"middle";
  ctx.save();ctx.translate(op.screenX,op.screenY);ctx.rotate(op.angle);
  ctx.font=`${weight}${size}px ${JSON.stringify(font)}, sans-serif`;
  ctx.textAlign=horizontal;ctx.textBaseline=vertical;
  const measured=Math.max(...lines.map(line=>ctx.measureText(line).width));
  const target=op.textWidth*scale;
  const widthFactor=measured>0&&target>0?Math.max(.25,Math.min(4,target/measured)):1;
  ctx.scale(widthFactor,1);
  const areaHeight=(style.height||0)*.3048*scale;
  const lineHeight=Math.max(size*1.1,areaHeight/Math.max(lines.length,1));
  const start=vertical==="top"?0:vertical==="bottom"?-(lines.length-1)*lineHeight:-(lines.length-1)*lineHeight/2;
  for(let i=0;i<lines.length;i++)ctx.fillText(lines[i],0,start+i*lineHeight);
  ctx.restore();
}
