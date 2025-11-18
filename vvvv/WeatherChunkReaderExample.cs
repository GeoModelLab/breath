// Example usage of WeatherChunkReader for vvvv gamma integration
// This demonstrates how to load and process E-OBS weather data chunks

using System;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using source;

namespace vvvvExamples
{
    public class WeatherChunkReaderExample
    {
        /// <summary>
        /// Example 1: Basic chunk loading and inspection
        /// </summary>
        public static async Task BasicLoadingExample()
        {
            Console.WriteLine("=== Example 1: Basic Loading ===\n");

            // Initialize reader
            var reader = new WeatherChunkReader(@"C:\REPO\breath\data\nc files");

            // Print specification info
            var spec = reader.Specification;
            Console.WriteLine($"Data Source: {spec.Source}");
            Console.WriteLine($"Coverage: {spec.StartYear}-{spec.EndYear} ({spec.TotalDays} days)");
            Console.WriteLine($"Grid: {spec.NumLonCells} × {spec.NumLatCells} = {spec.NumCoordinates} coords");
            Console.WriteLine($"Resolution: {spec.GridResolution}°");
            Console.WriteLine($"Bounds: Lon [{spec.MinLongitude}, {spec.MaxLongitude}], Lat [{spec.MinLatitude}, {spec.MaxLatitude}]");
            Console.WriteLine($"Total chunks: {spec.TotalChunks}\n");

            // List available attributes
            Console.WriteLine("Available attributes:");
            foreach (var attrName in reader.GetAttributeNames())
            {
                var info = reader.GetAttributeInfo(attrName);
                Console.WriteLine($"  {attrName}: {info.Description} ({info.Unit})");
                Console.WriteLine($"    Range: {info.RangeMin} to {info.RangeMax}");
                Console.WriteLine($"    Chunks: {info.Chunks.Count}");
            }

            // Load first chunk of TX (maximum temperature)
            Console.WriteLine("\nLoading TX chunk 0...");
            var chunk = await reader.LoadChunkAsync("TX", 0);

            Console.WriteLine($"Loaded: {chunk.AttributeName}");
            Console.WriteLine($"Dimensions: {chunk.NumCoordinates} coords × {chunk.NumDays} days");
            Console.WriteLine($"Total values: {chunk.Data.Length:N0}");
            Console.WriteLine($"File: {chunk.Info.File}");
            Console.WriteLine($"Stats: min={chunk.Info.StatMin:F2}, max={chunk.Info.StatMax:F2}, mean={chunk.Info.StatMean:F2}");
            Console.WriteLine($"Missing data: {chunk.Info.MissingDataPercent:F2}%\n");
        }

        /// <summary>
        /// Example 2: Extract and analyze time series for a single coordinate
        /// </summary>
        public static async Task TimeSeriesAnalysisExample()
        {
            Console.WriteLine("=== Example 2: Time Series Analysis ===\n");

            var reader = new WeatherChunkReader(@"C:\REPO\breath\data\nc files");

            // Pick a coordinate index (example: 100000)
            int coordIndex = 100000;

            // Load TX chunk 0
            var chunk = await reader.LoadChunkAsync("TX", 0);

            // Extract time series for this coordinate
            var timeSeries = chunk.GetCoordinateTimeSeries(coordIndex);

            Console.WriteLine($"Time series for coordinate {coordIndex}:");
            Console.WriteLine($"  Number of days: {timeSeries.Length}");

            // Filter out NaN values
            var validValues = timeSeries.Where(v => !float.IsNaN(v)).ToArray();
            Console.WriteLine($"  Valid values: {validValues.Length}");

            if (validValues.Length > 0)
            {
                Console.WriteLine($"  Min temperature: {validValues.Min():F2} °C");
                Console.WriteLine($"  Max temperature: {validValues.Max():F2} °C");
                Console.WriteLine($"  Mean temperature: {validValues.Average():F2} °C");
                Console.WriteLine($"  Std deviation: {CalculateStdDev(validValues):F2} °C");
            }

            // Show first 10 days
            Console.WriteLine("\nFirst 10 days:");
            for (int i = 0; i < Math.Min(10, timeSeries.Length); i++)
            {
                if (float.IsNaN(timeSeries[i]))
                    Console.WriteLine($"  Day {i + 1}: Missing (NaN)");
                else
                    Console.WriteLine($"  Day {i + 1}: {timeSeries[i]:F2} °C");
            }
            Console.WriteLine();
        }

        /// <summary>
        /// Example 3: Efficient single-coordinate loading (without loading full chunk)
        /// </summary>
        public static async Task EfficientCoordinateLoadingExample()
        {
            Console.WriteLine("=== Example 3: Efficient Single-Coordinate Loading ===\n");

            var reader = new WeatherChunkReader(@"C:\REPO\breath\data\nc files");

            int coordIndex = 50000;

            Console.WriteLine($"Loading only coordinate {coordIndex} from TX chunk 0 (efficient method)...");

            // This only reads the specific coordinate's data, not the entire chunk
            var timeSeries = await reader.GetCoordinateTimeSeriesAsync("TX", 0, coordIndex);

            Console.WriteLine($"Loaded time series: {timeSeries.Length} days");
            Console.WriteLine($"First 5 values: {string.Join(", ", timeSeries.Take(5).Select(v => $"{v:F2}"))}");
            Console.WriteLine();
        }

