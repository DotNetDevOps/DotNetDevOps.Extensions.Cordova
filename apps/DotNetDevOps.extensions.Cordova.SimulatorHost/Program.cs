using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using System;
using System.IO;

namespace DotNetDevOps.extensions.Cordova.SimulatorHost
{
    class Program
    {
        public static string TargetPath { get; set; }
        static void Main(string[] args)
        {
           

            TargetPath = Directory.GetCurrentDirectory().Replace("\\", "/").TrimEnd('/');

            Console.WriteLine("Starting Cordova Simulator Host: " + TargetPath);

            if (Directory.Exists(TargetPath))
                Directory.Delete(TargetPath, true);
            Directory.CreateDirectory(TargetPath);

              var host = Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                }).Build();

            host.Run();

            Console.WriteLine("Good bye");
        }

       
    }
}
