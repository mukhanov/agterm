using System.IO;

namespace Agterm.Windows;

public static class Program
{
    [MTAThread]
    public static void Main(string[] args)
    {
        try
        {
            WinRT.ComWrappersSupport.InitializeComWrappers();
            Microsoft.UI.Xaml.Application.Start(_ => new App());
        }
        catch (Exception e)
        {
            UiLog("Main: " + e);
            throw;
        }
    }

    internal static void UiLog(string message) =>
        File.AppendAllText(Path.Combine(Path.GetTempPath(), "agterm-ui.log"),
            DateTime.Now.ToString("HH:mm:ss.fff ") + message + Environment.NewLine);
}
