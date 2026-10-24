
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Net;
using System.Net.Mail;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

// Load optional local secrets.json files.
// On Render, configure secrets using Environment Variables.
foreach (var candidate in FindSecretsFiles(
    AppContext.BaseDirectory,
    Environment.CurrentDirectory))
{
    builder.Configuration.AddJsonFile(
        candidate,
        optional: true,
        reloadOnChange: false);
}

var launcherTickets =
    new ConcurrentDictionary<string, LauncherTicket>(
        StringComparer.Ordinal);

var emailChallenges =
    new ConcurrentDictionary<string, EmailChallenge>(
        StringComparer.OrdinalIgnoreCase);

var emailSendTimes =
    new ConcurrentDictionary<string, DateTimeOffset>(
        StringComparer.OrdinalIgnoreCase);

const string CookieScheme =
    CookieAuthenticationDefaults.AuthenticationScheme;

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = CookieScheme;
        options.DefaultAuthenticateScheme = CookieScheme;
        options.DefaultSignInScheme = CookieScheme;
    })
    .AddCookie(options =>
    {
        options.Cookie.Name = "FortuPurr.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.LoginPath = "/";
        options.AccessDeniedPath = "/";
    })
    .AddGoogle("Google", options =>
    {
        options.ClientId = builder.Configuration["Authentication:Google:ClientId"] ?? "";

        options.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"] ?? "";

        options.CallbackPath = "/signin-google";
        options.SaveTokens = false;
    })
    .AddOAuth("Discord", options =>
    {
        options.ClientId = builder.Configuration["Authentication:Discord:ClientId"] ?? "";

        options.ClientSecret = builder.Configuration["Authentication:Discord:ClientSecret"] ?? "";

        options.CallbackPath = "/signin-discord";
        options.AuthorizationEndpoint =
            "https://discord.com/oauth2/authorize";
        options.TokenEndpoint =
            "https://discord.com/api/oauth2/token";
        options.UserInformationEndpoint =
            "https://discord.com/api/users/@me";

        options.Scope.Add("identify");
        options.Scope.Add("email");

        options.ClaimActions.MapJsonKey(
            ClaimTypes.NameIdentifier, "id");
        options.ClaimActions.MapJsonKey(
            ClaimTypes.Name, "username");
        options.ClaimActions.MapJsonKey(
            ClaimTypes.Email, "email");

        options.Events = new OAuthEvents
        {
            OnCreatingTicket = async context =>
            {
                using var request = new HttpRequestMessage(
                    HttpMethod.Get,
                    context.Options.UserInformationEndpoint);

                request.Headers.Authorization =
                    new AuthenticationHeaderValue(
                        "Bearer", context.AccessToken);

                request.Headers.UserAgent.ParseAdd(
                    "FortuPurr.AuthApi");

                using var response =
                    await context.Backchannel.SendAsync(
                        request,
                        context.HttpContext.RequestAborted);

                response.EnsureSuccessStatusCode();

                using var document = JsonDocument.Parse(
                    await response.Content.ReadAsStringAsync(
                        context.HttpContext.RequestAborted));

                context.RunClaimActions(document.RootElement);
            }
        };
    })
    .AddOAuth("GitHub", options =>
    {
        options.ClientId = builder.Configuration["Authentication:GitHub:ClientId"] ?? "";

        options.ClientSecret = builder.Configuration["Authentication:GitHub:ClientSecret"] ?? "";

        options.CallbackPath = "/signin-github";
        options.AuthorizationEndpoint =
            "https://github.com/login/oauth/authorize";
        options.TokenEndpoint =
            "https://github.com/login/oauth/access_token";
        options.UserInformationEndpoint =
            "https://api.github.com/user";

        options.Scope.Add("read:user");

        options.ClaimActions.MapJsonKey(
            ClaimTypes.NameIdentifier, "id");
        options.ClaimActions.MapJsonKey(
            ClaimTypes.Name, "login");
        options.ClaimActions.MapJsonKey(
            ClaimTypes.Email, "email");

        options.Events = new OAuthEvents
        {
            OnCreatingTicket = async context =>
            {
                using var request = new HttpRequestMessage(
                    HttpMethod.Get,
                    context.Options.UserInformationEndpoint);

                request.Headers.Authorization =
                    new AuthenticationHeaderValue(
                        "Bearer", context.AccessToken);

                request.Headers.UserAgent.ParseAdd(
                    "FortuPurr.AuthApi");

                using var response =
                    await context.Backchannel.SendAsync(
                        request,
                        context.HttpContext.RequestAborted);

                response.EnsureSuccessStatusCode();

                using var document = JsonDocument.Parse(
                    await response.Content.ReadAsStringAsync(
                        context.HttpContext.RequestAborted));

                context.RunClaimActions(document.RootElement);
            }
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

// Configure forwarded headers before middleware that depends on
// the original request scheme, such as authentication and redirects.
// Keep the default trusted-proxy restrictions unless you explicitly
// configure Render's proxy addresses.
if (!app.Environment.IsDevelopment())
{
    app.UseForwardedHeaders(new ForwardedHeadersOptions
    {
        ForwardedHeaders =
            ForwardedHeaders.XForwardedFor |
            ForwardedHeaders.XForwardedProto
    });

    app.UseExceptionHandler("/error");
    app.UseHsts();
}

// Render terminates public HTTPS at its proxy.
// Do not force an HTTPS redirect on the internal container connection.
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    service = "FortuPurr.AuthApi",
    timestamp = DateTimeOffset.UtcNow
}));

