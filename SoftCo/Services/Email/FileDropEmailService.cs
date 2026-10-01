using System.Text;
using Microsoft.Extensions.Options;

namespace SoftCo.Services.Email;

/// <summary>
/// Development transport. Writes the composed message to App_Data/sent-email as a .eml file
/// instead of sending it, so the exact content that would have gone out can be opened and
/// checked - including attachments - without SMTP credentials and without the risk of mailing a
/// real supplier from a developer's machine.
/// </summary>
public sealed class FileDropEmailService : IEmailService
{
    private readonly string _folder;
    private readonly EmailOptions _options;
    private readonly ILogger<FileDropEmailService> _logger;

    public FileDropEmailService(IWebHostEnvironment env, IOptions<EmailOptions> options,
                                ILogger<FileDropEmailService> logger)
    {
        _folder = Path.Combine(env.ContentRootPath, "App_Data", "sent-email");
        _options = options.Value;
        _logger = logger;
    }

    public string DeliveryDescription => "written to App_Data/sent-email (development)";

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_folder);

        var name = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.eml".Replace(':', '-');
        var path = Path.Combine(_folder, name);

        var sb = new StringBuilder()
            .AppendLine($"From: {_options.FromName} <{_options.FromAddress}>")
            .AppendLine($"To: {message.ToName} <{message.ToAddress}>")
            .AppendLine($"Subject: {message.Subject}")
            .AppendLine($"Date: {DateTimeOffset.UtcNow:R}")
            .AppendLine("Content-Type: text/plain; charset=utf-8")
            .AppendLine()
            .AppendLine(message.BodyText);

        if (message.Attachments is { Count: > 0 })
        {
            sb.AppendLine().AppendLine("--- attachments ---");
            foreach (var a in message.Attachments)
                sb.AppendLine($"{a.FileName} ({a.ContentType}, {a.Content.Length:N0} bytes)");
        }

        await File.WriteAllTextAsync(path, sb.ToString(), Encoding.UTF8, cancellationToken);

        // Filename only. The body and recipient stay out of the log.
        _logger.LogInformation("Development email written to {File}.", name);
    }
}
