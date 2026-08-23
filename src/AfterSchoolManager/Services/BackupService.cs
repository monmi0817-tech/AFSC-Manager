using System.IO;
using System.IO.Compression;
using System.Text.Json;
using AfterSchoolManager.Utilities;
using Microsoft.Data.Sqlite;

namespace AfterSchoolManager.Services;

public sealed class BackupService
{
    private readonly string _databasePath;
    public BackupService(string databasePath)=>_databasePath=databasePath;

    public string CreateBackup(string destinationPath)
    {
        if(string.IsNullOrWhiteSpace(destinationPath))throw new ArgumentException("백업파일 저장 위치가 올바르지 않습니다.");
        var directory=Path.GetDirectoryName(destinationPath);if(string.IsNullOrWhiteSpace(directory))throw new ArgumentException("백업 폴더를 선택하세요.");
        Directory.CreateDirectory(directory);var tempDb=Path.Combine(Path.GetTempPath(),$"afterschool-backup-{Guid.NewGuid():N}.db");
        var tempPackage=Path.Combine(directory,$".{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            BackupDatabase(_databasePath,tempDb);var schemaVersion=ValidateDatabase(tempDb);
            using(var archive=ZipFile.Open(tempPackage,ZipArchiveMode.Create))
            {
                archive.CreateEntryFromFile(tempDb,"database.sqlite",CompressionLevel.Optimal);
                var manifest=archive.CreateEntry("manifest.json",CompressionLevel.Optimal);
                using(var writer=new StreamWriter(manifest.Open()))writer.Write(JsonSerializer.Serialize(new
                {
                    format="AfterSchoolIntegratedManagerBackup",formatVersion=1,createdAt=DateTime.Now,
                    schemaVersion,applicationVersion=typeof(BackupService).Assembly.GetName().Version?.ToString()??"0.0.0"
                },new JsonSerializerOptions{WriteIndented=true}));
                if(File.Exists(AppPaths.SettingsPath))archive.CreateEntryFromFile(AppPaths.SettingsPath,"settings.json",CompressionLevel.Optimal);
            }
            File.Move(tempPackage,destinationPath,true);
            return destinationPath;
        }
        finally{TryDelete(tempDb);TryDelete(tempPackage);}
    }

    public string RestoreBackup(string backupPath,string safetyBackupDirectory)
    {
        if(!File.Exists(backupPath))throw new FileNotFoundException("복원할 백업파일을 찾을 수 없습니다.",backupPath);
        Directory.CreateDirectory(safetyBackupDirectory);
        var safetyPath=Path.Combine(safetyBackupDirectory,$"복원전_자동백업_{DateTime.Now:yyyyMMdd_HHmmss}.afbackup");
        CreateBackup(safetyPath);
        var tempDb=Path.Combine(Path.GetTempPath(),$"afterschool-restore-{Guid.NewGuid():N}.db");
        try
        {
            using(var archive=ZipFile.OpenRead(backupPath))
            {
                var manifest=archive.GetEntry("manifest.json")??throw new InvalidDataException("방과후 통합관리 백업파일이 아닙니다.");
                using var manifestStream=manifest.Open();
                using(var document=JsonDocument.Parse(manifestStream))
                {
                    if(!document.RootElement.TryGetProperty("format",out var format)||format.GetString()!="AfterSchoolIntegratedManagerBackup")
                        throw new InvalidDataException("지원하지 않는 백업파일 형식입니다.");
                    if(document.RootElement.TryGetProperty("formatVersion",out var formatVersion)&&formatVersion.GetInt32()>1)
                        throw new InvalidDataException("현재 프로그램보다 새로운 백업파일 형식입니다.");
                }
                var database=archive.GetEntry("database.sqlite")??throw new InvalidDataException("백업파일에 업무 DB가 없습니다.");
                using var source=database.Open();using var target=File.Create(tempDb);source.CopyTo(target);
            }
            var schemaVersion=ValidateDatabase(tempDb);if(schemaVersion>4)throw new InvalidDataException("현재 프로그램보다 새로운 DB 버전의 백업입니다. 프로그램을 먼저 업데이트하세요.");
            BackupDatabase(tempDb,_databasePath);ValidateDatabase(_databasePath);
            return safetyPath;
        }
        catch
        {
            if(File.Exists(safetyPath))
            {
                try
                {
                    using(var archive=ZipFile.OpenRead(safetyPath))
                    {
                        var database=archive.GetEntry("database.sqlite");if(database is null)throw new InvalidDataException();
                        using var source=database.Open();using var target=File.Create(tempDb);source.CopyTo(target);
                    }
                    BackupDatabase(tempDb,_databasePath);
                }
                catch{}
            }
            throw;
        }
        finally{TryDelete(tempDb);}
    }

    public int ValidateDatabase(string path)
    {
        var cs=new SqliteConnectionStringBuilder{DataSource=path,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString();using var connection=new SqliteConnection(cs);connection.Open();
        using(var check=connection.CreateCommand()){check.CommandText="PRAGMA integrity_check;";var result=Convert.ToString(check.ExecuteScalar());if(!string.Equals(result,"ok",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("SQLite 무결성 검사에 실패했습니다: "+result);}
        var required=new[]{"student","workspace","support_eligibility","department","enrollment","charge","settlement","settlement_allocation","change_history"};
        foreach(var table in required){using var cmd=connection.CreateCommand();cmd.CommandText="SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name;";cmd.Parameters.AddWithValue("$name",table);if(Convert.ToInt32(cmd.ExecuteScalar())!=1)throw new InvalidDataException($"필수 데이터 테이블이 없습니다: {table}");}
        using var version=connection.CreateCommand();version.CommandText="SELECT COALESCE(MAX(version),0) FROM schema_info;";return Convert.ToInt32(version.ExecuteScalar());
    }

    private static void BackupDatabase(string sourcePath,string destinationPath)
    {
        // 임시 백업 DB는 곧바로 ZIP에 넣거나 삭제하므로 연결 풀에 파일 핸들이
        // 남지 않게 한다. Windows에서는 풀링된 핸들이 남으면 파일 잠금 오류가 난다.
        var sourceCs=new SqliteConnectionStringBuilder{DataSource=sourcePath,Mode=SqliteOpenMode.ReadWrite,ForeignKeys=true,Pooling=false}.ToString();
        var destinationCs=new SqliteConnectionStringBuilder{DataSource=destinationPath,Mode=SqliteOpenMode.ReadWriteCreate,ForeignKeys=true,Pooling=false}.ToString();
        using var source=new SqliteConnection(sourceCs);using var destination=new SqliteConnection(destinationCs);source.Open();destination.Open();source.BackupDatabase(destination);
    }

    private static void TryDelete(string path){try{if(File.Exists(path))File.Delete(path);}catch{}}
}