app.MapGet("/", (HttpContext context) =>
{
    if (context.User.Identity?.IsAuthenticated == true)
        return Results.Redirect("/login-success");

    var html = """
    <!doctype html>
    <html lang="th">
    <head>
      <meta charset="utf-8">
      <meta name="viewport" content="width=device-width, initial-scale=1">
      <title>FortuPurr Login</title>
      <style>
        body {
          font-family: Arial, sans-serif;
          background: #111827;
          color: white;
          display: grid;
          place-items: center;
          min-height: 100vh;
          margin: 0;
        }
        main {
          width: min(360px, 90%);
          background: #1f2937;
          padding: 28px;
          border-radius: 18px;
        }
        a {
          display: block;
          padding: 13px;
          margin-top: 12px;
          border-radius: 9px;
          color: white;
          background: #374151;
          text-align: center;
          text-decoration: none;
        }
        a:hover { background: #4b5563; }
      </style>
    </head>
    <body>
      <main>
        <h1>FortuPurr</h1>
        <p>เข้าสู่ระบบด้วยบัญชีของคุณ</p>
        <a href="/login/Google">เข้าสู่ระบบด้วย Google</a>
        <a href="/login/Discord">เข้าสู่ระบบด้วย Discord</a>
        <a href="/login/GitHub">เข้าสู่ระบบด้วย GitHub</a>
      </main>
    </body>
    </html>
    """;

    return Results.Content(html, "text/html; charset=utf-8");
});

app.MapGet("/login/{provider}", (string provider) =>
{
    var allowedProviders = new[] { "Google", "Discord", "GitHub" };

    var actualProvider = allowedProviders.FirstOrDefault(
        p => string.Equals(
            p, provider, StringComparison.OrdinalIgnoreCase));

    if (actualProvider is null)
        return Results.BadRequest("ไม่รองรับผู้ให้บริการนี้");

    if (string.IsNullOrWhiteSpace(builder.Configuration[$"Authentication:{actualProvider}:ClientId"]) ||
        string.IsNullOrWhiteSpace(builder.Configuration[$"Authentication:{actualProvider}:ClientSecret"]))
    {
        return Results.Problem(
            $"ยังไม่ได้ตั้งค่า OAuth ของ {actualProvider} บนเซิร์ฟเวอร์",
            statusCode: 503);
    }

    var properties = new AuthenticationProperties
    {
        RedirectUri = "/login-success"
    };

    return Results.Challenge(
        properties, new[] { actualProvider });
});

