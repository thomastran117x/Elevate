using System.Net;

namespace backend.worker.email_worker;

/// <summary>
/// Builds the shared, branded HTML chrome for every outbound email so that all
/// message types share one consistent look that mirrors the EventXperience
/// frontend (teal accent monogram, white rounded card on a cool gray page,
/// teal call-to-action button, wordmark + tagline footer).
///
/// The markup is intentionally email-safe: table-based layout, inline CSS only,
/// no flexbox/grid, and a solid-color button fallback for Outlook.
/// </summary>
internal static class EmailLayout
{
    // Design tokens (frontend light theme -> email-safe values).
    private const string PageBackground = "#f3f6fa";
    private const string CardBackground = "#ffffff";
    private const string CardBorder = "#e2e8f0";
    private const string HeadingColor = "#0f172a";
    private const string BodyColor = "#475569";
    private const string SubtleColor = "#64748b";
    private const string AccentColor = "#0f766e";
    private const string GradientCss = "linear-gradient(to right, #0f766e, #0d9488)";
    private const string CodeWellBackground = "#eef3f8";
    private const string FontStack = "-apple-system, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif";
    private const string Tagline = "A modern platform for creating, managing, and scaling unforgettable event experiences.";

    private static string Text(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    /// <summary>Card heading, styled like a frontend section title.</summary>
    public static string Heading(string text) =>
        $"""<h1 style="margin:0 0 16px;font-family:{FontStack};font-size:24px;line-height:1.25;font-weight:700;color:{HeadingColor};">{Text(text)}</h1>""";

    /// <summary>Body paragraph. <paramref name="text"/> is treated as plain text and HTML-encoded.</summary>
    public static string Paragraph(string text) =>
        $"""<p style="margin:0 0 16px;font-family:{FontStack};font-size:15px;line-height:1.6;color:{BodyColor};">{Text(text)}</p>""";

    /// <summary>Smaller, muted helper text (e.g. expiry notes, "didn't request this?").</summary>
    public static string MutedNote(string text) =>
        $"""<p style="margin:16px 0 0;font-family:{FontStack};font-size:13px;line-height:1.6;color:{SubtleColor};">{Text(text)}</p>""";

    /// <summary>Bulletproof gradient CTA button (solid accent fallback in Outlook).</summary>
    public static string Button(string url, string label)
    {
        var safeUrl = Text(url);
        return $"""
        <table role="presentation" cellpadding="0" cellspacing="0" border="0" style="margin:24px 0;">
          <tr>
            <td align="center" bgcolor="{AccentColor}" style="border-radius:12px;background-color:{AccentColor};background-image:{GradientCss};">
              <a href="{safeUrl}" target="_blank" style="display:inline-block;padding:12px 28px;font-family:{FontStack};font-size:14px;font-weight:600;line-height:1;color:#ffffff;text-decoration:none;border-radius:12px;">{Text(label)}</a>
            </td>
          </tr>
        </table>
        """;
    }

    /// <summary>Prominent verification-code well.</summary>
    public static string CodeBlock(string code) =>
        $"""
        <table role="presentation" cellpadding="0" cellspacing="0" border="0" width="100%" style="margin:8px 0 16px;">
          <tr>
            <td align="center" style="background-color:{CodeWellBackground};border-radius:12px;padding:18px 24px;">
              <div style="font-family:'SFMono-Regular',Consolas,'Liberation Mono',Menlo,monospace;font-size:28px;font-weight:700;letter-spacing:8px;color:{HeadingColor};">{Text(code)}</div>
            </td>
          </tr>
        </table>
        """;

    /// <summary>Plain-URL fallback for clients that strip the button.</summary>
    public static string LinkFallback(string url) =>
        $"""
        <p style="margin:0 0 4px;font-family:{FontStack};font-size:13px;line-height:1.5;color:{SubtleColor};">Or paste this link into your browser:</p>
        <p style="margin:0;font-family:{FontStack};font-size:13px;line-height:1.5;word-break:break-all;"><a href="{Text(url)}" target="_blank" style="color:{AccentColor};text-decoration:underline;">{Text(url)}</a></p>
        """;

    /// <summary>Wraps the assembled card body in the full branded document.</summary>
    public static string Document(string preheader, string bodyContent)
    {
        var year = DateTime.UtcNow.Year;

        return $"""
        <!DOCTYPE html>
        <html lang="en" xmlns="http://www.w3.org/1999/xhtml">
        <head>
          <meta charset="utf-8" />
          <meta name="viewport" content="width=device-width, initial-scale=1" />
          <meta http-equiv="X-UA-Compatible" content="IE=edge" />
          <meta name="color-scheme" content="light" />
          <meta name="supported-color-schemes" content="light" />
          <title>EventXperience</title>
        </head>
        <body style="margin:0;padding:0;background-color:{PageBackground};">
          <span style="display:none !important;visibility:hidden;opacity:0;color:transparent;height:0;width:0;overflow:hidden;mso-hide:all;">{Text(preheader)}</span>
          <table role="presentation" cellpadding="0" cellspacing="0" border="0" width="100%" style="background-color:{PageBackground};">
            <tr>
              <td align="center" style="padding:32px 16px;">
                <table role="presentation" cellpadding="0" cellspacing="0" border="0" width="600" style="width:600px;max-width:600px;">
                  <!-- Header / brand -->
                  <tr>
                    <td style="padding:0 8px 20px;">
                      <table role="presentation" cellpadding="0" cellspacing="0" border="0">
                        <tr>
                          <td valign="middle" style="padding-right:12px;">
                            <div style="width:40px;height:40px;border-radius:12px;background-color:{AccentColor};background-image:{GradientCss};text-align:center;">
                              <span style="display:inline-block;line-height:40px;font-family:{FontStack};font-size:15px;font-weight:800;letter-spacing:1px;color:#ffffff;">EX</span>
                            </div>
                          </td>
                          <td valign="middle">
                            <span style="font-family:{FontStack};font-size:22px;font-weight:800;letter-spacing:-0.5px;"><span style="color:{AccentColor};">Event</span><span style="color:{HeadingColor};">Xperience</span></span>
                          </td>
                        </tr>
                      </table>
                    </td>
                  </tr>
                  <!-- Card -->
                  <tr>
                    <td style="background-color:{CardBackground};border:1px solid {CardBorder};border-radius:24px;padding:32px;">
                      {bodyContent}
                    </td>
                  </tr>
                  <!-- Footer -->
                  <tr>
                    <td style="padding:24px 8px 0;">
                      <p style="margin:0 0 8px;font-family:{FontStack};font-size:13px;line-height:1.6;color:{SubtleColor};">{Text(Tagline)}</p>
                      <p style="margin:0;font-family:{FontStack};font-size:12px;line-height:1.6;color:{SubtleColor};">You received this email because of activity on your EventXperience account.</p>
                      <p style="margin:8px 0 0;font-family:{FontStack};font-size:12px;line-height:1.6;color:{SubtleColor};">&copy; {year} EventXperience</p>
                    </td>
                  </tr>
                </table>
              </td>
            </tr>
          </table>
        </body>
        </html>
        """;
    }
}
