using TreeLightsWeb.BackgroundTaskManagement;
using TreeLightsWeb.ImageCapture;
using TreeLightsWeb.Settings;
using WLEDClientWebHosting;
using WLEDInterface;

var builder = WebApplication.CreateBuilder(args);


var contentFileProvider = builder.Environment.ContentRootFileProvider;
var webrootCoordsFilePath = Path.Combine(builder.Environment.WebRootPath, "Config", "coordinates.csv");

string? coords = null;
if (File.Exists(webrootCoordsFilePath))
{
    coords = File.ReadAllText(webrootCoordsFilePath);
}
// Animations are sent to WLED with DDP (UDP), which is much faster than its JSON API; set TreeUseDDP=false to use JSON instead
var useDdp = !string.Equals(Environment.GetEnvironmentVariable("TreeUseDDP"), "false", StringComparison.OrdinalIgnoreCase);
var treeClient = new WledTreeClient(Environment.GetEnvironmentVariable("TreeURIBase") ?? "http://127.0.0.1", TimeSpan.FromSeconds(10), useDdp, coords);
await treeClient.LoadStateAsync();

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddSingleton(treeClient);
builder.Services.AddSingleton<IWledClientTaskManager<WledTreeClient>, WledClientTaskManager<WledTreeClient>>();
builder.Services.AddTransient<TreePatterns>();
builder.Services.AddSingleton<MusicLevels>(); // What the music page hears, for the music-reactive animation
builder.Services.AddSingleton<TreeSettingsService>(); // The Configuration page's settings (orientation, palettes, showcase)
builder.Services.AddSingleton<NowPlaying>(); // What's playing on the tree, for the home page
builder.Services.AddHostedService<WledClientControllingHostedService<WledTreeClient>>();
builder.Services.AddSingleton<CapturedImageStore>();
builder.Services.AddSingleton<ImageCaptureCoordinator>();
builder.Services.AddSignalR(o => {
    o.EnableDetailedErrors = true;
    o.MaximumReceiveMessageSize = 1024 * 1024 * 100; // 100MB
});

if (!builder.Environment.IsDevelopment())
{
    builder.WebHost.UseUrls("http://*:8080");
}

var app = builder.Build();
// (Created now, rather than when it's first needed, so the tree's client has its settings, e.g. how dim an LED can be)
app.Services.GetRequiredService<TreeSettingsService>();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapHub<TreeHub>("/treehub");
app.MapHub<NowPlayingHub>("/nowplayinghub");
// Stateful reconnect lets the capture page ride out brief network drops without losing its connection (or a frame request)
app.MapHub<ImageCaptureHub>("/imagecapturehub", options => options.AllowStatefulReconnects = true);

app.Run();
