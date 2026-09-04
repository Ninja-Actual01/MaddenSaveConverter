using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MaddenSaveConverter;

/// <summary>
/// A borderless, fullscreen-ish window that plays the bundled intro mp4 once.
/// The user can skip at any time (ESC / Space / Enter / click). Closes when the
/// video ends or is skipped. Never blocks other startup work; the caller awaits.
/// </summary>
public sealed class IntroVideo : Window
{
    readonly MediaElement _media;

    public bool Completed { get; private set; }   // true if it played to the end

    public IntroVideo()
    {
        Title = "Legion of Loom";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Brushes.Black;
        Width = 1280;
        Height = 720;
        AllowsTransparency = false;

        var grid = new Grid { Background = Brushes.Black };
        _media = new MediaElement
        {
            Stretch = Stretch.Uniform,
            LoadedBehavior = MediaState.Manual,
            UnloadedBehavior = MediaState.Close
        };
        grid.Children.Add(_media);
        Content = grid;

        KeyDown += OnKeyDown;
        MouseLeftButtonDown += (_, _) => Skip();
        _media.MediaEnded += (_, _) => Finish();
        _media.MediaFailed += (_, _) => Close();
        Loaded += OnLoaded;
    }

    void OnLoaded(object sender, RoutedEventArgs e)
    {
        var mp4 = FindBundled("Movies", "*.mp4");
        if (mp4 == null) { Close(); return; }
        try { _media.Source = new Uri(mp4, UriKind.Absolute); _media.Play(); }
        catch { Close(); }
    }

    void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape || e.Key == Key.Space || e.Key == Key.Enter)
            Skip();
    }

    void Skip()
    {
        Completed = false;
        Close();
    }

    void Finish()
    {
        Completed = true;
        Close();
    }

    static string FindBundled(string sub, string pattern)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, sub);
        if (!Directory.Exists(dir)) return null;
        return Directory.GetFiles(dir, pattern).FirstOrDefault();
    }
}