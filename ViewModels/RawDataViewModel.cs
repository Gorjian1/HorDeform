﻿using ClosedXML.Excel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Microsoft.VisualBasic;
using Osadka.Models;
using Osadka.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;

namespace Osadka.ViewModels
{
    public partial class RawDataViewModel : ObservableObject
    {
        [ObservableProperty]
        private CycleHeader header = new();

        private readonly Dictionary<int, string> _cycleHeaders = new();
        public IReadOnlyDictionary<int, string> CycleHeaders => _cycleHeaders;

        [ObservableProperty]
        private string _selectedCycleHeader = string.Empty;

        public ObservableCollection<int> ObjectNumbers { get; } = new();
        public ObservableCollection<int> CycleNumbers { get; } = new();

        public ObservableCollection<MeasurementRow> DataRows { get; } = new();
        public ObservableCollection<CoordRow> CoordRows { get; } = new();

        [ObservableProperty]
        private bool hasHeight;

        public enum CoordUnits { Millimeters, Centimeters, Decimeters, Meters }

        [ObservableProperty]
        private CoordUnits coordUnit = CoordUnits.Millimeters;

        public double CoordScale => coordUnit switch
        {
            CoordUnits.Millimeters => 1,
            CoordUnits.Centimeters => 10,
            CoordUnits.Decimeters => 100,
            _ => 1000
        };

        partial void OnCoordUnitChanged(CoordUnits oldValue, CoordUnits newValue)
        {
            double oldScale = oldValue switch
            {
                CoordUnits.Millimeters => 1,
                CoordUnits.Centimeters => 10,
                CoordUnits.Decimeters => 100,
                _ => 1000
            };
            double k = CoordScale / oldScale;
            foreach (var p in CoordRows)
            {
                p.X *= k;
                p.Y *= k;
            }
        }

        public bool ShowPlaceholder => DataRows.Count == 0;

        private readonly Dictionary<int, Dictionary<int, List<MeasurementRow>>> _objects = new();
        private readonly Dictionary<int, Dictionary<int, List<CoordRow>>> _coordsByObject = new();
        private readonly Dictionary<int, HashSet<int>> _hasHByObject = new();

        public IReadOnlyDictionary<int, Dictionary<int, List<MeasurementRow>>> Objects => _objects;

        public IReadOnlyDictionary<int, List<MeasurementRow>> CurrentCycles =>
            _objects.TryGetValue(Header.ObjectNumber, out var cycles) ? cycles
                : new Dictionary<int, List<MeasurementRow>>();

        // === Пользовательский шаблон ===

        [ObservableProperty]
        private string? templatePath;

        public bool HasCustomTemplate =>
            !string.IsNullOrWhiteSpace(TemplatePath) && File.Exists(TemplatePath);

        partial void OnTemplatePathChanged(string? value)
        {
            // сохраняем в пользовательские настройки
            UserSettings.Data.TemplatePath = value;
            try { UserSettings.Save(); } catch { /* безопасно молчим */ }

            // уведомляем биндинги (кнопка "Открыть/Выбрать" и т.п.)
            OnPropertyChanged(nameof(HasCustomTemplate));
        }

        public IRelayCommand ChooseOrOpenTemplateCommand { get; }
        public IRelayCommand ClearTemplateCommand { get; }
        public IRelayCommand PasteCommand { get; }
        public IRelayCommand LoadFromWorkbookCommand { get; }
        public IRelayCommand ClearCommand { get; }
        public IRelayCommand OpenTemplate { get; }

        public RawDataViewModel()
        {
            // загружаем ранее сохранённые настройки
            try { TemplatePath = UserSettings.Data.TemplatePath; } catch { TemplatePath = null; }

            PasteCommand = new RelayCommand(DoPaste);
            LoadFromWorkbookCommand = new RelayCommand(LoadFromWorkbook);
            ClearCommand = new RelayCommand(ClearAll);
            OpenTemplate = new RelayCommand(OpenTemplateFile);

            ChooseOrOpenTemplateCommand = new RelayCommand(ChooseOrOpenTemplate);
            ClearTemplateCommand = new RelayCommand(ClearTemplate, () => HasCustomTemplate);
            PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(HasCustomTemplate))
                    (ClearTemplateCommand as RelayCommand)?.NotifyCanExecuteChanged();
            };

