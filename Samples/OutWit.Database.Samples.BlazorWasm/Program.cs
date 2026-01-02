using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;
using OutWit.Database.Samples.BlazorWasm;
using OutWit.Database.Samples.BlazorWasm.Data;
using OutWit.Database.Samples.BlazorWasm.ViewModels;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Add MudBlazor
builder.Services.AddMudServices();

// Add Database Services
builder.Services.AddSingleton<DatabaseService>();
builder.Services.AddSingleton<ContactRepository>();
builder.Services.AddSingleton<NoteRepository>();

// Add ViewModels as Transient (new instance per page)
builder.Services.AddTransient<DashboardViewModel>();
builder.Services.AddTransient<ContactsViewModel>();
builder.Services.AddTransient<NotesViewModel>();

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

await builder.Build().RunAsync();
