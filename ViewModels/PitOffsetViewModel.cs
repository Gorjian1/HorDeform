// File: ViewModels/PitOffsetViewModel.cs
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Osadka.ViewModels
{
    public enum ArrowDisplayMode { AllCycles, LastCycle, SelectedCycles }
    public enum BuildTool { Point, Rect }

    public sealed partial class PitOffsetViewModel : ObservableObject
    {
        public RawDataViewModel Raw { get; }

        [ObservableProperty] private int? selectedObject;
        public ObservableCollection<int> Objects { get; } = new();
        public ObservableCollection<CycleToggle> Cycles { get; } = new();

        public VectorDisplaySettings VectorSettings { get; } = new();

        [ObservableProperty] private ArrowDisplayMode displayMode = ArrowDisplayMode.SelectedCycles;

        [ObservableProperty] private double scale = 1.0;
        [ObservableProperty] private double offsetX = 0.0;
        [ObservableProperty] private double offsetY = 0.0;
        [ObservableProperty] private double rotationDeg = 0.0;

        [ObservableProperty] private string? backgroundPath;
        [ObservableProperty] private BitmapImage? backgroundImage;

        public ObservableCollection<PitPointRow> Rows { get; } = new();
        public ICollectionView RowsView { get; }

        public ObservableCollection<CycleFilterOption> CycleFilterOptions { get; } = new();
        [ObservableProperty] private CycleFilterOption? selectedCycleFilter;

        [ObservableProperty] private bool isBuildMode;
        public ObservableCollection<AnchorPair> Anchors { get; } = new();
        [ObservableProperty] private double buildRms;
        public string BuildRmsInfo => Anchors.Count(a => a.HasWorld) >= 2 ? $"RMS: {BuildRms:F2}px ({Anchors.Count(a => a.HasWorld)} опорн.)" : "Выберите ≥ 2 опорных";

        [ObservableProperty] private BuildTool selectedBuildTool = BuildTool.Point;
        public bool IsToolPoint { get => SelectedBuildTool == BuildTool.Point; set { if (value) SelectedBuildTool = BuildTool.Point; } }
        public bool IsToolRect { get => SelectedBuildTool == BuildTool.Rect; set { if (value) SelectedBuildTool = BuildTool.Rect; } }
        partial void OnSelectedBuildToolChanged(BuildTool value) { OnPropertyChanged(nameof(IsToolPoint)); OnPropertyChanged(nameof(IsToolRect)); }

        private double savedScale, savedOffsetX, savedOffsetY, savedRot;

        // Геометрия контуров
        public ObservableCollection<CycleOverlayVm> CycleOverlays { get; } = new();

        // для взвешивания стрелок
        [ObservableProperty] private double maxVVisible;

        private readonly Dictionary<int, List<PitPointRow>> _rowsByCycle = new();
        private bool _rowCacheDirty = true;
        private bool _suspendGeometryUpdate;

        // Команды
        public IRelayCommand LoadBackgroundCommand { get; }
        public IRelayCommand ClearBackgroundCommand { get; }
        public IRelayCommand ExportPngCommand { get; }
        public IRelayCommand RefreshCommand { get; }

        public IRelayCommand BuildCommand { get; }
        public IRelayCommand AcceptBuildCommand { get; }
        public IRelayCommand CancelBuildCommand { get; }
        public IRelayCommand ClearAnchorsCommand { get; }
        public IRelayCommand RotateLeft90Command { get; }
        public IRelayCommand RotateRight90Command { get; }
        public IRelayCommand ResetTransformCommand { get; }
        public IRelayCommand OpenVectorSettingsCommand { get; }

        public PitOffsetViewModel(RawDataViewModel raw)
        {
            Raw = raw;

            RowsView = CollectionViewSource.GetDefaultView(Rows);
            RowsView.Filter = FilterRowForGrid;

            TryFillObjectsFromRaw();

            LoadBackgroundCommand = new RelayCommand(LoadBackground);
            ClearBackgroundCommand = new RelayCommand(() => { BackgroundImage = null; BackgroundPath = null; });
            ExportPngCommand = new RelayCommand(() => OnExportRequested?.Invoke(this, EventArgs.Empty));
            RefreshCommand = new RelayCommand(() => { _rowCacheDirty = true; RebuildRows(); RebuildCycleOverlays(); });

            BuildCommand = new RelayCommand(EnterBuildMode);
            AcceptBuildCommand = new RelayCommand(AcceptBuild, () => Anchors.Count(a => a.HasWorld) >= 2);
            CancelBuildCommand = new RelayCommand(CancelBuild);
            ClearAnchorsCommand = new RelayCommand(() => { Anchors.Clear(); BuildRms = 0; OnPropertyChanged(nameof(BuildRmsInfo)); RecomputeBuild(); });
            RotateLeft90Command = new RelayCommand(() => RotationDeg -= 90);
            RotateRight90Command = new RelayCommand(() => RotationDeg += 90);
            ResetTransformCommand = new RelayCommand(() => { Scale = 1; OffsetX = 0; OffsetY = 0; RotationDeg = 0; });
            OpenVectorSettingsCommand = new RelayCommand(() => OnOpenVectorSettingsRequested?.Invoke(this, EventArgs.Empty));

            // версионирование настроек — чтобы MultiBinding реагировал
            VectorSettings.PropertyChanged += OnVectorSettingsPropertyChanged;
            VectorSettings.CycleStyles.CollectionChanged += OnCycleStylesChanged;
            foreach (var style in VectorSettings.CycleStyles)
                AttachCycleStyle(style);

            if (Objects.Count > 0) SelectedObject = Objects[0];
            RebuildCycles();
            RebuildRows();
            RebuildCycleFilterOptions();
            RebuildCycleOverlays();

            if (Raw is INotifyPropertyChanged inpc)
                inpc.PropertyChanged += (_, __) =>
                {
                    _rowCacheDirty = true;
                    RebuildCycles();
                    RebuildRows();
                    RebuildCycleFilterOptions();
                    RebuildCycleOverlays();
                };
        }

        public event EventHandler? OnExportRequested;
        public event EventHandler? OnOpenVectorSettingsRequested;

        // ==== Реакции ====
        partial void OnScaleChanged(double value)
        {
            if (_suspendGeometryUpdate) return;
            UpdateRowsProjection();
            RebuildCycleOverlays(false);
        }
        partial void OnOffsetXChanged(double value)
        {
            if (_suspendGeometryUpdate) return;
            UpdateRowsProjection();
            RebuildCycleOverlays(false);
        }
        partial void OnOffsetYChanged(double value)
        {
            if (_suspendGeometryUpdate) return;
            UpdateRowsProjection();
            RebuildCycleOverlays(false);
        }
        partial void OnRotationDegChanged(double value)
        {
            if (_suspendGeometryUpdate) return;
            UpdateRowsProjection();
            RebuildCycleOverlays(false);
        }
        partial void OnSelectedObjectChanged(int? value)
        {
            _rowCacheDirty = true;
            RebuildCycles();
            RebuildRows();
            RebuildCycleFilterOptions();
            RebuildCycleOverlays();
        }
        partial void OnSelectedCycleFilterChanged(CycleFilterOption? value) => RowsView.Refresh();
        partial void OnDisplayModeChanged(ArrowDisplayMode value)
        {
            RebuildRows();
            RowsView.Refresh();
            RebuildCycleOverlays();
        }

        private void SubscribeCycleToggle(CycleToggle ct)
        {
            ct.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(CycleToggle.IsSelected))
                {
                    OnPropertyChanged(nameof(SelectedCyclesSummary));
                    RebuildRows();
                    RowsView.Refresh();
                    RebuildCycleOverlays();
                }
            };
        }

        public string SelectedCyclesSummary
        {
            get
            {
                if (Cycles.Count == 0) return "Циклы: —";
                bool all = Cycles.All(c => c.IsSelected);
                if (all) return $"Циклы: все ({Cycles.Count})";
                var list = Cycles.Where(c => c.IsSelected).Select(c => c.Id).OrderBy(i => i).ToArray();
                return list.Length == 0 ? "Циклы: нет" : $"Циклы: {string.Join(", ", list)}";
            }
        }

        private void TryFillObjectsFromRaw()
        {
            Objects.Clear();
            var fld = Raw.GetType().GetField("_objects", BindingFlags.Instance | BindingFlags.NonPublic);
            var dict = fld?.GetValue(Raw) as System.Collections.IDictionary;
            if (dict != null)
                foreach (var key in dict.Keys)
                    if (key is int i) Objects.Add(i);
        }

        private void RebuildCycles()
        {
            _rowCacheDirty = true;
            Cycles.Clear();
            var fld = Raw.GetType().GetField("_objects", BindingFlags.Instance | BindingFlags.NonPublic);
            var dict = fld?.GetValue(Raw) as System.Collections.IDictionary;
            if (dict == null || SelectedObject == null || !dict.Contains(SelectedObject)) return;

            var cycles = dict[SelectedObject] as System.Collections.IDictionary;
            if (cycles == null) return;

            foreach (var k in cycles.Keys)
                if (k is int c)
                {
                    var item = new CycleToggle { Id = c, IsSelected = true };
                    SubscribeCycleToggle(item);
                    Cycles.Add(item);

                    if (!VectorSettings.CycleStyles.Any(s => s.CycleId == c))
                        VectorSettings.CycleStyles.Add(new CycleStyle { CycleId = c, Color = Colors.Black });
                }

            OnPropertyChanged(nameof(SelectedCyclesSummary));
        }

        private bool FilterRowForGrid(object obj)
        {
            if (obj is not PitPointRow r) return false;
            var opt = SelectedCycleFilter;
            if (opt == null || opt.Id == null) return true;
            return r.CycleId == opt.Id.Value;
        }

        private static double? ReadDouble(object src, params string[] names)
        {
            foreach (var n in names)
            {
                var p = src.GetType().GetProperty(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
                if (p == null) continue;
                var val = p.GetValue(src);
                if (val is double d) return d;
                if (val is float f) return f;
                if (val is decimal m) return (double)m;
                if (val is int i) return i;
                if (val is long l) return l;
                if (val is string s && double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var dv)) return dv;
            }
            return null;
        }

        private static int? ReadInt(object src, params string[] names)
        {
            foreach (var n in names)
            {
                var p = src.GetType().GetProperty(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
                if (p == null) continue;
                var val = p.GetValue(src);
                if (val is int i) return i;
                if (val is long l) return (int)l;
                if (val is string s && int.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var iv)) return iv;
            }
            return null;
        }

        public IReadOnlyList<PitPointRow> GetRowsForCycle(int cycleId)
        {
            EnsureRowCache();
            return _rowsByCycle.TryGetValue(cycleId, out var list) ? list : Array.Empty<PitPointRow>();
        }

        private void EnsureRowCache()
        {
            if (!_rowCacheDirty) return;
            _rowsByCycle.Clear();

            if (SelectedObject == null)
            {
                _rowCacheDirty = false;
                return;
            }

            var fld = Raw.GetType().GetField("_objects", BindingFlags.Instance | BindingFlags.NonPublic);
            var dict = fld?.GetValue(Raw) as System.Collections.IDictionary;
            if (dict == null || !dict.Contains(SelectedObject))
            {
                _rowCacheDirty = false;
                return;
            }

            var cycles = dict[SelectedObject] as System.Collections.IDictionary;
            if (cycles != null)
            {
                foreach (var key in cycles.Keys)
                {
                    if (key is not int cycleId) continue;
                    var list = new List<PitPointRow>();
                    var src = cycles[cycleId] as System.Collections.IEnumerable;
                    foreach (var row in src ?? Array.Empty<object>())
                    {
                        var n = ReadInt(row, "Id", "N", "Number", "Point", "№");
                        var x = ReadDouble(row, "X", "XCoord");
                        var y = ReadDouble(row, "Y", "YCoord");
                        var dx = ReadDouble(row, "Dx", "dX", "DeltaX");
                        var dy = ReadDouble(row, "Dy", "dY", "DeltaY");

                        list.Add(new PitPointRow
                        {
                            N = n,
                            X = x,
                            Y = y,
                            Dx = dx,
                            Dy = dy,
                            CycleId = cycleId
                        });
                    }
                    _rowsByCycle[cycleId] = list;
                }
            }

            _rowCacheDirty = false;
        }

        private void OnVectorSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(VectorDisplaySettings.Version)) return;
            VectorSettings.Version++;
            UpdateRowsProjection();
        }

        private void OnCycleStylesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
                foreach (CycleStyle cs in e.OldItems)
                    DetachCycleStyle(cs);
            if (e.NewItems != null)
                foreach (CycleStyle cs in e.NewItems)
                    AttachCycleStyle(cs);
            VectorSettings.Version++;
        }

        private void AttachCycleStyle(CycleStyle style)
        {
            style.PropertyChanged += OnCycleStylePropertyChanged;
        }

        private void DetachCycleStyle(CycleStyle style)
        {
            style.PropertyChanged -= OnCycleStylePropertyChanged;
        }

        private void OnCycleStylePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            VectorSettings.Version++;
        }

        private void RebuildRows()
        {
            EnsureRowCache();
            Rows.Clear();
            if (SelectedObject == null) return;

            int lastId = Cycles.Count > 0 ? Cycles.Max(c => c.Id) : 0;

            if (DisplayMode == ArrowDisplayMode.LastCycle && lastId > 0)
            {
                foreach (var r in GetRowsForCycle(lastId)) Rows.Add(r);
            }
            else
            {
                foreach (var c in Cycles.OrderBy(i => i.Id))
                {
                    if (DisplayMode == ArrowDisplayMode.AllCycles || c.IsSelected)
                        foreach (var r in GetRowsForCycle(c.Id))
                            Rows.Add(r);
                }
            }

            MaxVVisible = Rows.Count > 0 ? Rows.Max(r => r.V ?? 0.0) : 0.0;
            RowsView.Refresh();
            UpdateRowsProjection();
        }

        private void RebuildCycleFilterOptions()
        {
            var prev = SelectedCycleFilter?.Id;
            CycleFilterOptions.Clear();
            CycleFilterOptions.Add(new CycleFilterOption { Id = null, Title = "Все циклы" });
            foreach (var c in Cycles.OrderBy(i => i.Id))
                CycleFilterOptions.Add(new CycleFilterOption { Id = c.Id, Title = $"Цикл {c.Id}" });
            SelectedCycleFilter = CycleFilterOptions.FirstOrDefault(o => o.Id == prev) ?? CycleFilterOptions.FirstOrDefault();
        }

        public void EnterBuildMode()
        {
            if (IsBuildMode) return;
            savedScale = Scale; savedOffsetX = OffsetX; savedOffsetY = OffsetY; savedRot = RotationDeg;
            Anchors.Clear();
            BuildRms = 0;
            SelectedBuildTool = BuildTool.Point;
            IsBuildMode = true;
            OnPropertyChanged(nameof(BuildRmsInfo));
        }

        public void AddAnchor(double imageU, double imageV, double worldX, double worldY)
        {
            Anchors.Add(new AnchorPair { ImageU = imageU, ImageV = imageV, WorldX = worldX, WorldY = worldY });
            RecomputeBuild();
        }

        public int AddAnchorsByProjectionInRect(Rect rect)
        {
            int added = 0;
            foreach (var r in Rows)
            {
                if (!r.X.HasValue || !r.Y.HasValue) continue;
                var (u, v) = ProjectWorldToImage(r.X.Value, r.Y.Value);
                if (rect.Contains(u, v))
                {
                    bool exists = Anchors.Any(a => Math.Abs(a.WorldX - r.X.Value) < 1e-6 && Math.Abs(a.WorldY - r.Y.Value) < 1e-6);
                    if (!exists)
                    {
                        Anchors.Add(new AnchorPair { ImageU = u, ImageV = v, WorldX = r.X.Value, WorldY = r.Y.Value });
                        added++;
                    }
                }
            }
            if (added > 0) RecomputeBuild();
            return added;
        }

        public void AcceptBuild()
        {
            if (!IsBuildMode) return;
            IsBuildMode = false;
            OnPropertyChanged(nameof(BuildRmsInfo));
        }

        public void CancelBuild()
        {
            if (!IsBuildMode) return;
            _suspendGeometryUpdate = true;
            Scale = savedScale;
            OffsetX = savedOffsetX;
            OffsetY = savedOffsetY;
            RotationDeg = savedRot;
            _suspendGeometryUpdate = false;
            Anchors.Clear();
            BuildRms = 0;
            IsBuildMode = false;
            OnPropertyChanged(nameof(BuildRmsInfo));
            UpdateRowsProjection();
            RebuildCycleOverlays(false);
        }

        public (double u, double v) ProjectWorldToImage(double x, double y)
        {
            double rad = RotationDeg * Math.PI / 180.0;
            double a = Scale * Math.Cos(rad);
            double b = Scale * Math.Sin(rad);
            double u = a * x - b * y + OffsetX;
            double v = b * x + a * y + OffsetY;
            return (u, v);
        }

        private void UpdateRowsProjection()
        {
            if (_suspendGeometryUpdate) return;
            EnsureRowCache();

            double scale = Scale;
            double offsetX = OffsetX;
            double offsetY = OffsetY;
            double rotation = RotationDeg;
            double maxV = MaxVVisible;
            double baseScale = VectorSettings.VectorScaleBase;
            bool useWeight = VectorSettings.UseRelativeWeight;
            double weightExponent = Math.Max(0.0, VectorSettings.WeightExponent);
            double minArrowPx = Math.Max(0.0, VectorSettings.MinArrowPx);
            double maxArrowPx = Math.Max(0.0, VectorSettings.MaxArrowPx);
            double headSize = Math.Max(0.0, VectorSettings.ArrowHeadSize);
            double rad = rotation * Math.PI / 180.0;
            double a = scale * Math.Cos(rad);
            double b = scale * Math.Sin(rad);

            foreach (var list in _rowsByCycle.Values)
            {
                foreach (var row in list)
                {
                    if (!row.X.HasValue || !row.Y.HasValue)
                    {
                        row.HasVector = false;
                        row.ScreenX = double.NaN;
                        row.ScreenY = double.NaN;
                        row.LabelX = double.NaN;
                        row.LabelY = double.NaN;
                        row.ArrowEndX = double.NaN;
                        row.ArrowEndY = double.NaN;
                        row.ArrowHead = new PointCollection();
                        continue;
                    }

                    double x = row.X.Value;
                    double y = row.Y.Value;
                    double sx = a * x - b * y + offsetX;
                    double sy = b * x + a * y + offsetY;

                    row.ScreenX = sx;
                    row.ScreenY = sy;
                    row.LabelX = sx;
                    row.LabelY = sy;

                    if (!row.Dx.HasValue || !row.Dy.HasValue)
                    {
                        row.HasVector = false;
                        row.ArrowEndX = sx;
                        row.ArrowEndY = sy;
                        row.ArrowHead = new PointCollection();
                        continue;
                    }

                    double dx = row.Dx.Value;
                    double dy = row.Dy.Value;
                    double vlen = Math.Sqrt(dx * dx + dy * dy);
                    if (vlen < 1e-9)
                    {
                        row.HasVector = false;
                        row.ArrowEndX = sx;
                        row.ArrowEndY = sy;
                        row.ArrowHead = new PointCollection();
                        continue;
                    }

                    double weight = 1.0;
                    if (useWeight && maxV > 1e-9)
                    {
                        double norm = Math.Max(0.0, vlen / maxV);
                        weight = Math.Pow(norm, weightExponent);
                    }

                    double k = baseScale * weight;
                    double pixelLength = vlen * k * scale;

                    if (minArrowPx > 0 && scale > 1e-9 && pixelLength < minArrowPx)
                        k = minArrowPx / (vlen * scale);

                    if (maxArrowPx > 0 && scale > 1e-9 && pixelLength > maxArrowPx && (maxArrowPx >= minArrowPx || minArrowPx <= 0))
                        k = maxArrowPx / (vlen * scale);

                    double x2 = x + dx * k;
                    double y2 = y + dy * k;

                    double ex = a * x2 - b * y2 + offsetX;
                    double ey = b * x2 + a * y2 + offsetY;

                    row.ArrowEndX = ex;
                    row.ArrowEndY = ey;

                    double vx = ex - sx;
                    double vy = ey - sy;
                    double len = Math.Sqrt(vx * vx + vy * vy);

                    PointCollection headPoints;
                    if (len > 1e-6 && headSize > 0)
                    {
                        double tip = Math.Min(headSize, len * 0.4);
                        double ux = vx / len;
                        double uy = vy / len;
                        double nx = -uy;
                        double ny = ux;
                        var tipPoint = new Point(ex, ey);
                        var p1 = new Point(ex - ux * tip + nx * tip * 0.4, ey - uy * tip + ny * tip * 0.4);
                        var p2 = new Point(ex - ux * tip - nx * tip * 0.4, ey - uy * tip - ny * tip * 0.4);
                        headPoints = new PointCollection { tipPoint, p1, p2 };
                    }
                    else
                    {
                        headPoints = new PointCollection { new Point(ex, ey) };
                    }

                    row.ArrowHead = headPoints;
                    row.HasVector = true;
                }
            }
        }

        public void RebuildCycleOverlays(bool full = true)
        {
            EnsureRowCache();
            UpdateRowsProjection();
            CycleOverlays.Clear();

            if (Cycles.Count == 0) return;

            int lastCycleId = Cycles.Count > 0 ? Cycles.Max(c => c.Id) : 0;

            var ids = Cycles.OrderBy(c => c.Id)
                            .Where(c => DisplayMode == ArrowDisplayMode.AllCycles ||
                                        c.IsSelected ||
                                        (DisplayMode == ArrowDisplayMode.LastCycle && c.Id == lastCycleId))
                            .Select(c => c.Id);

            foreach (var id in ids)
            {
                if (!_rowsByCycle.TryGetValue(id, out var rows) || rows.Count == 0)
                    continue;

                var points = rows
                    .Where(r => !double.IsNaN(r.ScreenX) && !double.IsNaN(r.ScreenY))
                    .Select(r => new System.Windows.Point(r.ScreenX, r.ScreenY))
                    .GroupBy(p => (Math.Round(p.X, 2), Math.Round(p.Y, 2)))
                    .Select(g => g.First())
                    .OrderBy(p => p.X)
                    .ThenBy(p => p.Y)
                    .ToList();

                if (points.Count == 0) continue;

                var overlay = new CycleOverlayVm { CycleId = id };

                if (points.Count >= 3)
                {
                    var hull = ComputeMonotoneHull(points);
                    var area = Math.Abs(ComputePolygonArea(hull));
                    if (hull.Count >= 3 && area > 1e-3)
                    {
                        var geometry = CreateStreamGeometry(hull, true);
                        overlay.FillGeometry = geometry;
                        overlay.StrokeGeometry = geometry;
                    }
                }

                if (overlay.StrokeGeometry == null)
                {
                    if (points.Count >= 2)
                    {
                        overlay.StrokeGeometry = CreateStreamGeometry(points, false);
                    }
                    else if (points.Count == 1)
                    {
                        var geo = new EllipseGeometry(points[0], 2, 2);
                        geo.Freeze();
                        overlay.StrokeGeometry = geo;
                    }
                }

                if (overlay.HasFill || overlay.HasStroke)
                    CycleOverlays.Add(overlay);
            }
        }

        private static System.Collections.Generic.List<System.Windows.Point> ComputeMonotoneHull(
            System.Collections.Generic.List<System.Windows.Point> pts)
        {
            var lower = new System.Collections.Generic.List<System.Windows.Point>();
            for (int i = 0; i < pts.Count; i++)
            {
                var p = pts[i];
                while (lower.Count >= 2 && Cross(lower[^2], lower[^1], p) <= 0) lower.RemoveAt(lower.Count - 1);
                lower.Add(p);
            }
            var upper = new System.Collections.Generic.List<System.Windows.Point>();
            for (int i = pts.Count - 1; i >= 0; i--)
            {
                var p = pts[i];
                while (upper.Count >= 2 && Cross(upper[^2], upper[^1], p) <= 0) upper.RemoveAt(upper.Count - 1);
                upper.Add(p);
            }
            lower.RemoveAt(lower.Count - 1); upper.RemoveAt(upper.Count - 1);
            lower.AddRange(upper);
            return lower;
        }

        private static double Cross(System.Windows.Point a, System.Windows.Point b, System.Windows.Point c)
            => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

        private static double ComputePolygonArea(IList<System.Windows.Point> polygon)
        {
            if (polygon.Count < 3) return 0;
            double area = 0;
            for (int i = 0; i < polygon.Count; i++)
            {
                var p1 = polygon[i];
                var p2 = polygon[(i + 1) % polygon.Count];
                area += p1.X * p2.Y - p2.X * p1.Y;
            }
            return 0.5 * area;
        }

        private static StreamGeometry CreateStreamGeometry(IList<System.Windows.Point> points, bool closed)
        {
            if (points.Count == 0) return new StreamGeometry();

            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(points[0], closed, closed);
                if (points.Count > 1)
                    ctx.PolyLineTo(points.Skip(1).ToList(), true, true);
            }
            geometry.Freeze();
            return geometry;
        }

        private void LoadBackground()
        {
            var dlg = new OpenFileDialog { Filter = "Изображения|*.png;*.jpg;*.jpeg;*.bmp|Все файлы|*.*" };
            if (dlg.ShowDialog() == true)
            {
                BackgroundPath = dlg.FileName;
                var img = new BitmapImage();
                using (var fs = File.OpenRead(dlg.FileName))
                {
                    img.BeginInit();
                    img.CacheOption = BitmapCacheOption.OnLoad;
                    img.StreamSource = fs;
                    img.EndInit();
                }
                BackgroundImage = img;
            }
        }

        // == Подгонка преобразования ==
        private void RecomputeBuild()
        {
            var valid = Anchors.Where(a => a.HasWorld).ToList();
            if (valid.Count < 2)
            {
                _suspendGeometryUpdate = true;
                Scale = savedScale;
                OffsetX = savedOffsetX;
                OffsetY = savedOffsetY;
                RotationDeg = savedRot;
                _suspendGeometryUpdate = false;
                foreach (var a in valid) a.ClearPrediction();
                BuildRms = 0;
                OnPropertyChanged(nameof(BuildRmsInfo));
                AcceptBuildCommand.NotifyCanExecuteChanged();
                UpdateRowsProjection();
                RebuildCycleOverlays(false);
                return;
            }

            double[,] ATA = new double[4, 4];
            double[] ATy = new double[4];
            foreach (var an in valid)
            {
                double x = an.WorldX, y = an.WorldY, u = an.ImageU, v = an.ImageV;
                double[] r1 = new[] { x, -y, 1.0, 0.0 };
                double[] r2 = new[] { y, x, 0.0, 1.0 };

                for (int k = 0; k < 4; k++)
                {
                    for (int l = 0; l < 4; l++)
                        ATA[k, l] += r1[k] * r1[l] + r2[k] * r2[l];
                    ATy[k] += r1[k] * u + r2[k] * v;
                }
            }

            var p = Solve4x4(ATA, ATy);
            if (p == null)
            {
                foreach (var a in valid) a.ClearPrediction();
                BuildRms = 0;
                OnPropertyChanged(nameof(BuildRmsInfo));
                AcceptBuildCommand.NotifyCanExecuteChanged();
                RebuildCycleOverlays(false);
                return;
            }

            double acoef = p[0], bcoef = p[1], tx = p[2], ty = p[3];
            double s = Math.Sqrt(acoef * acoef + bcoef * bcoef);
            double theta = Math.Atan2(bcoef, acoef) * 180.0 / Math.PI;

            _suspendGeometryUpdate = true;
            Scale = s;
            RotationDeg = theta;
            OffsetX = tx;
            OffsetY = ty;
            _suspendGeometryUpdate = false;

            double sum2 = 0;
            foreach (var an in valid)
            {
                double up = acoef * an.WorldX - bcoef * an.WorldY + tx;
                double vp = bcoef * an.WorldX + acoef * an.WorldY + ty;
                an.PredictedU = up; an.PredictedV = vp;
                var dx = up - an.ImageU; var dy = vp - an.ImageV;
                an.Residual = Math.Sqrt(dx * dx + dy * dy);
                sum2 += dx * dx + dy * dy;
            }
            BuildRms = Math.Sqrt(sum2 / valid.Count);
            OnPropertyChanged(nameof(BuildRmsInfo));
            AcceptBuildCommand.NotifyCanExecuteChanged();
            UpdateRowsProjection();
            RebuildCycleOverlays(false);
        }

        private static double[]? Solve4x4(double[,] A, double[] b)
        {
            int n = 4;
            double[,] M = new double[n, n + 1];
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++) M[i, j] = A[i, j];
                M[i, n] = b[i];
            }
            for (int col = 0; col < n; col++)
            {
                int pivot = col; double best = Math.Abs(M[pivot, col]);
                for (int r = col + 1; r < n; r++) { double v = Math.Abs(M[r, col]); if (v > best) { best = v; pivot = r; } }
                if (best < 1e-9) return null;
                if (pivot != col) for (int c = col; c <= n; c++) { var t = M[col, c]; M[col, c] = M[pivot, c]; M[pivot, c] = t; }
                double div = M[col, col]; for (int c = col; c <= n; c++) M[col, c] /= div;
                for (int r = 0; r < n; r++)
                {
                    if (r == col) continue;
                    double factor = M[r, col];
                    if (Math.Abs(factor) < 1e-12) continue;
                    for (int c = col; c <= n; c++) M[r, c] -= factor * M[col, c];
                }
            }
            var x = new double[n];
            for (int i = 0; i < n; i++) x[i] = M[i, n];
            return x;
        }
    }

    public sealed partial class CycleOverlayVm : ObservableObject
    {
        [ObservableProperty] private int cycleId;
        [ObservableProperty] private Geometry? fillGeometry;
        [ObservableProperty] private Geometry? strokeGeometry;

        public bool HasFill => FillGeometry != null;
        public bool HasStroke => StrokeGeometry != null;

        partial void OnFillGeometryChanged(Geometry? value) => OnPropertyChanged(nameof(HasFill));
        partial void OnStrokeGeometryChanged(Geometry? value) => OnPropertyChanged(nameof(HasStroke));
    }

    public sealed partial class CycleToggle : ObservableObject
    {
        [ObservableProperty] private int id;
        [ObservableProperty] private bool isSelected;
    }

    public sealed class CycleFilterOption
    {
        public int? Id { get; set; }
        public string Title { get; set; } = "";
        public override string ToString() => Title;
    }

    public sealed partial class PitPointRow : ObservableObject
    {
        public int? N { get; set; }
        public int CycleId { get; set; }
        public double? X { get; set; }
        public double? Y { get; set; }
        public double? Dx { get; set; }
        public double? Dy { get; set; }
        public double? V => (Dx.HasValue && Dy.HasValue) ? Math.Sqrt(Dx.Value * Dx.Value + Dy.Value * Dy.Value) : (double?)null;
        public override string ToString() => $"#{N} ({X:F3}; {Y:F3})";

        [ObservableProperty] private double screenX;
        [ObservableProperty] private double screenY;
        [ObservableProperty] private double arrowEndX;
        [ObservableProperty] private double arrowEndY;
        [ObservableProperty] private PointCollection arrowHead = new();
        [ObservableProperty] private double labelX;
        [ObservableProperty] private double labelY;
        [ObservableProperty] private bool hasVector;
    }

    public sealed partial class VectorDisplaySettings : ObservableObject
    {
        // Вектора
        [ObservableProperty] private double lineThickness = 2.0;
        [ObservableProperty] private double arrowHeadSize = 12.0;
        [ObservableProperty] private bool showLabels = true;
        [ObservableProperty] private double labelFontSize = 12.0;
        [ObservableProperty] private double vectorScaleBase = 1.0; // базовый множитель в мире

        // Вес/нормировка
        [ObservableProperty] private bool useRelativeWeight = true;
        [ObservableProperty] private double weightExponent = 1.0;   // 0..2 обычно
        [ObservableProperty] private double minArrowPx = 0.0;       // 0 — без ограничения
        [ObservableProperty] private double maxArrowPx = 0.0;

        // Подложка
        [ObservableProperty] private double backgroundOpacity = 1.0;

        // Заливка/штрих
        [ObservableProperty] private double fillOpacity = 0.30;
        [ObservableProperty] private double hatchSpacingPx = 12.0;
        [ObservableProperty] private double hatchAngleDeg = 45.0;
        [ObservableProperty] private double hatchOpacity = 0.60;

        // Цвета
        [ObservableProperty] private bool useSingleColor = false;
        [ObservableProperty] private Color singleColor = Colors.Orange;
        public ObservableCollection<CycleStyle> CycleStyles { get; } = new();

        // Версия (для триггера multibinding)
        [ObservableProperty] private int version = 1;
    }

    public sealed partial class CycleStyle : ObservableObject
    {
        [ObservableProperty] private int cycleId;
        [ObservableProperty] private Color color;
    }

    public sealed partial class AnchorPair : ObservableObject
    {
        [ObservableProperty] private double imageU;
        [ObservableProperty] private double imageV;
        [ObservableProperty] private double worldX;
        [ObservableProperty] private double worldY;

        [ObservableProperty] private double? predictedU;
        [ObservableProperty] private double? predictedV;
        [ObservableProperty] private double residual;

        public bool HasWorld => !double.IsNaN(WorldX) && !double.IsNaN(WorldY);
        public void ClearPrediction() { PredictedU = null; PredictedV = null; Residual = 0; }
    }
}
