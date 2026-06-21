using BadDiSample;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>();
builder.Services.AddHostedService<ReportWorker>();
builder.Services.AddSingleton<MutableClock>();
builder.Services.AddSingleton<SingletonPublisher>();
builder.Services.AddTransient<MutableFormatter>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddScoped<INotifier, EmailNotifier>();
builder.Services.AddScoped<IA, A>();
builder.Services.AddScoped<IB, B>();
builder.Services.AddScoped<BigService>();
builder.Services.AddScoped<ConcreteDependency>();

var app = builder.Build();
app.MapGet("/", () => "bad");
app.Run();

namespace BadDiSample
{
    public sealed class AppDbContext
    {
    }

    public sealed class ReportWorker(AppDbContext db) : BackgroundService
    {
        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _ = db;
            return Task.CompletedTask;
        }
    }

    public sealed class MutableClock
    {
        public DateTime LastSeen { get; set; }
    }

    public sealed class MutableFormatter
    {
    }

    public sealed class SingletonPublisher(MutableFormatter formatter)
    {
    }

    public interface IOrderService
    {
    }

    public sealed class OrderService(ConcreteDependency concrete, IConfiguration configuration, IServiceProvider serviceProvider) : IOrderService
    {
        public void Run()
        {
            _ = serviceProvider.GetRequiredService<IPaymentService>();
            _ = configuration["Key"];
            _ = concrete;
        }
    }

    public sealed class ConcreteDependency
    {
    }

    public interface IPaymentService
    {
    }

    public sealed class PaymentService(INotifier notifier) : IPaymentService
    {
    }

    public interface INotifier
    {
    }

    public sealed class EmailNotifier : INotifier
    {
    }

    public interface IA
    {
    }

    public interface IB
    {
    }

    public sealed class A(IB b) : IA
    {
    }

    public sealed class B(IA a) : IB
    {
    }

    public sealed class BigService(
        IOrderService orders,
        IPaymentService payments,
        INotifier notifier,
        IA a,
        IB b,
        ConcreteDependency concrete,
        MutableClock clock)
    {
    }

    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddDbContext<TContext>(this IServiceCollection services)
            where TContext : class
        {
            return services.AddScoped<TContext>();
        }
    }
}
