namespace GlucoseTray;

public class AppWrapper : ApplicationContext
{
    public AppWrapper(AppRunner app)
    {
        // app.Start() is async, so exceptions it throws happen after this constructor returns and
        // are never caught by a try/catch wrapped around the call itself — they must be awaited here
        // to be observed at all. Without this, a fatal error silently kills the polling loop while the
        // tray icon and message pump keep running, leaving a frozen zombie process with no indication anything failed.
        _ = RunAsync(app);
    }

    private async Task RunAsync(AppRunner app)
    {
        try
        {
            await app.Start();
        }
        catch (Exception)
        {
            Environment.Exit(1);
        }
    }
}
