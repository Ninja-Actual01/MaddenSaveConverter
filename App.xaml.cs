using System.Windows;

namespace MaddenSaveConverter;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Contains("--selftest"))
        {
            Shutdown(SelfTest.Run(e.Args));
            return;
        }
        if (e.Args.Contains("--convert"))
        {
            Shutdown(Cli.RunConvert(e.Args));
            return;
        }
        if (e.Args.Contains("--dump-fields"))
        {
            Shutdown(Cli.RunDumpFields(e.Args));
            return;
        }
        if (e.Args.Contains("--compare-inner"))
        {
            Shutdown(Cli.RunCompareInner(e.Args));
            return;
        }
        BootSound.Play();
        PlayIntroIfNeeded();
        base.OnStartup(e);
    }

    // Play the intro video only when explicitly enabled by the user (off by default).
    static void PlayIntroIfNeeded()
    {
        try
        {
            bool shouldPlay = AppSettings.HasIntroPref && AppSettings.ShowIntroOnBoot;
            if (!shouldPlay) return;

            var intro = new IntroVideo();
            intro.ShowDialog(); // blocks until the video ends or the user skips
        }
        catch { /* never break startup */ }
    }
}
