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

    public class ColumnDesignResult
    {
        public string Name { get; set; }
        public Point2d Center { get; set; }
        public double WidthIn { get; set; }
        public double DepthIn { get; set; }
        public double TribAreaSqFt { get; set; }
        public double ConnectedBeamLengthFt { get; set; }
        public double DL_Kips { get; set; }
        public double LL_Kips { get; set; }
        public double Pu_Kips { get; set; }
        
        // Multi-tier schedule sizes & bars
        public string Size_UpToPL { get; set; }
        public string Rebar_UpToPL { get; set; }
        public string Size_GFto2nd { get; set; }
        public string Rebar_GFto2nd { get; set; }
        public string Size_3rdToRoof { get; set; }
        public string Rebar_3rdToRoof { get; set; }
        public string TieSpec { get; set; }
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

            // 1. Dynamic Project Parameters
            PromptIntegerOptions pioStories = new PromptIntegerOptions("\nEnter Total Number of Stories (e.g., 6): ") { DefaultValue = 6 };
            PromptIntegerResult pirStories = ed.GetInteger(pioStories);
            if (pirStories.Status != PromptStatus.OK) return;
            int numStories = pirStories.Value;

            PromptDoubleOptions pdoHeight = new PromptDoubleOptions("\nEnter Story Height in FEET (e.g., 10.0): ") { DefaultValue = 10.0 };
            PromptDoubleResult pirHeight = ed.GetDouble(pdoHeight);
            if (pirHeight.Status != PromptStatus.OK) return;
            double storyHeightFt = pirHeight.Value;

            // Dynamic Live Load Prompt (Residential: 40 psf, Commercial: 65 - 100 psf)
            PromptDoubleOptions pdoLL = new PromptDoubleOptions("\nEnter Floor Live Load in PSF (Residential=40, Commercial=65-100): ") { DefaultValue = 40.0 };
            PromptDoubleResult pirLL = ed.GetDouble(pdoLL);
            if (pirLL.Status != PromptStatus.OK) return;
            double floorLL_unred_psf = pirLL.Value;

            // Dynamic Floor Finish Prompt (15 to 25 psf)
            PromptDoubleOptions pdoFF = new PromptDoubleOptions("\nEnter Floor Finish (FF) in PSF (15 to 25): ") { DefaultValue = 20.0 };
            PromptDoubleResult pirFF = ed.GetDouble(pdoFF);
            if (pirFF.Status != PromptStatus.OK) return;
            double ff_psf = pirFF.Value;

            // Dynamic Partition Wall Prompt
            PromptDoubleOptions pdoPW = new PromptDoubleOptions("\nEnter Random Partition Wall Load on Slab in PSF (e.g., 25.0): ") { DefaultValue = 25.0 };
            PromptDoubleResult pirPW = ed.GetDouble(pdoPW);
            if (pirPW.Status != PromptStatus.OK) return;
            double pw_psf = pirPW.Value;

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
                    ed.WriteMessage("\nError: Polyline on 'SLAB_OUTLINE' must be closed.");
                    return;
                }

                Extents3d slabExtents = slabPoly.GeometricExtents;
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

                List<ColumnDesignResult> columns = new List<ColumnDesignResult>();
                int cIndex = 1;
                foreach (SelectedObject so in psrCols.Value)
                {
                    Entity ent = tr.GetObject(so.ObjectId, OpenMode.ForRead) as Entity;
                    double widthIn = 12.0, depthIn = 15.0;
                    Point2d center = Point2d.Origin;

                    if (ent is Polyline pl)
                    {
                        center = GetPolylineCentroid(pl, toFtScale);
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

                    columns.Add(new ColumnDesignResult
                    {
                        Name = $"C-{cIndex++}",
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

                // 5. Engineering Load Formulations
                double gammaC = 150.0;
                double gammaM = 120.0;
                double tSlabFt = 5.0 / 12.0;
                double bBeamFt = 10.0 / 12.0;
                double hBeamFt = 15.0 / 12.0;
                double tWallFt = 5.0 / 12.0;
                double clearWallHtFt = Math.Max(0.0, storyHeightFt - hBeamFt);

                double beamNetSW_plf = bBeamFt * (hBeamFt - tSlabFt) * gammaC;
                double wallDL_plf = tWallFt * clearWallHtFt * gammaM * 0.85;
                double totalBeamLine_plf = beamNetSW_plf + wallDL_plf;

                double slabSW_psf = tSlabFt * gammaC;
                double floorDL_psf = slabSW_psf + ff_psf + pw_psf;
                double roofDL_psf = slabSW_psf + 35.0; // 35 psf roof lime terracing/screed
                double roofLL_psf = 20.0;

                // Map beam framing lengths
                double snapToleranceFt = 2.0;
                foreach (var beam in beams)
                {
                    ColumnDesignResult startCol = FindNearestColumn(beam.Start, columns, snapToleranceFt);
                    ColumnDesignResult endCol = FindNearestColumn(beam.End, columns, snapToleranceFt);

                    if (startCol != null && endCol != null)
                    {
                        startCol.ConnectedBeamLengthFt += beam.LengthFt * 0.5;
                        endCol.ConnectedBeamLengthFt += beam.LengthFt * 0.5;
                    }
                    else if (startCol != null)
                    {
                        startCol.ConnectedBeamLengthFt += beam.LengthFt;
                    }
                    else if (endCol != null)
                    {
                        endCol.ConnectedBeamLengthFt += beam.LengthFt;
                    }
                    else
                    {
                        ColumnDesignResult c1 = FindNearestColumn(beam.Start, columns, 25.0);
                        ColumnDesignResult c2 = FindNearestColumn(beam.End, columns, 25.0);
                        if (c1 != null) c1.ConnectedBeamLengthFt += beam.LengthFt * 0.5;
                        if (c2 != null) c2.ConnectedBeamLengthFt += beam.LengthFt * 0.5;
                    }
                }

                // Tributary areas
                foreach (var col in columns)
                {
                    col.TribAreaSqFt = CalculateTributaryArea(col.Center, columns, slabAreaSqFt);
                }

                // 6. Multi-Story Downward Axial Takedown & ACI 318 Sizing
                BlockTableRecord btr = tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite) as BlockTableRecord;

                foreach (var col in columns)
                {
                    double cumDL_lbs = 0.0;
                    double cumFloorLL_unred_lbs = 0.0;
                    double kLL = 4.0;
                    double colSW_per_story = (col.WidthIn / 12.0) * (col.DepthIn / 12.0) * storyHeightFt * gammaC;

                    for (int s = numStories; s >= 1; s--)
                    {
                        bool isRoof = (s == numStories);
                        double dl_psf = isRoof ? roofDL_psf : floorDL_psf;
                        double floorSlabDL = col.TribAreaSqFt * dl_psf;
                        double beamLineLoad = isRoof ? (beamNetSW_plf + 50.0) : totalBeamLine_plf;
                        double beamWallDL = col.ConnectedBeamLengthFt * beamLineLoad;

                        cumDL_lbs += (floorSlabDL + beamWallDL + colSW_per_story);

                        if (!isRoof)
                            cumFloorLL_unred_lbs += (col.TribAreaSqFt * floorLL_unred_psf);
                    }

                    // BNBC 2020 LLR
                    double influenceArea = kLL * (col.TribAreaSqFt * (numStories - 1));
                    double llrFactor = 1.0;
                    if (influenceArea >= 400.0)
                    {
                        llrFactor = 0.25 + (15.0 / Math.Sqrt(influenceArea));
                        llrFactor = Math.Max(0.40, Math.Min(1.0, llrFactor));
                    }

                    double totalReducedLL_lbs = (cumFloorLL_unred_lbs * llrFactor) + (col.TribAreaSqFt * roofLL_psf);

                    col.DL_Kips = cumDL_lbs / 1000.0;
                    col.LL_Kips = totalReducedLL_lbs / 1000.0;
                    col.Pu_Kips = Math.Max(1.4 * col.DL_Kips, (1.2 * col.DL_Kips) + (1.6 * col.LL_Kips));

                    // Multi-tier Column Design Calculations
                    // Tier 1: Base up to Plinth Level (100% Pu)
                    CalculateTierDesign(col.Pu_Kips, col.WidthIn, col.DepthIn, out string sz1, out string rb1);
                    col.Size_UpToPL = sz1;
                    col.Rebar_UpToPL = rb1;

                    // Tier 2: GF to 2nd Floor (~70% Pu)
                    CalculateTierDesign(col.Pu_Kips * 0.70, col.WidthIn, col.DepthIn, out string sz2, out string rb2);
                    col.Size_GFto2nd = sz2;
                    col.Rebar_GFto2nd = rb2;

                    // Tier 3: 3rd Floor to Roof (~40% Pu)
                    CalculateTierDesign(col.Pu_Kips * 0.40, col.WidthIn, col.DepthIn, out string sz3, out string rb3);
                    col.Size_3rdToRoof = sz3;
                    col.Rebar_3rdToRoof = rb3;

                    col.TieSpec = "10mm Ø @ 4\" c/c (L/4), @ 7\" c/c (mid)";

                    // In-drawing Column Text Annotation
                    double insertX = isInchDrawing ? (col.Center.X * 12.0 + 8.0) : (col.Center.X + 0.8);
                    double insertY = isInchDrawing ? (col.Center.Y * 12.0 + 8.0) : (col.Center.Y + 0.8);
                    double txtHt = isInchDrawing ? 7.0 : 0.60;

                    MText note = new MText
                    {
                        Location = new Point3d(insertX, insertY, 0),
                        Contents = $"\\C1;{col.Name}\\P" +
                                   $"\\C7;At: {col.TribAreaSqFt:0} sft\\P" +
                                   $"\\C7;DL: {col.DL_Kips:0.0}k | LL: {col.LL_Kips:0.0}k\\P" +
                                   $"\\C3;Pu: {col.Pu_Kips:0.0} kips\\P" +
                                   $"\\C2;PL: {col.Size_UpToPL} [{col.Rebar_UpToPL}]",
                        TextHeight = txtHt
                    };
                    btr.AppendEntity(note);
                    tr.AddNewlyCreatedDBObject(note, true);
                }

                // 7. Insert Column Design Schedule Table
                InsertColumnScheduleTable(db, tr, btr, columns, slabExtents, isInchDrawing);

                tr.Commit();
                ed.WriteMessage($"\nCompleted! Processed {columns.Count} columns and inserted the Column Design Table.");
            }
        }

        private void CalculateTierDesign(double puKips, double baseW, double baseD, out string sizeStr, out string rebarStr)
        {
            double fc = 3000.0, fy = 60000.0, rho = 0.015;
            double phiPnDenom = 0.52 * (0.85 * fc * (1.0 - rho) + fy * rho);
            double reqAg = (puKips * 1000.0) / phiPnDenom;

            double b = Math.Max(10.0, baseW);
            double h = Math.Ceiling((reqAg / b) / 2.0) * 2.0;
            h = Math.Max(b, Math.Max(baseD, h));

            sizeStr = $"{b:0}\"x{h:0}\"";

            double reqAst = Math.Max(b * h * 0.01, (puKips * 1000.0 / 0.52 - 0.85 * fc * (b * h)) / (fy - 0.85 * fc));
            
            // Rebar combinations (16mm = 0.31 sq in, 20mm = 0.49 sq in)
            int count20 = (int)Math.Max(4, Math.Ceiling(reqAst / 0.49));
            if (count20 % 2 != 0) count20++;

            rebarStr = $"{count20}-20mm Ø";
            if (count20 <= 4)
            {
                int count16 = (int)Math.Max(4, Math.Ceiling(reqAst / 0.31));
                if (count16 % 2 != 0) count16++;
                rebarStr = $"{count16}-16mm Ø";
            }
        }

        private void InsertColumnScheduleTable(Database db, Transaction tr, BlockTableRecord btr, List<ColumnDesignResult> columns, Extents3d slabExtents, bool isInch)
        {
            Table table = new Table();
            table.TableStyle = db.Tablestyle;

            int numRows = columns.Count + 2;
            int numCols = 8;
            table.SetSize(numRows, numCols);

            double rowHeight = isInch ? 28.0 : 2.5;
            double colWidth = isInch ? 95.0 : 8.0;

            table.SetRowHeight(rowHeight);
            table.SetColumnWidth(colWidth);

            // Title Row
            table.Cells[0, 0].TextString = "COLUMN DESIGN SCHEDULE (BNBC 2020 & ACI 318)";
            table.Cells[0, 0].TextHeight = isInch ? 14.0 : 1.2;

            // Headers
            string[] headers = {
                "COLUMN\nNAME",
                "TRIB AREA\n(sq ft)",
                "DEAD LOAD\n(kips)",
                "LIVE LOAD\n(kips)",
                "FACTORED Pu\n(kips)",
                "UP TO PL\n(Size & Rebar)",
                "GF TO 2ND FLR\n(Size & Rebar)",
                "3RD TO ROOF\n(Size & Rebar)"
            };

            for (int c = 0; c < numCols; c++)
            {
                table.Cells[1, c].TextString = headers[c];
                table.Cells[1, c].TextHeight = isInch ? 9.0 : 0.8;
                table.Cells[1, c].Alignment = CellAlignment.MiddleCenter;
            }

            // Populate rows
            for (int i = 0; i < columns.Count; i++)
            {
                var col = columns[i];
                int r = i + 2;

                table.Cells[r, 0].TextString = col.Name;
                table.Cells[r, 1].TextString = $"{col.TribAreaSqFt:0}";
                table.Cells[r, 2].TextString = $"{col.DL_Kips:0.1}";
                table.Cells[r, 3].TextString = $"{col.LL_Kips:0.1}";
                table.Cells[r, 4].TextString = $"{col.Pu_Kips:0.1}";
                table.Cells[r, 5].TextString = $"{col.Size_UpToPL}\n{col.Rebar_UpToPL}";
                table.Cells[r, 6].TextString = $"{col.Size_GFto2nd}\n{col.Rebar_GFto2nd}";
                table.Cells[r, 7].TextString = $"{col.Size_3rdToRoof}\n{col.Rebar_3rdToRoof}";

                for (int c = 0; c < numCols; c++)
                {
                    table.Cells[r, c].TextHeight = isInch ? 7.5 : 0.65;
                    table.Cells[r, c].Alignment = CellAlignment.MiddleCenter;
                }
            }

            // Position table to the right side of the floor plan
            double gap = isInch ? 60.0 : 5.0;
            double posX = slabExtents.MaxPoint.X + gap;
            double posY = slabExtents.MaxPoint.Y;

            table.Position = new Point3d(posX, posY, 0);

            btr.AppendEntity(table);
            tr.AddNewlyCreatedDBObject(table, true);
        }

        private ColumnDesignResult FindNearestColumn(Point2d pt, List<ColumnDesignResult> columns, double toleranceFt)
        {
            ColumnDesignResult nearest = null;
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

        private double CalculateTributaryArea(Point2d center, List<ColumnDesignResult> allCols, double totalSlabArea)
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
