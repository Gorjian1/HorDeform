// File: ViewModels/MainViewModel.cs
using ClosedXML.Excel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Osadka.Models;
using Osadka.Services;
using Osadka.Views;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Linq;
using System.Collections.Generic;

namespace Osadka.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private string? _currentPath;

        public RawDataViewModel RawVM { get; }
        public GeneralReportViewModel GenVM { get; }
        public DynamicsGrafficViewModel DynVM { get; }
        private readonly DynamicsReportService _dynSvc;

        public IRelayCommand HelpCommand { get; }
        public IRelayCommand<string> NavigateCommand { get; }
        public IRelayCommand NewProjectCommand { get; }
        public IRelayCommand OpenProjectCommand { get; }
        public IRelayCommand SaveProjectCommand { get; }
        public IRelayCommand SaveAsProjectCommand { get; }
        public IRelayCommand QuickReportCommand { get; }

        private object? _currentPage;

        private bool _includeGeneral = true;
        public bool IncludeGeneral
        {
            get => _includeGeneral;
            set => SetProperty(ref _includeGeneral, value);
        }

        private bool _includeGraphs = false;
        public bool IncludeGraphs
        {
            get => _includeGraphs;
            set => SetProperty(ref _includeGraphs, value);
        }

        public object? CurrentPage
        {
            get => _currentPage;
            set => SetProperty(ref _currentPage, value);
        }

        private static class PageKeys
        {
            public const string Raw = "RawData";
            public const string Diff = "SettlementDiff";
            public const string Graf = "Dynamics";
        }

        public MainViewModel()
        {
            RawVM = new RawDataViewModel();

            var genSvc = new GeneralReportService();
            GenVM = new GeneralReportViewModel(RawVM, genSvc); // Report пересчитывается при изменениях данных.

            _dynSvc = new DynamicsReportService();

            HelpCommand = new RelayCommand(OpenHelp);
            NavigateCommand = new RelayCommand<string>(Navigate);
            NewProjectCommand = new RelayCommand(NewProject);
            OpenProjectCommand = new RelayCommand(OpenProject);
            SaveProjectCommand = new RelayCommand(SaveProject);
            SaveAsProjectCommand = new RelayCommand(SaveAsProject);
            QuickReportCommand = new RelayCommand(DoQuickExport, () => GenVM.Report != null);

            Navigate(PageKeys.Raw);
        }

        private void OpenHelp()
        {
            string exeDir = AppContext.BaseDirectory;
            string docx = Path.Combine(exeDir, "help.docx");

            if (File.Exists(docx))
                Process.Start(new ProcessStartInfo(docx) { UseShellExecute = true });
            else
                MessageBox.Show("Файл справки не найден.",
                                "Справка",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
        }

        #region Navigation

        private void Navigate(string? key)
        {
            CurrentPage = key switch
            {
                PageKeys.Raw => new RawDataPage(RawVM),
                PageKeys.Diff => new GeneralReportPage(GenVM),
                PageKeys.Graf => new Osadka.Views.PitOffsetPage
                               {
               DataContext = new Osadka.ViewModels.PitOffsetViewModel(RawVM)
                               },
            };
        }

        #endregion

        private void NewProject()
        {
            RawVM.ClearCommand.Execute(null);
            _currentPath = null;
        }

        public void LoadProject(string path)
        {
            if (RawVM is not { } vm) return;

            try
            {
                var json = File.ReadAllText(path);
                var data = JsonSerializer.Deserialize<ProjectData>(json)
                           ?? throw new InvalidOperationException("Невалидный формат проекта");

                vm.Header.CycleNumber = data.Cycle;
                vm.Header.HorNomen = data.MaxNomen;

                vm.DataRows.Clear();
                foreach (var r in data.DataRows) vm.DataRows.Add(r);

                vm.CoordRows.Clear();
                foreach (var c in data.CoordRows) vm.CoordRows.Add(c);

                var fld = typeof(RawDataViewModel).GetField("_objects", BindingFlags.Instance | BindingFlags.NonPublic);
                if (fld != null)
                {
                    var dict = fld.GetValue(vm) as Dictionary<int, Dictionary<int, List<MeasurementRow>>>
                               ?? new Dictionary<int, Dictionary<int, List<MeasurementRow>>>();
                    dict.Clear();
                    foreach (var objKv in data.Objects)
                    {
                        dict[objKv.Key] = objKv.Value.ToDictionary(
                            cycleKv => cycleKv.Key,
                            cycleKv => cycleKv.Value.ToList());
                    }
                    fld.SetValue(vm, dict);
                }

                vm.ObjectNumbers.Clear();
                var currentObjects = (Dictionary<int, Dictionary<int, List<MeasurementRow>>>)(fld?.GetValue(vm)
                                        ?? new Dictionary<int, Dictionary<int, List<MeasurementRow>>>());
                foreach (var obj in currentObjects.Keys.OrderBy(k => k))
                    vm.ObjectNumbers.Add(obj);

                vm.CycleNumbers.Clear();
                if (currentObjects.TryGetValue(vm.Header.ObjectNumber, out var cycles))
                    foreach (var cyc in cycles.Keys.OrderBy(k => k))
                        vm.CycleNumbers.Add(cyc);

                _currentPath = path;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Ошибка при загрузке проекта:\n{ex.Message}",
                    "Ошибка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void OpenProject()
        {
            var dlg = new OpenFileDialog
            {
                Filter = "HorDeform Project (*.hdf)|*.hdf|All Files|*.*"
            };
            if (dlg.ShowDialog() != true) return;

            LoadProject(dlg.FileName);
        }

        private void SaveProject()
        {
            if (_currentPath == null)
            {
                SaveAsProject();
                return;
            }
            SaveTo(_currentPath);
        }

        private void SaveAsProject()
        {
            var dlg = new SaveFileDialog
            {
                Filter = "HorDeform Project (*.hdf)|*.hdf"
            };
            if (dlg.ShowDialog() != true) return;
            SaveTo(dlg.FileName);
            _currentPath = dlg.FileName;
        }

        private void SaveTo(string path)
        {
            if (RawVM is not { } vm) return;
            var fld = typeof(RawDataViewModel).GetField("_objects", BindingFlags.Instance | BindingFlags.NonPublic);
            var currentObjects = (Dictionary<int, Dictionary<int, List<MeasurementRow>>>)(fld?.GetValue(vm)
                                    ?? new Dictionary<int, Dictionary<int, List<MeasurementRow>>>());

            var data = new ProjectData
            {
                Cycle = vm.Header.CycleNumber,
                MaxNomen = vm.Header.HorNomen,

                DataRows = vm.DataRows.ToList(),
                CoordRows = vm.CoordRows.ToList(),

                Objects = currentObjects.ToDictionary(
                    objKv => objKv.Key,
                    objKv => objKv.Value.ToDictionary(
                        cycleKv => cycleKv.Key,
                        cycleKv => cycleKv.Value.ToList()))
            };

            var json = JsonSerializer.Serialize(
                data,
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }

        private void DoQuickExport()
        {
            if (GenVM.Report is null) return;

            if (!(IncludeGeneral || IncludeGraphs))
            {
                MessageBox.Show("Выберите хотя бы один пункт: Общий, Относительный, Графики.",
                                "Экспорт", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string exeDir = AppContext.BaseDirectory;

            // 1) Пользовательский шаблон или встроенный
            string? chosenTemplate = (RawVM.HasCustomTemplate ? RawVM.TemplatePath : null);
            string template = !string.IsNullOrWhiteSpace(chosenTemplate) && File.Exists(chosenTemplate)
                              ? chosenTemplate!
                              : Path.Combine(exeDir, "template.xlsx");
            if (!File.Exists(template))
            {
                MessageBox.Show("Не найден шаблон Excel.\nЛибо выберите пользовательский файл, либо положите template.xlsx рядом с программой.",
                                "Экспорт", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var dlg = new SaveFileDialog
            {
                Filter = "Excel|*.xlsx",
                FileName = $"Отчёт_{DateTime.Now:yyyyMMdd_HHmm}.xlsx"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                File.Copy(template, dlg.FileName, overwrite: true);

                using (var wb = new XLWorkbook(dlg.FileName))
                {
                    var generalWs = wb.Worksheets.First(); // титульный/общий

                    if (IncludeGeneral)
                    {
                        var map = BuildPlaceholderMap();

                        // Поддержка отключения блоков: если тег относится к выключенному блоку — удаляем всю строку.
                        var disabledTags = GenVM.Settings?.GetDisabledTags() ?? new HashSet<string>();

                        // Получаем снимок используемых текстовых ячеек заранее (чтобы безопасно удалять строки потом)
                        var usedTextCells = generalWs.CellsUsed(c => c.DataType == XLDataType.Text).ToList();

                        var rowsToDelete = new HashSet<int>();

                        foreach (var cell in usedTextCells)
                        {
                            string t = cell.GetString().Trim();

                            if (!t.StartsWith("/")) continue;

                            // Если тег выключен — помечаем строку на удаление
                            if (disabledTags.Contains(t))
                            {
                                rowsToDelete.Add(cell.Address.RowNumber);
                                continue;
                            }

                            // Иначе обычная подстановка, если есть в карте
                            if (map.TryGetValue(t, out var val))
                                cell.Value = val;
                        }

                        // Удаляем строки, где встретились выключенные теги (снизу вверх)
                        foreach (var r in rowsToDelete.OrderByDescending(x => x))
                            generalWs.Row(r).Delete();
                    }
                    else
                    {
                        generalWs.Delete();
                    }

                    if (IncludeGraphs)
                        AddDynamicsSheet(wb);
                    else
                    {
                        var dynWs = wb.Worksheets.FirstOrDefault(
                            ws => string.Equals(ws.Name, "Графики динамики", System.StringComparison.OrdinalIgnoreCase));
                        dynWs?.Delete();
                    }

                    wb.Save();
                }

                if (IncludeGraphs)
                {
                    RunSta(() => BuildChartFromDynTable_Quick_NoPIA(
                        filePath: dlg.FileName,
                        dataSheetName: "Графики динамики",
                        tableName: "DynTable",
                        chartSheetName: "Графики динамики",
                        left: 40, top: 200, width: 920, height: 440,
                        deleteOldCharts: true
                    ));
                }

                MessageBox.Show("Экспорт завершён", "Экспорт",
                                MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка экспорта: {ex.Message}", "Экспорт",
                                MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static void RunSta(System.Action action)
        {
            var t = new System.Threading.Thread(() => action()) { IsBackground = true };
            t.SetApartmentState(System.Threading.ApartmentState.STA);
            t.Start();
            t.Join();
        }

        private static void BuildChartFromDynTable_Quick_NoPIA(
            string filePath,
            string dataSheetName = "Графики динамики",
            string tableName = "DynTable",
            string chartSheetName = "Графики динамики",
            int left = 40, int top = 200, int width = 920, int height = 440,
            bool deleteOldCharts = true)
        {
            const int xlRows = 1;
            const int xlLine = 4;

            object app = null, wb = null, worksheets = null;
            object wsData = null, listObjects = null, lo = null, loRange = null, dataBodyRange = null;
            object wsChart = null, chartObjects = null, chartObj = null, chart = null, shapes = null, shape = null;

            try
            {
                var excelType = Type.GetTypeFromProgID("Excel.Application", throwOnError: false);
                if (excelType == null) throw new InvalidOperationException("Microsoft Excel не установлен.");

                app = Activator.CreateInstance(excelType);
                excelType.InvokeMember("Visible", BindingFlags.SetProperty, null, app, new object[] { false });
                excelType.InvokeMember("DisplayAlerts", BindingFlags.SetProperty, null, app, new object[] { false });

                var workbooks = excelType.InvokeMember("Workbooks", BindingFlags.GetProperty, null, app, null);
                wb = workbooks.GetType().InvokeMember("Open", BindingFlags.InvokeMethod, null, workbooks, new object[] { filePath });

                worksheets = wb.GetType().InvokeMember("Worksheets", BindingFlags.GetProperty, null, wb, null);

                wsData = worksheets.GetType().InvokeMember("Item", BindingFlags.GetProperty, null, worksheets, new object[] { dataSheetName });
                listObjects = wsData.GetType().InvokeMember("ListObjects", BindingFlags.GetProperty, null, wsData, null);
                lo = listObjects.GetType().InvokeMember("Item", BindingFlags.GetProperty, null, listObjects, new object[] { tableName });
                dataBodyRange = lo.GetType().InvokeMember("DataBodyRange", BindingFlags.GetProperty, null, lo, null);
                if (dataBodyRange == null) throw new InvalidOperationException("Таблица DynTable пуста (нет строк данных).");
                loRange = lo.GetType().InvokeMember("Range", BindingFlags.GetProperty, null, lo, null);

                object TryGetChartSheet()
                {
                    try { return worksheets.GetType().InvokeMember("Item", BindingFlags.GetProperty, null, worksheets, new object[] { chartSheetName }); }
                    catch { return null; }
                }
                wsChart = TryGetChartSheet();
                bool chartSheetIsWorksheet = false;
                if (wsChart != null)
                {
                    try
                    {
                        _ = wsChart.GetType().InvokeMember("ChartObjects", BindingFlags.InvokeMethod, null, wsChart, null);
                        chartSheetIsWorksheet = true;
                    }
                    catch { chartSheetIsWorksheet = false; }
                }
                if (wsChart == null || !chartSheetIsWorksheet)
                {
                    wsChart = worksheets.GetType().InvokeMember("Add", BindingFlags.InvokeMethod, null, worksheets, null);
                    wsChart.GetType().InvokeMember("Name", BindingFlags.SetProperty, null, wsChart, new object[] { chartSheetName });
                }

                try { wsChart.GetType().InvokeMember("Activate", BindingFlags.InvokeMethod, null, wsChart, null); } catch { }
                try { wsChart.GetType().InvokeMember("Select", BindingFlags.InvokeMethod, null, wsChart, new object[] { true }); } catch { }

                object missing = Type.Missing;
                try
                {
                    chartObjects = wsChart.GetType().InvokeMember("ChartObjects", BindingFlags.InvokeMethod, null, wsChart, new object[] { missing });
                    if (deleteOldCharts && chartObjects != null)
                    {
                        try
                        {
                            chartObjects.GetType().InvokeMember("Delete", BindingFlags.InvokeMethod, null, chartObjects, null);
                        }
                        catch
                        {
                            var cntObj = chartObjects.GetType().InvokeMember("Count", BindingFlags.GetProperty, null, chartObjects, null);
                            int cnt = cntObj is int i ? i : System.Convert.ToInt32(cntObj);
                            for (int j = cnt; j >= 1; j--)
                            {
                                var co = chartObjects.GetType().InvokeMember("Item", BindingFlags.GetProperty, null, chartObjects, new object[] { j });
                                try { co.GetType().InvokeMember("Delete", BindingFlags.InvokeMethod, null, co, null); } catch { }
                                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(co);
                            }
                        }
                    }
                }
                catch
                {
                    chartObjects = null;
                }

                bool created = false;
                try
                {
                    shapes = wsChart.GetType().InvokeMember("Shapes", BindingFlags.GetProperty, null, wsChart, null);

                    shape = shapes.GetType().InvokeMember("AddChart2", BindingFlags.InvokeMethod, null, shapes,
                            new object[] { 0, xlLine, (double)left, (double)top, (double)width, (double)height });
                    chart = shape.GetType().InvokeMember("Chart", BindingFlags.GetProperty, null, shape, null);
                    created = chart != null;
                }
                catch
                {
                    if (chartObjects == null)
                        chartObjects = wsChart.GetType().InvokeMember("ChartObjects", BindingFlags.InvokeMethod, null, wsChart, new object[] { missing });

                    chartObj = chartObjects.GetType().InvokeMember("Add", BindingFlags.InvokeMethod, null, chartObjects,
                        new object[] { (double)left, (double)top, (double)width, (double)height });
                    chart = chartObj.GetType().InvokeMember("Chart", BindingFlags.GetProperty, null, chartObj, null);
                    created = chart != null;
                }

                if (!created) throw new InvalidOperationException("Не удалось создать объект диаграммы на листе.");

                chart.GetType().InvokeMember("SetSourceData", BindingFlags.InvokeMethod, null, chart, new object[] { loRange, xlRows });
                chart.GetType().InvokeMember("ChartType", BindingFlags.SetProperty, null, chart, new object[] { xlLine });
                chart.GetType().InvokeMember("HasTitle", BindingFlags.SetProperty, null, chart, new object[] { false });

                wb.GetType().InvokeMember("Save", BindingFlags.InvokeMethod, null, wb, null);
            }
            finally
            {
                try { wb?.GetType().InvokeMember("Close", BindingFlags.InvokeMethod, null, wb, new object[] { false }); } catch { }
                try { app?.GetType().InvokeMember("Quit", BindingFlags.InvokeMethod, null, app, null); } catch { }

                void rel(object o) { if (o != null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(o); }
                rel(chart); rel(chartObj); rel(chartObjects); rel(shape); rel(shapes);
                rel(wsChart); rel(loRange); rel(dataBodyRange); rel(lo); rel(listObjects);
                rel(wsData); rel(worksheets); rel(wb);

                GC.Collect(); GC.WaitForPendingFinalizers();
                GC.Collect(); GC.WaitForPendingFinalizers();
            }
        }

        private Dictionary<string, string> BuildPlaceholderMap()
        {
            static string DashIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? "-" : s;
            static string JoinOrDash(IEnumerable<string>? ids)
                => (ids is null || !ids.Any()) ? "-" : string.Join(", ", ids);

            var r = (object)GenVM.Report!;

            double? ReadDouble(object src, string propName)
            {
                var p = src.GetType().GetProperty(propName);
                if (p == null) return null;
                var val = p.GetValue(src);
                if (val == null) return null;
                if (val is double d) return d;
                var vp = val.GetType().GetProperty("Value");
                if (vp != null && vp.GetValue(val) is double dv) return dv;
                return null;
            }

            IEnumerable<string>? ReadIds(object src, string baseName)
            {
                var pIds = src.GetType().GetProperty(baseName + "Ids");
                if (pIds?.GetValue(src) is IEnumerable<string> idsA) return idsA;

                var p = src.GetType().GetProperty(baseName);
                var val = p?.GetValue(src);
                var idsProp = val?.GetType().GetProperty("Ids");
                if (idsProp?.GetValue(val) is IEnumerable<string> idsB) return idsB;

                return null;
            }

            double? D(params string[] names) { foreach (var n in names) { var v = ReadDouble(r, n); if (v.HasValue) return v; } return null; }
            IEnumerable<string>? IDS(params string[] names) { foreach (var n in names) { var v = ReadIds(r, n); if (v != null) return v; } return null; }

            double? hor = RawVM.Header.HorNomen;

            var map = new Dictionary<string, string>
            {
                ["/цикл"] = DashIfEmpty(RawVM.SelectedCycleHeader),
                ["/горпревыш"] = DashIfEmpty(hor?.ToString()),

                ["/векторабс"] = D("VectorAbs")?.ToString("F0") ?? "-",
                ["/векторабсId"] = JoinOrDash(IDS("VectorAbs")),
                ["/дельтаХабс"] = D("DxAbs")?.ToString("F0") ?? "-",
                ["/дельтаХабсId"] = JoinOrDash(IDS("DxAbs")),
                ["/дельтаYабс"] = D("DyAbs")?.ToString("F0") ?? "-",
                ["/дельтаYабсId"] = JoinOrDash(IDS("DyAbs")),

                ["/дельтаHэкстреммин"] = D("DhMin")?.ToString("F1") ?? "-",
                ["/дельтаHэкстремминId"] = JoinOrDash(IDS("DhMin")),
                ["/дельтаHэкстреммакс"] = D("DhMax")?.ToString("F1") ?? "-",
                ["/дельтаHэкстреммаксId"] = JoinOrDash(IDS("DhMax")),

                ["/дельтаYпревыш"] = JoinOrDash(GenVM.Report?.ExceedYIds),
                ["/дельтаXпревыш"] = JoinOrDash(GenVM.Report?.ExceedXIds),
                ["/векторпревыш"] = JoinOrDash(GenVM.Report?.ExceedVectorIds),
                ["/дельтаHпревыш"] = JoinOrDash(GenVM.Report?.ExceedHIds),
            };

            if (GenVM.Report?.NoAccessIds is { } na) map["/нетдоступа"] = na.Any() ? string.Join(", ", na) : "-";
            if (GenVM.Report?.DestroyedIds is { } de) map["/уничтожены"] = de.Any() ? string.Join(", ", de) : "-";
            if (GenVM.Report?.NewIds is { } nw) map["/новые"] = nw.Any() ? string.Join(", ", nw) : "-";

            // Сопоставим "максимум за цикл" (синонимы) с уже рассчитанными абсолютами:
            map["/вектормакс"] = map["/векторабс"];
            map["/вектормаксId"] = map["/векторабсId"];
            map["/дельтаXмакс"] = map["/дельтаХабс"];
            map["/дельтаXмаксId"] = map["/дельтаХабсId"];
            map["/дельтаYмакс"] = map["/дельтаYабс"];
            map["/дельтаYмаксId"] = map["/дельтаYабсId"];

            // Поддержка лат/рус X:
            map["/дельтаXабс"] = map["/дельтаХабс"];

            // Синонимы из вашего шаблона:
            if (!map.ContainsKey("/сеттмакс") && map.TryGetValue("/вектормакс", out var vm))
                map["/сеттмакс"] = vm;
            if (!map.ContainsKey("/сеттмаксId") && map.TryGetValue("/вектормаксId", out var vmi))
                map["/сеттмаксId"] = vmi;

            return map;
        }

        private void AddDynamicsSheet(XLWorkbook wb)
        {
            const string sheetName = "Графики динамики";
            const string tableName = "DynTable";

            var ws = wb.Worksheets.FirstOrDefault(s =>
                         s.Name.Equals(sheetName, System.StringComparison.OrdinalIgnoreCase))
                     ?? wb.AddWorksheet(sheetName);

            var cycles = RawVM?.CurrentCycles?.Keys?.OrderBy(c => c).ToList()
                         ?? new List<int>();

            var dynVm = new DynamicsGrafficViewModel(RawVM, _dynSvc);

            ws.Cell(1, 1).Value = "Id";
            for (int i = 0; i < cycles.Count; i++)
            {
                int cyc = cycles[i];

                string headerText;
                if (RawVM.CycleHeaders.TryGetValue(cyc, out var rawLabel))
                    headerText = CycleLabelParsing.ExtractDateTail(rawLabel) ?? rawLabel;
                else
                    headerText = $"Cycle {cyc}";

                ws.Cell(1, i + 2).Value = headerText;
            }

            var colByCycle = cycles
                .Select((cycle, idx) => new { cycle, col = idx + 2 })
                .ToDictionary(x => x.cycle, x => x.col);

            int rr = 2;
            foreach (var ser in dynVm.Lines)
            {
                ws.Cell(rr, 1).Value = ser.Id;
                foreach (var pt in ser.Points)
                {
                    if (!colByCycle.TryGetValue(pt.Cycle, out int col)) continue;

                    ws.Cell(rr, col).Value = pt.Value;
                }
                rr++;
            }

            var lastCol = 1 + cycles.Count;
            var rng = ws.Range(1, 1, rr - 1, lastCol);
            var existingTable = ws.Tables.FirstOrDefault(t =>
                string.Equals(t.Name, tableName, System.StringComparison.OrdinalIgnoreCase));

            if (existingTable != null)
            {
                existingTable.Resize(rng);
            }
            else
            {
                rng.CreateTable(tableName);
            }

            ws.Columns().AdjustToContents();

            var wbDynTable = wb.DefinedNames.FirstOrDefault(n =>
                n.Name.Equals(tableName, System.StringComparison.OrdinalIgnoreCase));
            wbDynTable?.Delete();

            var wsDynTable = ws.DefinedNames.FirstOrDefault(n =>
                n.Name.Equals(tableName, System.StringComparison.OrdinalIgnoreCase));
            wsDynTable?.Delete();

            var wbDynData = wb.DefinedNames.FirstOrDefault(n =>
                n.Name.Equals("DynData", System.StringComparison.OrdinalIgnoreCase));
            wbDynData?.Delete();

            var wsDynData = ws.DefinedNames.FirstOrDefault(n =>
                n.Name.Equals("DynData", System.StringComparison.OrdinalIgnoreCase));
            wsDynData?.Delete();

            wb.CalculateMode = XLCalculateMode.Auto;
        }
    }
}
