using System.Windows;
using System.Windows.Threading;
using SekiroModManager.App.Theme;
using SekiroModManager.App.Views;

namespace SekiroModManager.App;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            try
            {
                SetCurrentProcessExplicitAppUserModelID("Sekiro.ModManager.App");
            }
            catch { }

            ThemeManager.Initialize();
            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"启动时发生错误：\n{ex.Message}\n\n详细堆栈：\n{ex.StackTrace}", "启动错误", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show($"应用程序发生未捕获异常：\n{e.Exception.Message}\n\n{e.Exception.StackTrace}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            MessageBox.Show($"严重错误：\n{ex.Message}\n\n{ex.StackTrace}", "严重错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [System.Runtime.InteropServices.DllImport("shell32.dll", SetLastError = true)]
    private static extern void SetCurrentProcessExplicitAppUserModelID(
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] string AppID);
}
