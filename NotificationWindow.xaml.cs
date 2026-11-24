using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace EventGhostDotNet;

public partial class NotificationWindow : Window
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(6);
    private readonly DispatcherTimer _timer;
    private bool _closing;

    public NotificationWindow(string title, string message, string? key)
    {
        InitializeComponent();
        Key = key;
        SetContent(title, message);

        _timer = new DispatcherTimer(Lifetime, DispatcherPriority.Background, (_, _) => Dismiss(), Dispatcher);
        _timer.Stop();

        Loaded += (_, _) => RestartTimer();
        Closed += (_, _) => _timer.Stop();
        MouseLeftButtonUp += (_, _) => ActivateFromUser();
        MouseEnter += (_, _) => _timer.Stop();
        MouseLeave += (_, _) => RestartTimer();
    }

    /// <summary>Clé de remplacement : une notification de même clé est mise à jour au lieu de s'empiler.</summary>
    public string? Key { get; }

    public event Action? ActivatedByUser;

    public void UpdateContent(string title, string message)
    {
        BeginAnimation(OpacityProperty, null);
        Opacity = 1;
        _closing = false;
        SetContent(title, message);
        RestartTimer();
    }

    /// <summary>La fenêtre suit la hauteur du contenu, sans ligne vide résiduelle.</summary>
    private void SetContent(string title, string message)
    {
        var normalizedTitle = title.TrimEnd();
        var normalizedMessage = message.TrimEnd();
        TitleText.Text = normalizedTitle;
        MessageText.Text = normalizedMessage;
        TitleText.Visibility = string.IsNullOrWhiteSpace(normalizedTitle) ? Visibility.Collapsed : Visibility.Visible;
        MessageText.Visibility = string.IsNullOrWhiteSpace(normalizedMessage) ? Visibility.Collapsed : Visibility.Visible;
        MessageText.Margin = TitleText.Visibility == Visibility.Visible ? new Thickness(0, 3, 0, 0) : new Thickness(0);
    }

    private void RestartTimer()
    {
        if (_closing)
            return;
        _timer.Stop();
        _timer.Start();
    }

    private void ActivateFromUser()
    {
        ActivatedByUser?.Invoke();
        Dismiss();
    }

    private void Dismiss()
    {
        if (_closing)
            return;
        _closing = true;
        _timer.Stop();
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(160));
        fade.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fade);
    }
}
