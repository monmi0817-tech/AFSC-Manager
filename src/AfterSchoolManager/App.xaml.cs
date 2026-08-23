using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using AfterSchoolManager.Services;
using AfterSchoolManager.Utilities;
using AfterSchoolManager.Views;

namespace AfterSchoolManager;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        base.OnStartup(e);

        try
        {
            AppPaths.EnsureDirectories();
            var database=new DatabaseService(AppPaths.DatabasePath);
            if(File.Exists(AppPaths.DatabasePath)&&new FileInfo(AppPaths.DatabasePath).Length>0)
            {
                var backup=new BackupService(AppPaths.DatabasePath);var version=backup.ValidateDatabase(AppPaths.DatabasePath);
                if(version<4)backup.CreateBackup(Path.Combine(AppPaths.RecoveryDirectory,$"스키마업데이트전_v{version}_{DateTime.Now:yyyyMMdd_HHmmss}.afbackup"));
            }
            database.Initialize();
            var window = new MainWindow();
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            ShowFatalError("프로그램을 시작하지 못했습니다.", ex);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        ShowFatalError("화면을 처리하는 중 오류가 발생했습니다.", e.Exception);
    }

    private void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex) WriteLog(ex);
    }

    private void ShowFatalError(string title, Exception ex)
    {
        WriteLog(ex);
        MessageBox.Show($"{title}\n\n{ex.Message}\n\n오류 기록:\n{AppPaths.LogPath}",
            "방과후 통합 관리 실행 오류", MessageBoxButton.OK, MessageBoxImage.Error);
        Shutdown(-1);
    }

    private static void WriteLog(Exception ex)
    {
        try
        {
            AppPaths.EnsureDirectories();
            var entry = new StringBuilder()
                .AppendLine(new string('=', 72))
                .AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
                .AppendLine(ex.ToString())
                .ToString();
            File.AppendAllText(AppPaths.LogPath, entry, Encoding.UTF8);
        }
        catch
        {
            // 로깅 실패가 원래 오류 처리를 방해하지 않게 한다.
        }
    }
}
