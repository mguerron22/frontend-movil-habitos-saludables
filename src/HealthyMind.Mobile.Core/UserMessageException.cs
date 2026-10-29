namespace HealthyMind.Mobile.Core;

/// <summary>Una regla de la app no se cumplió; el mensaje está escrito para mostrárselo al usuario tal cual.</summary>
public class UserMessageException(string message) : Exception(message);
