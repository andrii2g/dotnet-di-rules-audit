using GoodDiSample;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseDefaultServiceProvider(options =>
{
    options.ValidateScopes = true;
    options.ValidateOnBuild = true;
});

builder.Services.AddApplicationServices();
builder.Services.AddOptions<EmailOptions>().Bind(builder.Configuration.GetSection("Email")).ValidateOnStart();
builder.Services.AddHostedService<ReportWorker>();

var app = builder.Build();
app.MapGet("/", (IOrderService orders) => orders.Create());
app.Run();

namespace GoodDiSample
{
    public interface IOrderService
    {
        string Create();
    }

    public interface IEmailSender
    {
        void Send(string message);
    }

    public sealed class OrderService(IEmailSender sender, IOptions<EmailOptions> options) : IOrderService
    {
        public string Create()
        {
            sender.Send(options.Value.FromAddress);
            return "ok";
        }
    }

    public sealed class EmailSender : IEmailSender
    {
        public void Send(string message)
        {
        }
    }

    public sealed class EmailOptions
    {
        public string FromAddress { get; init; } = "";
    }

    public sealed class AppDbContext
    {
    }

    public sealed class ReportWorker(IServiceScopeFactory scopeFactory) : BackgroundService
    {
        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var scope = scopeFactory.CreateScope();
            _ = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return Task.CompletedTask;
        }
    }

    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddApplicationServices(this IServiceCollection services)
        {
            services.AddDbContext<AppDbContext>();
            services.AddScoped<IOrderService, OrderService>();
            services.AddTransient<IEmailSender, EmailSender>();
            return services;
        }

        public static IServiceCollection AddDbContext<TContext>(this IServiceCollection services)
            where TContext : class
        {
            return services.AddScoped<TContext>();
        }
    }
}
