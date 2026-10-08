using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace WallDivide;

// Splits newly drawn straight walls at every grid line that is not parallel to the wall.
public class App : IExternalApplication
{
    public static bool Enabled = true;
    static PushButton? _button;
    static readonly Guid UpdaterGuid = new("6f1d2a54-3b9e-4c0a-9a27-5d8c1e7b4a10");

    public Result OnStartup(UIControlledApplication app)
    {
        var updater = new DivideUpdater(app.ActiveAddInId, UpdaterGuid);
        UpdaterRegistry.RegisterUpdater(updater, isOptional: true);
        UpdaterRegistry.AddTrigger(updater.GetUpdaterId(),
            new ElementClassFilter(typeof(Wall)), Element.GetChangeTypeElementAddition());

        app.CreateRibbonTab("WallDivide");
        var panel = app.CreateRibbonPanel("WallDivide", "Wall Divide");
        _button = (PushButton)panel.AddItem(new PushButtonData("WallDivideToggle", "Divide",
            typeof(App).Assembly.Location, typeof(ToggleCommand).FullName));
        RefreshButton();
        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication app)
    {
        UpdaterRegistry.UnregisterUpdater(new UpdaterId(app.ActiveAddInId, UpdaterGuid));
        return Result.Succeeded;
    }

    // Shows the state on the button: green dot = ON, red dot = OFF.
    public static void RefreshButton()
    {
        if (_button == null) return;
        _button.ItemText = Enabled ? "Divide\nON" : "Divide\nOFF";
        _button.LargeImage = Dot(Enabled ? System.Windows.Media.Color.FromRgb(46, 160, 67)
                                         : System.Windows.Media.Color.FromRgb(200, 50, 50));
        _button.ToolTip = "Automatically divide new walls at grid lines (on/off)";
    }

    static System.Windows.Media.Imaging.BitmapSource Dot(System.Windows.Media.Color color)
    {
        var visual = new System.Windows.Media.DrawingVisual();
        using (var dc = visual.RenderOpen())
            dc.DrawEllipse(new System.Windows.Media.SolidColorBrush(color), null,
                new System.Windows.Point(16, 16), 14, 14);
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(32, 32, 96, 96,
            System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }
}

[Transaction(TransactionMode.Manual)]
public class ToggleCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
    {
        App.Enabled = !App.Enabled;
        App.RefreshButton();
        return Result.Succeeded;
    }
}

public class DivideUpdater : IUpdater
{
    const double Tol = 1.0 / 304.8; // 1 mm in feet
    readonly UpdaterId _id;

    public DivideUpdater(AddInId addInId, Guid guid) => _id = new UpdaterId(addInId, guid);

    public UpdaterId GetUpdaterId() => _id;
    public string GetUpdaterName() => "WallDivide";
    public string GetAdditionalInformation() => "Divides new walls at grid lines";
    public ChangePriority GetChangePriority() => ChangePriority.FloorsRoofsStructuralWalls;

    public void Execute(UpdaterData data)
    {
        if (!App.Enabled) return;
        var doc = data.GetDocument();
        if (doc.IsFamilyDocument) return;

        var grids = new FilteredElementCollector(doc).OfClass(typeof(Grid))
            .Cast<Grid>().Select(g => g.Curve as Line).Where(l => l != null).Select(l => l!).ToList();
        if (grids.Count == 0) return;

        foreach (var id in data.GetAddedElementIds())
        {
            if (doc.GetElement(id) is not Wall wall) continue;
            if (wall.Location is not LocationCurve lc || lc.Curve is not Line line) continue;

            var pts = SplitPoints(line, grids);
            if (pts.Count < 3) continue; // only start and end, nothing in between

            Log($"Wall {id}: {pts.Count - 1} pieces, start {Fmt(pts[0])} end {Fmt(pts[pts.Count - 1])}");
            lc.Curve = Line.CreateBound(pts[0], pts[1]); // the original becomes the first piece
            for (int i = 1; i < pts.Count - 1; i++)
            {
                // Copy far away first: a copy placed on top of the original would be joined to it
                // at the identical ends, and moving it afterwards would drag the neighbours along.
                var newId = ElementTransformUtils.CopyElement(doc, id, new XYZ(10000, 0, 0)).First();
                var piece = (Wall)doc.GetElement(newId);
                ((LocationCurve)piece.Location).Curve = Line.CreateBound(pts[i], pts[i + 1]);
                Log($"  piece {i}: {newId} {Fmt(pts[i])} -> {Fmt(pts[i + 1])}");
            }
        }
    }

    static string Fmt(XYZ p) => $"({p.X * 304.8:F0}, {p.Y * 304.8:F0}, {p.Z * 304.8:F0}) mm";

    static void Log(string text)
    {
        try
        {
            var dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WallDivide");
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "log.txt"),
                $"{DateTime.Now:HH:mm:ss} {text}{Environment.NewLine}");
        }
        catch { /* the log is diagnostics only */ }
    }

    // Returns start, sorted cut points and end. Grids are treated as infinite lines in plan.
    static List<XYZ> SplitPoints(Line wall, List<Line> grids)
    {
        var p0 = wall.GetEndPoint(0);
        var p1 = wall.GetEndPoint(1);
        double dx = p1.X - p0.X, dy = p1.Y - p0.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        var ts = new List<double>();
        if (len < Tol) return new List<XYZ> { p0, p1 };

        foreach (var g in grids)
        {
            var gp = g.GetEndPoint(0);
            var gq = g.GetEndPoint(1);
            double gx = gq.X - gp.X, gy = gq.Y - gp.Y;
            double glen = Math.Sqrt(gx * gx + gy * gy);
            if (glen < Tol) continue;

            double cross = dx * gy - dy * gx;
            if (Math.Abs(cross) / (len * glen) < 1e-6) continue; // parallel to the wall

            double t = ((gp.X - p0.X) * gy - (gp.Y - p0.Y) * gx) / cross;
            if (t * len <= Tol || (1 - t) * len <= Tol) continue; // at the ends or outside
            ts.Add(t);
        }

        ts.Sort();
        var result = new List<XYZ> { p0 };
        double last = 0;
        foreach (var t in ts)
        {
            if ((t - last) * len <= Tol) continue; // duplicate cuts (e.g. two grids on top of each other)
            result.Add(p0 + t * (p1 - p0));
            last = t;
        }
        result.Add(p1);
        return result;
    }
}
