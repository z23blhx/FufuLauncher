/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
namespace MihoyoBBS;

public class ZenlessZoneZero : GameCheckin
{
    private const string NapLunaApi = "https://act-nap-api.mihoyo.com/event/luna/zzz";

    protected override string CheckinRewardsUrl => NapLunaApi + "/home";
    protected override string IsSignUrl => NapLunaApi + "/info";
    protected override string SignUrl => NapLunaApi + "/sign";

    public ZenlessZoneZero() : base("nap_cn", "绝区零", "e202406242138391", "绳匠") { }

    public override async Task InitializeAsync(Config config)
    {
        SetHeaders(config);
        Headers["x-rpc-signgame"] = "zzz";
        Headers["Referer"] = "https://act.mihoyo.com/";

        AccountList = await GetAccountListAsync(config).ConfigureAwait(false);
        if (AccountList.Count > 0)
            CheckinRewards = await GetCheckinRewardsAsync().ConfigureAwait(false);
    }
}