        /// <summary>
        /// Example 4: Observable streaming for vvvv reactive processing
        /// </summary>
        public static void ObservableStreamingExample()
        {
            Console.WriteLine("=== Example 4: Observable Streaming (vvvv integration) ===\n");

            var reader = new WeatherChunkReader(@"C:\REPO\breath\data\nc files");

            // Create observable stream of float arrays
            IObservable<IReadOnlyList<float>> stream = reader.StreamChunksAsFloatArraysAsync("TN");

            Console.WriteLine("Streaming TN (minimum temperature) chunks...\n");

            // Subscribe to stream
            stream.Subscribe(
                onNext: floatArray =>
                {
                    // This would be where you upload to GPU in vvvv
                    int validCount = floatArray.Count(v => !float.IsNaN(v));
                    float validPercent = (validCount / (float)floatArray.Count) * 100;

                    Console.WriteLine($"Received chunk: {floatArray.Count:N0} values, {validPercent:F2}% valid");

                    // Simulate GPU upload delay
                    System.Threading.Thread.Sleep(100);
                },
                onError: ex =>
                {
                    Console.WriteLine($"Error: {ex.Message}");
                },
                onCompleted: () =>
                {
                    Console.WriteLine("\nAll chunks streamed successfully!");
                }
            );

            Console.WriteLine();
        }

        /// <summary>
        /// Example 5: Compare multiple attributes for the same coordinate
        /// </summary>
        public static async Task MultiAttributeComparisonExample()
        {
            Console.WriteLine("=== Example 5: Multi-Attribute Comparison ===\n");

            var reader = new WeatherChunkReader(@"C:\REPO\breath\data\nc files");

            int coordIndex = 75000;
            int chunkIndex = 0;

            Console.WriteLine($"Comparing TX, TN, RR for coordinate {coordIndex} in chunk {chunkIndex}...\n");

            // Load multiple attributes for the same coordinate
            var txSeries = await reader.GetCoordinateTimeSeriesAsync("TX", chunkIndex, coordIndex);
            var tnSeries = await reader.GetCoordinateTimeSeriesAsync("TN", chunkIndex, coordIndex);
            var rrSeries = await reader.GetCoordinateTimeSeriesAsync("RR", chunkIndex, coordIndex);

            Console.WriteLine($"First 10 days:");
            Console.WriteLine("Day | TX (max) | TN (min) | RR (precip)");
            Console.WriteLine("----|----------|----------|------------");

            for (int i = 0; i < Math.Min(10, txSeries.Length); i++)
            {
                Console.WriteLine($"{i + 1,3} | {FormatValue(txSeries[i]),8} | {FormatValue(tnSeries[i]),8} | {FormatValue(rrSeries[i]),11}");
            }

            Console.WriteLine();
        }

        /// <summary>
        /// Example 6: Chunk metadata query without loading data
        /// </summary>
        public static void MetadataQueryExample()
        {
            Console.WriteLine("=== Example 6: Metadata Query (No Data Loading) ===\n");

            var reader = new WeatherChunkReader(@"C:\REPO\breath\data\nc files");

            var txInfo = reader.GetAttributeInfo("TX");

            Console.WriteLine($"TX Attribute: {txInfo.Description}");
            Console.WriteLine($"Unit: {txInfo.Unit}");
            Console.WriteLine($"Expected range: {txInfo.RangeMin} to {txInfo.RangeMax}\n");

            Console.WriteLine("Chunk overview:");
            Console.WriteLine("Idx | File                                  | Year  | Days | Size (MB) | Min     | Max");
            Console.WriteLine("----|---------------------------------------|-------|------|-----------|---------|--------");

            for (int i = 0; i < Math.Min(10, txInfo.Chunks.Count); i++)
            {
                var chunk = txInfo.Chunks[i];
                string fileName = System.IO.Path.GetFileName(chunk.File);
                Console.WriteLine($"{i,3} | {fileName,-37} | {chunk.StartYear} | {chunk.NumDays,4} | {chunk.SizeBytes / 1_000_000.0,9:F1} | {chunk.StatMin,7:F2} | {chunk.StatMax,7:F2}");
            }

            Console.WriteLine($"\n... and {txInfo.Chunks.Count - 10} more chunks\n");
        }

        // Helper methods

        private static double CalculateStdDev(float[] values)
        {
            if (values.Length == 0) return 0;
            double mean = values.Average();
            double sumSquaredDiff = values.Sum(v => Math.Pow(v - mean, 2));
            return Math.Sqrt(sumSquaredDiff / values.Length);
        }

        private static string FormatValue(float value)
        {
            return float.IsNaN(value) ? "NaN" : $"{value:F2}";
        }

        // Main entry point for running all examples
        public static async Task Main(string[] args)
        {
            Console.WriteLine("╔════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║  WeatherChunkReader Examples for vvvv gamma               ║");
            Console.WriteLine("╚════════════════════════════════════════════════════════════╝\n");

            try
            {
                // Run examples
                await BasicLoadingExample();
                Console.WriteLine("\n" + new string('─', 60) + "\n");

                await TimeSeriesAnalysisExample();
                Console.WriteLine("\n" + new string('─', 60) + "\n");

                await EfficientCoordinateLoadingExample();
                Console.WriteLine("\n" + new string('─', 60) + "\n");

                MetadataQueryExample();
                Console.WriteLine("\n" + new string('─', 60) + "\n");

                await MultiAttributeComparisonExample();
                Console.WriteLine("\n" + new string('─', 60) + "\n");

                // Note: ObservableStreamingExample() is synchronous but spawns async work
                // Run it last as it will process all chunks
                Console.WriteLine("Note: Skipping Example 4 (Observable Streaming) in batch mode.");
                Console.WriteLine("To test streaming, call ObservableStreamingExample() separately.\n");

                Console.WriteLine("╔════════════════════════════════════════════════════════════╗");
                Console.WriteLine("║  All examples completed successfully!                      ║");
                Console.WriteLine("╚════════════════════════════════════════════════════════════╝");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n❌ Error: {ex.Message}");
                Console.WriteLine($"Stack trace:\n{ex.StackTrace}");
            }
        }
    }
}
