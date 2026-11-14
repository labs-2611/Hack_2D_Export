using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Xml.Linq;
using System.Windows.Forms;

const string addinId = "47e64a62-0b5f-4c69-a087-7dccdf731180";
const string resourceName = "Hack2DExport.dll";

try
{
    if (args.Length > 1 || (args.Length == 1 && args[0] is not ("--check" or "--quiet")))
        throw new ArgumentException("Usage: Hack2DExport-Revit2026-Installer.exe [--check|--quiet]");

    string revitDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        "Autodesk", "Revit 2026");
    string apiPath = Path.Combine(revitDir, "RevitAPI.dll");
    if (!File.Exists(apiPath)) throw new FileNotFoundException("Revit 2026 is not installed.", apiPath);
    var version = FileVersionInfo.GetVersionInfo(apiPath);
    if (version.FileMajorPart != 26 || version.FileMinorPart >= 5)
        throw new NotSupportedException($"Installed Revit API {version.FileVersion} is not supported by this .NET 8 package. Use the Revit 2026.5/.NET 10 build for 2026.5 or later.");

    string addins = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Autodesk", "Revit", "Addins", "2026");
    string deployment = Path.Combine(addins, "Hack2DExport");
    string dllPath = Path.Combine(deployment, "Hack2DExport.dll");
    string manifestPath = Path.Combine(addins, "Hack2DExport.addin");
    byte[] payload;
    using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
        ?? throw new InvalidOperationException("The embedded exporter DLL is missing."))
    using (var buffer = new MemoryStream())
    {
        stream.CopyTo(buffer);
        payload = buffer.ToArray();
    }
    string payloadHash = Convert.ToHexString(SHA256.HashData(payload));

    if (args.Length == 1 && args[0] == "--check")
    {
        bool installed = File.Exists(dllPath) && File.Exists(manifestPath) &&
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dllPath))) == payloadHash &&
            IsOurManifest(manifestPath, dllPath);
        Console.WriteLine($"Revit API: {version.FileVersion} (.NET 8 package)");
        Console.WriteLine($"Installed package matches: {installed}");
        Console.WriteLine($"Exporter SHA-256: {payloadHash}");
        return installed ? 0 : 2;
    }

    var running = Process.GetProcessesByName("Revit").Any(process => IsRevit2026(process, revitDir));
    if (running) throw new InvalidOperationException("Close Revit 2026 before installing the exporter, then run this installer again.");
    if (File.Exists(manifestPath) && !IsOurManifestId(manifestPath))
        throw new InvalidOperationException($"An unrelated add-in already owns {manifestPath}; installation stopped.");

    Directory.CreateDirectory(deployment);
    string dllTemp = Path.Combine(deployment, "Hack2DExport.dll.new");
    File.WriteAllBytes(dllTemp, payload);
    File.Move(dllTemp, dllPath, true);

    var xml = new XDocument(
        new XDeclaration("1.0", "utf-8", null),
        new XElement("RevitAddIns",
            new XElement("AddIn", new XAttribute("Type", "Application"),
                new XElement("Name", "Hack 2D Export"),
                new XElement("Assembly", dllPath),
                new XElement("AddInId", addinId),
                new XElement("FullClassName", "Hack2DExport.AddinApplication"),
                new XElement("VendorId", "2611"),
                new XElement("VendorDescription", "2611 Labs"))));
    string manifestTemp = manifestPath + ".new";
    xml.Save(manifestTemp);
    File.Move(manifestTemp, manifestPath, true);
    Console.WriteLine("Hack 2D Export installed for Revit 2026.");
    Console.WriteLine($"Manifest: {manifestPath}");
    Console.WriteLine($"Exporter SHA-256: {payloadHash}");
    Console.WriteLine("Start Revit 2026 to use the Add-Ins > Hack 2D Export ribbon panel.");
    if (args.Length == 0)
        MessageBox.Show("Hack 2D Export is installed for Revit 2026.\n\nStart Revit and open Add-Ins > Hack 2D Export.",
            "Hack 2D Export", MessageBoxButtons.OK, MessageBoxIcon.Information);
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine($"Installation failed: {error.Message}");
    if (args.Length == 0)
        MessageBox.Show(error.Message, "Hack 2D Export installation failed",
            MessageBoxButtons.OK, MessageBoxIcon.Error);
    return 1;
}

static bool IsRevit2026(Process process, string revitDir)
{
    try
    {
        string? path = process.MainModule?.FileName;
        return path is not null && Path.GetFullPath(path).Equals(
            Path.GetFullPath(Path.Combine(revitDir, "Revit.exe")), StringComparison.OrdinalIgnoreCase);
    }
    catch (System.ComponentModel.Win32Exception)
    {
        return false;
    }
    finally
    {
        process.Dispose();
    }
}

static bool IsOurManifestId(string path)
{
    try
    {
        return XDocument.Load(path).Descendants("AddInId").Any(id =>
            string.Equals(id.Value.Trim(), addinId, StringComparison.OrdinalIgnoreCase));
    }
    catch (System.Xml.XmlException)
    {
        return false;
    }
}

static bool IsOurManifest(string path, string dllPath)
{
    if (!IsOurManifestId(path)) return false;
    try
    {
        return XDocument.Load(path).Descendants("Assembly").Any(assembly =>
            string.Equals(Path.GetFullPath(assembly.Value.Trim()), Path.GetFullPath(dllPath),
                StringComparison.OrdinalIgnoreCase));
    }
    catch (Exception error) when (error is ArgumentException or IOException or System.Xml.XmlException)
    {
        return false;
    }
}
