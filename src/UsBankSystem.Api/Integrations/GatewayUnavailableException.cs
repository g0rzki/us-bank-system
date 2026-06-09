namespace UsBankSystem.Api.Integrations;

public class GatewayUnavailableException(string message) : Exception(message);
