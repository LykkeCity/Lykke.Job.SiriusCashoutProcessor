using System;
using Swisschain.Sirius.Api.ApiClient.Utils.Builders.V2;
using Swisschain.Sirius.Api.ApiContract.Withdrawal;

namespace Lykke.Job.SiriusCashoutProcessor.Services
{
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
            return withdrawal.UserNativeId;
        }

        public static string GetAccountReferenceId(this WithdrawalResponse withdrawal)
        {
        
            if (withdrawal == null)
                throw new ArgumentNullException(nameof(withdrawal));

            if(withdrawal.Properties.TryGetValue(KnownProperties.WalletId, out var walletId))
            {
                return walletId;
            }

            //fallback to old property
            return withdrawal.AccountReferenceId;
        }
    }
}
