using Osadka.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Osadka.Services
{
    /// <summary>Экстремум с перечнем марок.</summary>
    public record Extremum(double Value, IReadOnlyList<string> Ids);

    /// <summary>Данные для общего отчёта.</summary>
    public record GeneralReportData(
        Extremum VectorAbs,
        Extremum DxAbs,
        Extremum DyAbs,
        Extremum? DhMin,
        Extremum? DhMax,
        IReadOnlyList<string> NoAccessIds,
        IReadOnlyList<string> NewIds,
        IReadOnlyList<string> DestroyedIds,
        IReadOnlyList<string> ExceedXIds,
        IReadOnlyList<string> ExceedYIds,
        IReadOnlyList<string> ExceedVectorIds,
        IReadOnlyList<string> ExceedHIds);

    public sealed class GeneralReportService
    {
        /// <param name="horLimit">Порог «Расчётное горизонтальное превышение, мм».</param>
        public GeneralReportData Build(IEnumerable<MeasurementRow> rows, double horLimit)
        {
            horLimit = Math.Abs(horLimit);

            var vec = rows.Where(r => r.Vector is { } v && !double.IsNaN(v)).ToList();
            var dxs = rows.Where(r => r.Dx is { } v && !double.IsNaN(v)).ToList();
            var dys = rows.Where(r => r.Dy is { } v && !double.IsNaN(v)).ToList();
            var dhs = rows.Where(r => r.Dh is { } v && !double.IsNaN(v)).ToList();

            Extremum GetMaxAbs(List<MeasurementRow> src, Func<MeasurementRow, double?> sel)
            {
                if (src.Count == 0) return new Extremum(double.NaN, Array.Empty<string>());
                double maxAbs = src.Max(r => Math.Abs(sel(r)!.Value));
                var winners = src.Where(r => Math.Abs(Math.Abs(sel(r)!.Value) - maxAbs) < 1e-9).ToList();
                double signed = winners.First().Let(w => sel(w)!.Value);
                var ids = winners.Select(r => r.Id).ToList();
                return new Extremum(Math.Round(signed, 4), ids);
            }

            Extremum GetMin(List<MeasurementRow> src, Func<MeasurementRow, double?> sel)
            {
                if (src.Count == 0) return new Extremum(double.NaN, Array.Empty<string>());
                double min = src.Min(r => sel(r)!.Value);
                var ids = src.Where(r => Math.Abs(sel(r)!.Value - min) < 1e-9).Select(r => r.Id).ToList();
                return new Extremum(Math.Round(min, 4), ids);
            }

            Extremum GetMax(List<MeasurementRow> src, Func<MeasurementRow, double?> sel)
            {
                if (src.Count == 0) return new Extremum(double.NaN, Array.Empty<string>());
                double max = src.Max(r => sel(r)!.Value);
                var ids = src.Where(r => Math.Abs(sel(r)!.Value - max) < 1e-9).Select(r => r.Id).ToList();
                return new Extremum(Math.Round(max, 4), ids);
            }

            var vecAbs = GetMaxAbs(vec, r => r.Vector);
            var dxAbs = GetMaxAbs(dxs, r => r.Dx);
            var dyAbs = GetMaxAbs(dys, r => r.Dy);

            Extremum? dhMin = dhs.Count > 0 ? GetMin(dhs, r => r.Dh) : null;
            Extremum? dhMax = dhs.Count > 0 ? GetMax(dhs, r => r.Dh) : null;

            // Текстовые статусы (как раньше — по эвристике текстовых пометок в исходных столбцах)
            var noAccess = rows.Where(r => r.MarkRaw.Contains("нет доступ", StringComparison.OrdinalIgnoreCase))
                               .Select(r => r.Id).ToList();
            var @new = rows.Where(r => r.SettlRaw.Contains("нов", StringComparison.OrdinalIgnoreCase))
                               .Select(r => r.Id).ToList();
            var destroyed = rows.Where(r => r.MarkRaw.Contains("унич", StringComparison.OrdinalIgnoreCase))
                               .Select(r => r.Id).ToList();

            // Превышения по одному порогу (берём модуль)
            static bool ExceededAbs(double x, double lim) => Math.Abs(x) > lim;
            var exX = dxs.Where(r => ExceededAbs(r.Dx!.Value, horLimit)).Select(r => r.Id).ToList();
            var exY = dys.Where(r => ExceededAbs(r.Dy!.Value, horLimit)).Select(r => r.Id).ToList();
            var exV = vec.Where(r => ExceededAbs(r.Vector!.Value, horLimit)).Select(r => r.Id).ToList();
            var exH = dhs.Where(r => ExceededAbs(r.Dh!.Value, horLimit)).Select(r => r.Id).ToList();

            return new GeneralReportData(vecAbs, dxAbs, dyAbs, dhMin, dhMax,
                                         noAccess, @new, destroyed,
                                         exX, exY, exV, exH);
        }
    }

    internal static class _Ext
    {
        public static TResult Let<T, TResult>(this T self, Func<T, TResult> f) => f(self);
    }
}
