namespace SoftCo.Services.Email;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public bool UseStartTls { get; set; } = true;
    public string User { get; set; } = "";
    public string Password { get; set; } = "";
    public string FromAddress { get; set; } = "noreply@softandco.co.za";
    public string FromName { get; set; } = "Soft & Co.";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(FromAddress);
}

public sealed record EmailAttachment(string FileName, string ContentType, byte[] Content);

public sealed record EmailMessage(
    string ToAddress,
    string ToName,
    string Subject,
    string BodyText,
    IReadOnlyList<EmailAttachment>? Attachments = null);

/// <summary>
/// Typed failure, matching the shape ExchangeRateException sets elsewhere in the project, so
/// callers can show the user something useful without unwrapping a provider exception.
/// </summary>
public sealed class EmailException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
