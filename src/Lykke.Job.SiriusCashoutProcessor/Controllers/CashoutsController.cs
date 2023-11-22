using System;
using Lykke.Job.SiriusCashoutProcessor.Services;
using Microsoft.AspNetCore.Mvc;

namespace Lykke.Job.SiriusCashoutProcessor.Controllers
{
    [ApiController]
    [Route("api/cashouts")]
    public class CashoutsController : ControllerBase
    {
        private readonly BlockedCashoutsManager _blockedWithdrawalsManager;

        public CashoutsController(BlockedCashoutsManager blockedWithdrawalsManager)
        {
            _blockedWithdrawalsManager = blockedWithdrawalsManager;
        }

        [HttpPost("{id}/block")]
        public void Block(Guid id)
        {
            _blockedWithdrawalsManager.Add(id);
        }

        [HttpGet("{id}/is-blocked")]
        public bool GetIsBlocked(Guid id)
        {
            return _blockedWithdrawalsManager.IsBlocked(id);
        }
    }
}
