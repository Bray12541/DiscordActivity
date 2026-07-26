namespace DiscordActivity;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DiscordActivity");
        CrashLogger.Initialize(dataDirectory);
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, args) =>
        {
            CrashLogger.LogError("Unhandled Windows Forms exception", args.Exception);
            MessageBox.Show(
                $"Discord Activity encountered an unexpected error and logged it to:\n{CrashLogger.LogDirectory}\n\n{args.Exception.Message}",
                "Discord Activity error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var exception = args.ExceptionObject as Exception
                            ?? new Exception(args.ExceptionObject?.ToString() ?? "Unknown fatal error");
            CrashLogger.RecordCrash("Unhandled application exception", exception);
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            CrashLogger.LogError("Unobserved background task exception", args.Exception);
            args.SetObserved();
        };

        using var mutex = new Mutex(true, "DiscordActivity.SingleInstance", out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show("Discord Activity is already running.", "Discord Activity",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            Application.Run(new TrayApplicationContext());
            CrashLogger.LogInfo("Discord Activity exited normally.");
        }
        catch (Exception ex)
        {
            CrashLogger.RecordCrash("Fatal startup or application-loop exception", ex);
            MessageBox.Show(
                $"Discord Activity could not continue. A crash log was saved to:\n{CrashLogger.LogDirectory}\n\n{ex.Message}",
                "Discord Activity crashed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
