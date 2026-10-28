using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.Windows;

[assembly: ExtensionApplication(typeof(StructuralTakedown.RibbonExtension))]
[assembly: CommandClass(typeof(StructuralTakedown.ColumnLoadPlugin))]

namespace StructuralTakedown
{
    public class RibbonExtension : IExtensionApplication
    {
        public void Initialize()
        {
            if (ComponentManager.Ribbon == null)
                ComponentManager.ItemInitialized += ComponentManager_ItemInitialized;
            else
                BuildRibbon();
        }

        private void ComponentManager_ItemInitialized(object sender, RibbonItemEventArgs e)
        {
            if (ComponentManager.Ribbon != null)
            {
                ComponentManager.ItemInitialized -= ComponentManager_ItemInitialized;
                BuildRibbon();
            }
        }

        public void Terminate() { }

        private void BuildRibbon()
        {
            RibbonControl ribbon = ComponentManager.Ribbon;
            if (ribbon == null) return;

            string tabId = "BNBC_STRUCTURAL_TAB";
            RibbonTab tab = ribbon.FindTab(tabId);
            if (tab == null)
            {
                tab = new RibbonTab { Title = "BNBC / ACI Tools", Id = tabId };
                ribbon.Tabs.Add(tab);
            }

            RibbonPanelSource panelSource = new RibbonPanelSource { Title = "Axial Takedown" };
            RibbonPanel panel = new RibbonPanel { Source = panelSource };
            tab.Panels.Add(panel);

            RibbonButton btn = new RibbonButton
            {
                Text = "Calculate\nColumn Loads",
                ShowText = true,
                Size = RibbonItemSize.Large,
                Orientation = System.Windows.Controls.Orientation.Vertical,
                CommandParameter = "CALCCOLLOADS ",
                CommandHandler = new SimpleCommandHandler()
            };

            panelSource.Items.Add(btn);
            tab.IsActive = true;
        }
    }

    public class SimpleCommandHandler : System.Windows.Input.ICommand
    {
        public bool CanExecute(object parameter) => true;
        public event EventHandler CanExecuteChanged;
        public void Execute(object parameter)
        {
            if (parameter is RibbonButton btn)
            {
                Application.DocumentManager.MdiActiveDocument.SendStringToExecute(btn.CommandParameter.ToString(), true, false, true);
            }
        }
    }

    public class ColumnData
    {
        public string Id { get; set; }
        public Point2d Center { get; set; }
        public double WidthIn { get; set; }
        public double DepthIn { get; set; }
        public double TribAreaSqFt { get; set; }
        public double ConnectedBeamLengthFt { get; set; }
        public double CumulativeDL_Kips { get; set; }
        public double CumulativeLL_Kips { get; set; }
        public double FactoredPu_Kips { get; set; }
    }

    public class BeamSegment
    {
        public Point2d Start { get; set; }
        public Point2d End { get; set; }
        public double LengthFt { get; set; }
    }

    public class ColumnLoadPlugin
    {
        [CommandMethod("CALCCOLLOADS")]
        public void CalculateLoadsFPS()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            // 1. User Prompts for Building Stories & Height
            PromptIntegerOptions pioStories = new PromptIntegerOptions("\nEnter Total Number of Stories (e.g. 6): ") { DefaultValue = 6 };
            PromptIntegerResult pirStories = ed.GetInteger(pioStories);
            if (pirStories.Status != PromptStatus.OK) return;
            int numStories = pirStories.Value;

            PromptDoubleOptions pdoHeight = new PromptDoubleOptions("\nEnter Story Height in FEET (e.g. 10.0): ") { DefaultValue = 10.0 };
            PromptDoubleResult pirHeight = ed.GetDouble(pdoHeight);
            if (pirHeight.Status != PromptStatus.OK) return;
            double storyHeightFt = pirHeight.Value;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                // 2. Select Slab Outline (Layer: SLAB_OUTLINE)
                TypedValue[] slabFilter = new TypedValue[] {
                    new TypedValue((int)DxfCode.LayerName, "SLAB_OUTLINE"),
                    new TypedValue((int)DxfCode.Start, "LWPOLYLINE")
                };
                PromptSelectionResult psrSlab = ed.SelectAll(new SelectionFilter(slabFilter));
                if (psrSlab.Status != PromptStatus.OK || psrSlab.Value.Count == 0)
                {
                    ed.WriteMessage("\nError: Could not find closed polyline on layer 'SLAB_OUTLINE'.");
                    return;
                }

