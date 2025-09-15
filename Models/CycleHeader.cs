using CommunityToolkit.Mvvm.ComponentModel;
using System.ComponentModel;

public partial class CycleHeader : ObservableObject, IDataErrorInfo
{
    [ObservableProperty] private int _cycleNumber; 
    [ObservableProperty] private int _objectNumber;
    [ObservableProperty] private int _totalCycles; 
    [ObservableProperty] private int _totalObjects; 

    [ObservableProperty] private double? _horNomen;

    [ObservableProperty] private bool _isCycleEditable = true;

    public string Error => null!;
    public string this[string col] => col switch
    {
        nameof(CycleNumber) when CycleNumber <= 0 => "Номер цикла > 0",
        _ => string.Empty
    };
}
