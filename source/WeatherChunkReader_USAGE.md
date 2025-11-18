# WeatherChunkReader Usage Guide

## Overview

The `WeatherChunkReader` is a C# plugin for reading binary weather data chunks exported by the R script [eobs_chunk_exporter.R](R/eobs_chunk_exporter.R). It provides async file loading and reactive streaming support for vvvv gamma integration.

## Data Format

Binary chunk files contain:
- **Format**: Little-endian float32 values (4 bytes each)
- **Layout**: Coordinate-major, time-minor
  - For each coordinate, all time values are sequential: `[coord_0_day_0, coord_0_day_1, ..., coord_0_day_N, coord_1_day_0, ...]`
  - Array index formula: `data[coord_idx * num_days + day_idx]`
- **Missing values**: Raw files contain `-99.99` (NetCDF _FillValue), automatically converted to IEEE 754 NaN by the reader
- **Metadata**: Stored in `specification.xml` in the data folder

## Example Files

- Specification: `C:\REPO\breath\data\nc files\specification.xml`
- Example chunk: `C:\REPO\breath\data\nc files\TX\chunk_0001-0730_1980-1981.bin`
  - 262,500 coordinates × 730 days × 4 bytes = 766,500,000 bytes

## Basic Usage

### 1. Initialize Reader

```csharp
using source;

// Initialize with data folder path
var reader = new WeatherChunkReader(@"C:\REPO\breath\data\nc files");

// Access specification metadata
var spec = reader.Specification;
Console.WriteLine($"Data covers {spec.StartYear}-{spec.EndYear}");
Console.WriteLine($"Total coordinates: {spec.NumCoordinates}");
Console.WriteLine($"Grid resolution: {spec.GridResolution}°");
```

### 1b. Using Split Operation (vvvv-friendly)

All data classes include a `Split()` method that returns all properties as `out` parameters **grouped by semantic meaning**, avoiding the need to call individual getters:

```csharp
// Split ChunkInfo - grouped by: file info, temporal, spatial, statistics
var chunk = chunkData.Info;
chunk.Split(
    out string file,
    out long sizeBytes,
    out int startYear,
    out int startDayOfYear,
    out int numDays,
    out int numCoordinates,
    out float statMin,
    out float statMax,
    out float statMean,
    out float missingDataPercent);

Console.WriteLine($"File: {file} ({sizeBytes / 1_000_000} MB)");
Console.WriteLine($"Year: {startYear}, Days: {numDays}");
Console.WriteLine($"Stats: min={statMin}, max={statMax}");

// Split AttributeInfo - grouped by: identification, description, range, data
var txInfo = reader.GetAttributeInfo("TX");
txInfo.Split(
    out string name,
    out string description,
    out string unit,
    out float rangeMin,
    out float rangeMax,
    out List<ChunkInfo> chunks);
Console.WriteLine($"{name}: {description} ({unit})");

// Split ChunkData - grouped by: identification, data, dimensions, metadata
var chunkData = await reader.LoadChunkAsync("TX", 0);
chunkData.Split(
    out string attributeName,
    out float[] data,
    out int numCoords,
    out int numDays,
    out ChunkInfo info);
Console.WriteLine($"Loaded {attributeName}: {data.Length} values");

// Split WeatherDataSpecification (all 23 properties!)
// Grouped by: metadata, temporal coverage, spatial grid, data organization, technical
spec.Split(
    out int version,
    out string source,
    out string converter,
    out DateTime created,
    out float processingTimeSeconds,
    out int startYear,
    out int endYear,
    out int totalDays,
    out int chunkSizeDays,
    out int totalChunks,
    out float minLongitude,
    out float maxLongitude,
    out float minLatitude,
    out float maxLatitude,
    out float gridResolution,
    out int numLonCells,
    out int numLatCells,
    out int numCoordinates,
    out string coordinateOrdering,
    out string lookupTableFile,
    out string dataType,
    out string missingValueEncoding,
    out Dictionary<string, AttributeInfo> attributes);
```

This pattern is especially useful in vvvv where each `out` parameter becomes a separate output pin on the node. **Parameters are semantically grouped** (e.g., all temporal info together, all spatial info together), making it intuitive to connect related outputs.

### 2. List Available Attributes

```csharp
// Get all attribute names
var attributes = reader.GetAttributeNames();
foreach (var attr in attributes)
{
    var info = reader.GetAttributeInfo(attr);
    Console.WriteLine($"{attr}: {info.Description} ({info.Unit})");
    Console.WriteLine($"  Chunks: {info.Chunks.Count}");
}

// Output:
// TX: Maximum Temperature (°C)
//   Chunks: 23
// TN: Minimum Temperature (°C)
//   Chunks: 23
// RR: Precipitation (mm)
//   Chunks: 23
```

