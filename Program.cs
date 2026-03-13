namespace LegacyConsolePackEditor;

static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main()
    {
        // Global exception handlers to avoid silent crashes.
        Application.ThreadException += (sender, e) =>
        {
            MessageBox.Show($"Unhandled UI exception:\n{e.Exception}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        };

        AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                MessageBox.Show($"Unhandled exception:\n{ex}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };

        try
        {
            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            ApplicationConfiguration.Initialize();
            Application.Run(new Form1());
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Unhandled exception:\n{ex}", "Startup Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Environment.Exit(1);
        }
    }
}