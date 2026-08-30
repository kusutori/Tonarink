using Tonarink.Application;
using Tonarink.Web;
using Tonarink.Web.Components;
using Tonarink.LocalSend;
using System.Globalization;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddSingleton<IPlatformServices, BrowserPlatformServices>();
builder.Services.AddSingleton<TonarinkAppState>();
builder.Services.AddSingleton<ITonarinkRuntime, LocalSendRuntime>();
builder.Services.AddHostedService<TonarinkNodeHostedService>();
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    var cultures = new[] { new CultureInfo("en-US"), new CultureInfo("zh-CN") };
    options.DefaultRequestCulture = new("en-US");
    options.SupportedCultures = cultures;
    options.SupportedUICultures = cultures;
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseRequestLocalization();
app.UseMiddleware<TonarinkWebAccessMiddleware>();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(Tonarink.Blazor.Shared.Routes).Assembly);

app.Run();
