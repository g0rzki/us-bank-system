namespace UsBankSystem.Api.Integrations.Rtp;

public class RtpApiKeyStore
{
    private volatile string _key = "";
    public string ApiKey => _key;
    public bool HasKey => !string.IsNullOrEmpty(_key);
    public void Set(string key) => _key = key;
}
