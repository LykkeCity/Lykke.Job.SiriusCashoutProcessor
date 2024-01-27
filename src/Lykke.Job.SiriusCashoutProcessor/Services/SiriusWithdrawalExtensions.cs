using System;
using Swisschain.Sirius.Api.ApiClient.Utils.Builders.V2;
using Swisschain.Sirius.Api.ApiContract.Withdrawal;

namespace Lykke.Job.SiriusCashoutProcessor.Services;

public static class SiriusWithdrawalExtensions
{
    public static string GetUserNativeId(this WithdrawalResponse withdrawal)
    {
        if (withdrawal == null)
            throw new ArgumentNullException(nameof(withdrawal));

        if(withdrawal.Properties.TryGetValue(KnownProperties.UserId, out var userId))
        {
            return userId;
        }

        //fallback to old property
        return !string.IsNullOrWhiteSpace(withdrawal.UserNativeId) ? withdrawal.UserNativeId : null;
    }
}
