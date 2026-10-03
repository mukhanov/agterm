using Agterm.Terminal;
using Microsoft.UI.Xaml;

namespace Agterm.Windows;

public partial class App : Application
{
    public static MainWindow? Window { get; private set; }

    public App()
    {
        InitializeComponent();
        Application.Current.UnhandledException += (_, e) =>
        {
            Program.UiLog("xaml unhandled: " + e.Message + Environment.NewLine + e.Exception);
            e.Handled = true;
        };
        System.AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Program.UiLog("domain unhandled: " + e.ExceptionObject);
        TerminalDiagnostics.Sink = m => Program.UiLog(m);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            Window = new MainWindow();
            Window.Activate();
        }
        catch (Exception e)
        {
            Program.UiLog("OnLaunched: " + e);
            throw;
        }
    }
}