// Desktop launcher OAuth flow
app.MapGet(
    "/launcher/login/{provider}",
    (string provider, int port, string state) =>
{
    var allowed = new[] { "Google", "Discord", "GitHub" };

    var actual = allowed.FirstOrDefault(
        p => string.Equals(
            p, provider, StringComparison.OrdinalIgnoreCase));

    if (actual is null)
        return Results.BadRequest("ไม่รองรับผู้ให้บริการนี้");

    if (string.IsNullOrWhiteSpace(builder.Configuration[$"Authentication:{actual}:ClientId"]) ||
        string.IsNullOrWhiteSpace(builder.Configuration[$"Authentication:{actual}:ClientSecret"]))
    {
        return Results.Problem(
            $"ยังไม่ได้ตั้งค่า OAuth ของ {actual} บนเซิร์ฟเวอร์",
            statusCode: 503);
    }

    if (port is < 1024 or > 65535 ||
        string.IsNullOrWhiteSpace(state) ||
        state.Length > 128)
    {
        return Results.BadRequest(
            "พารามิเตอร์ launcher ไม่ถูกต้อง");
    }

    var properties = new AuthenticationProperties
    {
        RedirectUri = "/launcher/complete"
    };

    properties.Items["launcher_port"] =
        port.ToString(
            System.Globalization.CultureInfo.InvariantCulture);

    properties.Items["launcher_state"] = state;
    properties.Items["launcher_provider"] = actual;

    return Results.Challenge(properties, new[] { actual });
});

app.MapGet("/launcher/complete", (HttpContext context) =>
{
    if (context.User.Identity?.IsAuthenticated != true)
        return Results.Redirect("/");

    var auth = context.Features
        .Get<Microsoft.AspNetCore.Authentication.IAuthenticateResultFeature>()
        ?.AuthenticateResult;

    string? portText = null;
    string? state = null;
    string provider = "OAuth";

    if (auth?.Properties is { } authProperties)
    {
        authProperties.Items.TryGetValue(
            "launcher_port", out portText);

        authProperties.Items.TryGetValue(
            "launcher_state", out state);

        if (authProperties.Items.TryGetValue(
                "launcher_provider", out var savedProvider) &&
            !string.IsNullOrWhiteSpace(savedProvider))
        {
            provider = savedProvider;
        }
    }

    if (!int.TryParse(portText, out var port) ||
        port is < 1024 or > 65535 ||
        string.IsNullOrWhiteSpace(state))
    {
        return Results.BadRequest(
            "ไม่พบข้อมูล launcher ที่ถูกต้อง กรุณาเริ่มเข้าสู่ระบบใหม่");
    }

    var ticket = Convert.ToHexString(
        RandomNumberGenerator.GetBytes(32));

    launcherTickets[ticket] = new LauncherTicket(
        context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "",
        context.User.FindFirst(ClaimTypes.Name)?.Value
            ?? context.User.FindFirst("name")?.Value
            ?? "ผู้ใช้",
        context.User.FindFirst(ClaimTypes.Email)?.Value ?? "",
        provider,
        DateTimeOffset.UtcNow.AddMinutes(2));

    var now = DateTimeOffset.UtcNow;

    foreach (var item in launcherTickets
        .Where(x => x.Value.ExpiresAt <= now).ToArray())
    {
        launcherTickets.TryRemove(item.Key, out _);
    }

    var callback =
        $"http://127.0.0.1:{port}/callback" +
        $"?ticket={Uri.EscapeDataString(ticket)}" +
        $"&state={Uri.EscapeDataString(state)}";

    return Results.Redirect(callback);
});

app.MapPost("/launcher/exchange", async (HttpContext context) =>
{
    using var body = await JsonDocument.ParseAsync(
        context.Request.Body,
        cancellationToken: context.RequestAborted);

    var ticket = body.RootElement.TryGetProperty(
        "ticket", out var value)
        ? value.GetString()
        : null;

    if (string.IsNullOrWhiteSpace(ticket) ||
        !launcherTickets.TryRemove(ticket, out var result) ||
        result.ExpiresAt <= DateTimeOffset.UtcNow)
    {
        return Results.Unauthorized();
    }

    return Results.Json(new
    {
        result.Subject,
        result.Name,
        result.Email,
        result.Provider
    });
});