                Polyline slabPoly = tr.GetObject(psrSlab.Value[0].ObjectId, OpenMode.ForRead) as Polyline;
                if (!slabPoly.Closed)
                {
                    ed.WriteMessage("\nError: The boundary on 'SLAB_OUTLINE' must be closed.");
                    return;
                }

                // Detect drawing base unit (Inches vs Feet)
                bool isInchDrawing = slabPoly.Area > 20000.0;
                double toFtScale = isInchDrawing ? (1.0 / 12.0) : 1.0;
                double toSqFtScale = isInchDrawing ? (1.0 / 144.0) : 1.0;
                double slabAreaSqFt = slabPoly.Area * toSqFtScale;

                // 3. Extract Columns (Layer: COLUMNS)
                TypedValue[] colFilter = new TypedValue[] {
                    new TypedValue((int)DxfCode.LayerName, "COLUMNS")
                };
                PromptSelectionResult psrCols = ed.SelectAll(new SelectionFilter(colFilter));
                if (psrCols.Status != PromptStatus.OK || psrCols.Value.Count == 0)
                {
                    ed.WriteMessage("\nError: No entities found on layer 'COLUMNS'.");
                    return;
                }

                List<ColumnData> columns = new List<ColumnData>();
                int cIndex = 1;
                foreach (SelectedObject so in psrCols.Value)
                {
                    Entity ent = tr.GetObject(so.ObjectId, OpenMode.ForRead) as Entity;
                    double widthIn = 12.0, depthIn = 15.0; // Defaults
                    Point2d center = Point2d.Origin;

                    if (ent is Polyline pl)
                    {
                        center = GetPolylineCentroid(pl, toFtScale);
                        // Read actual column dimensions from CAD bounding box
                        Extents3d ext = pl.GeometricExtents;
                        double dx = (ext.MaxPoint.X - ext.MinPoint.X) * (isInchDrawing ? 1.0 : 12.0);
                        double dy = (ext.MaxPoint.Y - ext.MinPoint.Y) * (isInchDrawing ? 1.0 : 12.0);
                        widthIn = Math.Max(10.0, Math.Round(Math.Min(dx, dy)));
                        depthIn = Math.Max(10.0, Math.Round(Math.Max(dx, dy)));
                    }
                    else if (ent is BlockReference blk)
                    {
                        center = new Point2d(blk.Position.X * toFtScale, blk.Position.Y * toFtScale);
                    }

                    columns.Add(new ColumnData
                    {
                        Id = $"C{cIndex++}",
                        Center = center,
                        WidthIn = widthIn,
                        DepthIn = depthIn
                    });
                }

                // 4. Extract Beams (Layer: BEAM_LINES)
                TypedValue[] beamFilter = new TypedValue[] {
                    new TypedValue((int)DxfCode.LayerName, "BEAM_LINES")
                };
                PromptSelectionResult psrBeams = ed.SelectAll(new SelectionFilter(beamFilter));
                List<BeamSegment> beams = new List<BeamSegment>();

                if (psrBeams.Status == PromptStatus.OK)
                {
                    foreach (SelectedObject so in psrBeams.Value)
                    {
                        Entity ent = tr.GetObject(so.ObjectId, OpenMode.ForRead) as Entity;
                        if (ent is Line line)
                        {
                            beams.Add(new BeamSegment
                            {
                                Start = new Point2d(line.StartPoint.X * toFtScale, line.StartPoint.Y * toFtScale),
                                End = new Point2d(line.EndPoint.X * toFtScale, line.EndPoint.Y * toFtScale),
                                LengthFt = line.Length * toFtScale
                            });
                        }
                        else if (ent is Polyline pl)
                        {
                            for (int i = 0; i < pl.NumberOfVertices - 1; i++)
                            {
                                Point2d p1 = new Point2d(pl.GetPoint2dAt(i).X * toFtScale, pl.GetPoint2dAt(i).Y * toFtScale);
                                Point2d p2 = new Point2d(pl.GetPoint2dAt(i + 1).X * toFtScale, pl.GetPoint2dAt(i + 1).Y * toFtScale);
                                beams.Add(new BeamSegment { Start = p1, End = p2, LengthFt = p1.GetDistanceTo(p2) });
                            }
                        }
                    }
                }

                // 5. Engineering Load Rates (BNBC 2020 / FPS Units)
                double gammaC = 150.0;           // pcf
                double gammaM = 120.0;           // pcf
                double tSlabFt = 5.0 / 12.0;     // 5" slab
                double bBeamFt = 10.0 / 12.0;    // 10" beam
                double hBeamFt = 15.0 / 12.0;    // 15" beam
                double tWallFt = 5.0 / 12.0;     // 5" wall
                double clearWallHtFt = Math.Max(0.0, storyHeightFt - hBeamFt);

