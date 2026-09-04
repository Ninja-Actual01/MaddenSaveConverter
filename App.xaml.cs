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

    // First boot: play the intro video, then ask whether to replay it next time
    // (Yes / No / Just-once). Later boots honor the stored preference.
    static void PlayIntroIfNeeded()
    {
        try
        {
            bool shouldPlay = AppSettings.HasIntroPref ? AppSettings.ShowIntroOnBoot : true;
            if (!shouldPlay) return;

            var intro = new IntroVideo();
            intro.ShowDialog(); // blocks until the video ends or the user skips

            if (!AppSettings.HasIntroPref)
            {
                var res = MessageBox.Show(
                    "Would you like to replay the intro on future launches?",
                    "Legion of Loom",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);
                // Yes = replay next boot; No or Cancel = do not (it already played just this once)
                AppSettings.SetIntroEnabled(res == MessageBoxResult.Yes);
            }
        }
        catch { /* never break startup */ }
    }
}
