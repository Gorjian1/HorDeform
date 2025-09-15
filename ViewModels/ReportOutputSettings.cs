// File: ViewModels/ReportOutputSettings.cs
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;

namespace Osadka.ViewModels
{
    public class BlockSetting : ObservableObject
    {
        private bool _isEnabled = true;
        public string Key { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public List<string> Tags { get; set; } = new();

        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetProperty(ref _isEnabled, value);
        }
    }
    public class ReportOutputSettings : ObservableObject
    {
        public ObservableCollection<BlockSetting> Blocks { get; } = new();

        public ReportOutputSettings()
        {
            Blocks.Add(new BlockSetting
            {
                Key = "MaxCycle",
                Tags = new() { "/цикл" },
                IsEnabled = true
            });
            Blocks.Add(new BlockSetting
            {
                Key = "MaxHor",
                Tags = new() { "/горпревыш" },
                IsEnabled = true
            });
            Blocks.Add(new BlockSetting
            {
                Key = "DeltaX",
                Tags = new() { "/дельтаXмакс", "/дельтаXмаксId" },
                IsEnabled = true
            });
            Blocks.Add(new BlockSetting
            {
                Key = "DeltaY",
                Tags = new() { "/дельтаYмакс", "/дельтаYмаксId" },
                IsEnabled = true
            });
            Blocks.Add(new BlockSetting
            {
                Key = "DeltaVector",
                Tags = new() { "/векторабс", "/векторабсId", },
                IsEnabled = true
            });
            Blocks.Add(new BlockSetting
            {
                Key = "DeltaVertical",
                Tags = new() { "/дельтаHэкстреммин", "/дельтаHэкстремминId", "/дельтаHэкстреммакс", "/дельтаHэкстреммаксId" },
                IsEnabled = true
            });
            Blocks.Add(new BlockSetting
            {
                Key = "Exceeds",
                Tags = new() { "/дельтаXпревыш", "/дельтаYпревыш", "/векторпревыш", "/дельтаHпревыш" },
                IsEnabled = true
            });
        }
            public BlockSetting? FindBlock(string keyOrTitle)
    {
        if (string.IsNullOrWhiteSpace(keyOrTitle)) return null;
        var key = keyOrTitle.Trim();
        var byKey = Blocks.FirstOrDefault(b => string.Equals(b.Key, key, StringComparison.OrdinalIgnoreCase));
        if (byKey != null) return byKey;
        return Blocks.FirstOrDefault(b => string.Equals(b.Title, key, StringComparison.OrdinalIgnoreCase));
    }
        public HashSet<string> GetDisabledTags()
            => new HashSet<string>(Blocks.Where(b => !b.IsEnabled).SelectMany(b => b.Tags).Select(t => t.Trim()).Where(t => t.Length > 0));
    }
}
