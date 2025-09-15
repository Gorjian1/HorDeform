using System;
using System.IO;
using System.Text.Json;

namespace Osadka.Services
{
    public static class UserSettings
    {
        private static readonly string Dir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HorDeform");
        private static readonly string FilePath = Path.Combine(Dir, "user.settings.json");

        public static UserSettingsModel Data { get; private set; } = new();

        public static void Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var json = File.ReadAllText(FilePath);
                    Data = JsonSerializer.Deserialize<UserSettingsModel>(json) ?? new UserSettingsModel();
                }
            }
            catch
            {
                Data = new UserSettingsModel();
            }
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var json = JsonSerializer.Serialize(Data, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(FilePath, json);
            }
            catch
            {
                // тихо игнорируем — настройки не критичны для работы
            }
        }
    }

    public class UserSettingsModel
    {
        public string? TemplatePath { get; set; }
    }
}
