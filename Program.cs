namespace HotasBridge;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        var settings = AppSettings.Load();
        Theme.SetTextScale(settings.TextScale); // before any control picks up a Theme font
        Application.Run(new MainForm(settings));
    }
}
