using StrangerThingsLights.BackgroundTaskManagement;
using WLEDClientWebHosting;
using WLEDInterface;

var builder = WebApplication.CreateBuilder(args);

var wledClient = new WledClient(Environment.GetEnvironmentVariable("WledURIBase") ?? "http://127.0.0.1", TimeSpan.FromSeconds(10));
await wledClient.LoadStateAsync();

// Add services to the container.
builder.Services.AddControllersWithViews();

builder.Services.AddSingleton(wledClient);
builder.Services.AddSingleton<IWledClientTaskManager<WledClient>, WledClientTaskManager<WledClient>>();
builder.Services.AddTransient<WledPatterns>();
builder.Services.AddHostedService<WledClientControllingHostedService<WledClient>>();

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

app.Run();
