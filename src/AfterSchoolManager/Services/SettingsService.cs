using System.IO;
using System.Text.Json;
using AfterSchoolManager.Models;
using AfterSchoolManager.Utilities;

namespace AfterSchoolManager.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions=new(){WriteIndented=true};

    public AppSettingsItem Load()
    {
        try
        {
            if(File.Exists(AppPaths.SettingsPath))
            {
                var loaded=JsonSerializer.Deserialize<AppSettingsItem>(File.ReadAllText(AppPaths.SettingsPath),JsonOptions);
                if(loaded is not null)return ApplyDefaults(loaded);
            }
        }
        catch
        {
            // 손상된 설정 파일은 업무 DB에 영향을 주지 않고 기본값으로 복구한다.
        }
        return ApplyDefaults(new AppSettingsItem());
    }

    public void Save(AppSettingsItem settings)
    {
        AppPaths.EnsureDirectories();ApplyDefaults(settings);
        var temp=AppPaths.SettingsPath+".tmp";
        File.WriteAllText(temp,JsonSerializer.Serialize(settings,JsonOptions));
        File.Move(temp,AppPaths.SettingsPath,true);
    }

    private static AppSettingsItem ApplyDefaults(AppSettingsItem settings)
    {
        if(string.IsNullOrWhiteSpace(settings.BackupDirectory))
            settings.BackupDirectory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"방과후 통합관리 백업");
        return settings;
    }
}
