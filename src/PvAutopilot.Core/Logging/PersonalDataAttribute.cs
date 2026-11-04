namespace PvAutopilot.Core.Logging;

/// <summary>
/// Markiert personenbezogene Daten. Solche Werte werden beim strukturierten Loggen maskiert
/// (Railway-Logs liegen in den USA). Einzelwerte nie direkt als Log-Parameter übergeben.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class PersonalDataAttribute : Attribute;
