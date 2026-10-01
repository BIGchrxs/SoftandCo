using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace SoftCo.Services.Email;

/// <summary>
/// Production transport. MailKit rather than System.Net.Mail.SmtpClient, which Microsoft has
/// marked obsolete and does not support modern TLS negotiation properly.
///
/// Nothing here logs the host, the credentials, the recipient or the body. On failure the caller
/// gets a short, safe message and the provider's own exception text is deliberately discarded -
/// SMTP errors routinely echo back the address being delivered to.
/// </summary>
public sealed class SmtpEmailService : IEmailService
{
    private readonly EmailOptions _options;
    private readonly ILogger<SmtpEmailService> _logger;

    public SmtpEmailService(IOptions<EmailOptions> options, ILogger<SmtpEmailService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public string DeliveryDescription => _options.IsConfigured ? "sent by email" : "not configured";

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        if (!_options.IsConfigured)
            throw new EmailException("not_configured",
                "Email is not configured on this server. Ask an administrator to set the mail settings.");

        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(_options.FromName, _options.FromAddress));
        mime.To.Add(new MailboxAddress(message.ToName, message.ToAddress));
        mime.Subject = message.Subject;

        var builder = new BodyBuilder { TextBody = message.BodyText };

        if (message.Attachments is { Count: > 0 })
        {
            foreach (var a in message.Attachments)
                builder.Attachments.Add(a.FileName, a.Content, ContentType.Parse(a.ContentType));
        }

        mime.Body = builder.ToMessageBody();

        try
        {
            using var client = new SmtpClient();
            client.Timeout = 15_000;

            await client.ConnectAsync(_options.Host, _options.Port,
                _options.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.SslOnConnect,
                cancellationToken);

            if (!string.IsNullOrWhiteSpace(_options.User))
                await client.AuthenticateAsync(_options.User, _options.Password, cancellationToken);

            await client.SendAsync(mime, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Type only - the message can contain the recipient address or server banner.
            _logger.LogWarning("Email delivery failed ({Type}).", ex.GetType().Name);
            throw new EmailException("send_failed",
                "The email could not be sent. The request has been recorded as failed - try again shortly.");
        }
    }
}
