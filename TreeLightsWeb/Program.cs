using TreeLightsWeb.BackgroundTaskManagement;
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
var treeClient = new WledTreeClient(Environment.GetEnvironmentVariable("TreeURIBase") ?? "http://127.0.0.1", TimeSpan.FromSeconds(10), false, coords);
await treeClient.LoadStateAsync();

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddSingleton(treeClient);
builder.Services.AddSingleton<IWledClientTaskManager<WledTreeClient>, WledClientTaskManager<WledTreeClient>>();
builder.Services.AddTransient<TreePatterns>();
builder.Services.AddHostedService<WledClientControllingHostedService<WledTreeClient>>();
builder.Services.AddSignalR(o => {
    o.EnableDetailedErrors = true;
    o.MaximumReceiveMessageSize = 1024 * 1024 * 100; // 100MB
});

if (!builder.Environment.IsDevelopment())
{
    builder.WebHost.UseUrls("http://*:8080");
}

var app = builder.Build();

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

app.Run();
