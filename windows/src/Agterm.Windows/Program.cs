namespace Agterm.Windows;

public static class Program
{
    [MTAThread]
    public static void Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Microsoft.UI.Xaml.Application.Start(_ => new App());
    }
}
