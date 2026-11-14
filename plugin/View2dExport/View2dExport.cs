#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Agentulator.Contracts;
using Agentulator.Contracts.Artifacts;
using Agentulator.RevitSdk;

namespace Agentulator.Tools;

public sealed class View2dExport : IRevitTool
{
    public string Name => "View2dExport";

    public Envelope Execute(UIApplication app, ToolContext ctx)
    {
        var report = ctx.Report("1.0.0");
        var doc = app.ActiveUIDocument?.Document;
        if (doc is null)
            return report.Needs("user_input", "Open a Revit model first.").Done("No open document.");
        var scope = ctx.GetString("scope", "host");
        if (scope == "source")
        {
            var linkId = ctx.GetInt("linkInstanceId", -1);
            var link = new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>().FirstOrDefault(x => x.Id.Value == linkId);
            if (link?.GetLinkDocument() is not Document source)
                return report.Needs("linkInstanceId", "Choose a loaded direct Revit link.")
                    .Done("No linked source document selected.");
            doc = source;
        }

        var selectedViewId = ctx.GetInt("viewId", -1);
        var maxViews = ctx.GetInt("maxViews", 1);
        var levelName = ctx.GetString("levelNameContains", "").Trim();
        var viewName = ctx.GetString("viewNameContains", "").Trim();
        var candidates = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
            .Where(v => !v.IsTemplate && IsDrawable2d(v.ViewType))
            .Where(v => !ctx.GetBool("plansOnly", false) || IsPlan(v.ViewType))
            .Where(v => levelName.Length == 0 ||
                (v is ViewPlan plan && plan.GenLevel?.Name.Contains(levelName, StringComparison.OrdinalIgnoreCase) == true))
            .Where(v => viewName.Length == 0 || v.Name.Contains(viewName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(v => v.ViewType == ViewType.FloorPlan ? 0 : 1).ThenBy(v => v.Id.Value)
            .ToList();
        if (selectedViewId > 0)
            candidates = candidates.Where(v => v.Id.Value == selectedViewId).ToList();
        else
            candidates = candidates.Take(maxViews).ToList();
        if (ctx.GetBool("listOnly", false))
        {
            var listing = candidates.Select(v => new {
                id = v.Id.Value, name = v.Name, type = v.ViewType.ToString(),
                level = (v as ViewPlan)?.GenLevel?.Name, scale = v.Scale,
                detailLevel = v.DetailLevel.ToString() }).ToArray();
            report.Produced(ctx.Artifacts.WriteJson("view-list.json", listing));
            return report.Count("views", listing.Length).Meta("documentTitle", doc.Title)
                .Meta("views", listing).Done("Listed " + listing.Length + " drawable views.");
        }
        if (candidates.Count == 0)
            return report.Needs("viewId", "Select a native floor plan, section, elevation, detail, drafting view, or legend. Revit's 2D CustomExporter does not accept a sheet directly.")
                .Done("No exportable 2D view matched.");

        HashSet<long>? selectedIds = null;
        if (ctx.TryGet("elementIds", out var idsArgument))
        {
            selectedIds = idsArgument.EnumerateArray().Select(id => id.GetInt64()).ToHashSet();
            if (selectedIds.Count == 0 || candidates.Count != 1 || selectedIds.Any(id => id <= 0 || doc.GetElement(new ElementId(id)) is null))
                return report.Needs("elementIds", "Choose existing element IDs in one selected source document and one native view.")
                    .Done("Element-filtered export was not started.");
        }
        var total = Stopwatch.StartNew();
        var summaries = new List<object>();
        long totalRows = 0, totalPrimitives = 0, totalBytes = 0;
        var failed = 0;
        foreach (var view in candidates)
        {
            if (ctx.CancellationRequested) break;
            ctx.Progress.Report(summaries.Count + 1, candidates.Count, "Exporting 2D geometry: " + view.Name);
            var capture = new Capture(doc, view, () => ctx.CancellationRequested,
                ctx.GetInt("maxPrimitives", 0), selectedIds);
            var exportClock = Stopwatch.StartNew();
            string? error = null;
            Transaction? detailTransaction = null;
            try
            {
                var requestedDetail = ctx.GetString("detailLevel", "view");
                if (requestedDetail != "view")
                {
                    var targetDetail = requestedDetail switch
                    {
                        "Coarse" => ViewDetailLevel.Coarse,
                        "Medium" => ViewDetailLevel.Medium,
                        "Fine" => ViewDetailLevel.Fine,
                        _ => throw new ArgumentException("detailLevel must be view, Coarse, Medium or Fine")
                    };
                    if (view.DetailLevel != targetDetail)
                    {
                        detailTransaction = new Transaction(doc, "Temporary 2D export detail level");
                        detailTransaction.Start();
                        view.DetailLevel = targetDetail;
                        doc.Regenerate();
                    }
                }
                capture.DetailLevel = view.DetailLevel.ToString();
                using var exporter = new CustomExporter(doc, capture)
                {
                    IncludeGeometricObjects = true,
                    Export2DIncludingAnnotationObjects = ctx.GetBool("includeAnnotations", true),
                    Export2DGeometricObjectsIncludingPatternLines = ctx.GetBool("includePatternLines", false),
                    ShouldStopOnError = false
                };
                exporter.Export2DForceDisplayStyle = ctx.GetString("displayStyle", "view") switch
                {
                    "wireframe" => DisplayStyle.Wireframe,
                    "hlr" => DisplayStyle.HLR,
                    _ => DisplayStyle.Undefined
                };
                exporter.Export(view);
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                failed++;
                report.Warn(view.Id.Value + " " + view.Name + ": " + error);
            }
            finally
            {
                if (detailTransaction?.GetStatus() == TransactionStatus.Started)
                    detailTransaction.RollBack();
                detailTransaction?.Dispose();
            }
            var exportMs = exportClock.Elapsed.TotalMilliseconds;
            var encodeClock = Stopwatch.StartNew();
            var bytes = Encode(capture);
            var encodeMs = encodeClock.Elapsed.TotalMilliseconds;
            var writeClock = Stopwatch.StartNew();
            var artifact = ctx.Artifacts.Write("view-" + view.Id.Value + ".v2dg", bytes,
                new ArtifactOptions
                {
                    Summary = "Element-grouped 2D drawing geometry from " + view.Name,
                    Count = capture.Rows.Sum(x => x.Primitives.Count),
                    ContentType = "application/octet-stream"
                });
            report.Produced(artifact);
            var patterns = UsedLinePatterns(doc, capture);
            var patternArtifact = ctx.Artifacts.WriteJson("view-" + view.Id.Value + "-line-patterns.json", patterns);
            report.Produced(patternArtifact);
            var writeMs = writeClock.Elapsed.TotalMilliseconds;
            totalRows += capture.Rows.Count;
            totalPrimitives += capture.PrimitiveCount;
            totalBytes += bytes.Length;
            summaries.Add(new
            {
                viewId = view.Id.Value,
                viewName = view.Name,
                viewType = view.ViewType.ToString(),
                scale = view.Scale,
                detailLevel = capture.DetailLevel,
                nativeDetailLevelAfterExport = view.DetailLevel.ToString(),
                requestedDetailLevel = ctx.GetString("detailLevel", "view"),
                displayStyle = view.DisplayStyle.ToString(),
                requestedDisplayStyle = ctx.GetString("displayStyle", "view"),
                includePatternLines = ctx.GetBool("includePatternLines", false),
                selectedElementIds = selectedIds?.ToArray(),
                missingSelectedElementIds = selectedIds?.Except(capture.Rows.Select(x => x.ElementId)).ToArray(),
                origin = Point(view.Origin),
                right = Point(view.RightDirection),
                up = Point(view.UpDirection),
                viewDirection = Point(view.ViewDirection),
                rows = capture.Rows.Count,
                primitives = capture.PrimitiveCount,
                callbacks = capture.Callbacks,
                curveKinds = capture.CurveKinds,
                styleSamples = capture.StyleSamples,
                ungrouped = capture.Ungrouped,
                captureErrors = capture.Errors.Take(8).ToArray(),
                linePatterns = patterns.Length,
                annotationRows = capture.Rows.Count(x => x.CategoryKind == "Annotation"),
                annotationPrimitives = capture.Rows.Where(x => x.CategoryKind == "Annotation").Sum(x => x.Primitives.Count),
                partial = capture.WasCanceled || error is not null,
                error,
                exportMs = Math.Round(exportMs, 3),
                encodeMs = Math.Round(encodeMs, 3),
                writeMs = Math.Round(writeMs, 3),
                bytes = bytes.Length,
                artifact = artifact.Path,
                linePatternArtifact = patternArtifact.Path
            });
        }
        report.Count("views", summaries.Count).Count("elementRows", (int)totalRows)
            .Count("primitives", (int)totalPrimitives)
            .Meta("scope", scope).Meta("documentTitle", doc.Title)
            .Meta("views", summaries).Meta("failedViews", failed)
            .Meta("binaryBytes", totalBytes)
            .Meta("timingsMs", new { insideRevitTotal = Math.Round(total.Elapsed.TotalMilliseconds, 3) });
        return report.Done("Captured " + totalPrimitives + " drawing primitives in " + totalRows
            + " element rows across " + summaries.Count + " view(s); " + failed + " failed.");
    }

    private static bool IsDrawable2d(ViewType t) => t is
        ViewType.AreaPlan or ViewType.CeilingPlan or ViewType.Detail or
        ViewType.DraftingView or ViewType.Elevation or
        ViewType.EngineeringPlan or ViewType.FloorPlan or ViewType.Legend or ViewType.Section;

    private static bool IsPlan(ViewType t) => t is
        ViewType.AreaPlan or ViewType.CeilingPlan or ViewType.EngineeringPlan or ViewType.FloorPlan;

    private static object[] UsedLinePatterns(Document host, Capture capture)
    {
        var docs = new Dictionary<string, Document> { [host.Title] = host };
        foreach (var link in new FilteredElementCollector(host).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>())
            if (link.GetLinkDocument() is Document linked) docs[linked.Title] = linked;
        return capture.Rows.SelectMany(row => row.Primitives
            .Where(p => p.Stroke.PatternId != -1)
            .Select(p => (row.DocumentTitle, p.Stroke.PatternId)))
            .Distinct().Select(item => {
                docs.TryGetValue(item.DocumentTitle, out var source);
                LinePattern? pattern = null;
                try { if (source is not null) pattern = LinePatternElement.GetLinePattern(source, new ElementId(item.PatternId)); }
                catch { }
                var segments = pattern?.GetSegments().Select(s => new { type = s.Type.ToString(), lengthFeet = s.Length }).ToArray();
                return (object)new { sourceDocument = item.DocumentTitle, id = item.PatternId,
                    segments, resolved = pattern is not null };
            }).ToArray();
    }

    private static double[] Point(XYZ p) => new[] { p.X, p.Y, p.Z };
    private static double[] Points(IEnumerable<XYZ> points) =>
        points.SelectMany(Point).ToArray();

    private static byte[] Encode(Capture capture)
    {
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream, Encoding.UTF8, true);
        w.Write(Encoding.ASCII.GetBytes("V2DG"));
        w.Write((ushort)6);
        w.Write(capture.Document.Title);
        w.Write(capture.View.Id.Value);
        w.Write(capture.View.Name);
        w.Write(capture.View.ViewType.ToString());
        w.Write(capture.View.Scale);
        w.Write(capture.DetailLevel);
        foreach (var p in new[] { capture.View.Origin, capture.View.RightDirection,
            capture.View.UpDirection, capture.View.ViewDirection })
            foreach (var v in Point(p)) w.Write(v);
        w.Write(capture.Rows.Count);
        foreach (var row in capture.Rows)
        {
            w.Write(row.DocumentTitle);
            w.Write(row.LinkInstanceId);
            w.Write(row.ElementId);
            w.Write(row.UniqueId);
            w.Write(row.Category);
            w.Write(row.CategoryKind);
            w.Write(row.FamilyName);
            w.Write(row.TypeName);
            w.Write(row.Primitives.Count);
            foreach (var p in row.Primitives)
            {
                w.Write(p.Sequence);
                w.Write(p.Kind);
                w.Write(p.Text ?? "");
                w.Write(p.Data.Length);
                foreach (var v in p.Data) w.Write(v);
                w.Write(p.Stroke.Red);
                w.Write(p.Stroke.Green);
                w.Write(p.Stroke.Blue);
                w.Write(p.Stroke.Width);
                w.Write(p.Stroke.PatternId);
                w.Write(p.Stroke.Transparency);
                w.Write(p.TextStyle is not null);
                if (p.TextStyle is TextStyleInfo ts)
                {
                    w.Write(ts.HorizontalAlignment);
                    w.Write(ts.VerticalAlignment);
                    w.Write(ts.FontName);
                    w.Write(ts.IsBold);
                    w.Write(ts.Height);
                }
            }
        }
        w.Flush();
        return stream.ToArray();
    }

    private sealed class Capture : IExportContext2D
    {
        private readonly Func<bool> _cancel;
        private readonly int _maxPrimitives;
        private Row? _current;
        private readonly Dictionary<(string, long, long), Row> _byElement = new();
        public Document Document { get; }
        public View View { get; }
        public string DetailLevel { get; set; }
        public List<Row> Rows { get; } = new();
        public Dictionary<string, int> Callbacks { get; } = new();
        public Dictionary<string, int> CurveKinds { get; } = new();
        public List<object> StyleSamples { get; } = new();
        public List<string> Errors { get; } = new();
        public int PrimitiveCount { get; private set; }
        public int Ungrouped { get; private set; }
        public bool WasCanceled { get; private set; }

        private readonly HashSet<long>? _selectedIds;

        public Capture(Document document, View view, Func<bool> cancel, int maxPrimitives,
            HashSet<long>? selectedIds = null)
        {
            Document = document; View = view; DetailLevel = view.DetailLevel.ToString();
            _cancel = cancel; _maxPrimitives = maxPrimitives;
            _selectedIds = selectedIds;
        }

        private void Count(string name) => Callbacks[name] = Callbacks.GetValueOrDefault(name) + 1;
        private static Stroke Style(LineProperties? p)
        {
            if (p is null) return new Stroke();
            var c = p.Color;
            return new Stroke { Red = c.Red, Green = c.Green, Blue = c.Blue,
                Width = p.LineWidth, PatternId = p.PatternId.Value,
                Transparency = p.Transparency };
        }
        private void Add(string kind, double[] data, LineProperties? style = null, string? text = null)
        {
            var row = _current;
            if (row is null)
            {
                Ungrouped++;
                return;
            }
            var stroke = Style(style);
            row.Primitives.Add(new Primitive { Sequence = PrimitiveCount, Kind = kind,
                Data = data, Stroke = stroke, Text = text });
            PrimitiveCount++;
            if (StyleSamples.Count < 10)
                StyleSamples.Add(new { kind, stroke.Red, stroke.Green, stroke.Blue,
                    stroke.Width, stroke.PatternId, stroke.Transparency });
        }
        private void AddCurve(string source, Curve curve, LineProperties? style)
        {
            var kind = curve.GetType().Name;
            CurveKinds[kind] = CurveKinds.GetValueOrDefault(kind) + 1;
            try
            {
                if (curve is Autodesk.Revit.DB.Line line)
                {
                    Add(source + ":Line", Points(new[] { line.GetEndPoint(0), line.GetEndPoint(1) }), style);
                    return;
                }
                if (curve is Arc arc)
                {
                    var start = arc.IsBound ? arc.GetEndParameter(0) : 0.0;
                    var end = arc.IsBound ? arc.GetEndParameter(1) : 2.0 * Math.PI;
                    Add(source + ":Arc", Point(arc.Center)
                        .Concat(Point(arc.XDirection)).Concat(Point(arc.YDirection))
                        .Concat(new[] { arc.Radius, start, end })
                        .ToArray(), style);
                    return;
                }
                if (curve is Ellipse ellipse)
                {
                    var start = ellipse.IsBound ? ellipse.GetEndParameter(0) : 0.0;
                    var end = ellipse.IsBound ? ellipse.GetEndParameter(1) : 2.0 * Math.PI;
                    Add(source + ":Ellipse", Point(ellipse.Center)
                        .Concat(Point(ellipse.XDirection)).Concat(Point(ellipse.YDirection))
                        .Concat(new[] { ellipse.RadiusX, ellipse.RadiusY, start, end })
                        .ToArray(), style);
                    return;
                }
                if (curve is HermiteSpline hermite)
                {
                    var controls = Points(hermite.ControlPoints.Cast<XYZ>());
                    var tangents = Points(hermite.Tangents.Cast<XYZ>());
                    var parameters = hermite.Parameters.Cast<double>().ToArray();
                    Add(source + ":HermiteSpline", new double[]
                        { hermite.IsPeriodic ? 1 : 0, controls.Length / 3,
                          tangents.Length / 3, parameters.Length }
                        .Concat(controls).Concat(tangents).Concat(parameters).ToArray(), style);
                    return;
                }
                if (curve is NurbSpline spline)
                {
                    var knots = spline.Knots.Cast<double>().ToArray();
                    var weights = spline.Weights.Cast<double>().ToArray();
                    var controls = Points(spline.CtrlPoints.Cast<XYZ>());
                    Add(source + ":NurbSpline", new double[] { spline.Degree, knots.Length, weights.Length,
                        controls.Length / 3 }.Concat(knots).Concat(weights).Concat(controls).ToArray(), style);
                    return;
                }
                Add(source + ":" + kind + ":tessellated", Points(curve.Tessellate()), style);
            }
            catch (Exception ex)
            {
                if (Errors.Count < 30) Errors.Add(source + " " + kind + ": " + ex.Message);
            }
        }

        public bool IsCanceled()
        {
            WasCanceled = _cancel() || (_maxPrimitives > 0 && PrimitiveCount >= _maxPrimitives);
            return WasCanceled;
        }
        public bool Start() { Count("Start"); return true; }
        public void Finish() { Count("Finish"); }
        public RenderNodeAction OnElementBegin2D(ElementNode node)
        {
            Count("OnElementBegin2D");
            _current = null;
            if (_selectedIds is not null && (node.LinkInstanceId.Value != -1 || !_selectedIds.Contains(node.ElementId.Value)))
                return RenderNodeAction.Skip;
            var documentTitle = node.Document?.Title ?? Document.Title;
            var key = (documentTitle, node.LinkInstanceId.Value, node.ElementId.Value);
            if (!_byElement.TryGetValue(key, out _current))
            {
                var sourceElement = (node.Document ?? Document).GetElement(node.ElementId);
                _current = new Row { DocumentTitle = documentTitle,
                    LinkInstanceId = key.Item2, ElementId = key.Item3,
                    UniqueId = sourceElement?.UniqueId ?? "",
                    Category = sourceElement?.Category?.Name ?? "",
                    CategoryKind = sourceElement?.Category?.CategoryType.ToString() ?? "",
                    FamilyName = (sourceElement as FamilyInstance)?.Symbol?.FamilyName ?? "",
                    TypeName = sourceElement?.Document.GetElement(sourceElement.GetTypeId())?.Name ?? "" };
                _byElement.Add(key, _current);
                Rows.Add(_current);
            }
            return RenderNodeAction.Proceed;
        }
        public void OnElementEnd2D(ElementNode node)
        {
            Count("OnElementEnd2D"); _current = null;
        }
        public RenderNodeAction OnCurve(CurveNode node)
        {
            Count("OnCurve"); AddCurve("curve", node.GetCurve(), node.LineProperties);
            return RenderNodeAction.Skip;
        }
        public RenderNodeAction OnFaceEdge2D(FaceEdgeNode node)
        {
            Count("OnFaceEdge2D");
            AddCurve("faceEdge", node.GetFaceEdge().AsCurve(), node.LineProperties);
            return RenderNodeAction.Skip;
        }
        public RenderNodeAction OnFaceSilhouette2D(FaceSilhouetteNode node)
        {
            Count("OnFaceSilhouette2D");
            return RenderNodeAction.Proceed;
        }
        public RenderNodeAction OnPolyline(PolylineNode node)
        {
            Count("OnPolyline"); Add("polyline", Points(node.GetPolyline().GetCoordinates()),
                node.LineProperties);
            return RenderNodeAction.Skip;
        }
        public void OnLineSegment(LineSegment node)
        {
            Count("OnLineSegment"); Add("tessellatedLine",
                Points(new[] { node.StartPoint, node.EndPoint }), node.LineProperties);
        }
        public void OnPolylineSegments(PolylineSegments node)
        {
            Count("OnPolylineSegments"); Add(node.IsFilled ? "tessellatedFilledPolyline" : "tessellatedPolyline",
                Points(node.GetVertices()), node.LineProperties);
        }
        public void OnText(TextNode node)
        {
            Count("OnText");
            var c = node.Color;
            Add("text", Point(node.Position).Concat(Point(node.BaseDirection))
                .Concat(Point(node.UpDirection)).Concat(new[] { node.Width, node.FontHeight }).ToArray(),
                null, node.Text);
            if (_current?.Primitives.LastOrDefault() is Primitive p)
            {
                p.Stroke = new Stroke { Red = c.Red, Green = c.Green, Blue = c.Blue };
                p.TextStyle = new TextStyleInfo {
                    HorizontalAlignment = node.HorizontalAlignment.ToString(),
                    VerticalAlignment = node.VerticalAlignment.ToString(),
                    FontName = node.FontName ?? "",
                    IsBold = node.IsBold,
                    Height = node.Height
                };
            }
        }

        public RenderNodeAction OnViewBegin(ViewNode node) { Count("OnViewBegin"); return RenderNodeAction.Proceed; }
        public void OnViewEnd(ElementId id) { Count("OnViewEnd"); }
        public RenderNodeAction OnElementBegin(ElementId id) { Count("OnElementBegin"); return RenderNodeAction.Proceed; }
        public void OnElementEnd(ElementId id) { Count("OnElementEnd"); }
        public RenderNodeAction OnInstanceBegin(InstanceNode node) { Count("OnInstanceBegin"); return RenderNodeAction.Proceed; }
        public void OnInstanceEnd(InstanceNode node) { Count("OnInstanceEnd"); }
        public RenderNodeAction OnLinkBegin(LinkNode node) { Count("OnLinkBegin"); return RenderNodeAction.Proceed; }
        public void OnLinkEnd(LinkNode node) { Count("OnLinkEnd"); }
        public RenderNodeAction OnFaceBegin(FaceNode node) { Count("OnFaceBegin"); return RenderNodeAction.Proceed; }
        public void OnFaceEnd(FaceNode node) { Count("OnFaceEnd"); }
        public void OnPolymesh(PolymeshTopology node) { Count("OnPolymesh"); }
        public void OnMaterial(MaterialNode node) { Count("OnMaterial"); }
        public void OnLight(LightNode node) { Count("OnLight"); }
        public void OnRPC(RPCNode node) { Count("OnRPC"); }
    }

    private sealed class Row
    {
        public string DocumentTitle = "";
        public long LinkInstanceId;
        public long ElementId;
        public string UniqueId = "";
        public string Category = "";
        public string CategoryKind = "";
        public string FamilyName = "";
        public string TypeName = "";
        public List<Primitive> Primitives { get; } = new();
    }
    private sealed class Primitive
    {
        public long Sequence;
        public string Kind = "";
        public string? Text;
        public double[] Data = Array.Empty<double>();
        public Stroke Stroke = new();
        public TextStyleInfo? TextStyle;
    }
    private sealed class TextStyleInfo
    {
        public string HorizontalAlignment = "";
        public string VerticalAlignment = "";
        public string FontName = "";
        public bool IsBold;
        public double Height;
    }
    private sealed class Stroke
    {
        public byte Red, Green, Blue;
        public double Width;
        public long PatternId;
        public int Transparency;
    }
}
