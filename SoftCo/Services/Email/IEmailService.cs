namespace SoftCo.Services.Email;

public interface IEmailService
{
    /// <summary>
    /// Delivers the message, or throws <see cref="EmailException"/> with a message safe to show
    /// a user. Implementations must never log credentials, recipient addresses or message bodies
    /// - the same discipline ExchangeRateService applies to its secret-bearing provider URL.
    /// </summary>
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);

    /// <summary>Describes where mail is going, for display on screen. Never includes secrets.</summary>
    string DeliveryDescription { get; }
}
