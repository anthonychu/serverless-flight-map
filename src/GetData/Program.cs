using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace GetData
{
    class Program
    {
        static async Task Main(string[] args)
        {
            if (args.Length != 6)
            {
                Console.WriteLine("Needs command line arguments: iterations minLatitude minLongitude maxLatitude maxLongitude outputDir");
                return;
            }

            var maxIterations = Convert.ToInt16(args[0]);
            var minLatitude = args[1];
            var minLongitude = args[2];
            var maxLatitude = args[3];
            var maxLongitude = args[4];
            var outDir = args[5];

            var httpClient = new HttpClient();

            for (var i = 0; i < maxIterations; i++)
            {
                var path = Path.Combine(outDir, i + ".json");
                using (var response = await httpClient.GetStreamAsync($"https://opensky-network.org/api/states/all?lamin={minLatitude}&lomin={minLongitude}&lamax={maxLatitude}&lomax={maxLongitude}"))
                using (Stream file = File.Create(path))
                {
                    await response.CopyToAsync(file);
                    file.Close();
                    Console.WriteLine(i);
                }

                await Task.Delay(12000);
            }
        }
    }

    public class Data
    {
        public int time { get; set; }
        public List<List<object>> states { get; set; }
    }
}
