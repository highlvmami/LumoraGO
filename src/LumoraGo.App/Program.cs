using Velopack;

namespace LumoraGo.App;

public static class Program
{
    /// <summary>Kurulum/güncelleme sırasında Velopack uygulamayı özel argümanlarla çağırır; bu yüzden ilk iş VelopackApp çalışır.</summary>
    internal static string? RestartedAfterUpdate { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build()
            .OnRestarted(version => RestartedAfterUpdate = version.ToString())
            .Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