### 3. Load Complete Chunk

```csharp
// Load chunk 0 of TX (maximum temperature)
var chunkData = await reader.LoadChunkAsync("TX", chunkIndex: 0);

Console.WriteLine($"Loaded {chunkData.AttributeName}");
Console.WriteLine($"Dimensions: {chunkData.NumCoordinates} coords × {chunkData.NumDays} days");
Console.WriteLine($"Total values: {chunkData.Data.Length}");
Console.WriteLine($"Date range: {chunkData.Info.StartYear}, day {chunkData.Info.StartDayOfYear}");
```

### 4. Extract Single Coordinate Time Series

```csharp
// Get time series for coordinate index 12345
var timeSeries = chunkData.GetCoordinateTimeSeries(coordIndex: 12345);

Console.WriteLine($"Time series for coordinate 12345: {timeSeries.Length} days");
foreach (var value in timeSeries.Take(10))
{
    if (float.IsNaN(value))
        Console.WriteLine("  Missing data (NaN)");
    else
        Console.WriteLine($"  {value:F2} °C");
}
```

### 5. Efficient Single-Coordinate Loading

If you only need one coordinate's data, avoid loading the entire chunk:

```csharp
// Load only coordinate 12345 from chunk 0
var timeSeries = await reader.GetCoordinateTimeSeriesAsync(
    attributeName: "TX",
    chunkIndex: 0,
    coordIndex: 12345
);

// Much faster and lower memory usage than loading full chunk
```

## vvvv Gamma Integration

### Reactive Streaming for GPU Upload

The reader provides `IObservable<IReadOnlyList<float>>` for reactive streaming, perfect for dynamic GPU upload in vvvv:

```csharp
// Stream all TX chunks as float arrays
IObservable<IReadOnlyList<float>> chunkStream =
    reader.StreamChunksAsFloatArraysAsync("TX");

// Subscribe to process chunks as they load
chunkStream.Subscribe(
    onNext: floatArray => {
        // Upload to GPU texture/buffer
        Console.WriteLine($"Received chunk with {floatArray.Count} values");
        // TODO: Upload floatArray to GPU
    },
    onError: ex => Console.WriteLine($"Error: {ex.Message}"),
    onCompleted: () => Console.WriteLine("All chunks loaded")
);
```

### Alternative: Stream ChunkData Objects

For more control, stream full `ChunkData` objects:

```csharp
IObservable<ChunkData> chunkStream = reader.StreamChunksAsync("TX");

chunkStream.Subscribe(chunk => {
    Console.WriteLine($"Processing chunk {chunk.Info.StartYear}");
    Console.WriteLine($"  Size: {chunk.NumCoordinates} × {chunk.NumDays}");
    Console.WriteLine($"  Stats: min={chunk.Info.StatMin}, max={chunk.Info.StatMax}");

    // Access raw data array
    float[] data = chunk.Data;

    // Or extract specific coordinates
    var coord0 = chunk.GetCoordinateTimeSeries(0);
});
```

## Data Access Patterns

### Pattern 1: Coordinate-Major Access (Native Layout)

Efficient for processing all days for each coordinate sequentially:

```csharp
var chunk = await reader.LoadChunkAsync("TX", 0);

for (int coordIdx = 0; coordIdx < chunk.NumCoordinates; coordIdx++)
{
    var timeSeries = chunk.GetCoordinateTimeSeries(coordIdx);
    // Process all days for this coordinate
    float avgTemp = timeSeries.Where(v => !float.IsNaN(v)).Average();
}
```

### Pattern 2: Day-Major Access (Requires Transpose)

If you need all coordinates for a specific day, extract manually:

```csharp
var chunk = await reader.LoadChunkAsync("TX", 0);

// Get all coordinates for day 5
float[] daySnapshot = new float[chunk.NumCoordinates];
for (int coordIdx = 0; coordIdx < chunk.NumCoordinates; coordIdx++)
{
    daySnapshot[coordIdx] = chunk.GetValue(coordIdx, dayIndex: 5);
}
```

### Pattern 3: Spatial Query (Using Lookup Table)

To query by geographic coordinates, you'll need to:
1. Load the coordinate lookup table (`coords_lookup.bin`)
2. Map (lon, lat) → coordinate index
3. Use coordinate index to query chunk data

*Note: Lookup table reader not yet implemented. See R script lines 320-395 for format.*

## Handling Missing Data

**Important:** E-OBS NetCDF files use `-99.99` as the `_FillValue` (missing data marker). The C# reader automatically converts these to IEEE 754 NaN values when loading. Always check for NaN in your code:

