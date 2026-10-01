using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.AdditiveCustom;
using FufuLauncher.Messages;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI;

namespace FufuLauncher;

/// <summary>
/// Runtime-only UI extension. The official XAML and code-behind remain untouched.
/// </summary>
public sealed partial class MainWindow
{
    private const int AdditiveMaximumNotifications = 20;
    private readonly object _additiveNotificationRecipient = new();
    private readonly DualGameDailyCheckinService _additiveCheckinService = new();
    private Grid? _additiveLayout;
    private Border? _additiveNotificationCard;
    private StackPanel? _additiveNotificationPanel;
    private RowDefinition? _additiveNotificationRow;
    private Button? _additiveCheckinButton;
    private bool _additiveAutoCheckinStarted;

    static MainWindow()
    {
        var queue = DispatcherQueue.GetForCurrentThread();
        queue?.TryEnqueue(async () =>
        {
            for (int attempt = 0; attempt < 100; attempt++)
            {
                if (App.MainWindow is MainWindow window)
                {
                    window.InitializeAdditiveCustomization();
                    return;
                }

                await Task.Delay(50);
            }
        });
    }

    private void InitializeAdditiveCustomization()
    {
        if (_additiveLayout != null)
            return;

        HideOfficialNotificationCenter();
        InjectAdditiveLayout();

        WeakReferenceMessenger.Default.Register<NotificationMessage>(
            _additiveNotificationRecipient,
            (_, message) => dispatcherQueue.TryEnqueue(() =>
            {
                HideOfficialNotificationCenter();
                AddPersistentNotification(message);
            }));

        ContentFrame.Navigated += AdditiveContentFrame_Navigated;
        UpdateAdditiveCardVisibility(ContentFrame.CurrentSourcePageType == typeof(Views.MainPage));
    }

    private void InjectAdditiveLayout()
    {
        if (NavigationView.Content is not FrameworkElement officialContent)
            return;

        NavigationView.Content = null;

        _additiveNotificationRow = new RowDefinition { Height = GridLength.Auto };
        _additiveLayout = new Grid();
        _additiveLayout.RowDefinitions.Add(_additiveNotificationRow);
        _additiveLayout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _additiveLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _additiveLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _additiveNotificationCard = CreateNotificationCard();
        Grid.SetRow(_additiveNotificationCard, 0);
        Grid.SetColumn(_additiveNotificationCard, 0);
        _additiveLayout.Children.Add(_additiveNotificationCard);

        Grid.SetRow(officialContent, 1);
        Grid.SetColumnSpan(officialContent, 2);
        _additiveLayout.Children.Add(officialContent);
        NavigationView.Content = _additiveLayout;
    }

    private Border CreateNotificationCard()
    {
        var header = new Grid { Padding = new Thickness(16, 10, 12, 6) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new FontIcon
        {
            Glyph = "\uE7E7",
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        header.Children.Add(icon);

        var title = new TextBlock
        {
            Text = "通知中心",
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(title, 1);
        header.Children.Add(title);

        _additiveCheckinButton = new Button
        {
            Content = "原神 + 绝区零签到",
            FontSize = 12,
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(4, 0, 4, 0)
        };
        _additiveCheckinButton.Click += async (_, _) =>
        {
            _additiveCheckinButton.IsEnabled = false;
            try { await _additiveCheckinService.RunManuallyAsync(); }
            finally { _additiveCheckinButton.IsEnabled = true; }
        };
        Grid.SetColumn(_additiveCheckinButton, 2);
        header.Children.Add(_additiveCheckinButton);

        var clearButton = new Button
        {
            Content = "全部清除",
            FontSize = 12,
            Padding = new Thickness(8, 4, 8, 4),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0)
        };
        clearButton.Click += (_, _) => _additiveNotificationPanel?.Children.Clear();
        Grid.SetColumn(clearButton, 3);
        header.Children.Add(clearButton);

        _additiveNotificationPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 6,
            MinHeight = 28
        };

        var scrollViewer = new ScrollViewer
        {
            MaxHeight = 132,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(12, 4, 12, 10),
            Content = _additiveNotificationPanel
        };

        var content = new Grid();
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.Children.Add(header);
        Grid.SetRow(scrollViewer, 1);
        content.Children.Add(scrollViewer);

        var acrylic = new AcrylicBrush
        {
            TintColor = Microsoft.UI.Colors.Black,
            TintOpacity = 0.15,
            FallbackColor = Color.FromArgb(220, 16, 16, 16)
        };

        return new Border
        {
            Margin = new Thickness(24, 48, 24, 8),
            MaxHeight = 190,
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(48, 255, 255, 255)),
            Background = acrylic,
            Child = content
        };
    }

    private void AdditiveContentFrame_Navigated(object sender, NavigationEventArgs e)
    {
        bool isMainPage = e.SourcePageType == typeof(Views.MainPage);
        UpdateAdditiveCardVisibility(isMainPage);
        if (isMainPage && !_additiveAutoCheckinStarted)
        {
            _additiveAutoCheckinStarted = true;
            _ = _additiveCheckinService.RunAutomaticallyOnceTodayAsync();
        }
    }

    private void UpdateAdditiveCardVisibility(bool isMainPage)
    {
        if (_additiveNotificationCard == null || _additiveNotificationRow == null)
            return;

        _additiveNotificationCard.Visibility = isMainPage ? Visibility.Visible : Visibility.Collapsed;
        _additiveNotificationRow.Height = isMainPage ? GridLength.Auto : new GridLength(0);
    }

    private void HideOfficialNotificationCenter()
    {
        NotificationContainer.Visibility = Visibility.Collapsed;
        NotificationPanel.Children.Clear();
    }

    private void AddPersistentNotification(NotificationMessage message)
    {
        if (_additiveNotificationPanel == null)
            return;

        var infoBar = new InfoBar
        {
            Title = message.Title,
            Message = message.Message,
            Severity = message.Type switch
            {
                NotificationType.Success => InfoBarSeverity.Success,
                NotificationType.Warning => InfoBarSeverity.Warning,
                NotificationType.Error => InfoBarSeverity.Error,
                _ => InfoBarSeverity.Informational
            },
            IsOpen = true,
            IsClosable = true,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        if (!string.IsNullOrEmpty(message.CopyText))
            infoBar.ActionButton = CreateAdditiveCopyButton(message.CopyText);

        infoBar.Closing += (_, args) =>
        {
            args.Cancel = true;
            _additiveNotificationPanel.Children.Remove(infoBar);
        };

        _additiveNotificationPanel.Children.Insert(0, infoBar);
        while (_additiveNotificationPanel.Children.Count > AdditiveMaximumNotifications)
            _additiveNotificationPanel.Children.RemoveAt(_additiveNotificationPanel.Children.Count - 1);
    }

    private static Button CreateAdditiveCopyButton(string copyText)
    {
        var button = new Button { Content = "复制" };
        button.Click += (_, _) =>
        {
            var package = new DataPackage();
            package.SetText(copyText);
            Clipboard.SetContent(package);
            button.Content = "已复制";
            button.IsEnabled = false;
        };
        return button;
    }
}
