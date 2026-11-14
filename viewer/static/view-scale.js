const inchesPerMeter=39.37007874;
export function pixelsPerModelMeter(screenCssPixelsPerInch,viewScale){
  return screenCssPixelsPerInch*inchesPerMeter/viewScale;
}
export function displayedScaleDenominator(screenCssPixelsPerInch,pixelsPerModelMeter){
  return Math.round(screenCssPixelsPerInch*inchesPerMeter/pixelsPerModelMeter);
}
