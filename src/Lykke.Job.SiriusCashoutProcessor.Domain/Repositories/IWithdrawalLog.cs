using System;

namespace Lykke.Job.SiriusCashoutProcessor.Domain.Repositories
{
    public interface IWithdrawalLog
    {
        string WithdrawalId { get; set; }
        DateTime CreatedAt { get; set; }
        string Message { get; set; }
        string TechData { get; set; }
    }
}
