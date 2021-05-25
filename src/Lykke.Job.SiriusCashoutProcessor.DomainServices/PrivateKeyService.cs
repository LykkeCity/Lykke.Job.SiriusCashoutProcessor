using System;
using System.Threading.Tasks;
using Microsoft.Azure.KeyVault;
using Microsoft.Extensions.Logging;

namespace Lykke.Job.SiriusCashoutProcessor.DomainServices
{
    public class PrivateKeyService
    {
        private readonly string _vaultBaseUrl;
        private readonly string _keyName;
        private readonly KeyVaultClient _keyVaultClient;
        private readonly ILogger<PrivateKeyService> _logger;
        private string _privateKey;

        public PrivateKeyService(
            string vaultBaseUrl,
            string keyName,
            KeyVaultClient keyVaultClient,
            ILogger<PrivateKeyService> logger

            )
        {
            _vaultBaseUrl = vaultBaseUrl;
            _keyName = keyName;
            _keyVaultClient = keyVaultClient;
            _logger = logger;
        }

        public async Task InitAsync()
        {
            try
            {
                var secretBundle = await _keyVaultClient.GetSecretAsync(_vaultBaseUrl, _keyName);
                _privateKey = secretBundle.Value;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting private key from azure key vault");
                throw;
            }
        }

        public string GetPrivateKey()
        {
            return _privateKey;
        }
    }
}
