/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using System.Diagnostics;
using FufuLauncher.Helpers;
using FufuLauncher.Messages;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace FufuLauncher;

public sealed partial class MainWindow
{
    #region Notifications

    private const int MaximumHomeNotifications = 20;

    private void ShowNotification(NotificationMessage message)
    {
        try
        {
            var infoBar = CreateInfoBar(message);
            NotificationPanel.Children.Insert(0, infoBar);

            while (NotificationPanel.Children.Count > MaximumHomeNotifications)
            {
                NotificationPanel.Children.RemoveAt(NotificationPanel.Children.Count - 1);
            }

            PlayEntranceAnimation(infoBar);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"显示主页通知异常: {ex.Message}");
        }
    }

    private InfoBar CreateInfoBar(NotificationMessage message)
    {
        var infoBar = new InfoBar
        {
            Title = message.Title,
            Message = message.Message,
            Severity = GetInfoBarSeverity(message.Type),
            IsOpen = true,
            IsClosable = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            RenderTransform = new TranslateTransform { Y = -10 },
            Opacity = 0
        };

        if (!string.IsNullOrEmpty(message.CopyText))
        {
            infoBar.ActionButton = CreateCopyActionButton(message.CopyText);
        }

        infoBar.Closing += (_, args) =>
        {
            args.Cancel = true;
            if (infoBar.Tag is string state && state == "Closing")
            {
                return;
            }

            infoBar.Tag = "Closing";
            infoBar.IsHitTestVisible = false;
            DismissInfoBar(infoBar);
        };

        return infoBar;
    }

    private Button CreateCopyActionButton(string copyText)
    {
        var copyButton = new Button { Content = "CopyBtn".GetLocalized() };

        copyButton.Click += (_, _) =>
        {
            try
            {
                var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
                package.SetText(copyText);
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            }
            catch
            {
                // ignored
            }

            copyButton.Content = "Btn_Copied".GetLocalized();
            copyButton.IsEnabled = false;

            Task.Delay(1000).ContinueWith(_ =>
            {
                dispatcherQueue.TryEnqueue(() =>
                {
                    copyButton.Content = "CopyBtn".GetLocalized();
                    copyButton.IsEnabled = true;
                });
            });
        };

        return copyButton;
    }

    private static InfoBarSeverity GetInfoBarSeverity(NotificationType type)
    {
        return type switch
        {
            NotificationType.Success => InfoBarSeverity.Success,
            NotificationType.Warning => InfoBarSeverity.Warning,
            NotificationType.Error => InfoBarSeverity.Error,
            _ => InfoBarSeverity.Informational
        };
    }

    private static void PlayEntranceAnimation(FrameworkElement element)
    {
        var transformAnimation = new DoubleAnimation
        {
            From = -10,
            To = 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(300)),
            EasingFunction = new CircleEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(transformAnimation, element.RenderTransform);
        Storyboard.SetTargetProperty(transformAnimation, "Y");

        var opacityAnimation = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = new Duration(TimeSpan.FromMilliseconds(300)),
            EasingFunction = new CircleEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(opacityAnimation, element);
        Storyboard.SetTargetProperty(opacityAnimation, "Opacity");

        var storyboard = new Storyboard();
        storyboard.Children.Add(transformAnimation);
        storyboard.Children.Add(opacityAnimation);
        storyboard.Begin();
    }

    private void DismissInfoBar(FrameworkElement element)
    {
        if (element is InfoBar infoBar &&
            (infoBar.Title == "RedeemCodeExpired".GetLocalized() ||
             infoBar.Title == "RedeemCodeToday".GetLocalized() ||
             infoBar.Title == "RedeemCodeNew".GetLocalized() ||
             infoBar.Title == "RedeemCodeExpiring".GetLocalized()))
        {
            _ = _localSettingsService.SaveSettingAsync("LastRedeemCodeReminderDate", DateTime.Now.ToString("yyyy-MM-dd"));
            Debug.WriteLine("[RedeemCodes] 已将关闭状态写入数据库");
        }

        var transformAnimation = new DoubleAnimation
        {
            From = 0,
            To = -10,
            Duration = new Duration(TimeSpan.FromMilliseconds(300)),
            EasingFunction = new CircleEase { EasingMode = EasingMode.EaseIn }
        };
        Storyboard.SetTarget(transformAnimation, element.RenderTransform);
        Storyboard.SetTargetProperty(transformAnimation, "Y");

        var opacityAnimation = new DoubleAnimation
        {
            From = 1,
            To = 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(300)),
            EasingFunction = new CircleEase { EasingMode = EasingMode.EaseIn }
        };
        Storyboard.SetTarget(opacityAnimation, element);
        Storyboard.SetTargetProperty(opacityAnimation, "Opacity");

        var storyboard = new Storyboard();
        storyboard.Children.Add(transformAnimation);
        storyboard.Children.Add(opacityAnimation);
        storyboard.Completed += (_, _) =>
        {
            try
            {
                NotificationPanel.Children.Remove(element);
            }
            catch
            {
                // ignored
            }
        };
        storyboard.Begin();
    }

    private void ClearAllNotifications_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            foreach (var infoBar in NotificationPanel.Children.OfType<InfoBar>().ToList())
            {
                if (infoBar.Tag is string state && state == "Closing")
                {
                    continue;
                }

                infoBar.Tag = "Closing";
                infoBar.IsHitTestVisible = false;
                DismissInfoBar(infoBar);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"一键清除通知异常: {ex.Message}");
        }
    }

    private void UpdateNotificationCardVisibility(bool isMainPage)
    {
        NotificationContainer.Visibility = isMainPage ? Visibility.Visible : Visibility.Collapsed;
    }

    #endregion
}