                double beamNetSW_plf = bBeamFt * (hBeamFt - tSlabFt) * gammaC; // 104.2 plf
                double wallDL_plf = tWallFt * clearWallHtFt * gammaM * 0.85;   // 15% opening deduction
                double totalBeamLine_plf = beamNetSW_plf + wallDL_plf;

                double slabSW_psf = tSlabFt * gammaC;       // 62.5 psf
                double floorDL_psf = slabSW_psf + 55.0;     // 117.5 psf (Slab + 30 psf FF + 25 psf PW)
                double roofDL_psf = slabSW_psf + 35.0;      // 97.5 psf (Slab + 35 psf Lime Terracing)

                double floorLL_unred_psf = 40.0;  // Residential occupancy
                double roofLL_psf = 20.0;        // Flat roof

                // 6. Geometric Tributary Areas & Beam Distribution
                // Assign beam loads accounting for eccentricities, cantilevers, and secondary connections
                double snapToleranceFt = 2.0; // 2 ft snap radius to column boundary
                foreach (var beam in beams)
                {
                    ColumnData startCol = FindNearestColumn(beam.Start, columns, snapToleranceFt);
                    ColumnData endCol = FindNearestColumn(beam.End, columns, snapToleranceFt);

                    if (startCol != null && endCol != null)
                    {
                        // Standard or eccentric beam framed between two columns
                        startCol.ConnectedBeamLengthFt += beam.LengthFt * 0.5;
                        endCol.ConnectedBeamLengthFt += beam.LengthFt * 0.5;
                    }
                    else if (startCol != null && endCol == null)
                    {
                        // Cantilever beam projecting outward from startCol
                        startCol.ConnectedBeamLengthFt += beam.LengthFt;
                    }
                    else if (startCol == null && endCol != null)
                    {
                        // Cantilever projecting from endCol
                        endCol.ConnectedBeamLengthFt += beam.LengthFt;
                    }
                    else
                    {
                        // Secondary beam framing into other beams: split equally to nearest columns
                        ColumnData c1 = FindNearestColumn(beam.Start, columns, 25.0);
                        ColumnData c2 = FindNearestColumn(beam.End, columns, 25.0);
                        if (c1 != null) c1.ConnectedBeamLengthFt += beam.LengthFt * 0.5;
                        if (c2 != null) c2.ConnectedBeamLengthFt += beam.LengthFt * 0.5;
                    }
                }

                // Calculate Tributary Area (Voronoi approximation bounded by slab)
                foreach (var col in columns)
                {
                    col.TribAreaSqFt = CalculateTributaryArea(col.Center, columns, slabAreaSqFt);
                }

                // 7. Multi-Story Downward Load Takedown & BNBC Live Load Reduction
                BlockTableRecord btr = tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite) as BlockTableRecord;

