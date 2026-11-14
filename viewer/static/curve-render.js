// Interpolate Revit's resolved polyline only when its vertices describe a smooth bend.
// Keep corners and straight paths exact; never infer a curve from two or three points.
export function isSmoothPolyline(points){
  if(points.length<4)return false;
  let sign=0,turns=0;
  for(let i=1;i<points.length-1;i++){
    const a=points[i-1],b=points[i],c=points[i+1];
    const ux=b[0]-a[0],uy=b[1]-a[1],vx=c[0]-b[0],vy=c[1]-b[1];
    const cross=ux*vy-uy*vx,dot=ux*vx+uy*vy,turn=Math.atan2(cross,dot);
    if(Math.abs(turn)>Math.PI/5)return false;
    if(Math.abs(turn)<0.003)continue;
    const current=Math.sign(turn);
    if(sign&&current!==sign)return false;
    sign=current;turns++;
  }
  return turns>=2;
}

export function tracePolyline(ctx,points,project,smooth=isSmoothPolyline(points)){
  const screen=points.map(([x,y])=>project(x,y));
  ctx.moveTo(...screen[0]);
  if(!smooth){for(let i=1;i<screen.length;i++)ctx.lineTo(...screen[i]);return}
  // Catmull-Rom to cubic Beziers. The curve passes through every exported vertex.
  for(let i=0;i<screen.length-1;i++){
    const p0=screen[Math.max(0,i-1)],p1=screen[i],p2=screen[i+1],p3=screen[Math.min(screen.length-1,i+2)];
    ctx.bezierCurveTo(
      p1[0]+(p2[0]-p0[0])/6,p1[1]+(p2[1]-p0[1])/6,
      p2[0]-(p3[0]-p1[0])/6,p2[1]-(p3[1]-p1[1])/6,
      p2[0],p2[1]);
  }
}
