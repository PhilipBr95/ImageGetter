using ImageGetter.Extensions;
using ImageGetter.Models;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;

namespace ImageGetter
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.

            builder.Services.AddControllers();

            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            builder.Services.AddEndpointsApiExplorer();
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
            });            
            builder.Services.AddSingleton<Services.IImageService, Services.ImageService>();
            builder.Services.AddTransient<Settings>(s =>
            {
                var settings = new Settings
                {
                    Password = Environment.GetEnvironmentVariable("IMAGEGETTER_PASSWORD")
                };

                if (string.IsNullOrEmpty(settings.Password))
                {
                    throw new Exception("IMAGEGETTER_PASSWORD environment variable not set");
                }

                return settings;
            });

            var serviceProvider = builder.Services.BuildServiceProvider();
            ImageMetadataExtensions.Initialize(serviceProvider);

            var app = builder.Build();
            
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