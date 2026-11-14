#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.Win32;

namespace Hack2DExport;

public sealed class AddinApplication : IExternalApplication
{
    public Result OnStartup(UIControlledApplication application)
    {
        var panel = application.CreateRibbonPanel("Hack 2D Export");
        var assembly = Assembly.GetExecutingAssembly().Location;
        panel.AddItem(new PushButtonData("Hack2DExportView", "Export 2D\nview", assembly,
            typeof(ExportViewCommand).FullName));
        panel.AddItem(new PushButtonData("Hack2DExportSelection", "Export selected\nelements", assembly,
            typeof(ExportSelectionCommand).FullName));
        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application) => Result.Succeeded;
}

[Transaction(TransactionMode.ReadOnly)]
public sealed class ExportViewCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
        NativeExport.Run(commandData, false, ref message);
}

[Transaction(TransactionMode.ReadOnly)]
public sealed class ExportSelectionCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
        NativeExport.Run(commandData, true, ref message);
}

internal static class NativeExport
{
    public static Result Run(ExternalCommandData commandData, bool selectionOnly, ref string message)
    {
        var uiDocument = commandData.Application.ActiveUIDocument;
        if (uiDocument is null)
        {
            message = "Open a Revit project first.";
            return Result.Cancelled;
        }
        var view = uiDocument.ActiveView;
        if (!View2dCore.Supports(view))
        {
            message = "Open a native floor plan, ceiling plan, area plan, section, elevation, detail, drafting view, or legend.";
            return Result.Cancelled;
        }
        var selection = uiDocument.Selection.GetElementIds().ToList();
        var selectedIds = selectionOnly ? selection.Select(id => id.Value).ToHashSet() : null;
        if (selectionOnly && selectedIds!.Count == 0)
        {
            message = "Select one or more elements in the active view.";
            return Result.Cancelled;
        }
        var dialog = new SaveFileDialog
        {
            Title = selectionOnly ? "Export selected 2D elements" : "Export 2D view",
            FileName = $"view-{view.Id.Value}{(selectionOnly ? "-selection" : "")}.v2dg",
            DefaultExt = ".v2dg",
            Filter = "V2DG drawing (*.v2dg)|*.v2dg",
            AddExtension = true,
            OverwritePrompt = true
        };
        if (dialog.ShowDialog() != true) return Result.Cancelled;
        try
        {
            // Revit's 2D exporter otherwise reports the cyan selection highlight
            // as the element's stroke color. Restore the UI selection on all paths.
            V2dgExportResult capture;
            if (selection.Count > 0) uiDocument.Selection.SetElementIds(Array.Empty<ElementId>());
            try
            {
                capture = View2dCore.Export(uiDocument.Document, view, selectedIds);
            }
            finally
            {
                if (selection.Count > 0) uiDocument.Selection.SetElementIds(selection);
            }
            File.WriteAllBytes(dialog.FileName, capture.Bytes);
            var patternPath = Path.ChangeExtension(dialog.FileName, ".line-patterns.json");
            File.WriteAllText(patternPath, JsonSerializer.Serialize(capture.LinePatterns,
                new JsonSerializerOptions { WriteIndented = true }));
            var missing = capture.MissingSelectedElementIds.Length == 0 ? "" :
                $"\nRequested IDs without callbacks: {string.Join(", ", capture.MissingSelectedElementIds)}";
            TaskDialog.Show("Hack 2D Export", $"Exported {capture.ElementRows} element rows and " +
                $"{capture.Primitives} primitives to:\n{dialog.FileName}{missing}");
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = ex.GetType().Name + ": " + ex.Message;
            return Result.Failed;
        }
    }
}
