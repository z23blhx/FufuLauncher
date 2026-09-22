/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using FufuLauncher.Helpers;
using MihoyoBBS;

namespace FufuLauncher.Services;

internal static class ZenlessCheckinService
{
    // null means this miHoYo account has no enabled Zenless Zone Zero role.
    public static async Task<(bool? Success, string Message)> CheckInAsync(Config config, HashSet<string> disabledUids)
    {
        try
        {
            GameCheckin.LastApiError = string.Empty;
            var zenless = new ZenlessZoneZero();
            await zenless.InitializeAsync(config);

            if (zenless.AccountList.Count == 0)
            {
                return string.IsNullOrEmpty(GameCheckin.LastApiError)
                    ? (null, "Checkin_ZenlessNotBound".GetLocalized())
                    : (false, GameCheckin.LastApiError);
            }

            if (zenless.AccountList.All(role => disabledUids.Contains(role.GameUid)))
                return (null, "Checkin_AllDisabled".GetLocalized());

            var message = await zenless.SignAccountAsync(config, null, disabledUids);
            return (zenless.LastSignSucceeded, message);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
