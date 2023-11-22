using System;
using System.Collections.Concurrent;

namespace Lykke.Job.SiriusCashoutProcessor.Services
{
    public class BlockedCashoutsManager
    {
        private ConcurrentDictionary<Guid, bool> _store = new ConcurrentDictionary<Guid, bool>();

        public void Add(Guid withdrawalId)
        {
            _store.TryAdd(withdrawalId, true);
        }

        public bool IsBlocked(Guid withdrawalId)
        {
            return _store.ContainsKey(withdrawalId);
        }
    }
}
