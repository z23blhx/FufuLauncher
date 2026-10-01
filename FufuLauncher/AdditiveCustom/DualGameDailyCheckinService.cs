using System.Net;
using System.Text;
using System.Text.Json;
using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Messages;
using FufuLauncher.Services;
using MihoyoBBS;

namespace FufuLauncher.AdditiveCustom;

/// <summary>
/// Self-contained Genshin + Zenless Zone Zero daily check-in. This file deliberately
/// does not depend on modifications to the launcher's built-in check-in classes.
/// </summary>
internal sealed class DualGameDailyCheckinService
{
    private const string LastAutomaticRunKey = "AdditiveCustom_DualCheckinLastRunDate";
    private static readonly SemaphoreSlim RunLock = new(1, 1);

    private static readonly GameDefinition Genshin = new(
        "原神", "hk4e_cn", "hk4e", "e202311201442471",
        "https://api-takumi.mihoyo.com/event/luna/info",
        "https://api-takumi.mihoyo.com/event/luna/sign");

    private static readonly GameDefinition Zenless = new(
        "绝区零", "nap_cn", "zzz", "e202406242138391",
        "https://act-nap-api.mihoyo.com/event/luna/zzz/info",
        "https://act-nap-api.mihoyo.com/event/luna/zzz/sign");

    private readonly AccountManager _accountManager = App.GetService<AccountManager>();
    private readonly ILocalSettingsService _settings = App.GetService<ILocalSettingsService>();

    public async Task RunAutomaticallyOnceTodayAsync()
    {
        string today = DateTime.Now.ToString("yyyy-MM-dd");
        var lastRun = await _settings.ReadSettingAsync(LastAutomaticRunKey);
        if (string.Equals(lastRun?.ToString(), today, StringComparison.Ordinal))
            return;

        // Record the attempt first, so a failed network request does not run repeatedly
        // every time the user returns to the home page on the same day.
        await _settings.SaveSettingAsync(LastAutomaticRunKey, today);
        await RunAsync(isAutomatic: true);
    }

    public async Task RunManuallyAsync() => await RunAsync(isAutomatic: false);

