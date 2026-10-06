using System.Globalization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SoftCo.Data;
using SoftCo.Filters;
using SoftCo.Models;
using SoftCo.Services;
using SoftCo.Services.Documents;
using SoftCo.Services.Email;
using SoftCo.Services.ExchangeRates;
using SoftCo.Services.Approvals;
using SoftCo.Services.Numbering;
using SoftCo.Services.Pdf;

var builder = WebApplication.CreateBuilder(args);

// Every money field in this application is a decimal bound from a form post. Pinning the request
// culture to InvariantCulture keeps "1234.56" parsing the same way regardless of the server's or
// the browser's locale - on a South African machine the default culture uses a comma as the
// decimal separator, which silently turns 1234.56 into 123456 on the way in.
var invariant = new[] { new CultureInfo("en-ZA") { NumberFormat = CultureInfo.InvariantCulture.NumberFormat } };
builder.Services.Configure<RequestLocalizationOptions>(o =>
{
    o.DefaultRequestCulture = new Microsoft.AspNetCore.Localization.RequestCulture(invariant[0]);
    o.SupportedCultures = invariant;
    o.SupportedUICultures = invariant;
});
CultureInfo.DefaultThreadCurrentCulture = invariant[0];
CultureInfo.DefaultThreadCurrentUICulture = invariant[0];

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(DbConnectionStringFactory.Build(builder.Configuration, builder.Environment)));

// AddIdentity rather than AddDefaultIdentity: the brief requires a branded login portal, so the
// login, logout and access-denied screens are this application's own views rather than the
// scaffolded Identity UI Razor Pages.
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.SignIn.RequireConfirmedAccount = false;

    options.Password.RequiredLength = 12;
    options.Password.RequireDigit = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireNonAlphanumeric = true;

    // Brute-force defence: five attempts, then a fifteen minute lockout.
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.Lockout.AllowedForNewUsers = true;

    options.User.RequireUniqueEmail = true;
})
    .AddEntityFrameworkStores<AppDbContext>()
    // Puts the must-change-password marker on the signed-in principal, so the filter below can
    // check it without hitting the database on every request.
    .AddClaimsPrincipalFactory<AppUserClaimsPrincipalFactory>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/Denied";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IAuditService, AuditService>();

// Purchase-order, invoice and credit-note references all come from Postgres sequences, so these
// need the request's DbContext. IPoNumberGenerator is a named delegate to the shared generator and
// must be registered after it.
builder.Services.AddScoped<IDocumentNumberGenerator, DocumentNumberGenerator>();
builder.Services.AddScoped<IPoNumberGenerator, PoNumberGenerator>();

// Order attachments live under App_Data, outside wwwroot.
builder.Services.AddSingleton<IDocumentStore, DocumentStore>();

// PDF rendering. PDFsharp resolves fonts through process-wide static state rather than DI, so the
// resolver is assigned once here; Verify() then reads every declared face, turning a missing or
// mis-pathed font file into a startup failure instead of a 500 the first time somebody asks the
// Financial Director to approve something.
var fontResolver = new EmbeddedFontResolver();
fontResolver.Verify();
PdfSharp.Fonts.GlobalFontSettings.FontResolver = fontResolver;

builder.Services.Configure<CompanyOptions>(builder.Configuration.GetSection(CompanyOptions.SectionName));
builder.Services.AddSingleton<IPdfRenderer, MigraDocPdfRenderer>();
builder.Services.AddScoped<IPurchaseOrderDocumentService, PurchaseOrderDocumentService>();

// The approval workflow. The judgement lives in the pure ApprovalRules; these do the I/O.
builder.Services.AddScoped<IPurchaseOrderApprovalService, PurchaseOrderApprovalService>();
builder.Services.AddScoped<IApprovalNotifier, ApprovalNotifier>();

// Email: in Development the composed message is written to App_Data/sent-email instead of being
// sent, so the flow can be exercised end to end without credentials and without the risk of
// mailing a real supplier from a developer's machine. Production uses SMTP with the same
// interface, so no calling code changes when credentials are supplied.
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection(EmailOptions.SectionName));
if (builder.Environment.IsDevelopment())
    builder.Services.AddSingleton<IEmailService, FileDropEmailService>();
else
    builder.Services.AddSingleton<IEmailService, SmtpEmailService>();

builder.Services.Configure<ExchangeRateOptions>(builder.Configuration.GetSection(ExchangeRateOptions.SectionName));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpClient("exchange-rates", client =>
{
    client.Timeout = TimeSpan.FromSeconds(8);
    client.MaxResponseContentBufferSize = 65_536;
})
    // ExchangeRate-API puts its key in the URL path, so default HTTP request logging is unsafe.
    .RemoveAllLoggers()
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5)
    });
builder.Services.AddSingleton(sp => new ExchangeRateService(
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("exchange-rates"),
    sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ExchangeRateOptions>>(),
    sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<ILogger<ExchangeRateService>>()));

builder.Services.AddControllersWithViews(options =>
{
    // Registered globally rather than per-controller: a controller added later would otherwise be
    // reachable by someone still holding an administrator-set password.
    options.Filters.Add<MustChangePasswordFilter>();
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseRequestLocalization();
app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

// Apply migrations and seed roles/bootstrap admin at startup. Behind ECS this runs once per task;
// EF's migration history table makes concurrent attempts safe - the loser sees the migration
// already applied rather than applying it twice.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await SeedData.InitializeAsync(scope.ServiceProvider, app.Configuration, app.Environment);
    await TrackerSeed.SeedAsync(db, app.Environment);
}

app.Run();
/* Developer: Christopher Graham
   Code src: please note:
        This code has been created similtaniously with my IDA program,
        it has been created for a subcompany of inhouse design so,
        a lot of the code features have been taken from IDA for more
        effective development*/
