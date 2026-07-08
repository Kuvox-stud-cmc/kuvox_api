using System.Net;

namespace Kuvox.Api.Modules.Shared.Infrastructure.Email;

internal static class EmailTemplate
{
    public static string Action(
        string eyebrow,
        string title,
        IReadOnlyList<string> paragraphs,
        string ctaLabel,
        string ctaUrl,
        string note)
    {
        var safeTitle = Html(title);
        var safeEyebrow = Html(eyebrow);
        var safeCtaLabel = Html(ctaLabel);
        var safeCtaUrl = Html(ctaUrl);
        var safeNote = Html(note);
        var preview = paragraphs.Count > 0 ? Html(paragraphs[0]) : safeTitle;
        var paragraphHtml = string.Concat(paragraphs.Select(paragraph =>
            $"""
            <p style="margin:0 0 16px;color:#c7c4d7;font-size:15px;line-height:1.6;">{Html(paragraph)}</p>
            """));

        return
            $$"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>{{safeTitle}}</title>
            </head>
            <body style="margin:0;padding:0;background:#131315;color:#e5e1e4;font-family:Geist,Inter,'Segoe UI',Arial,sans-serif;">
              <div style="display:none;max-height:0;overflow:hidden;opacity:0;color:transparent;">{{preview}}</div>
              <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background:#131315;margin:0;padding:0;">
                <tr>
                  <td align="center" style="padding:32px 16px;">
                    <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="max-width:560px;background:#0e0e10;border:1px solid #464554;border-radius:16px;overflow:hidden;box-shadow:0 8px 32px rgba(0,0,0,0.5);">
                      <tr>
                        <td style="padding:28px 28px 0;">
                          <table role="presentation" cellspacing="0" cellpadding="0">
                            <tr>
                              <td style="width:32px;height:32px;border-radius:10px;background:#c0c1ff;color:#1000a9;font-weight:800;font-size:16px;text-align:center;vertical-align:middle;">K</td>
                              <td style="padding-left:10px;color:#e5e1e4;font-size:18px;font-weight:700;letter-spacing:0;">Kuvox</td>
                            </tr>
                          </table>
                        </td>
                      </tr>
                      <tr>
                        <td style="padding:28px;">
                          <div style="margin:0 0 12px;color:#c0c1ff;font-size:12px;font-weight:700;letter-spacing:.08em;text-transform:uppercase;">{{safeEyebrow}}</div>
                          <h1 style="margin:0 0 18px;color:#e5e1e4;font-size:28px;line-height:1.2;font-weight:800;letter-spacing:0;">{{safeTitle}}</h1>
                          {{paragraphHtml}}
                          <table role="presentation" cellspacing="0" cellpadding="0" style="margin:28px 0 20px;">
                            <tr>
                              <td>
                                <a href="{{safeCtaUrl}}" style="display:inline-block;border-radius:10px;background:#c0c1ff;color:#1000a9;font-size:14px;font-weight:700;text-decoration:none;padding:13px 18px;">{{safeCtaLabel}}</a>
                              </td>
                            </tr>
                          </table>
                          <div style="margin:0 0 18px;padding:14px;border:1px solid #464554;border-radius:12px;background:#201f22;">
                            <p style="margin:0 0 8px;color:#c7c4d7;font-size:13px;line-height:1.5;">If the button does not work, copy and paste this link into your browser:</p>
                            <a href="{{safeCtaUrl}}" style="color:#c0c1ff;font-size:13px;line-height:1.5;word-break:break-all;text-decoration:none;">{{safeCtaUrl}}</a>
                          </div>
                          <p style="margin:0;color:#908fa0;font-size:13px;line-height:1.5;">{{safeNote}}</p>
                        </td>
                      </tr>
                      <tr>
                        <td style="padding:18px 28px;border-top:1px solid #2a2a2c;background:#1c1b1d;color:#908fa0;font-size:12px;line-height:1.5;">
                          Kuvox helps teams create, edit, and manage visual workspaces.
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

    private static string Html(string value) => WebUtility.HtmlEncode(value);
}
