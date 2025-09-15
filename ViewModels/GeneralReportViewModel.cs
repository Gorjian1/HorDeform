using ClosedXML.Excel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Osadka.Services;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;

namespace Osadka.ViewModels
{
    public partial class GeneralReportViewModel : ObservableObject
    {
        public IRelayCommand CalculateCommand { get; }
        public IRelayCommand OpenTemplate { get; }
        public ReportOutputSettings Settings { get; } = new();
        [ObservableProperty] private string _exceedXDisplay = string.Empty;
        [ObservableProperty] private string _exceedYDisplay = string.Empty;
        [ObservableProperty] private string _exceedVectorDisplay = string.Empty;
        [ObservableProperty] private string _exceedHDisplay = string.Empty;

        [ObservableProperty] private GeneralReportData? _report;

        private readonly RawDataViewModel _raw;
        private readonly GeneralReportService _svc;

        public GeneralReportViewModel(RawDataViewModel raw, GeneralReportService svc)
        {
            _raw = raw ?? throw new ArgumentNullException(nameof(raw));
            _svc = svc ?? throw new ArgumentNullException(nameof(svc));

            CalculateCommand = new RelayCommand(Recalc);
            OpenTemplate = new RelayCommand(OpenTemplateFile);

            _raw.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(RawDataViewModel.DataRows)
                    or nameof(RawDataViewModel.CoordRows))
                    Recalc();
            };
            _raw.DataRows.CollectionChanged += (_, __) => Recalc();
            _raw.CoordRows.CollectionChanged += (_, __) => Recalc();

            _raw.Header.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(CycleHeader.HorNomen)
                    or nameof(CycleHeader.CycleNumber)
                    or nameof(CycleHeader.ObjectNumber))
                    Recalc();
            };

            Recalc();
        }

        private void OpenTemplateFile()
        {
            string exeDir = AppContext.BaseDirectory;
            string template = Path.Combine(exeDir, "template.xlsx");
            if (!File.Exists(template))
            {
                MessageBox.Show("template.xlsx не найден", "Экспорт",
                                MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            Process.Start(new ProcessStartInfo(template) { UseShellExecute = true });
        }

        private void Recalc()
        {
            double horLimit = Math.Abs(_raw.Header.HorNomen ?? 0);
            Report = _svc.Build(_raw.DataRows, horLimit);

            if (Report is { } rep)
            {
                ExceedXDisplay = rep.ExceedXIds.Count > 0 ? string.Join(", ", rep.ExceedXIds) : string.Empty;
                ExceedYDisplay = rep.ExceedYIds.Count > 0 ? string.Join(", ", rep.ExceedYIds) : string.Empty;
                ExceedVectorDisplay = rep.ExceedVectorIds.Count > 0 ? string.Join(", ", rep.ExceedVectorIds) : string.Empty;
                ExceedHDisplay = rep.ExceedHIds.Count > 0 ? string.Join(", ", rep.ExceedHIds) : string.Empty;
            }
            else
            {
                ExceedXDisplay = ExceedYDisplay = ExceedVectorDisplay = ExceedHDisplay = string.Empty;
            }
        }
    }
}
