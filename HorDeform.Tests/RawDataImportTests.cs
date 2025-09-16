using System.Linq;
using System.Reflection;
using ClosedXML.Excel;
using Osadka.ViewModels;
using Xunit;

namespace HorDeform.Tests;

public class RawDataImportTests
{
    [Fact]
    public void ReadAllObjects_PreservesTextualValuesFromSourceSheet()
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Data");

        ws.Cell(1, 1).Value = "ID";
        ws.Cell(2, 1).Value = "ID";
        ws.Cell(2, 2).Value = "X";
        ws.Cell(2, 3).Value = "Y";
        ws.Cell(2, 4).Value = "H";
        ws.Cell(2, 5).Value = "ΔX";
        ws.Cell(2, 6).Value = "ΔY";
        ws.Cell(2, 7).Value = "ΔH";
        ws.Cell(2, 8).Value = "Vector";

        ws.Cell(3, 1).Value = "P1";
        ws.Cell(3, 4).Value = "нет доступа";
        ws.Cell(3, 5).Value = 1.2;
        ws.Cell(3, 6).Value = 3.4;
        ws.Cell(3, 7).Value = "новая";
        ws.Cell(3, 8).Value = "5.6";

        var viewModel = new RawDataViewModel();

        var importMethod = typeof(RawDataViewModel)
            .GetMethod("ReadAllObjects", BindingFlags.Instance | BindingFlags.NonPublic)!;

        var objectItems = new[]
        {
            new { HeaderRow = 1, IdColumn = 1, Index = 1 }
        };

        importMethod.Invoke(viewModel, new object[] { ws, objectItems });

        var row = viewModel.Objects[1][1].Single();

        Assert.Equal("нет доступа", row.MarkRaw);
        Assert.Equal("новая", row.SettlRaw);
        Assert.Equal("5.6", row.TotalRaw);
    }
}
