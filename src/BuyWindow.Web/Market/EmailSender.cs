using MailKit.Security;
using MimeKit;

namespace BuyWindow.Web.Market;

/// <summary>Sends mail through the Smtp:* mailbox, Gmail by default. A blank user or password means email is off.</summary>
public sealed class EmailSender(IConfiguration config)
{
    private string Host => config["Smtp:Host"] is { Length: > 0 } host ? host : "smtp.gmail.com";
    private int Port => int.TryParse(config["Smtp:Port"], out var port) ? port : 587;
    private string User => config["Smtp:User"] ?? "";
    private string Password => config["Smtp:Password"] ?? "";

    private SecureSocketOptions Security =>
        Enum.TryParse<SecureSocketOptions>(config["Smtp:Security"], ignoreCase: true, out var parsed)
            ? parsed
            : SecureSocketOptions.StartTls;

    public string Recipient => config["Smtp:AlertRecipient"] is { Length: > 0 } recipient ? recipient : User;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(User) && !string.IsNullOrWhiteSpace(Password);

    public async Task SendAsync(string subject, string body, CancellationToken ct = default)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("BuyWindow", User));
        message.To.Add(MailboxAddress.Parse(Recipient));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };

        using var client = new MailKit.Net.Smtp.SmtpClient();
        await client.ConnectAsync(Host, Port, Security, ct);
        await client.AuthenticateAsync(User, Password, ct);
        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);
    }
}