    private async Task RunAsync(bool isAutomatic)
    {
        if (!await RunLock.WaitAsync(0))
        {
            Notify("每日签到", "签到正在进行中，请稍候。", NotificationType.Information);
            return;
        }

        try
        {
            string prefix = isAutomatic ? "今日首次启动，" : string.Empty;
            Notify("原神 + 绝区零签到", prefix + "正在领取每日奖励…", NotificationType.Information);

            var activeId = _accountManager.ActiveAccountId;
            if (string.IsNullOrWhiteSpace(activeId))
            {
                Notify("原神 + 绝区零签到", "没有已激活的米游社账号。", NotificationType.Error);
                return;
            }

            var entry = _accountManager.GetActiveAccountEntry();
            if (entry == null)
            {
                Notify("原神 + 绝区零签到", "找不到当前米游社账号。", NotificationType.Error);
                return;
            }

            if (entry.Id.StartsWith("os_", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(entry.ServerType, "os", StringComparison.OrdinalIgnoreCase))
            {
                Notify("原神 + 绝区零签到", "此独立功能目前仅支持米游社国服账号。", NotificationType.Warning);
                return;
            }

            var cookies = await _accountManager.LoadCookiesAsync(activeId);
            if (cookies == null || cookies.Count == 0)
            {
                Notify("原神 + 绝区零签到", "账号 Cookie 不存在，请重新登录米游社账号。", NotificationType.Error);
                return;
            }

            var disabledUids = await LoadDisabledUidsAsync();
            string cookie = string.Join("; ", cookies.Select(pair => $"{pair.Key}={pair.Value}"));

            using var client = CreateClient(cookie);
            var genshin = await CheckInGameAsync(client, Genshin, disabledUids);
            await Task.Delay(1500);
            var zenless = await CheckInGameAsync(client, Zenless, disabledUids);

            var type = genshin.Success == false || zenless.Success == false
                ? (genshin.Success == true || zenless.Success == true ? NotificationType.Warning : NotificationType.Error)
                : NotificationType.Success;

            Notify("原神 + 绝区零签到", $"{genshin.Message}\n{zenless.Message}", type);
        }
        catch (Exception ex)
        {
            Notify("原神 + 绝区零签到", $"执行失败：{ex.Message}", NotificationType.Error);
        }
        finally
        {
            RunLock.Release();
        }
    }

    private async Task<HashSet<string>> LoadDisabledUidsAsync()
    {
        var json = await _settings.ReadSettingAsync("CheckinDisabledUids");
        if (json == null)
            return new HashSet<string>();

        try
        {
            return JsonSerializer.Deserialize<HashSet<string>>(json.ToString() ?? "[]") ?? new HashSet<string>();
        }
        catch
        {
            return new HashSet<string>();
        }
    }

    private static HttpClient CreateClient(string cookie)
    {
        var client = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
        })
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "zh-CN,en-US;q=0.8");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Origin", "https://act.mihoyo.com");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://act.mihoyo.com/");
        client.DefaultRequestHeaders.TryAddWithoutValidation("x-rpc-channel", "miyousheluodi");
        client.DefaultRequestHeaders.TryAddWithoutValidation("x-rpc-app_version", "2.93.1");
        client.DefaultRequestHeaders.TryAddWithoutValidation("x-rpc-client_type", "5");
        client.DefaultRequestHeaders.TryAddWithoutValidation("x-rpc-device_id", Tools.GetDeviceId(cookie));
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Requested-With", "com.mihoyo.hyperion");
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", Tools.GetUserAgent(string.Empty));
        client.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", Tools.TidyCookie(cookie));
        return client;
    }

    private static async Task<GameCheckinResult> CheckInGameAsync(
        HttpClient client,
        GameDefinition game,
        HashSet<string> disabledUids)
    {
        try
        {
            var rolesUrl = $"https://api-takumi.mihoyo.com/binding/api/getUserGameRolesByCookie?game_biz={game.GameBiz}";
            var rolesResponse = await SendAsync<AccountInfoData>(client, HttpMethod.Get, rolesUrl, game.SignGame);
            if (rolesResponse == null)
                return new(false, $"{game.Name}：角色查询响应无效");
            if (rolesResponse.RetCode != 0)
                return new(false, $"{game.Name}：获取角色失败（{rolesResponse.Message}）");

            var roles = rolesResponse.Data?.List?
                .Where(role => !disabledUids.Contains(role.GameUid))
                .ToList() ?? new List<AccountItem>();
            if (roles.Count == 0)
                return new(null, $"{game.Name}：未绑定角色，已跳过");

            bool allSucceeded = true;
            var messages = new List<string>();
            foreach (var role in roles)
            {
                var infoUrl = $"{game.InfoUrl}?lang=zh-cn&act_id={game.ActId}&region={Uri.EscapeDataString(role.Region)}&uid={Uri.EscapeDataString(role.GameUid)}";
                var info = await SendAsync<IsSignData>(client, HttpMethod.Get, infoUrl, game.SignGame);
                if (info?.RetCode != 0 || info.Data == null)
                {
                    allSucceeded = false;
                    messages.Add($"{role.Nickname} 查询失败（{info?.Message ?? "无响应"}）");
                    continue;
                }

                if (info.Data.FirstBind)
                {
                    allSucceeded = false;
                    messages.Add($"{role.Nickname} 需先在米游社完成首次绑定");
                    continue;
                }

                if (info.Data.IsSign)
                {
                    messages.Add($"{role.Nickname} 今日已领取");
                    continue;
                }

                var body = JsonSerializer.Serialize(new
                {
                    act_id = game.ActId,
                    region = role.Region,
                    uid = role.GameUid
                });
                var sign = await SendAsync<SignResponseData>(client, HttpMethod.Post, game.SignUrl, game.SignGame, body);
                if (sign?.RetCode == 0 && sign.Data?.Success == 0)
                {
                    messages.Add($"{role.Nickname} 领取成功");
                }
                else if (sign?.RetCode == -5003)
                {
                    messages.Add($"{role.Nickname} 今日已领取");
                }
                else
                {
                    allSucceeded = false;
                    string detail = sign?.Data?.Success == 1 ? "触发安全验证，请在米游社手动领取" : sign?.Message ?? "无响应";
                    messages.Add($"{role.Nickname} 领取失败（{detail}）");
                }

                if (roles.Count > 1)
                    await Task.Delay(1500);
            }

            return new(allSucceeded, $"{game.Name}：{string.Join("；", messages)}");
        }
        catch (Exception ex)
        {
            return new(false, $"{game.Name}：{ex.Message}");
        }
    }

    private static async Task<ApiResponse<T>?> SendAsync<T>(
        HttpClient client,
        HttpMethod method,
        string url,
        string signGame,
        string? jsonBody = null)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.TryAddWithoutValidation("DS", Tools.GetDs(true));
        request.Headers.TryAddWithoutValidation("x-rpc-signgame", signGame);
        if (jsonBody != null)
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request);
        string json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<ApiResponse<T>>(json);
    }

    private static void Notify(string title, string message, NotificationType type) =>
        WeakReferenceMessenger.Default.Send(new NotificationMessage(title, message, type, 0));

    private sealed record GameDefinition(
        string Name,
        string GameBiz,
        string SignGame,
        string ActId,
        string InfoUrl,
        string SignUrl);

    private sealed record GameCheckinResult(bool? Success, string Message);
}
