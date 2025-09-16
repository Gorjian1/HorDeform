// File: ViewModels/PitOffsetViewModel.cs
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
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
            RefreshCommand = new RelayCommand(() => { RebuildRows(); RebuildCycleOverlays(); });

            BuildCommand = new RelayCommand(EnterBuildMode);
            AcceptBuildCommand = new RelayCommand(AcceptBuild, () => Anchors.Count(a => a.HasWorld) >= 2);
            CancelBuildCommand = new RelayCommand(CancelBuild);
            ClearAnchorsCommand = new RelayCommand(() => { Anchors.Clear(); BuildRms = 0; OnPropertyChanged(nameof(BuildRmsInfo)); RecomputeBuild(); });
            RotateLeft90Command = new RelayCommand(() => RotationDeg -= 90);
            RotateRight90Command = new RelayCommand(() => RotationDeg += 90);
            ResetTransformCommand = new RelayCommand(() => { Scale = 1; OffsetX = 0; OffsetY = 0; RotationDeg = 0; });
            OpenVectorSettingsCommand = new RelayCommand(() => OnOpenVectorSettingsRequested?.Invoke(this, EventArgs.Empty));

            // версионирование настроек — чтобы MultiBinding реагировал
            VectorSettings.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(VectorDisplaySettings.Version)) return;
                VectorSettings.Version++;
            };

            if (Objects.Count > 0) SelectedObject = Objects[0];
            RebuildCycles();
            RebuildRows();
            RebuildCycleFilterOptions();
            RebuildCycleOverlays();

            if (Raw is INotifyPropertyChanged inpc)
                inpc.PropertyChanged += (_, __) =>
                {
                    RebuildCycles();
                    RebuildRows();
                    RebuildCycleFilterOptions();
                    RebuildCycleOverlays();
                };
        }

        public event EventHandler? OnExportRequested;
        public event EventHandler? OnOpenVectorSettingsRequested;

        // ==== Реакции ====
        partial void OnScaleChanged(double value) { RebuildCycleOverlays(false); }
        partial void OnOffsetXChanged(double value) { RebuildCycleOverlays(false); }
        partial void OnOffsetYChanged(double value) { RebuildCycleOverlays(false); }
        partial void OnRotationDegChanged(double value) { RebuildCycleOverlays(false); }
        partial void OnSelectedObjectChanged(int? value)
        {
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

        public ObservableCollection<PitPointRow> GetRowsForCycle(int cycleId)
        {
            var rows = new ObservableCollection<PitPointRow>();
            var fld = Raw.GetType().GetField("_objects", BindingFlags.Instance | BindingFlags.NonPublic);
            var dict = fld?.GetValue(Raw) as System.Collections.IDictionary;
            if (dict == null || SelectedObject == null || !dict.Contains(SelectedObject)) return rows;

            var cycles = dict[SelectedObject] as System.Collections.IDictionary;
            if (cycles == null || !cycles.Contains(cycleId)) return rows;

            var list = cycles[cycleId] as System.Collections.IEnumerable;
            foreach (var row in list ?? Array.Empty<object>())
            {
                var n = ReadInt(row, "Id", "N", "Number", "Point", "№");
                var x = ReadDouble(row, "X", "XCoord");
                var y = ReadDouble(row, "Y", "YCoord");
                var dx = ReadDouble(row, "Dx", "dX", "DeltaX");
                var dy = ReadDouble(row, "Dy", "dY", "DeltaY");

                rows.Add(new PitPointRow { N = n, X = x, Y = y, Dx = dx, Dy = dy, CycleId = cycleId });
            }
            return rows;
        }

        private void RebuildRows()
        {
            Rows.Clear();
            if (SelectedObject == null) return;

            int lastId = 0;
            foreach (var c in Cycles) if (c.Id > lastId) lastId = c.Id;

            if (DisplayMode == ArrowDisplayMode.LastCycle && lastId > 0)
            {
                foreach (var r in GetRowsForCycle(lastId)) Rows.Add(r);
            }
            else
            {
                foreach (var c in Cycles.OrderBy(i => i.Id))
                    if (DisplayMode == ArrowDisplayMode.AllCycles || c.IsSelected)
                        foreach (var r in GetRowsForCycle(c.Id))
                            Rows.Add(r);
            }

            MaxVVisible = Rows.Count > 0 ? Rows.Max(r => r.V ?? 0.0) : 0.0;
            RowsView.Refresh();
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
            Scale = savedScale; OffsetX = savedOffsetX; OffsetY = savedOffsetY; RotationDeg = savedRot;
            Anchors.Clear();
            BuildRms = 0;
            IsBuildMode = false;
            OnPropertyChanged(nameof(BuildRmsInfo));
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

        public void RebuildCycleOverlays(bool full = true)
        {
            CycleOverlays.Clear();

            var ids = Cycles.OrderBy(c => c.Id)
                            .Where(c => DisplayMode == ArrowDisplayMode.AllCycles || c.IsSelected || (DisplayMode == ArrowDisplayMode.LastCycle && c.Id == Cycles.Max(x => x.Id)))
                            .Select(c => c.Id);

            foreach (var id in ids)
            {
                var rows = GetRowsForCycle(id);
                var pts = new System.Collections.Generic.List<System.Windows.Point>();
                foreach (var r in rows)
                {
                    if (!r.X.HasValue || !r.Y.HasValue) continue;
                    var (u, v) = ProjectWorldToImage(r.X.Value, r.Y.Value);
                    pts.Add(new System.Windows.Point(u, v));
                }

                // убираем дубли
                pts = pts.Distinct().OrderBy(p => p.X).ThenBy(p => p.Y).ToList();

                var overlay = new CycleOverlayVm { CycleId = id, HasFill = false, Points = new System.Windows.Media.PointCollection() };

                if (pts.Count >= 3)
                {
                    var hull = ComputeMonotoneHull(pts);
                    double area = 0;
                    for (int i = 0; i < hull.Count; i++)
                    {
                        var p1 = hull[i];
                        var p2 = hull[(i + 1) % hull.Count];
                        area += (p1.X * p2.Y - p2.X * p1.Y);
                    }
                    area = Math.Abs(area) * 0.5;

                    if (hull.Count >= 3 && area > 1e-3)
                    {
                        overlay.HasFill = true;
                        overlay.Points = new System.Windows.Media.PointCollection(hull);
                    }
                    else
                    {
                        overlay.HasFill = false;
                        overlay.Points = new System.Windows.Media.PointCollection(pts);
                    }
                }
                else
                {
                    overlay.HasFill = false;
                    overlay.Points = new System.Windows.Media.PointCollection(pts);
                }

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
                Scale = savedScale; OffsetX = savedOffsetX; OffsetY = savedOffsetY; RotationDeg = savedRot;
                foreach (var a in valid) a.ClearPrediction();
                BuildRms = 0;
                OnPropertyChanged(nameof(BuildRmsInfo));
                AcceptBuildCommand.NotifyCanExecuteChanged();
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

            Scale = s; RotationDeg = theta; OffsetX = tx; OffsetY = ty;

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
        [ObservableProperty] private PointCollection points = new();
        [ObservableProperty] private bool hasFill;
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