```csharp
var chunk = await reader.LoadChunkAsync("RR", 0);

int missingCount = 0;
foreach (var value in chunk.Data)
{
    if (float.IsNaN(value))
        missingCount++;
}

float missingPercent = (missingCount / (float)chunk.Data.Length) * 100;
Console.WriteLine($"Missing data: {missingPercent:F2}%");
```

## Performance Considerations

### Memory Usage

Each chunk uses approximately:
- TX/TN: 766 MB per chunk (262,500 coords × 730 days × 4 bytes)
- Last chunk: ~396 MB (377 days instead of 730)

For 23 chunks, loading all at once would require ~17 GB. **Use streaming for large datasets.**

### Async Loading

All loading operations are async to prevent blocking:

```csharp
// Load multiple chunks in parallel
var tasks = new List<Task<ChunkData>>();
for (int i = 0; i < 5; i++)
{
    tasks.Add(reader.LoadChunkAsync("TX", i));
}

var chunks = await Task.WhenAll(tasks);
Console.WriteLine($"Loaded {chunks.Length} chunks in parallel");
```

### Cancellation Support

All async methods support cancellation:

```csharp
var cts = new CancellationTokenSource();
cts.CancelAfter(TimeSpan.FromSeconds(5));

try
{
    var chunk = await reader.LoadChunkAsync("TX", 0, cts.Token);
}
catch (OperationCanceledException)
{
    Console.WriteLine("Load cancelled");
}
```

## Error Handling

The reader validates file integrity:

```csharp
try
{
    var chunk = await reader.LoadChunkAsync("TX", 0);
}
catch (FileNotFoundException ex)
{
    Console.WriteLine($"Chunk file missing: {ex.Message}");
}
catch (InvalidDataException ex)
{
    Console.WriteLine($"File size mismatch: {ex.Message}");
}
catch (ArgumentException ex)
{
    Console.WriteLine($"Invalid attribute or chunk index: {ex.Message}");
}
```

## Metadata Access

Query chunk metadata without loading data:

```csharp
var txInfo = reader.GetAttributeInfo("TX");

foreach (var chunk in txInfo.Chunks)
{
    Console.WriteLine($"Chunk: {chunk.File}");
    Console.WriteLine($"  Period: {chunk.StartYear}, day {chunk.StartDayOfYear}");
    Console.WriteLine($"  Days: {chunk.NumDays}");
    Console.WriteLine($"  Size: {chunk.SizeBytes / 1_000_000} MB");
    Console.WriteLine($"  Range: {chunk.StatMin:F2} to {chunk.StatMax:F2} {txInfo.Unit}");
    Console.WriteLine($"  Missing: {chunk.MissingDataPercent:F2}%");
}
```

## Integration with Existing SWELL Model

To integrate with the existing phenology model:

```csharp
// Load weather data for specific time range
var reader = new WeatherChunkReader(@"C:\REPO\breath\data\nc files");

// Get TX data for 1980-1981 (chunk 0)
var txChunk = await reader.LoadChunkAsync("TX", 0);
var tnChunk = await reader.LoadChunkAsync("TN", 0);
var rrChunk = await reader.LoadChunkAsync("RR", 0);

// Extract time series for a specific pixel coordinate
int pixelCoordIndex = 12345; // Example coordinate
var txTimeSeries = txChunk.GetCoordinateTimeSeries(pixelCoordIndex);
var tnTimeSeries = tnChunk.GetCoordinateTimeSeries(pixelCoordIndex);
var rrTimeSeries = rrChunk.GetCoordinateTimeSeries(pixelCoordIndex);

// Feed into existing model for each day
var vvvv = new vvvvInterface();
output outputT0 = new output();
output outputT1 = new output();

for (int day = 0; day < txChunk.NumDays; day++)
{
    // Create input object
    var input = new input
    {
        airTemperatureMin = tnTimeSeries[day],
        airTemperatureMax = txTimeSeries[day],
        precipitation = rrTimeSeries[day],
        // ... other inputs
    };

    // Execute model
    outputT1 = vvvv.vvvvExecution(input, parameters);

    // Swap outputs for next timestep
    outputT0 = outputT1;
}
```

## Building and Testing

Build the project:

```bash
dotnet build source/source.csproj
```

Run a test (create a simple console app):

```csharp
using source;

var reader = new WeatherChunkReader(@"C:\REPO\breath\data\nc files");
var chunk = await reader.LoadChunkAsync("TX", 0);
Console.WriteLine($"Successfully loaded {chunk.Data.Length} values!");
```

## See Also

- R Export Script: [source/R/eobs_chunk_exporter.R](R/eobs_chunk_exporter.R)
- Data Specification: `C:\REPO\breath\data\nc files\specification.xml`
- SWELL Model Documentation: [CLAUDE.md](../CLAUDE.md)