// Send email verification code
app.MapPost(
    "/email/send-code",
    async (EmailCodeRequest input, HttpContext context) =>
{
    var email = (input.Email ?? "").Trim().ToLowerInvariant();
    var purpose = input.Purpose is "link" ? "link" : "register";

    if (email.Length > 254 ||
        !MailAddress.TryCreate(email, out var parsed) ||
        !string.Equals(
            parsed.Address, email, StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest(new
        {
            ok = false,
            message = "กรุณากรอกอีเมลให้ถูกต้อง"
        });
    }

    var now = DateTimeOffset.UtcNow;

    if (emailSendTimes.TryGetValue(email, out var last) &&
        now - last < TimeSpan.FromSeconds(60))
    {
        return Results.Json(new
        {
            ok = false,
            message = "กรุณารอ 60 วินาทีก่อนขอรหัสใหม่"
        }, statusCode: 429);
    }

    var smtpHost = builder.Configuration["Email:Smtp:Host"];
    var smtpUser = builder.Configuration["Email:Smtp:Username"];
    var smtpPass = builder.Configuration["Email:Smtp:AppPassword"];
    var fromAddress = builder.Configuration["Email:Smtp:From"] ?? smtpUser;

    var smtpPort = int.TryParse(
        builder.Configuration["Email:Smtp:Port"],
        out var parsedPort) ? parsedPort : 587;

    if (string.IsNullOrWhiteSpace(smtpHost) ||
        string.IsNullOrWhiteSpace(smtpUser) ||
        string.IsNullOrWhiteSpace(smtpPass) ||
        string.IsNullOrWhiteSpace(fromAddress))
    {
        return Results.Problem(
            "ยังไม่ได้ตั้งค่า Gmail SMTP",
            statusCode: 503);
    }

    var code = RandomNumberGenerator.GetInt32(0, 1_000_000)
        .ToString("D6", System.Globalization.CultureInfo.InvariantCulture);

    var salt = RandomNumberGenerator.GetBytes(16);
    var hash = SHA256.HashData(
        salt.Concat(System.Text.Encoding.UTF8.GetBytes(code)).ToArray());

    emailChallenges[email] = new EmailChallenge(
        Convert.ToBase64String(salt),
        Convert.ToBase64String(hash),
        purpose,
        now.AddMinutes(10),
        0);

    emailSendTimes[email] = now;

    try
    {
        using var message = new MailMessage();
        message.From = new MailAddress(fromAddress, "FortuPurr");
        message.To.Add(new MailAddress(email));
        message.Subject = "รหัสยืนยันอีเมล FortuPurr";
        message.Body =
            $"รหัสยืนยันอีเมล FortuPurr ของคุณคือ {code}\r\n\r\n" +
            "รหัสนี้หมดอายุภายใน 10 นาที และใช้ได้ครั้งเดียว";
        message.IsBodyHtml = false;

        using var smtp = new SmtpClient(smtpHost, smtpPort)
        {
            EnableSsl = true,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(smtpUser, smtpPass),
            Timeout = 15000
        };

        await smtp.SendMailAsync(message, context.RequestAborted);

        return Results.Ok(new
        {
            ok = true,
            message = "ส่งรหัสยืนยันไปยังอีเมลแล้ว"
        });
    }
    catch
    {
        emailChallenges.TryRemove(email, out _);
        emailSendTimes.TryRemove(email, out _);

        return Results.Problem(
            "ส่งอีเมลไม่สำเร็จ โปรดตรวจสอบ Gmail App Password และ SMTP",
            statusCode: 502);
    }
});

// Verify email code
app.MapPost(
    "/email/verify-code",
    (EmailCodeVerifyRequest input) =>
{
    var email = (input.Email ?? "").Trim().ToLowerInvariant();
    var code = (input.Code ?? "").Trim();
    var purpose = input.Purpose is "link" ? "link" : "register";

    if (code.Length != 6 || code.Any(c => c < '0' || c > '9'))
    {
        return Results.BadRequest(new
        {
            ok = false,
            message = "กรุณากรอกรหัส 6 หลัก"
        });
    }

    if (!emailChallenges.TryGetValue(email, out var challenge) ||
        challenge.ExpiresAt <= DateTimeOffset.UtcNow ||
        !string.Equals(challenge.Purpose, purpose, StringComparison.Ordinal))
    {
        emailChallenges.TryRemove(email, out _);

        return Results.BadRequest(new
        {
            ok = false,
            message = "รหัสหมดอายุหรือไม่มีรหัสที่ใช้งานได้ กรุณาขอรหัสใหม่"
        });
    }

    if (challenge.Attempts >= 5)
    {
        emailChallenges.TryRemove(email, out _);

        return Results.Json(new
        {
            ok = false,
            message = "กรอกรหัสผิดเกินกำหนด กรุณาขอรหัสใหม่"
        }, statusCode: 429);
    }

    var actual = SHA256.HashData(
        Convert.FromBase64String(challenge.Salt)
        .Concat(System.Text.Encoding.UTF8.GetBytes(code))
        .ToArray());

    var expected = Convert.FromBase64String(challenge.CodeHash);

    if (!CryptographicOperations.FixedTimeEquals(actual, expected))
    {
        emailChallenges[email] =
            challenge with { Attempts = challenge.Attempts + 1 };

        return Results.BadRequest(new
        {
            ok = false,
            message = $"รหัสไม่ถูกต้อง (เหลือ {4 - challenge.Attempts} ครั้ง)"
        });
    }

    emailChallenges.TryRemove(email, out _);
    emailSendTimes.TryRemove(email, out _);

    return Results.Ok(new
    {
        ok = true,
        email,
        message = "ยืนยันอีเมลสำเร็จ"
    });
});

app.MapGet("/login-success", (ClaimsPrincipal user) =>
{
    if (user.Identity?.IsAuthenticated != true)
        return Results.Redirect("/");

    var name = user.FindFirst(ClaimTypes.Name)?.Value
        ?? user.FindFirst("name")?.Value
        ?? "ผู้ใช้";

    var safeName = WebUtility.HtmlEncode(name);

    var html = $$"""
    <!doctype html>
    <html lang="th">
    <head>
      <meta charset="utf-8">
      <meta name="viewport" content="width=device-width, initial-scale=1">
      <title>เข้าสู่ระบบสำเร็จ</title>
    </head>
    <body>
      <h1>เข้าสู่ระบบสำเร็จ</h1>
      <p>ยินดีต้อนรับ {{safeName}}</p>
      <p><a href="/me">ดูข้อมูลบัญชี</a></p>
      <form method="post" action="/logout">
        <button type="submit">ออกจากระบบ</button>
      </form>
    </body>
    </html>
    """;

    return Results.Content(html, "text/html; charset=utf-8");
}).RequireAuthorization();

app.MapGet("/me", (ClaimsPrincipal user) =>
{
    var claims = user.Claims.Select(claim => new
    {
        claim.Type,
        claim.Value
    });

    return Results.Ok(new
    {
        IsAuthenticated = user.Identity?.IsAuthenticated == true,
        Provider = user.FindFirst("identity_provider")?.Value,
        Name = user.FindFirst(ClaimTypes.Name)?.Value
            ?? user.FindFirst("name")?.Value,
        Email = user.FindFirst(ClaimTypes.Email)?.Value,
        Claims = claims
    });
}).RequireAuthorization();

app.MapPost("/logout", async (HttpContext context) =>
{
    await context.SignOutAsync(CookieScheme);
    return Results.Redirect("/");
});

app.MapGet("/error", () =>
    Results.Problem("เกิดข้อผิดพลาดภายในระบบ"));

app.Run();

static IEnumerable<string> FindSecretsFiles(params string[] startingDirectories)
{
    var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    foreach (var start in startingDirectories.Where(Directory.Exists))
    {
        var current = new DirectoryInfo(start);

        for (var depth = 0;
             current is not null && depth < 6;
             depth++, current = current.Parent)
        {
            var candidate = Path.Combine(current.FullName, "secrets.json");

            if (File.Exists(candidate) && found.Add(candidate))
                yield return candidate;
        }
    }
}

static string RequiredSetting(IConfiguration configuration, string key)
{
    var value = configuration[key];

    if (string.IsNullOrWhiteSpace(value))
    {
        throw new InvalidOperationException(
            $"ยังไม่ได้ตั้งค่า {key} ใน Configuration หรือ Environment Variables");
    }

    return value;
}

record LauncherTicket(
    string Subject,
    string Name,
    string Email,
    string Provider,
    DateTimeOffset ExpiresAt);

record EmailCodeRequest(string? Email, string? Purpose);

record EmailCodeVerifyRequest(
    string? Email,
    string? Code,
    string? Purpose);

record EmailChallenge(
    string Salt,
    string CodeHash,
    string Purpose,
    DateTimeOffset ExpiresAt,
    int Attempts);
