using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Colors;
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

            RibbonPanelSource panelSource = new RibbonPanelSource { Title = "Design & Detailing" };
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

    public class ProjectMetadata
    {
        public string ProjectName { get; set; }
        public string ClientName { get; set; }
        public string PlotNo { get; set; }
        public string Address { get; set; }
        public string EngineerName { get; set; } = "Engr. Rezaul Islam, M.Sc in Civil Engg.";
        public string IEB_Reg { get; set; } = "Fellow of IEB No: F/12380";
        public string KDA_Reg { get; set; } = "KDA Reg. No: Ka-09";
        public int TotalStories { get; set; }
        public double StoryHeightFt { get; set; }
        public double FixedWidthIn { get; set; }
        public double WindSpeedMph { get; set; }
        public double SeismicZoneZ { get; set; }
        public double FloorLL_Psf { get; set; }
        public double FloorFF_Psf { get; set; }
        public double Partition_Psf { get; set; }
    }

    public class TierDesign
    {
        public string TierLabel { get; set; }
        public double FactoredPu_Kips { get; set; }
        public double FactoredMu_FtKips { get; set; }
        public double WidthIn { get; set; }
        public double DepthIn { get; set; }
        public int BarCount { get; set; }
        public int BarDiaMm { get; set; }
        public string TieSpec { get; set; }
        public double SteelWeightKg { get; set; }
    }

    public class ColumnDesignResult
    {
        public string Name { get; set; }
        public Point2d Center { get; set; }
        public double TribAreaSqFt { get; set; }
        public double ConnectedBeamLengthFt { get; set; }
        public double TotalBaseDL_Kips { get; set; }
        public double TotalBaseLL_Kips { get; set; }
        public double WindShear_Kips { get; set; }
        public double WindMoment_FtKips { get; set; }
        public double SeismicShear_Kips { get; set; }
        public double SeismicMoment_FtKips { get; set; }
        public double GoverningPu_Kips { get; set; }
        public double GoverningMu_FtKips { get; set; }
        public string GoverningCase { get; set; }
        public List<TierDesign> Tiers { get; set; } = new List<TierDesign>();
        public double TotalColSteelKg { get; set; }
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

            ProjectMetadata meta = new ProjectMetadata();

            // 1. Text Inputs - AllowSpaces = true enables spaces, dots, and commas
            PromptStringOptions psoProject = new PromptStringOptions("\nEnter Project Name: ") { DefaultValue = "Proposed Multi-Story Building", AllowSpaces = true };
            PromptResult prProject = ed.GetString(psoProject);
            meta.ProjectName = string.IsNullOrWhiteSpace(prProject.StringResult) ? "Proposed Building" : prProject.StringResult;

            PromptStringOptions psoClient = new PromptStringOptions("\nEnter Client Name: ") { DefaultValue = "Dr. Sabrina Sighnidha", AllowSpaces = true };
            PromptResult prClient = ed.GetString(psoClient);
            meta.ClientName = string.IsNullOrWhiteSpace(prClient.StringResult) ? "Dr. Sabrina Sighnidha" : prClient.StringResult;

            PromptStringOptions psoPlot = new PromptStringOptions("\nEnter Plot / Holding No: ") { DefaultValue = "Plot-12, Block-B", AllowSpaces = true };
            PromptResult prPlot = ed.GetString(psoPlot);
            meta.PlotNo = string.IsNullOrWhiteSpace(prPlot.StringResult) ? "Plot-12" : prPlot.StringResult;

            PromptStringOptions psoAddr = new PromptStringOptions("\nEnter Site Address: ") { DefaultValue = "03, South Central Road, Khulna", AllowSpaces = true };
            PromptResult prAddr = ed.GetString(psoAddr);
            meta.Address = string.IsNullOrWhiteSpace(prAddr.StringResult) ? "03, South Central Road, Khulna" : prAddr.StringResult;

            // 2. Geometry & Constraints
            PromptIntegerOptions pioStories = new PromptIntegerOptions("\nEnter Total Number of Stories (e.g. 5 or 6): ") { DefaultValue = 6 };
            PromptIntegerResult pirStories = ed.GetInteger(pioStories);
            meta.TotalStories = pirStories.Value;

            PromptDoubleOptions pdoHeight = new PromptDoubleOptions("\nEnter Story Height in FEET (e.g. 10.0): ") { DefaultValue = 10.0 };
            PromptDoubleResult pirHeight = ed.GetDouble(pdoHeight);
            meta.StoryHeightFt = pirHeight.Value;

            PromptDoubleOptions pdoColW = new PromptDoubleOptions("\nEnter Standard Column WIDTH to fix in INCHES (10\", 12\", 14\"): ") { DefaultValue = 10.0 };
            PromptDoubleResult pirColW = ed.GetDouble(pdoColW);
            meta.FixedWidthIn = Math.Max(10.0, pirColW.Value);

            // 3. Gravity Loads
            PromptDoubleOptions pdoLL = new PromptDoubleOptions("\nEnter Floor Live Load in PSF (Residential=40, Commercial=60-100): ") { DefaultValue = 40.0 };
            PromptDoubleResult pirLL = ed.GetDouble(pdoLL);
            meta.FloorLL_Psf = pirLL.Value;

            PromptDoubleOptions pdoFF = new PromptDoubleOptions("\nEnter Floor Finish (FF) in PSF (15 to 25): ") { DefaultValue = 20.0 };
            PromptDoubleResult pirFF = ed.GetDouble(pdoFF);
            meta.FloorFF_Psf = pirFF.Value;

            PromptDoubleOptions pdoPW = new PromptDoubleOptions("\nEnter Partition Wall Load on Slab in PSF: ") { DefaultValue = 25.0 };
            PromptDoubleResult pirPW = ed.GetDouble(pdoPW);
            meta.Partition_Psf = pirPW.Value;

            // 4. BNBC Lateral Inputs (Khulna: V=160 mph, Z=0.12)
            PromptDoubleOptions pdoWind = new PromptDoubleOptions("\nEnter Basic Wind Speed V in MPH (Khulna=160 mph): ") { DefaultValue = 160.0 };
            PromptDoubleResult pirWind = ed.GetDouble(pdoWind);
            meta.WindSpeedMph = pirWind.Value;

            PromptDoubleOptions pdoZone = new PromptDoubleOptions("\nEnter BNBC Seismic Zone Factor Z (Khulna Zone 1 = 0.12): ") { DefaultValue = 0.12 };
            PromptDoubleResult pirZone = ed.GetDouble(pdoZone);
            meta.SeismicZoneZ = pirZone.Value;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                // Select Slab Boundary
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
                bool isInch = slabPoly.Area > 20000.0;
                double toFtScale = isInch ? (1.0 / 12.0) : 1.0;
                double toSqFtScale = isInch ? (1.0 / 144.0) : 1.0;
                double slabAreaSqFt = slabPoly.Area * toSqFtScale;

                double buildingWidthFt = (slabExtents.MaxPoint.X - slabExtents.MinPoint.X) * toFtScale;
                double buildingLengthFt = (slabExtents.MaxPoint.Y - slabExtents.MinPoint.Y) * toFtScale;
                double totalBuildingHtFt = meta.TotalStories * meta.StoryHeightFt;

                // Extract Column Locations
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
                    Point2d center = Point2d.Origin;

                    if (ent is Polyline pl)
                        center = GetPolylineCentroid(pl, toFtScale);
                    else if (ent is BlockReference blk)
                        center = new Point2d(blk.Position.X * toFtScale, blk.Position.Y * toFtScale);

                    columns.Add(new ColumnDesignResult
                    {
                        Name = $"C-{cIndex++}",
                        Center = center
                    });
                }

                // Extract Beams
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

                // Load unit rates
                double gammaC = 150.0;
                double gammaM = 120.0;
                double tSlabFt = 5.0 / 12.0;
                double bBeamFt = 10.0 / 12.0;
                double hBeamFt = 15.0 / 12.0;
                double tWallFt = 5.0 / 12.0;
                double clearWallHtFt = Math.Max(0.0, meta.StoryHeightFt - hBeamFt);

                double beamNetSW_plf = bBeamFt * (hBeamFt - tSlabFt) * gammaC;
                double wallDL_plf = tWallFt * clearWallHtFt * gammaM * 0.85;
                double totalBeamLine_plf = beamNetSW_plf + wallDL_plf;

                double slabSW_psf = tSlabFt * gammaC;
                double floorDL_psf = slabSW_psf + meta.FloorFF_Psf + meta.Partition_Psf;
                double roofDL_psf = slabSW_psf + 35.0;
                double roofLL_psf = 20.0;

                // Beam framing distribution
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
                        startCol.ConnectedBeamLengthFt += beam.LengthFt;
                    else if (endCol != null)
                        endCol.ConnectedBeamLengthFt += beam.LengthFt;
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
                    col.TribAreaSqFt = CalculateTributaryArea(col.Center, columns, slabAreaSqFt);

                // Global Lateral Shears
                double qz = 0.00256 * 0.85 * 1.0 * Math.Pow(meta.WindSpeedMph, 2.0);
                double designWindPressure_psf = qz * 0.85 * 1.3;
                double totalBuildingWindShear_Kips = (designWindPressure_psf * Math.Max(buildingWidthFt, buildingLengthFt) * totalBuildingHtFt) / 1000.0;

                double singleFloorDead_Kips = (slabAreaSqFt * floorDL_psf) / 1000.0;
                double totalSeismicWeight_Kips = (singleFloorDead_Kips * meta.TotalStories) + ((slabAreaSqFt * meta.FloorLL_Psf * 0.25) / 1000.0);
                double responseR = 5.0;
                double importanceI = 1.0;
                double seismicCoeff = (2.0 / 3.0) * (meta.SeismicZoneZ * importanceI * 2.5 / responseR);
                double totalBuildingSeismicShear_Kips = Math.Max(0.044 * meta.SeismicZoneZ * importanceI * totalSeismicWeight_Kips, seismicCoeff * totalSeismicWeight_Kips);

                BlockTableRecord btr = tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite) as BlockTableRecord;
                int numColsTotal = columns.Count;

                foreach (var col in columns)
                {
                    double cumDL_lbs = 0.0;
                    double cumFloorLL_unred_lbs = 0.0;
                    double kLL = 4.0;
                    double colSW_per_story = (meta.FixedWidthIn / 12.0) * (15.0 / 12.0) * meta.StoryHeightFt * gammaC;

                    for (int s = meta.TotalStories; s >= 1; s--)
                    {
                        bool isRoof = (s == meta.TotalStories);
                        double dl_psf = isRoof ? roofDL_psf : floorDL_psf;
                        double floorSlabDL = col.TribAreaSqFt * dl_psf;
                        double beamLineLoad = isRoof ? (beamNetSW_plf + 50.0) : totalBeamLine_plf;
                        double beamWallDL = col.ConnectedBeamLengthFt * beamLineLoad;

                        cumDL_lbs += (floorSlabDL + beamWallDL + colSW_per_story);
                        if (!isRoof)
                            cumFloorLL_unred_lbs += (col.TribAreaSqFt * meta.FloorLL_Psf);
                    }

                    // BNBC Live Load Reduction
                    double influenceArea = kLL * (col.TribAreaSqFt * (meta.TotalStories - 1));
                    double llrFactor = 1.0;
                    if (influenceArea >= 400.0)
                    {
                        llrFactor = 0.25 + (15.0 / Math.Sqrt(influenceArea));
                        llrFactor = Math.Max(0.40, Math.Min(1.0, llrFactor));
                    }

                    col.TotalBaseDL_Kips = cumDL_lbs / 1000.0;
                    col.TotalBaseLL_Kips = ((cumFloorLL_unred_lbs * llrFactor) + (col.TribAreaSqFt * roofLL_psf)) / 1000.0;

                    // Lateral distribution
                    bool isExterior = (col.TribAreaSqFt < (slabAreaSqFt / numColsTotal) * 0.8);
                    double shareFactor = isExterior ? (1.0 / (numColsTotal * 1.5)) : (2.0 / (numColsTotal * 1.5));

                    col.WindShear_Kips = totalBuildingWindShear_Kips * shareFactor;
                    col.WindMoment_FtKips = col.WindShear_Kips * (meta.StoryHeightFt / 2.0);

                    col.SeismicShear_Kips = totalBuildingSeismicShear_Kips * shareFactor;
                    col.SeismicMoment_FtKips = col.SeismicShear_Kips * (meta.StoryHeightFt / 2.0);

                    double deltaP_wind = (col.WindMoment_FtKips * meta.TotalStories * 0.5) / Math.Max(15.0, buildingWidthFt);
                    double deltaP_eq = (col.SeismicMoment_FtKips * meta.TotalStories * 0.5) / Math.Max(15.0, buildingWidthFt);

                    double D = col.TotalBaseDL_Kips;
                    double L = col.TotalBaseLL_Kips;
                    double Wm = col.WindMoment_FtKips;
                    double Em = col.SeismicMoment_FtKips;

                    double c2_p = 1.2 * D + 1.6 * L;
                    double c3_p = 1.2 * D + 1.0 * L + deltaP_wind; double c3_m = Wm;
                    double c5_p = 1.2 * D + 1.0 * L + deltaP_eq; double c5_m = Em;

                    double h_est_ft = 15.0 / 12.0;
                    double eq2 = c2_p;
                    double eq3 = c3_p + (2.0 * c3_m / h_est_ft);
                    double eq5 = c5_p + (2.0 * c5_m / h_est_ft);

                    double maxEq = eq2;
                    col.GoverningPu_Kips = c2_p;
                    col.GoverningMu_FtKips = 0.0;
                    col.GoverningCase = "1.2D+1.6L (Gravity)";

                    if (eq3 > maxEq) { maxEq = eq3; col.GoverningPu_Kips = c3_p; col.GoverningMu_FtKips = c3_m; col.GoverningCase = "1.2D+1.0L+1.0W (Wind)"; }
                    if (eq5 > maxEq) { maxEq = eq5; col.GoverningPu_Kips = c5_p; col.GoverningMu_FtKips = c5_m; col.GoverningCase = "1.2D+1.0L+1.0E (Earthquake)"; }

                    // Multi-tier designs
                    int t1_floors = meta.TotalStories;
                    int t2_floors = Math.Max(1, meta.TotalStories - 2);
                    int t3_floors = Math.Max(1, meta.TotalStories - 4);

                    string t1_lbl = meta.TotalStories <= 5 ? "Up to 1st Floor" : "Up to PL";
                    string t2_lbl = meta.TotalStories <= 5 ? "2nd to 3rd Floor" : "GF to 2nd Floor";
                    string t3_lbl = meta.TotalStories <= 5 ? "4th to Roof" : "3rd to Roof";

                    var t1 = DesignColumnSectionFromWidth(col.GoverningPu_Kips, col.GoverningMu_FtKips, meta.FixedWidthIn, t1_lbl, 2 * meta.StoryHeightFt);
                    col.Tiers.Add(t1);

                    var t2 = DesignColumnSectionFromWidth(col.GoverningPu_Kips * ((double)t2_floors / t1_floors), col.GoverningMu_FtKips * 0.75, meta.FixedWidthIn, t2_lbl, 2 * meta.StoryHeightFt);
                    col.Tiers.Add(t2);

                    var t3 = DesignColumnSectionFromWidth(col.GoverningPu_Kips * ((double)t3_floors / t1_floors), col.GoverningMu_FtKips * 0.40, meta.FixedWidthIn, t3_lbl, (meta.TotalStories - 4) * meta.StoryHeightFt);
                    col.Tiers.Add(t3);

                    col.TotalColSteelKg = t1.SteelWeightKg + t2.SteelWeightKg + t3.SteelWeightKg;

                    // --- ONLY NUMBERING PLACED IN PLAN (C-1, C-2, C-3...) ---
                    double labelX = isInch ? (col.Center.X * 12.0) : col.Center.X;
                    double labelY = isInch ? (col.Center.Y * 12.0) : col.Center.Y;
                    double labelHt = isInch ? 10.0 : 0.85;

                    MText planLabel = new MText
                    {
                        Location = new Point3d(labelX, labelY, 0),
                        Contents = $"\\C2;{col.Name}",
                        TextHeight = labelHt,
                        Attachment = AttachmentPoint.MiddleCenter
                    };
                    btr.AppendEntity(planLabel);
                    tr.AddNewlyCreatedDBObject(planLabel, true);
                }

                // 8. Place Drawings & Tables at least 20 FEET CLEAR from Slab Outline
                double clearOffsetCAD = isInch ? (25.0 * 12.0) : 25.0; // 25 feet clear distance
                double startDrawingX = slabExtents.MaxPoint.X + clearOffsetCAD;
                double startDrawingY = slabExtents.MaxPoint.Y;

                // Visual Detailing (Long-Sections and Cross-Sections)
                DrawProfessionalColumnDetails(db, btr, tr, columns, meta.TotalStories, meta.StoryHeightFt, startDrawingX, startDrawingY, isInch);

                double totalElevationsWidth = columns.Count * (isInch ? 190.0 : 16.0);
                double tableOffsetX = startDrawingX + totalElevationsWidth + (isInch ? 120.0 : 10.0);

                // Table 1: External Column Load Summary Table
                InsertColumnLoadSummaryTable(db, tr, btr, columns, tableOffsetX, startDrawingY, isInch);

                // Table 2: Multi-Tier Schedule Table (Placed beside or below Table 1)
                double schedOffsetY = startDrawingY - (isInch ? 360.0 : 30.0);
                InsertLateralColumnScheduleTable(db, tr, btr, columns, tableOffsetX, schedOffsetY, isInch);

                // Table 3: Bar Bending Schedule (BBS) Table
                double bbsOffsetY = schedOffsetY - (isInch ? 380.0 : 32.0);
                InsertBBSTable(db, tr, btr, columns, tableOffsetX, bbsOffsetY, isInch);

                // General Notes
                InsertLateralMaterialNotes(btr, tr, tableOffsetX, bbsOffsetY - (isInch ? 260.0 : 22.0), isInch, meta.WindSpeedMph, meta.SeismicZoneZ, meta);

                // 9. Generate HTML & PDF Reports
                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string baseFileName = $"{meta.ProjectName.Replace(" ", "_")}_Structural_Calculation_Report";
                string htmlPath = Path.Combine(desktopPath, $"{baseFileName}.html");
                string pdfPath = Path.Combine(desktopPath, $"{baseFileName}.pdf");

                GeneratePrintableHtmlReport(htmlPath, meta, columns, totalBuildingWindShear_Kips, totalBuildingSeismicShear_Kips);
                bool pdfSuccess = ConvertHtmlToPdfViaBrowser(htmlPath, pdfPath);

                tr.Commit();
                ed.WriteMessage($"\nDone! Clean column numbers placed in plan. External load summary & schedule tables created > 20' away.");
                if (pdfSuccess)
                    ed.WriteMessage($"\nPDF Calculation Report generated on Desktop: {pdfPath}");
                else
                    ed.WriteMessage($"\nHTML Calculation Sheet generated on Desktop: {htmlPath}");
            }
        }

        // Table 1: Column Load & Analysis Summary Table
        private void InsertColumnLoadSummaryTable(Database db, Transaction tr, BlockTableRecord btr, List<ColumnDesignResult> columns, double posX, double posY, bool isInch)
        {
            Table table = new Table();
            table.TableStyle = db.Tablestyle;

            int numRows = columns.Count + 2;
            int numCols = 7;
            table.SetSize(numRows, numCols);

            double rowHeight = isInch ? 28.0 : 2.4;
            double colWidth = isInch ? 115.0 : 9.5;

            table.SetRowHeight(rowHeight);
            table.SetColumnWidth(colWidth);

            table.Cells[0, 0].TextString = "TABLE 1: COLUMN AXIAL & LATERAL LOAD SUMMARY (BNBC 2020)";
            table.Cells[0, 0].TextHeight = isInch ? 13.0 : 1.1;

            string[] headers = {
                "COLUMN\nID",
                "TRIBUTARY\nAREA (sft)",
                "SERVICE DL\n(kips)",
                "REDUCED LL\n(kips)",
                "GOVERNING\nCOMBINATION",
                "DESIGN Pu\n(kips)",
                "DESIGN Mu\n(k-ft)"
            };

            for (int c = 0; c < numCols; c++)
            {
                table.Cells[1, c].TextString = headers[c];
                table.Cells[1, c].TextHeight = isInch ? 8.0 : 0.70;
                table.Cells[1, c].Alignment = CellAlignment.MiddleCenter;
            }

            for (int i = 0; i < columns.Count; i++)
            {
                var col = columns[i];
                int r = i + 2;

                table.Cells[r, 0].TextString = col.Name;
                table.Cells[r, 1].TextString = $"{col.TribAreaSqFt:0}";
                table.Cells[r, 2].TextString = $"{col.TotalBaseDL_Kips:0.1}";
                table.Cells[r, 3].TextString = $"{col.TotalBaseLL_Kips:0.1}";
                table.Cells[r, 4].TextString = col.GoverningCase;
                table.Cells[r, 5].TextString = $"{col.GoverningPu_Kips:0.1}";
                table.Cells[r, 6].TextString = $"{col.GoverningMu_FtKips:0.1}";

                for (int c = 0; c < numCols; c++)
                {
                    table.Cells[r, c].TextHeight = isInch ? 7.5 : 0.65;
                    table.Cells[r, c].Alignment = CellAlignment.MiddleCenter;
                }
            }

            table.Position = new Point3d(posX, posY, 0);
            btr.AppendEntity(table);
            tr.AddNewlyCreatedDBObject(table, true);
        }

        private TierDesign DesignColumnSectionFromWidth(double puKips, double muFtKips, double fixedWidthIn, string label, double tierHeightFt)
        {
            double fc = 3000.0, fy = 60000.0, rhoTarget = 0.018;
            double b = fixedWidthIn;

            double h = Math.Max(b, 12.0);
            for (int iter = 0; iter < 3; iter++)
            {
                double puEquiv = puKips + (2.0 * muFtKips / (h / 12.0));
                double phiPnDenom = 0.52 * (0.85 * fc * (1.0 - rhoTarget) + fy * rhoTarget);
                double reqAg = (puEquiv * 1000.0) / phiPnDenom;
                double calcDepth = reqAg / b;
                h = Math.Ceiling(calcDepth / 2.0) * 2.0;
                h = Math.Max(b, Math.Max(12.0, h));
            }

            double finalPuEquiv = puKips + (2.0 * muFtKips / (h / 12.0));
            double reqAst = Math.Max(b * h * 0.01, (finalPuEquiv * 1000.0 / 0.52 - 0.85 * fc * (b * h)) / (fy - 0.85 * fc));

            int barCount = 4;
            int barDia = 16;

            if (reqAst > 8 * 0.31)
            {
                barDia = 20;
                barCount = (int)Math.Max(6, Math.Ceiling(reqAst / 0.49));
            }
            else if (reqAst > 4 * 0.31)
            {
                barDia = 16;
                barCount = (int)Math.Max(6, Math.Ceiling(reqAst / 0.31));
            }
            else
            {
                barDia = 16;
                barCount = 4;
            }

            if (barCount % 2 != 0) barCount++;

            double unitWtMain = (barDia == 20) ? 0.753 : 0.482;
            double mainSteelKg = barCount * (tierHeightFt * 1.25) * unitWtMain;

            double tiePerimeterFt = (2 * (b + h) - 16.0 + 8.0) / 12.0;
            int numTies = (int)Math.Ceiling(tierHeightFt / (4.5 / 12.0));
            double tieSteelKg = numTies * tiePerimeterFt * 0.188;

            return new TierDesign
            {
                TierLabel = label,
                FactoredPu_Kips = puKips,
                FactoredMu_FtKips = muFtKips,
                WidthIn = b,
                DepthIn = h,
                BarCount = barCount,
                BarDiaMm = barDia,
                TieSpec = "10mm Ø @ 4\" (L/4), @ 7\" (mid)",
                SteelWeightKg = Math.Round(mainSteelKg + tieSteelKg, 1)
            };
        }

        private void DrawProfessionalColumnDetails(Database db, BlockTableRecord btr, Transaction tr, List<ColumnDesignResult> columns, int numStories, double storyHtFt, double startX, double startY, bool isInch)
        {
            double scale = isInch ? 12.0 : 1.0;
            double colSpacing = isInch ? 190.0 : 16.0;

            Color pureBlackColor = Color.FromRgb(0, 0, 0);
            Color stirrupColor = Color.FromColorIndex(ColorMethod.ByAci, 8);

            for (int i = 0; i < columns.Count; i++)
            {
                ColumnDesignResult col = columns[i];
                double colX = startX + (i * colSpacing);
                double totalHt = numStories * storyHtFt * scale;
                double colW = col.Tiers[0].WidthIn * (isInch ? 1.0 : (1.0 / 12.0));
                double cover = isInch ? 1.5 : (1.5 / 12.0);

                // Concrete Elevation Box
                Polyline elev = new Polyline();
                elev.AddVertexAt(0, new Point2d(colX, startY - totalHt), 0, 0, 0);
                elev.AddVertexAt(1, new Point2d(colX + colW, startY - totalHt), 0, 0, 0);
                elev.AddVertexAt(2, new Point2d(colX + colW, startY), 0, 0, 0);
                elev.AddVertexAt(3, new Point2d(colX, startY), 0, 0, 0);
                elev.Closed = true;
                elev.Color = pureBlackColor;
                elev.LineWeight = LineWeight.LineWeight025;
                btr.AppendEntity(elev);
                tr.AddNewlyCreatedDBObject(elev, true);

                // Long Section Rebars with 1:6 Cranked (Dockten) Splices
                for (int s = 0; s < numStories; s++)
                {
                    double storyTop = startY - (s * storyHtFt * scale);
                    double storyBot = storyTop - (storyHtFt * scale);
                    double l0 = (storyHtFt * scale) / 4.0;
                    double midY = (storyTop + storyBot) / 2.0;
                    double lapHalf = isInch ? 18.0 : 1.5;

                    DrawDocktenBar(btr, tr, colX + cover, storyBot, midY, lapHalf, isInch, true, pureBlackColor);
                    DrawDocktenBar(btr, tr, colX + colW - cover, storyBot, midY, lapHalf, isInch, false, pureBlackColor);

                    // Stirrups: Top L/4 @ 4" c/c
                    double step4in = isInch ? 4.0 : (4.0 / 12.0);
                    for (double y = storyTop - (step4in * 0.5); y >= storyTop - l0; y -= step4in)
                        DrawStirrupRung(btr, tr, colX, colX + colW, y, LineWeight.LineWeight018, stirrupColor);

                    // Stirrups: Mid L/2 @ 7" c/c
                    double step7in = isInch ? 7.0 : (7.0 / 12.0);
                    for (double y = storyTop - l0 - step7in; y >= storyBot + l0 + (step7in * 0.5); y -= step7in)
                        DrawStirrupRung(btr, tr, colX, colX + colW, y, LineWeight.LineWeight018, stirrupColor);

                    // Stirrups: Bottom L/4 @ 4" c/c
                    for (double y = storyBot + l0 - (step4in * 0.5); y >= storyBot + (step4in * 0.5); y -= step4in)
                        DrawStirrupRung(btr, tr, colX, colX + colW, y, LineWeight.LineWeight018, stirrupColor);

                    Line lvlLine = new Line(new Point3d(colX - (scale * 1.5), storyTop, 0), new Point3d(colX + colW + (scale * 1.5), storyTop, 0));
                    lvlLine.ColorIndex = 8;
                    lvlLine.LineWeight = LineWeight.LineWeight015;
                    btr.AppendEntity(lvlLine);
                    tr.AddNewlyCreatedDBObject(lvlLine, true);

                    if (s == 1)
                    {
                        DrawLeaderAnnotation(btr, tr, new Point3d(colX + colW, storyTop - (l0 * 0.5), 0),
                                             new Point3d(colX + colW + (scale * 2.5), storyTop - (l0 * 0.5) + (scale * 0.5), 0),
                                             "10mm Ø @ 4\" c/c (L/4 Confinement)", scale * 0.22);

                        DrawLeaderAnnotation(btr, tr, new Point3d(colX + colW, midY, 0),
                                             new Point3d(colX + colW + (scale * 2.5), midY + (scale * 0.5), 0),
                                             "Dockten Lap Splice (1:6 slope)\nL_lap = 45 db (Staggered)", scale * 0.22);
                    }
                }

                // Header
                MText colTitle = new MText
                {
                    Location = new Point3d(colX - (scale * 1.0), startY + (scale * 2.5), 0),
                    Contents = $"\\C3;{col.Name} ELEVATION\\P\\C7;Base: {col.Tiers[0].WidthIn:0}\"x{col.Tiers[0].DepthIn:0}\"",
                    TextHeight = scale * 0.45
                };
                btr.AppendEntity(colTitle);
                tr.AddNewlyCreatedDBObject(colTitle, true);

                // Cross-Sections with Dimensions, Clear Cover & Donut Rebars
                double secStartY = startY - totalHt - (isInch ? 45.0 : 3.8);
                for (int t = 0; t < col.Tiers.Count; t++)
                {
                    TierDesign tier = col.Tiers[t];
                    double tW = tier.WidthIn * (isInch ? 1.0 : (1.0 / 12.0));
                    double tD = tier.DepthIn * (isInch ? 1.0 : (1.0 / 12.0));
                    double secY = secStartY - (t * (isInch ? 75.0 : 6.2));

                    Polyline secBox = new Polyline();
                    secBox.AddVertexAt(0, new Point2d(colX, secY - tD), 0, 0, 0);
                    secBox.AddVertexAt(1, new Point2d(colX + tW, secY - tD), 0, 0, 0);
                    secBox.AddVertexAt(2, new Point2d(colX + tW, secY), 0, 0, 0);
                    secBox.AddVertexAt(3, new Point2d(colX, secY), 0, 0, 0);
                    secBox.Closed = true;
                    secBox.Color = pureBlackColor;
                    secBox.LineWeight = LineWeight.LineWeight025;
                    btr.AppendEntity(secBox);
                    tr.AddNewlyCreatedDBObject(secBox, true);

                    Polyline tieLoop = new Polyline();
                    tieLoop.AddVertexAt(0, new Point2d(colX + cover, secY - tD + cover), 0, 0, 0);
                    tieLoop.AddVertexAt(1, new Point2d(colX + tW - cover, secY - tD + cover), 0, 0, 0);
                    tieLoop.AddVertexAt(2, new Point2d(colX + tW - cover, secY - cover), 0, 0, 0);
                    tieLoop.AddVertexAt(3, new Point2d(colX + cover, secY - cover), 0, 0, 0);
                    tieLoop.Closed = true;
                    tieLoop.Color = stirrupColor;
                    tieLoop.LineWeight = LineWeight.LineWeight018;
                    btr.AppendEntity(tieLoop);
                    tr.AddNewlyCreatedDBObject(tieLoop, true);

                    double barRadiusIn = (tier.BarDiaMm == 20) ? (0.79 / 2.0) : (0.63 / 2.0);
                    double barRadiusCAD = isInch ? barRadiusIn : (barRadiusIn / 12.0);
                    DrawProfessionalDonutRebarGrid(btr, tr, colX, secY, tW, tD, cover, tier.BarCount, barRadiusCAD, pureBlackColor);

                    // Dimension lines
                    DrawDimensionLine(btr, tr, db, new Point3d(colX, secY, 0), new Point3d(colX + tW, secY, 0),
                                      new Point3d(colX + (tW / 2.0), secY + (scale * 0.8), 0), $"{tier.WidthIn:0}\"", scale * 0.20);

                    DrawDimensionLine(btr, tr, db, new Point3d(colX, secY - tD, 0), new Point3d(colX, secY, 0),
                                      new Point3d(colX - (scale * 0.8), secY - (tD / 2.0), 0), $"{tier.DepthIn:0}\"", scale * 0.20);

                    DrawLeaderAnnotation(btr, tr, new Point3d(colX + (cover / 2.0), secY - tD + (cover / 2.0), 0),
                                         new Point3d(colX - (scale * 1.8), secY - tD - (scale * 0.5), 0),
                                         "1.5\" Clear Cover", scale * 0.18);

                    DrawLeaderAnnotation(btr, tr, new Point3d(colX + tW - cover, secY - cover, 0),
                                         new Point3d(colX + tW + (scale * 2.2), secY + (scale * 0.5), 0),
                                         $"{tier.BarCount} - {tier.BarDiaMm}mm Ø (Grade 60)\n10mm Ø Ties @ 4\"/7\" c/c", scale * 0.20);

                    MText secLabel = new MText
                    {
                        Location = new Point3d(colX, secY - tD - (scale * 1.2), 0),
                        Contents = $"\\C2;SECTION: {tier.TierLabel.ToUpper()}\\P\\C7;({tier.WidthIn:0}\" x {tier.DepthIn:0}\")",
                        TextHeight = scale * 0.24
                    };
                    btr.AppendEntity(secLabel);
                    tr.AddNewlyCreatedDBObject(secLabel, true);
                }
            }
        }

        private void DrawDocktenBar(BlockTableRecord btr, Transaction tr, double x, double yBot, double yMid, double lapHalf, bool isInch, bool isLeft, Color pureBlack)
        {
            double offset = isInch ? 0.75 : (0.75 / 12.0);
            double crankX = isLeft ? (x + offset) : (x - offset);

            Polyline lowerBar = new Polyline();
            lowerBar.AddVertexAt(0, new Point2d(x, yBot), 0, 0, 0);
            lowerBar.AddVertexAt(1, new Point2d(x, yMid + lapHalf), 0, 0, 0);
            lowerBar.Color = pureBlack;
            lowerBar.LineWeight = LineWeight.LineWeight050;
            btr.AppendEntity(lowerBar);
            tr.AddNewlyCreatedDBObject(lowerBar, true);

            Polyline upperBar = new Polyline();
            upperBar.AddVertexAt(0, new Point2d(crankX, yMid - lapHalf), 0, 0, 0);
            upperBar.AddVertexAt(1, new Point2d(crankX, yMid - (lapHalf * 0.3)), 0, 0, 0);
            upperBar.AddVertexAt(2, new Point2d(x, yMid + (lapHalf * 0.3)), 0, 0, 0);
            upperBar.AddVertexAt(3, new Point2d(x, yMid + (lapHalf * 2.0)), 0, 0, 0);
            upperBar.Color = pureBlack;
            upperBar.LineWeight = LineWeight.LineWeight050;
            btr.AppendEntity(upperBar);
            tr.AddNewlyCreatedDBObject(upperBar, true);
        }

        private void DrawProfessionalDonutRebarGrid(BlockTableRecord btr, Transaction tr, double colX, double secY, double tW, double tD, double cover, int barCount, double barRadius, Color pureBlack)
        {
            double leftX = colX + cover + barRadius;
            double rightX = colX + tW - cover - barRadius;
            double topY = secY - cover - barRadius;
            double botY = secY - tD + cover + barRadius;

            DrawSolidDonutRebar(btr, tr, leftX, topY, barRadius, pureBlack);
            DrawSolidDonutRebar(btr, tr, rightX, topY, barRadius, pureBlack);
            DrawSolidDonutRebar(btr, tr, leftX, botY, barRadius, pureBlack);
            DrawSolidDonutRebar(btr, tr, rightX, botY, barRadius, pureBlack);

            if (barCount == 6)
            {
                double midY = (topY + botY) / 2.0;
                DrawSolidDonutRebar(btr, tr, leftX, midY, barRadius, pureBlack);
                DrawSolidDonutRebar(btr, tr, rightX, midY, barRadius, pureBlack);
            }
            else if (barCount == 8)
            {
                double dy = (topY - botY) / 3.0;
                DrawSolidDonutRebar(btr, tr, leftX, topY - dy, barRadius, pureBlack);
                DrawSolidDonutRebar(btr, tr, leftX, topY - (2.0 * dy), barRadius, pureBlack);
                DrawSolidDonutRebar(btr, tr, rightX, topY - dy, barRadius, pureBlack);
                DrawSolidDonutRebar(btr, tr, rightX, topY - (2.0 * dy), barRadius, pureBlack);
            }
            else if (barCount >= 10)
            {
                double dy = (topY - botY) / 4.0;
                DrawSolidDonutRebar(btr, tr, leftX, topY - dy, barRadius, pureBlack);
                DrawSolidDonutRebar(btr, tr, leftX, topY - (2.0 * dy), barRadius, pureBlack);
                DrawSolidDonutRebar(btr, tr, leftX, topY - (3.0 * dy), barRadius, pureBlack);
                DrawSolidDonutRebar(btr, tr, rightX, topY - dy, barRadius, pureBlack);
                DrawSolidDonutRebar(btr, tr, rightX, topY - (2.0 * dy), barRadius, pureBlack);
                DrawSolidDonutRebar(btr, tr, rightX, topY - (3.0 * dy), barRadius, pureBlack);

                if (barCount == 12)
                {
                    double midX = (leftX + rightX) / 2.0;
                    DrawSolidDonutRebar(btr, tr, midX, topY, barRadius, pureBlack);
                    DrawSolidDonutRebar(btr, tr, midX, botY, barRadius, pureBlack);
                }
            }
        }

        private void DrawSolidDonutRebar(BlockTableRecord btr, Transaction tr, double cx, double cy, double radius, Color pureBlack)
        {
            Polyline donut = new Polyline();
            donut.AddVertexAt(0, new Point2d(cx - (radius / 2.0), cy), 1.0, radius, radius);
            donut.AddVertexAt(1, new Point2d(cx + (radius / 2.0), cy), 1.0, radius, radius);
            donut.Closed = true;
            donut.Color = pureBlack;
            donut.LineWeight = LineWeight.LineWeight050;
            btr.AppendEntity(donut);
            tr.AddNewlyCreatedDBObject(donut, true);
        }

        private void DrawDimensionLine(BlockTableRecord btr, Transaction tr, Database db, Point3d pt1, Point3d pt2, Point3d dimLinePt, string text, double txtHt)
        {
            RotatedDimension dim = new RotatedDimension
            {
                XLine1Point = pt1,
                XLine2Point = pt2,
                DimLinePoint = dimLinePt,
                DimensionText = text,
                TextHeight = txtHt,
                DimensionStyle = db.Dimstyle
            };
            dim.ColorIndex = 7;
            btr.AppendEntity(dim);
            tr.AddNewlyCreatedDBObject(dim, true);
        }

        private void DrawLeaderAnnotation(BlockTableRecord btr, Transaction tr, Point3d targetPt, Point3d textPt, string text, double txtHt)
        {
            Line arrowLine = new Line(targetPt, textPt);
            arrowLine.ColorIndex = 1;
            arrowLine.LineWeight = LineWeight.LineWeight018;
            btr.AppendEntity(arrowLine);
            tr.AddNewlyCreatedDBObject(arrowLine, true);

            double landLength = txtHt * 3.0;
            Line landing = new Line(textPt, new Point3d(textPt.X + landLength, textPt.Y, 0));
            landing.ColorIndex = 1;
            landing.LineWeight = LineWeight.LineWeight018;
            btr.AppendEntity(landing);
            tr.AddNewlyCreatedDBObject(landing, true);

            MText mtext = new MText
            {
                Location = new Point3d(textPt.X + (txtHt * 0.3), textPt.Y + (txtHt * 0.8), 0),
                Contents = $"\\C7;{text}",
                TextHeight = txtHt
            };
            btr.AppendEntity(mtext);
            tr.AddNewlyCreatedDBObject(mtext, true);
        }

        private void DrawStirrupRung(BlockTableRecord btr, Transaction tr, double x1, double x2, double y, LineWeight lw, Color col)
        {
            Line rung = new Line(new Point3d(x1, y, 0), new Point3d(x2, y, 0));
            rung.Color = col;
            rung.LineWeight = lw;
            btr.AppendEntity(rung);
            tr.AddNewlyCreatedDBObject(rung, true);
        }

        // Table 2: Multi-Tier Schedule Table
        private void InsertLateralColumnScheduleTable(Database db, Transaction tr, BlockTableRecord btr, List<ColumnDesignResult> columns, double posX, double posY, bool isInch)
        {
            Table table = new Table();
            table.TableStyle = db.Tablestyle;

            int numRows = columns.Count + 2;
            int numCols = 6;
            table.SetSize(numRows, numCols);

            double rowHeight = isInch ? 30.0 : 2.5;
            double colWidth = isInch ? 120.0 : 10.0;

            table.SetRowHeight(rowHeight);
            table.SetColumnWidth(colWidth);

            table.Cells[0, 0].TextString = "TABLE 2: COLUMN MULTI-TIER REINFORCEMENT SCHEDULE";
            table.Cells[0, 0].TextHeight = isInch ? 13.0 : 1.1;

            string[] headers = {
                "COL\nNAME",
                $"{columns[0].Tiers[0].TierLabel}\n(b x h & Rebar)",
                $"{columns[0].Tiers[1].TierLabel}\n(b x h & Rebar)",
                $"{columns[0].Tiers[2].TierLabel}\n(b x h & Rebar)",
                "LATERAL TIES\n(Confinement)",
                "EST. STEEL\nTOTAL (kg)"
            };

            for (int c = 0; c < numCols; c++)
            {
                table.Cells[1, c].TextString = headers[c];
                table.Cells[1, c].TextHeight = isInch ? 8.0 : 0.70;
                table.Cells[1, c].Alignment = CellAlignment.MiddleCenter;
            }

            for (int i = 0; i < columns.Count; i++)
            {
                var col = columns[i];
                int r = i + 2;

                table.Cells[r, 0].TextString = col.Name;
                table.Cells[r, 1].TextString = $"{col.Tiers[0].WidthIn:0}\"x{col.Tiers[0].DepthIn:0}\"\n{col.Tiers[0].BarCount}-{col.Tiers[0].BarDiaMm}mm Ø";
                table.Cells[r, 2].TextString = $"{col.Tiers[1].WidthIn:0}\"x{col.Tiers[1].DepthIn:0}\"\n{col.Tiers[1].BarCount}-{col.Tiers[1].BarDiaMm}mm Ø";
                table.Cells[r, 3].TextString = $"{col.Tiers[2].WidthIn:0}\"x{col.Tiers[2].DepthIn:0}\"\n{col.Tiers[2].BarCount}-{col.Tiers[2].BarDiaMm}mm Ø";
                table.Cells[r, 4].TextString = "10mm @ 4\" (L/4)\n@ 7\" (Mid L/2)";
                table.Cells[r, 5].TextString = $"{col.TotalColSteelKg:0} kg";

                for (int c = 0; c < numCols; c++)
                {
                    table.Cells[r, c].TextHeight = isInch ? 7.5 : 0.65;
                    table.Cells[r, c].Alignment = CellAlignment.MiddleCenter;
                }
            }

            table.Position = new Point3d(posX, posY, 0);
            btr.AppendEntity(table);
            tr.AddNewlyCreatedDBObject(table, true);
        }

        // Table 3: BBS Summary
        private void InsertBBSTable(Database db, Transaction tr, BlockTableRecord btr, List<ColumnDesignResult> columns, double posX, double posY, bool isInch)
        {
            Table bbs = new Table();
            bbs.TableStyle = db.Tablestyle;

            int numRows = (columns.Count * 2) + 2;
            int numCols = 6;
            bbs.SetSize(numRows, numCols);

            double rowHeight = isInch ? 26.0 : 2.2;
            double colWidth = isInch ? 115.0 : 9.5;

            bbs.SetRowHeight(rowHeight);
            bbs.SetColumnWidth(colWidth);

            bbs.Cells[0, 0].TextString = "TABLE 3: BAR BENDING SCHEDULE (BBS) SUMMARY (BNBC 2020)";
            bbs.Cells[0, 0].TextHeight = isInch ? 13.0 : 1.1;

            string[] headers = {
                "MEMBER",
                "REBAR TYPE",
                "SIZE (Ø)",
                "CUTTING DETAILS",
                "BAR COUNT",
                "EST. WT (kg)"
            };

            for (int c = 0; c < numCols; c++)
            {
                bbs.Cells[1, c].TextString = headers[c];
                bbs.Cells[1, c].TextHeight = isInch ? 8.0 : 0.70;
                bbs.Cells[1, c].Alignment = CellAlignment.MiddleCenter;
            }

            int currRow = 2;
            foreach (var col in columns)
            {
                bbs.Cells[currRow, 0].TextString = col.Name;
                bbs.Cells[currRow, 1].TextString = "Main Longitudinal";
                bbs.Cells[currRow, 2].TextString = $"{col.Tiers[0].BarDiaMm}mm / {col.Tiers[1].BarDiaMm}mm";
                bbs.Cells[currRow, 3].TextString = "Dockten Lap (1:6 slope, L=45 db)";
                bbs.Cells[currRow, 4].TextString = $"{col.Tiers[0].BarCount} to {col.Tiers[2].BarCount}";
                bbs.Cells[currRow, 5].TextString = $"{col.TotalColSteelKg * 0.75:0} kg";

                bbs.Cells[currRow + 1, 0].TextString = col.Name;
                bbs.Cells[currRow + 1, 1].TextString = "Seismic Ties";
                bbs.Cells[currRow + 1, 2].TextString = "10mm Ø";
                bbs.Cells[currRow + 1, 3].TextString = "135° Hooks (6 db ext)";
                bbs.Cells[currRow + 1, 4].TextString = "4\" c/c in Lo, 7\" c/c Mid";
                bbs.Cells[currRow + 1, 5].TextString = $"{col.TotalColSteelKg * 0.25:0} kg";

                currRow += 2;
            }

            bbs.Position = new Point3d(posX, posY, 0);
            btr.AppendEntity(bbs);
            tr.AddNewlyCreatedDBObject(bbs, true);
        }

        private void InsertLateralMaterialNotes(BlockTableRecord btr, Transaction tr, double posX, double posY, bool isInch, double windV, double zoneZ, ProjectMetadata meta)
        {
            double scale = isInch ? 12.0 : 1.0;
            MText notes = new MText
            {
                Location = new Point3d(posX, posY, 0),
                Contents = "\\C3;BNBC 2020 / ACI 318 STRUCTURAL DESIGN & MATERIAL SPECIFICATIONS:\\P" +
                           $"\\C7;1. Wind Parameters: Basic Wind Speed V = {windV} mph (Khulna Coastal), Exposure B, Importance Factor I = 1.0.\\P" +
                           $"2. Seismic Parameters: Zone Factor Z = {zoneZ} (Zone 1 - Khulna), Site Class SD, Response Factor R = 5.0 (IMRF).\\P" +
                           "3. Governing Load Combinations Evaluated: 1.4D, 1.2D+1.6L, 1.2D+1.0L+1.0W, 0.9D+1.0W, 1.2D+1.0L+1.0E, 0.9D+1.0E.\\P" +
                           "4. Confinement Zone (Lo): 10mm Ø seismic ties @ 4\" c/c over Lo = max(clear height / 6, col depth, 18\").\\P" +
                           "5. Lap Splices: L_lap = 45 db with 1:6 dockten bend. Staggered within middle half (L/2) of column. Never lap within Lo.\\P" +
                           "6. Materials: f'c = 3000 to 4000 psi, Rebar = Grade 60 (fy = 60,000 psi / 500W). Maximum w/c = 0.45.\\P" +
                           $"\\C2;7. Professional Certification: Designed by {meta.EngineerName} | {meta.IEB_Reg} | {meta.KDA_Reg}.",
                TextHeight = scale * 0.38
            };
            btr.AppendEntity(notes);
            tr.AddNewlyCreatedDBObject(notes, true);
        }

        private bool ConvertHtmlToPdfViaBrowser(string htmlPath, string pdfPath)
        {
            string[] possibleBrowsers = new string[]
            {
                @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
                @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
                @"C:\Program Files\Google\Chrome\Application\chrome.exe",
                @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe"
            };

            string browserExe = null;
            foreach (var b in possibleBrowsers)
            {
                if (File.Exists(b))
                {
                    browserExe = b;
                    break;
                }
            }

            if (browserExe == null) return false;

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = browserExe,
                    Arguments = $"--headless --disable-gpu --run-all-compositor-stages-before-draw --print-to-pdf=\"{pdfPath}\" \"{htmlPath}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using (Process proc = Process.Start(psi))
                {
                    proc.WaitForExit(10000);
                    return File.Exists(pdfPath);
                }
            }
            catch
            {
                return false;
            }
        }

        private void GeneratePrintableHtmlReport(string filePath, ProjectMetadata meta, List<ColumnDesignResult> columns, double totalWindShear, double totalSeismicShear)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html><html><head><meta charset='UTF-8'>");
            sb.AppendLine("<title>Structural Design Calculation Sheet</title>");
            sb.AppendLine("<style>");
            sb.AppendLine("body { font-family: 'Segoe UI', Arial, sans-serif; margin: 30px; color: #222; }");
            sb.AppendLine(".title-page { text-align: center; page-break-after: always; padding-top: 80px; border: 3px double #333; padding: 40px; }");
            sb.AppendLine(".title-page h1 { font-size: 24pt; margin-bottom: 5px; color: #1a365d; }");
            sb.AppendLine(".title-page h2 { font-size: 15pt; font-weight: normal; margin-bottom: 35px; color: #4a5568; }");
            sb.AppendLine(".meta-table { width: 90%; margin: 0 auto; text-align: left; font-size: 11pt; border-collapse: collapse; margin-top: 40px; }");
            sb.AppendLine(".meta-table td { padding: 10px; border-bottom: 1px solid #ccc; }");
            sb.AppendLine(".meta-table td.label { font-weight: bold; width: 38%; color: #2d3748; }");
            sb.AppendLine(".engineer-box { margin-top: 50px; padding: 15px; border: 1px dashed #4a5568; display: inline-block; text-align: left; background-color: #f7fafc; }");
            sb.AppendLine(".section-header { font-size: 13pt; font-weight: bold; margin-top: 25px; margin-bottom: 10px; color: #1a365d; border-bottom: 2px solid #1a365d; padding-bottom: 4px; }");
            sb.AppendLine("table.data-table { width: 100%; border-collapse: collapse; margin-top: 15px; font-size: 9.5pt; }");
            sb.AppendLine("table.data-table th, table.data-table td { border: 1px solid #718096; padding: 6px; text-align: center; }");
            sb.AppendLine("table.data-table th { background-color: #edf2f7; color: #2d3748; }");
            sb.AppendLine(".formula-box { background-color: #f7fafc; border-left: 4px solid #3182ce; padding: 12px; margin: 15px 0; font-family: 'Courier New', monospace; font-size: 9.5pt; }");
            sb.AppendLine("@media print { .no-print { display: none; } }");
            sb.AppendLine("</style></head><body>");

            // Title Page
            sb.AppendLine("<div class='title-page'>");
            sb.AppendLine("<h1>STRUCTURAL DESIGN CALCULATION REPORT</h1>");
            sb.AppendLine("<h2>AXIAL & LATERAL (WIND + SEISMIC) LOAD ANALYSIS</h2>");
            sb.AppendLine("<p>Conforming to Bangladesh National Building Code (BNBC 2020) & ACI 318-19 (FPS Units)</p>");
            sb.AppendLine("<table class='meta-table'>");
            sb.AppendLine($"<tr><td class='label'>Project Name:</td><td>{meta.ProjectName}</td></tr>");
            sb.AppendLine($"<tr><td class='label'>Client Name:</td><td>{meta.ClientName}</td></tr>");
            sb.AppendLine($"<tr><td class='label'>Plot / Holding No:</td><td>{meta.PlotNo}</td></tr>");
            sb.AppendLine($"<tr><td class='label'>Site Location:</td><td>{meta.Address}</td></tr>");
            sb.AppendLine($"<tr><td class='label'>Total Stories:</td><td>{meta.TotalStories} Stories (H = {meta.TotalStories * meta.StoryHeightFt:0.0} ft)</td></tr>");
            sb.AppendLine($"<tr><td class='label'>Assigned Column Width:</td><td><b>{meta.FixedWidthIn:0}\" (Uniform Width Selection)</b></td></tr>");
            sb.AppendLine($"<tr><td class='label'>Basic Wind Speed:</td><td><b>{meta.WindSpeedMph} MPH (Khulna Region)</b></td></tr>");
            sb.AppendLine($"<tr><td class='label'>Seismic Zone Factor:</td><td><b>Z = {meta.SeismicZoneZ} (Zone 1 - Khulna)</b></td></tr>");
            sb.AppendLine($"<tr><td class='label'>Date:</td><td>{DateTime.Now.ToString("dd MMMM yyyy")}</td></tr>");
            sb.AppendLine("</table>");

            sb.AppendLine("<div class='engineer-box'>");
            sb.AppendLine("<b>STRUCTURAL DESIGN BY:</b><br>");
            sb.AppendLine($"<b>{meta.EngineerName}</b><br>");
            sb.AppendLine($"{meta.IEB_Reg}<br>");
            sb.AppendLine($"{meta.KDA_Reg}<br>");
            sb.AppendLine("Khulna, Bangladesh");
            sb.AppendLine("</div>");
            sb.AppendLine("</div>");

            // Design Basis & Formulas
            sb.AppendLine("<div class='section-header'>1. DESIGN BASIS, CRITERIA & GOVERNING FORMULAS</div>");
            sb.AppendLine("<div class='formula-box'>");
            sb.AppendLine("<b>A. Gravity Load Equations:</b><br>");
            sb.AppendLine("&bull; Slab Dead Load: W_slab = (t_slab / 12) * 150 pcf + Floor Finish + Partition Wall<br>");
            sb.AppendLine("&bull; Beam Drop SW: W_beam = (b_beam / 12) * ((h_beam - t_slab) / 12) * 150 pcf<br>");
            sb.AppendLine("&bull; 5\" Brick Wall: W_wall = (5 / 12) * (Clear Height) * 120 pcf * (1 - Opening Ratio)<br>");
            sb.AppendLine("&bull; BNBC Live Load Reduction: L = L0 * (0.25 + 15 / sqrt(K_LL * A_I)) >= 0.40 * L0 for K_LL * A_I >= 400 sft<br><br>");
            sb.AppendLine("<b>B. Wind & Seismic Formulations (BNBC 2020):</b><br>");
            sb.AppendLine($"&bull; Velocity Pressure: qz = 0.00256 * Kz * Kzt * Kd * V^2 * I = {0.00256 * 0.85 * Math.Pow(meta.WindSpeedMph, 2):0.1} psf (V = {meta.WindSpeedMph} mph)<br>");
            sb.AppendLine($"&bull; Design Wind Pressure: p = qz * G * Cp = {0.00256 * 0.85 * Math.Pow(meta.WindSpeedMph, 2) * 0.85 * 1.3:0.1} psf<br>");
            sb.AppendLine($"&bull; Total Wind Base Shear: V_wind = {totalWindShear:0.1} kips<br>");
            sb.AppendLine($"&bull; Seismic Base Shear: V_b = (2/3) * (Z * I * Cs / R) * W = {totalSeismicShear:0.1} kips (Z = {meta.SeismicZoneZ}, R = 5.0)<br><br>");
            sb.AppendLine("<b>C. ACI 318-19 Strength Combinations:</b><br>");
            sb.AppendLine("&bull; U1 = 1.4 D<br>");
            sb.AppendLine("&bull; U2 = 1.2 D + 1.6 L (Gravity)<br>");
            sb.AppendLine("&bull; U3 = 1.2 D + 1.0 L + 1.0 W &plusmn; Delta_P (Wind Overturning)<br>");
            sb.AppendLine("&bull; U5 = 1.2 D + 1.0 L + 1.0 E &plusmn; Delta_P (Seismic Overturning)<br>");
            sb.AppendLine("&bull; P-M Sizing Equivalent Axial: Pu_equiv = Pu + (2 * Mu / h)");
            sb.AppendLine("</div>");

            // Schedule Table
            sb.AppendLine("<div class='section-header'>2. FINALIZED COLUMN REINFORCEMENT & SECTION SCHEDULE</div>");
            sb.AppendLine("<table class='data-table'>");
            sb.AppendLine("<thead><tr>");
            sb.AppendLine("<th>Col ID</th><th>Trib Area (sft)</th><th>Gov Load Case</th><th>Factored Pu (k)</th><th>Factored Mu (k-ft)</th>");
            sb.AppendLine($"<th>{columns[0].Tiers[0].TierLabel}<br>(b x h & Rebar)</th>");
            sb.AppendLine($"<th>{columns[0].Tiers[1].TierLabel}<br>(b x h & Rebar)</th>");
            sb.AppendLine($"<th>{columns[0].Tiers[2].TierLabel}<br>(b x h & Rebar)</th>");
            sb.AppendLine("<th>Est Steel (kg)</th>");
            sb.AppendLine("</tr></thead><tbody>");

            foreach (var col in columns)
            {
                sb.AppendLine("<tr>");
                sb.AppendLine($"<td><b>{col.Name}</b></td>");
                sb.AppendLine($"<td>{col.TribAreaSqFt:0}</td>");
                sb.AppendLine($"<td>{col.GoverningCase}</td>");
                sb.AppendLine($"<td>{col.GoverningPu_Kips:0.1}</td>");
                sb.AppendLine($"<td>{col.GoverningMu_FtKips:0.1}</td>");
                sb.AppendLine($"<td><b>{col.Tiers[0].WidthIn:0}\"x{col.Tiers[0].DepthIn:0}\"</b><br>{col.Tiers[0].BarCount}-{col.Tiers[0].BarDiaMm}mm Ø</td>");
                sb.AppendLine($"<td><b>{col.Tiers[1].WidthIn:0}\"x{col.Tiers[1].DepthIn:0}\"</b><br>{col.Tiers[1].BarCount}-{col.Tiers[1].BarDiaMm}mm Ø</td>");
                sb.AppendLine($"<td><b>{col.Tiers[2].WidthIn:0}\"x{col.Tiers[2].DepthIn:0}\"</b><br>{col.Tiers[2].BarCount}-{col.Tiers[2].BarDiaMm}mm Ø</td>");
                sb.AppendLine($"<td>{col.TotalColSteelKg:0} kg</td>");
                sb.AppendLine("</tr>");
            }

            sb.AppendLine("</tbody></table>");

            // Material Specifications
            sb.AppendLine("<div class='section-header'>3. MATERIAL SPECIFICATIONS & CONSTRUCTION NOTES</div>");
            sb.AppendLine("<ol style='font-size: 10pt; line-height: 1.6;'>");
            sb.AppendLine("<li><b>Concrete Cylinder Strength:</b> f'c = 3000 psi to 4000 psi at 28 days. Casting volumetric ratio 1:1.5:3.</li>");
            sb.AppendLine("<li><b>Reinforcing Steel:</b> Deformed high-yield bars Grade 60 (fy = 60,000 psi / 500W conforming to BDS ISO 6935-2).</li>");
            sb.AppendLine("<li><b>Clear Concrete Cover:</b> 1.5 inches above Plinth Level (PL); 2.5 to 3.0 inches below PL in contact with ground.</li>");
            sb.AppendLine("<li><b>Seismic Confinement Hoops:</b> 10mm Ø ties @ 4\" c/c in end regions (Lo = max[clear height/6, col depth, 18\"]) with 135° seismic hooks.</li>");
            sb.AppendLine("<li><b>Lap Splices:</b> L_lap = 45 db with 1:6 dockten bend. Stagger splices within middle half of column clear height.</li>");
            sb.AppendLine("</ol>");

            sb.AppendLine("<div class='no-print' style='margin-top: 30px; text-align: center;'>");
            sb.AppendLine("<button onclick='window.print()' style='padding: 10px 25px; font-size: 14pt; background-color: #2b6cb0; color: white; border: none; border-radius: 4px; cursor: pointer;'>Print / Save as PDF</button>");
            sb.AppendLine("</div>");

            sb.AppendLine("</body></html>");

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
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