            Header.PropertyChanged += Header_PropertyChanged;
        }

        private void Header_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(CycleHeader.ObjectNumber) ||
                e.PropertyName == nameof(CycleHeader.CycleNumber))
            {
                RefreshData();
            }
        }

        private void ChooseOrOpenTemplate()
        {
            try
            {
                if (HasCustomTemplate)
                {
                    // Уже выбран — открываем файл
                    Process.Start(new ProcessStartInfo(TemplatePath!) { UseShellExecute = true });
                    return;
                }

                var dlg = new OpenFileDialog
                {
                    Filter = "Excel шаблон|*.xlsx;*.xlsm;*.xlsb",
                    Title = "Выбор пользовательского шаблона Excel"
                };
                if (dlg.ShowDialog() != true) return;

                TemplatePath = dlg.FileName;
                MessageBox.Show("Пользовательский шаблон успешно выбран.\n" +
                                "Файл открыт не будет — текст кнопки теперь «Открыть шаблон».",
                                "Шаблон", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Шаблон", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ClearTemplate()
        {
            try
            {
                TemplatePath = null;
                MessageBox.Show("Путь к пользовательскому шаблону очищен.\nБудет использован встроенный template.xlsx.",
                                "Шаблон", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Шаблон", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenTemplateFile()
        {
            // Открыть встроенный шаблон рядом с программой (оставляем как было)
            try
            {
                string exeDir = AppContext.BaseDirectory;
                string template = System.IO.Path.Combine(exeDir, "template.xlsx");
                if (File.Exists(template))
                    Process.Start(new ProcessStartInfo(template) { UseShellExecute = true });
                else
                    MessageBox.Show("template.xlsx не найден рядом с программой", "Шаблон", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Открытие шаблона", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // === Остальной существующий функционал ===

        private static readonly Regex _num = new(@"^-?\d+([.,]\d+)?$", RegexOptions.Compiled);

        private static (double? value, string raw) TryParseCell(string? text)
        {
            text ??= string.Empty;
            var t = text.Trim();
            if (string.IsNullOrEmpty(t)) return (null, string.Empty);
            var norm = t.Replace(',', '.');
            if (double.TryParse(norm, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                return (v, t);
            return (null, t);
        }

        private void DoPaste()
        {
            try
            {
                string text = Clipboard.GetText();
                if (string.IsNullOrWhiteSpace(text)) return;

                var lines = text
                    .Replace('\r', '\n')
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries);

                CoordRows.Clear();
                foreach (var line in lines)
                {
                    var parts = line.Split(new[] { '\t', ';', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2) continue;

                    var (x, _) = TryParseCell(parts[0]);
                    var (y, _) = TryParseCell(parts[1]);

                    if (x is null || y is null) continue;
                    CoordRows.Add(new CoordRow { X = x.Value * CoordScale, Y = y.Value * CoordScale });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Вставка координат", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ClearAll()
        {
            DataRows.Clear();
            CoordRows.Clear();
            ObjectNumbers.Clear();
            CycleNumbers.Clear();
            _objects.Clear();
            _coordsByObject.Clear();
            _cycleHeaders.Clear();
            _hasHByObject.Clear();

            Header.ObjectNumber = 1;
            Header.CycleNumber = 1;
            SelectedCycleHeader = string.Empty;

            OnPropertyChanged(nameof(ShowPlaceholder));
        }

        private void LoadFromWorkbook()
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Excel files|*.xlsx;*.xlsm;*.xlsb",
                Title = "Выбор файла Excel"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                using var sharedWorkbook = SharedWorkbook.Open(dlg.FileName);
                var wb = sharedWorkbook.Workbook;
                var win = new ImportSelectionWindow(wb);
                if (win.ShowDialog() != true) return;

                dynamic vm = win.DataContext!;
                var wsItem = vm.SelectedWorksheet;
                var objList = vm.Objects;
                if (wsItem == null || objList == null || objList.Count == 0)
                {
                    MessageBox.Show("Не выбраны данные для импорта.", "Импорт", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var sheet = wsItem.Sheet as IXLWorksheet;

                int pickObject = 0;
                int pickCycle = 0;
                try
                {
                    pickObject = win.SelectedObjectIndex;
                    pickCycle = win.SelectedCycleIndex;
                }
                catch { }

                ReadAllObjects(sheet, objList);

                if (pickObject > 0 && _objects.ContainsKey(pickObject))
                {
                    Header.ObjectNumber = pickObject;

                    CycleNumbers.Clear();
                    foreach (var c in _objects[pickObject].Keys.OrderBy(c => c))
                        CycleNumbers.Add(c);

                    if (CycleNumbers.Contains(pickCycle))
                        Header.CycleNumber = pickCycle;
                    else
                        Header.CycleNumber = CycleNumbers.FirstOrDefault();

                    RefreshData();
                }
                UpdateCache();

            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Импорт Excel", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static bool IsX(string s) => Regex.IsMatch(s ?? "", @"^\s*[XxХх]\s*$");
        private static bool IsY(string s) => Regex.IsMatch(s ?? "", @"^\s*[Yy]\s*$");
        private static bool IsH(string s) => Regex.IsMatch(s ?? "", @"^\s*[HhНн]\s*$");
        private static bool IsDx(string s) => Regex.IsMatch(s ?? "", @"^\s*(Δ|d)\s*X", RegexOptions.IgnoreCase);
        private static bool IsDy(string s) => Regex.IsMatch(s ?? "", @"^\s*(Δ|d)\s*Y", RegexOptions.IgnoreCase);
        private static bool IsDh(string s) => Regex.IsMatch(s ?? "", @"^\s*(Δ|d)\s*H", RegexOptions.IgnoreCase);
        private static bool IsVector(string s) => Regex.IsMatch(s ?? "", @"вектор|vector", RegexOptions.IgnoreCase);

        private static int DetermineBlockWidth(IXLWorksheet s, int subRow, int startCol)
        {
            var c0 = s.Cell(subRow, startCol).GetString();
            var c1 = s.Cell(subRow, startCol + 1).GetString();
            if (!(IsX(c0) && IsY(c1)))
                throw new InvalidOperationException("Ожидались заголовки X и Y.");

            var c2 = s.Cell(subRow, startCol + 2).GetString();
            bool hasH = IsH(c2);
            int vcol = hasH ? startCol + 6 : startCol + 4;
            var vh = s.Cell(subRow, vcol).GetString();
            if (!IsVector(vh))
            {
                var dxh = s.Cell(subRow, hasH ? startCol + 3 : startCol + 2).GetString();
                var dyh = s.Cell(subRow, hasH ? startCol + 4 : startCol + 3).GetString();
                if (!(IsDx(dxh) && IsDy(dyh)))
                    throw new InvalidOperationException("Не удалось распознать структуру блока 5/7 столбцов.");
            }
            return hasH ? 7 : 5;
        }

        private static (int colX, int colY, int colH, int colDx, int colDy, int colDh, int colVector)
            MapColumns(IXLWorksheet s, int subHdrRow, int startCol, int width)
        {
            int endCol = startCol + width - 1;
            int colX = -1, colY = -1, colH = -1, colDx = -1, colDy = -1, colDh = -1, colVector = -1;

            for (int c = startCol; c <= endCol; c++)
            {
                string h = s.Cell(subHdrRow, c).GetString();
                if (colX < 0 && IsX(h)) { colX = c; continue; }
                if (colY < 0 && IsY(h)) { colY = c; continue; }
                if (colH < 0 && IsH(h)) { colH = c; continue; }
                if (colDx < 0 && IsDx(h)) { colDx = c; continue; }
                if (colDy < 0 && IsDy(h)) { colDy = c; continue; }
                if (colDh < 0 && IsDh(h)) { colDh = c; continue; }
                if (colVector < 0 && IsVector(h)) { colVector = c; continue; }
            }

            if (colX < 0) colX = startCol;
            if (colY < 0) colY = startCol + 1;

            if (width == 7)
            {
                if (colH < 0) colH = startCol + 2;
                if (colDx < 0) colDx = startCol + 3;
                if (colDy < 0) colDy = startCol + 4;
                if (colDh < 0) colDh = startCol + 5;
                if (colVector < 0) colVector = startCol + 6;
            }
            else
            {
                colH = -1;
                if (colDx < 0) colDx = startCol + 2;
                if (colDy < 0) colDy = startCol + 3;
                colDh = -1;
                if (colVector < 0) colVector = startCol + 4;
            }

            if (colX < 0 || colY < 0 || colDx < 0 || colDy < 0 || colVector < 0)
                throw new InvalidOperationException("Не удалось сопоставить столбцы блока (X/Y/ΔX/ΔY/Vector).");

            return (colX, colY, colH, colDx, colDy, colDh, colVector);
        }

        private static int FindSubHeaderRow(IXLWorksheet s, int headerRow, int idColumn)
        {
            int last = Math.Min(headerRow + 6, s.LastRowUsed().RowNumber());
            for (int r = headerRow + 1; r <= last; r++)
            {
                var cells = s.Row(r).CellsUsed().ToList();
                if (cells.Count == 0) continue;
                bool hasX = cells.Any(c => IsX(c.GetString()) && c.Address.ColumnNumber != idColumn);
                bool hasY = cells.Any(c => IsY(c.GetString()) && c.Address.ColumnNumber != idColumn);
                if (hasX && hasY) return r;
            }
            return headerRow + 1;
        }

        private static List<int> FindCycleStarts(IXLWorksheet s, int subHdrRow, int idColumn)
        {
            return s.Row(subHdrRow)
                    .CellsUsed()
                    .Where(c => IsX(c.GetString()) && c.Address.ColumnNumber != idColumn)
                    .Select(c => c.Address.ColumnNumber)
                    .OrderBy(c => c)
                    .ToList();
        }

        private static string BuildCycleHeaderLabel(IXLWorksheet s, int startCol, int subHdrRow, int headerRow)
        {
            var parts = new List<string>();
            int r1 = subHdrRow - 1;
            int r2 = subHdrRow - 2;

            if (r2 >= headerRow && !string.IsNullOrWhiteSpace(s.Cell(r2, startCol).GetString()))
                parts.Add(s.Cell(r2, startCol).GetString().Trim());

            if (r1 >= headerRow && !string.IsNullOrWhiteSpace(s.Cell(r1, startCol).GetString()))
                parts.Add(s.Cell(r1, startCol).GetString().Trim());

            return string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        }

        private static (double? val, string raw) ParseCell(IXLCell cell)
        {
            var txt = cell.GetString();
            var (v, raw) = TryParseCell(txt);
            return (v, raw);
        }

        public void LoadWorkbookFromFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                MessageBox.Show("Файл не найден.", "Импорт", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                using var sharedWorkbook = SharedWorkbook.Open(path);
                var wb = sharedWorkbook.Workbook;
                var win = new ImportSelectionWindow(wb);
                if (win.ShowDialog() != true) return;

                dynamic vm = win.DataContext!;
                var wsItem = vm.SelectedWorksheet;
                var objList = vm.Objects;
                if (wsItem == null || objList == null || objList.Count == 0)
                {
                    MessageBox.Show("Не выбраны данные для импорта.", "Импорт", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var sheet = wsItem.Sheet as IXLWorksheet;

                int pickObject = 0;
                int pickCycle = 0;
                try
                {
                    pickObject = win.SelectedObjectIndex;
                    pickCycle = win.SelectedCycleIndex;
                }
                catch { }

                ReadAllObjects(sheet, objList);

                if (pickObject > 0 && _objects.ContainsKey(pickObject))
                {
                    Header.ObjectNumber = pickObject;

                    CycleNumbers.Clear();
                    foreach (var c in _objects[pickObject].Keys.OrderBy(c => c))
                        CycleNumbers.Add(c);

                    if (CycleNumbers.Contains(pickCycle))
                        Header.CycleNumber = pickCycle;
                    else
                        Header.CycleNumber = CycleNumbers.FirstOrDefault();

                    RefreshData();
                }
                UpdateCache();

            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Импорт Excel", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ReadAllObjects(IXLWorksheet sheet, IEnumerable<object> objectItems)
        {
            _objects.Clear();
            _coordsByObject.Clear();
            _cycleHeaders.Clear();
            _hasHByObject.Clear();
            ObjectNumbers.Clear();

            // objectItems приходят из ImportSelectionVM.Objects (ObjectItem: HeaderRow, IdColumn, Index)
            foreach (var obj in objectItems)
            {
                int headerRow = (int)obj.GetType().GetProperty("HeaderRow")!.GetValue(obj)!;
                int idColumn = (int)obj.GetType().GetProperty("IdColumn")!.GetValue(obj)!;
                int index = (int)obj.GetType().GetProperty("Index")!.GetValue(obj)!;

                int subHdrRow = FindSubHeaderRow(sheet, headerRow, idColumn);
                var cycleStarts = FindCycleStarts(sheet, subHdrRow, idColumn);
                if (cycleStarts.Count == 0) continue;

                var cyclesDict = new Dictionary<int, List<MeasurementRow>>();
                var coordsDict = new Dictionary<int, List<CoordRow>>();
                var hasHSet = new HashSet<int>();

                int dataFirst = subHdrRow + 1;
                int dataLast = sheet.LastRowUsed().RowNumber();

                for (int cycIdx = 0; cycIdx < cycleStarts.Count; cycIdx++)
                {
                    int startCol = cycleStarts[cycIdx];
                    int width = DetermineBlockWidth(sheet, subHdrRow, startCol); // 5 или 7
                    bool hasH = width == 7;
                    if (hasH) hasHSet.Add(cycIdx + 1);

                    string cycLabel = BuildCycleHeaderLabel(sheet, startCol, subHdrRow, headerRow);
                    if (!string.IsNullOrWhiteSpace(cycLabel))
                        _cycleHeaders[cycIdx + 1] = cycLabel;

                    var cols = MapColumns(sheet, subHdrRow, startCol, width);
                    int colX = cols.colX;
                    int colY = cols.colY;
                    int colH = cols.colH;
                    int colDx = cols.colDx;
                    int colDy = cols.colDy;
                    int colDh = cols.colDh;
                    int colVector = cols.colVector;

                    var rows = new List<MeasurementRow>();
                    var coordsForCycle = new List<CoordRow>();
                    int blanks = 0;

                    for (int r = dataFirst; r <= dataLast; r++)
                    {
                        string idText = sheet.Cell(r, idColumn).GetString().Trim();

                        if (string.IsNullOrEmpty(idText))
                        {
                            blanks++;
                            if (blanks >= 3) break;
                            continue;
                        }
                        blanks = 0;

                        var (x, xRaw) = ParseCell(sheet.Cell(r, colX));
                        var (y, yRaw) = ParseCell(sheet.Cell(r, colY));
                        var (h, hRaw) = colH >= 0 ? ParseCell(sheet.Cell(r, colH)) : (null, "");
                        var (dx, dxRaw) = ParseCell(sheet.Cell(r, colDx));
                        var (dy, dyRaw) = ParseCell(sheet.Cell(r, colDy));
                        var (dh, dhRaw) = colDh >= 0 ? ParseCell(sheet.Cell(r, colDh)) : (null, "");
                        var (vec, vecRaw) = ParseCell(sheet.Cell(r, colVector));

                        bool allEmpty =
                            (x is null && y is null && (h is null) && dx is null && dy is null && (dh is null) && vec is null) &&
                            string.IsNullOrWhiteSpace(xRaw) && string.IsNullOrWhiteSpace(yRaw) &&
                            string.IsNullOrWhiteSpace(hRaw) && string.IsNullOrWhiteSpace(dxRaw) &&
                            string.IsNullOrWhiteSpace(dyRaw) && string.IsNullOrWhiteSpace(dhRaw) &&
                            string.IsNullOrWhiteSpace(vecRaw);

                        if (allEmpty) continue;

                        if (dh.HasValue) dh = Math.Round(dh.Value, 1);
                        if (vec.HasValue) vec = Math.Round(vec.Value, 1);

                        var mr = new MeasurementRow
                        {
                            Id = idText,
                            Mark = h,
                            MarkRaw = hRaw,
                            X = x,
                            Y = y,
                            H = h,
                            Dx = dx,
                            Dy = dy,
                            Dh = dh,
                            Vector = vec,
                            Settl = dh,
                            SettlRaw = dhRaw,
                            Total = vec,
                            TotalRaw = vecRaw,
                            Cycle = cycIdx + 1
                        };

                        rows.Add(mr);

                        if (x.HasValue && y.HasValue)
                            coordsForCycle.Add(new CoordRow { X = x.Value, Y = y.Value });
                    }

                    cyclesDict[cycIdx + 1] = rows;
                    if (coordsForCycle.Count > 0)
                        coordsDict[cycIdx + 1] = coordsForCycle;
                }

                _objects[index] = cyclesDict;
                _coordsByObject[index] = coordsDict;
                _hasHByObject[index] = hasHSet;
                ObjectNumbers.Add(index);
            }

            if (ObjectNumbers.Count == 0)
            {
                MessageBox.Show("Не удалось распознать данные по выбранному объекту/листу.", "Импорт", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Выбор по умолчанию
            if (!_objects.ContainsKey(Header.ObjectNumber))
                Header.ObjectNumber = ObjectNumbers.First();

            CycleNumbers.Clear();
            if (_objects.TryGetValue(Header.ObjectNumber, out var cycles))
            {
                foreach (var c in cycles.Keys.OrderBy(c => c))
                    CycleNumbers.Add(c);
                if (!CycleNumbers.Contains(Header.CycleNumber)) Header.CycleNumber = CycleNumbers.FirstOrDefault();
            }

            RefreshData();
        }

        private void UpdateCache()
        {
            ObjectNumbers.Clear();
            foreach (var k in _objects.Keys.OrderBy(k => k))
                ObjectNumbers.Add(k);

            CycleNumbers.Clear();
            if (_objects.TryGetValue(Header.ObjectNumber, out var cycles))
            {
                foreach (var k in cycles.Keys.OrderBy(k => k))
                    CycleNumbers.Add(k);
            }

            RefreshData();
        }

        private void RefreshData()
        {
            DataRows.Clear();
            if (_objects.TryGetValue(Header.ObjectNumber, out var cycles) &&
                cycles.TryGetValue(Header.CycleNumber, out var rows))
            {
                foreach (var r in rows) DataRows.Add(r);
            }
            CoordRows.Clear();
            if (_coordsByObject.TryGetValue(Header.ObjectNumber, out var coordsByCycle) &&
                coordsByCycle.TryGetValue(Header.CycleNumber, out var coords))
            {
                foreach (var p in coords)
                    CoordRows.Add(new CoordRow { X = p.X * CoordScale, Y = p.Y * CoordScale });
            }

            SelectedCycleHeader =
                _cycleHeaders.TryGetValue(Header.CycleNumber, out var h) ? h : string.Empty;

            HasHeight = _hasHByObject.TryGetValue(Header.ObjectNumber, out var set) &&
                        set.Contains(Header.CycleNumber);

            OnPropertyChanged(nameof(ShowPlaceholder));
        }

        public IReadOnlyDictionary<int, Dictionary<int, List<MeasurementRow>>> GetObjects() => _objects;

        public static bool AskInt(string prompt, int min, int max, out int value)
        {
            value = 0;
            string s = Interaction.InputBox(prompt, "Выбор", min.ToString());
            return int.TryParse(s, out value) && value >= min && value <= max;
        }

        private sealed class SharedWorkbook : IDisposable
        {
            public XLWorkbook Workbook { get; }
            private readonly FileStream _stream;

            private SharedWorkbook(string path)
            {
                _stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                try
                {
                    Workbook = new XLWorkbook(_stream);
                }
                catch
                {
                    _stream.Dispose();
                    throw;
                }
            }

            public static SharedWorkbook Open(string path) => new SharedWorkbook(path);

            public void Dispose()
            {
                Workbook.Dispose();
                _stream.Dispose();
            }
        }
    }

    // Простейшие пользовательские настройки в %AppData%\Osadka\user.settings.json
    internal static class UserSettings
    {
        private static readonly string Dir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HorDeform");
        private static readonly string FilePath = Path.Combine(Dir, "user.settings.json");

        internal class DataModel
        {
            public string? TemplatePath { get; set; }
        }

        public static DataModel Data { get; private set; } = Load();

        private static DataModel Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<DataModel>(File.ReadAllText(FilePath)) ?? new DataModel();
            }
            catch { }
            return new DataModel();
        }

        public static void Save()
        {
            Directory.CreateDirectory(Dir);
            var json = JsonSerializer.Serialize(Data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FilePath, json);
        }
    }
}