                foreach (var col in columns)
                {
                    double cumDL_lbs = 0.0;
                    double cumFloorLL_unred_lbs = 0.0;
                    double kLL = 4.0; // Interior column influence factor

                    double colSW_per_story = (col.WidthIn / 12.0) * (col.DepthIn / 12.0) * storyHeightFt * gammaC;

                    for (int s = numStories; s >= 1; s--)
                    {
                        bool isRoof = (s == numStories);
                        double dl_psf = isRoof ? roofDL_psf : floorDL_psf;
                        double floorSlabDL = col.TribAreaSqFt * dl_psf;
                        double beamLineLoad = isRoof ? (beamNetSW_plf + 50.0) : totalBeamLine_plf; // 50 plf roof parapet
                        double beamWallDL = col.ConnectedBeamLengthFt * beamLineLoad;

                        cumDL_lbs += (floorSlabDL + beamWallDL + colSW_per_story);

                        if (!isRoof)
                            cumFloorLL_unred_lbs += (col.TribAreaSqFt * floorLL_unred_psf);
                    }

                    // BNBC 2020 Live Load Reduction Formula (FPS)
                    double influenceArea = kLL * (col.TribAreaSqFt * (numStories - 1));
                    double llrFactor = 1.0;
                    if (influenceArea >= 400.0)
                    {
                        llrFactor = 0.25 + (15.0 / Math.Sqrt(influenceArea));
                        llrFactor = Math.Max(0.40, Math.Min(1.0, llrFactor)); // Maximum 60% reduction
                    }

                    double totalReducedLL_lbs = (cumFloorLL_unred_lbs * llrFactor) + (col.TribAreaSqFt * roofLL_psf);

                    // Convert to Kips (1 Kip = 1000 lbs)
                    col.CumulativeDL_Kips = cumDL_lbs / 1000.0;
                    col.CumulativeLL_Kips = totalReducedLL_lbs / 1000.0;

                    // BNBC 2020 / ACI 318 Factored Combinations
                    double pu1 = 1.4 * col.CumulativeDL_Kips;
                    double pu2 = (1.2 * col.CumulativeDL_Kips) + (1.6 * col.CumulativeLL_Kips);
                    col.FactoredPu_Kips = Math.Max(pu1, pu2);

                    // ACI 318 Section Design Check (fc=3000 psi, fy=60000 psi, target rho=1.5%)
                    double fc = 3000.0, fy = 60000.0, rho = 0.015;
                    double phiPnMax_denom = 0.52 * (0.85 * fc * (1.0 - rho) + fy * rho);
                    double reqAg_sqin = (col.FactoredPu_Kips * 1000.0) / phiPnMax_denom;

                    // If existing CAD dimension is smaller than required, recommend enlarged size
                    double b_in = col.WidthIn;
                    double h_in = Math.Ceiling((reqAg_sqin / b_in) / 2.0) * 2.0;
                    h_in = Math.Max(col.DepthIn, h_in);

                    double reqAst = Math.Max(b_in * h_in * 0.01, (col.FactoredPu_Kips * 1000.0 / 0.52 - 0.85 * fc * (b_in * h_in)) / (fy - 0.85 * fc));
                    int rebarCount = (int)Math.Max(4, Math.Ceiling(reqAst / 0.60)); // #7 or #8 bars
                    if (rebarCount % 2 != 0) rebarCount++;

                    // 8. Place Formatted Structural Note on Drawing
                    double insertX = isInchDrawing ? (col.Center.X * 12.0 + 8.0) : (col.Center.X + 0.8);
                    double insertY = isInchDrawing ? (col.Center.Y * 12.0 + 8.0) : (col.Center.Y + 0.8);
                    double textHeight = isInchDrawing ? 8.0 : 0.65;

                    MText note = new MText
                    {
                        Location = new Point3d(insertX, insertY, 0),
                        Contents = $"\\C1;{col.Id} (Base Level)\\P" +
                                   $"\\C7;Trib Area: {col.TribAreaSqFt:0} sft (LLR: {llrFactor:0.00})\\P" +
                                   $"\\C7;DL: {col.CumulativeDL_Kips:0.0} kips | LL: {col.CumulativeLL_Kips:0.0} kips\\P" +
                                   $"\\C3;Pu: {col.FactoredPu_Kips:0.0} kips (1.2D+1.6L)\\P" +
                                   $"\\C2;Rec. Size: {b_in:0}\"x{h_in:0}\" [{rebarCount}-#8]",
                        TextHeight = textHeight
                    };

                    btr.AppendEntity(note);
                    tr.AddNewlyCreatedDBObject(note, true);
                }

                tr.Commit();
                ed.WriteMessage($"\nSuccessfully calculated loads for {columns.Count} columns using BNBC 2020 FPS criteria.");
            }
        }

        private ColumnData FindNearestColumn(Point2d pt, List<ColumnData> columns, double toleranceFt)
        {
            ColumnData nearest = null;
            double minDist = double.MaxValue;
            foreach (var col in columns)
            {
                double d = pt.GetDistanceTo(col.Center);
                if (d < minDist && d <= toleranceFt)
                {
                    minDist = d;
                    nearest = col;
                }
            }
            return nearest;
        }

        private Point2d GetPolylineCentroid(Polyline pl, double scale)
        {
            double x = 0, y = 0;
            for (int i = 0; i < pl.NumberOfVertices; i++)
            {
                x += pl.GetPoint2dAt(i).X;
                y += pl.GetPoint2dAt(i).Y;
            }
            return new Point2d((x / pl.NumberOfVertices) * scale, (y / pl.NumberOfVertices) * scale);
        }

        private double CalculateTributaryArea(Point2d center, List<ColumnData> allCols, double totalSlabArea)
        {
            double minDist = double.MaxValue;
            foreach (var other in allCols)
            {
                if (other.Center == center) continue;
                double d = center.GetDistanceTo(other.Center);
                if (d < minDist) minDist = d;
            }
            if (minDist == double.MaxValue || minDist > 40.0) minDist = 14.0;
            double estimatedArea = Math.Pow(minDist, 2.0);
            return Math.Round(Math.Min(estimatedArea, totalSlabArea / Math.Max(1, allCols.Count * 0.75)), 1);
        }
    }
}