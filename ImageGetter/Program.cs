using ImageGetter.Extensions;
using ImageGetter.Models;
using ImageGetter.Services;

namespace ImageGetter
{
    public class Program
    {
        public static async Task Main(string[] args)
        {            
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
            builder.WebHost.UseSentry(o =>
            {
                o.Dsn = "https://6fb306e657459615387132566e5ab4cf@o4510574883373056.ingest.de.sentry.io/4510574885666896";
                // When configuring for the first time, to see what the SDK is doing:
                //o.Debug = true;

                //o.MinimumBreadcrumbLevel = LogLevel.Information; // Logs captured as breadcrumbs
                //o.MinimumEventLevel = LogLevel.Error; // Logs sent as events
                o.EnableLogs = true;
                o.TracesSampleRate = 1.0; // Capture 100% of transactions for performance monitoring.
            });

            //builder.Services.AddControllers();

            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddHttpClient();
            builder.Services.AddSwaggerGen();
            builder.Services.AddCors(options =>
            {
                options.AddPolicy("AllowSpecificOrigins",
                builder =>
                {
                    builder.AllowAnyOrigin()
                           .AllowAnyHeader()
                           .WithMethods("GET", "HEAD", "OPTIONS");
                });
            });
            builder.Services.AddLogging(options =>
            {
                options.ClearProviders();
                options.AddSimpleConsole(consoleOptions =>
                {
                    consoleOptions.TimestampFormat = "HH:mm:ss ";
                        consoleOptions.SingleLine = true;
                });
                options.AddDebug().SetMinimumLevel(LogLevel.Debug);
                options.AddSentry();
            });            
            builder.Services.AddSingleton<IImageRetrievalService, ImageRetrievalService>();
            builder.Services.AddTransient<IImageService, ImageService>();
            builder.Services.AddMemoryCache();
           
            builder.Services.Configure<Settings>(settings =>
            {
                settings.ImagePassword = Environment.GetEnvironmentVariable("IMAGEGETTER_PASSWORD");                
                if (string.IsNullOrEmpty(settings.ImagePassword))
                    throw new Exception("IMAGEGETTER_PASSWORD environment variable not set");

                settings.GoogleApiKey = Environment.GetEnvironmentVariable("GOOGLE_APIKEY2");
            });

            var serviceProvider = builder.Services.BuildServiceProvider();
            ImageMetadataExtensions.Initialize(serviceProvider);

            var app = builder.Build();

            app.UseSentryTracing();
            var settings = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<Settings>>().Value;
            if (string.IsNullOrEmpty(settings.GoogleApiKey))
            {
                //How do you get a logger here :-(

                var logger = app.Services.GetRequiredService<ILogger<Program>>();
                logger.LogWarning("GOOGLE_APIKEY environment variable not set, location lookups will be disabled");
            }

            //Cache an image on startup to speed up the first request
            await app.Services.GetRequiredService<IImageService>().CacheImageAsync();

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseCors("AllowSpecificOrigins");
            //app.UseHttpsRedirection();

            app.UseAuthorization();

            app.MapControllers();

            app.Run();
        }
    }

}