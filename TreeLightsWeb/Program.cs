using TreeLightsWeb.BackgroundTaskManagement;

//var builder = WebApplication.CreateBuilder(new WebApplicationOptions() { Args = args, WebRootPath = "wwwroot" });
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddSingleton<ITreeTaskManager, TreeTaskManager>();
builder.Services.AddTransient<TreePatterns>();
builder.Services.AddHostedService<TreeControllingHostedService>();
builder.Services.AddSignalR();

if (!builder.Environment.IsDevelopment())
{
    builder.WebHost.UseUrls("http://*:5000");
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
