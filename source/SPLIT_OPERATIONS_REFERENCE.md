# Split Operations Reference

All data classes in `WeatherChunkReader.cs` include a `Split()` method that returns all properties as individual `out` parameters. Parameters are **grouped by semantic meaning** for intuitive usage in vvvv gamma, where each `out` parameter becomes a separate output pin on the node.

---

## ChunkInfo.Split()

Returns metadata for a single chunk file.

**Signature:**
```csharp
public void Split(
    out string file,
    out long sizeBytes,
    out int startYear,
    out int startDayOfYear,
    out int numDays,
    out int numCoordinates,
    out float statMin,
    out float statMax,
    out float statMean,
    out float missingDataPercent)
```

**Output Parameters (Grouped by Meaning):**

| Group | Parameter Name     | Type   | Description                                      |
|-------|--------------------|--------|--------------------------------------------------|
| **File Info** | file | string | Relative file path (e.g., "TX/chunk_0001-0730_1980-1981.bin") |
| | sizeBytes | long | File size in bytes |
| **Temporal** | startYear | int | Starting year of the chunk |
| | startDayOfYear | int | Day of year (1-366) for first entry |
| | numDays | int | Number of days in this chunk |
| **Spatial** | numCoordinates | int | Number of spatial coordinates |
| **Statistics** | statMin | float | Statistical minimum value (excluding NaN) |
| | statMax | float | Statistical maximum value (excluding NaN) |
| | statMean | float | Statistical mean value (excluding NaN) |
| | missingDataPercent | float | Percentage of missing data (NaN values) |

**Example:**
```csharp
var chunkInfo = reader.GetAttributeInfo("TX").Chunks[0];
chunkInfo.Split(
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
Console.WriteLine($"Period: {startYear}, day {startDayOfYear}, {numDays} days");
Console.WriteLine($"Coordinates: {numCoordinates}");
Console.WriteLine($"Value range: [{statMin}, {statMax}], mean = {statMean}");
Console.WriteLine($"Missing data: {missingDataPercent}%");
```

---

## AttributeInfo.Split()

Returns metadata for a weather attribute (e.g., TX, TN, RR).

**Signature:**
```csharp
public void Split(
    out string name,
    out string description,
    out string unit,
    out float rangeMin,
    out float rangeMax,
    out List<ChunkInfo> chunks)
```

**Output Parameters (Grouped by Meaning):**

| Group | Parameter Name | Type | Description |
|-------|----------------|------|-------------|
| **Identification** | name | string | Attribute name (e.g., "TX", "TN") |
| **Description** | description | string | Human-readable description |
| | unit | string | Physical unit (e.g., "°C", "mm") |
| **Range** | rangeMin | float | Expected minimum value range |
| | rangeMax | float | Expected maximum value range |
| **Data** | chunks | List<ChunkInfo> | List of all chunks for this attribute |

**Example:**
```csharp
var txInfo = reader.GetAttributeInfo("TX");
txInfo.Split(
    out string name,
    out string description,
    out string unit,
    out float rangeMin,
    out float rangeMax,
    out List<ChunkInfo> chunks);

Console.WriteLine($"{name}: {description} ({unit})");
Console.WriteLine($"Expected range: [{rangeMin}, {rangeMax}]");
Console.WriteLine($"Total chunks: {chunks.Count}");

// Iterate through chunks
foreach (var chunk in chunks)
{
    chunk.Split(
        out string file,
        out long sizeBytes,
        out int year,
        out _,
        out int days,
        out _,
        out _,
        out _,
        out _,
        out _);
    Console.WriteLine($"  {file}: {year}, {days} days");
}
```

---

## ChunkData.Split()

Returns loaded chunk data with all metadata.

**Signature:**
```csharp
public void Split(
    out string attributeName,
    out float[] data,
    out int numCoordinates,
    out int numDays,
    out ChunkInfo info)
```

**Output Parameters (Grouped by Meaning):**

| Group | Parameter Name | Type | Description |
|-------|----------------|------|-------------|
| **Identification** | attributeName | string | Attribute name (e.g., "TX") |
| **Data** | data | float[] | Raw float32 array (coordinate-major, time-minor) |
| **Dimensions** | numCoordinates | int | Number of coordinates (cached from info) |
| | numDays | int | Number of days (cached from info) |
| **Metadata** | info | ChunkInfo | Chunk metadata (can be split further) |

**Example:**
```csharp
var chunkData = await reader.LoadChunkAsync("TX", 0);
chunkData.Split(
    out string attributeName,
    out float[] data,
    out int numCoordinates,
    out int numDays,
    out ChunkInfo info);

Console.WriteLine($"Loaded attribute: {attributeName}");
Console.WriteLine($"Dimensions: {numCoordinates} × {numDays}");
Console.WriteLine($"Total values: {data.Length}");

// Further split the ChunkInfo for additional metadata
info.Split(
    out string file,
    out long sizeBytes,
    out int startYear,
    out _,
    out _,
    out _,
    out float statMin,
    out float statMax,
    out _,
    out _);
Console.WriteLine($"File: {file}");
Console.WriteLine($"Year: {startYear}");
Console.WriteLine($"Value range: [{statMin}, {statMax}]");

// Access raw data
float firstValue = data[0];
Console.WriteLine($"First value: {firstValue}");
```

---

## WeatherDataSpecification.Split()

Returns complete specification metadata (all 23 properties).

**Signature:**
```csharp
public void Split(
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
    out Dictionary<string, AttributeInfo> attributes)
```

**Output Parameters (Grouped by Meaning):**

| Group | Parameter Name | Type | Description |
|-------|----------------|------|-------------|
| **Metadata** | version | int | Specification format version |
| | source | string | Data source identifier |
| | converter | string | Converter tool identifier |
| | created | DateTime | Creation timestamp |
| | processingTimeSeconds | float | Processing time in seconds |
| **Temporal Coverage** | startYear | int | Start year of data coverage |
| | endYear | int | End year of data coverage |
| | totalDays | int | Total days across all chunks |
| | chunkSizeDays | int | Standard chunk size in days |
| | totalChunks | int | Total number of chunks |
| **Spatial Grid** | minLongitude | float | Minimum longitude bound |
| | maxLongitude | float | Maximum longitude bound |
| | minLatitude | float | Minimum latitude bound |
| | maxLatitude | float | Maximum latitude bound |
| | gridResolution | float | Grid resolution in degrees |
| | numLonCells | int | Number of longitude grid cells |
| | numLatCells | int | Number of latitude grid cells |
| | numCoordinates | int | Total number of spatial coordinates |
| **Data Organization** | coordinateOrdering | string | Coordinate ordering scheme |
| | lookupTableFile | string | Lookup table binary filename |
| **Technical** | dataType | string | Data type identifier (e.g., "float32") |
| | missingValueEncoding | string | Missing value encoding (e.g., "NaN") |
| | attributes | Dictionary<string, AttributeInfo> | Dictionary of all attributes by name |

**Example:**
```csharp
var reader = new WeatherChunkReader(@"C:\REPO\breath\data\nc files");
var spec = reader.Specification;

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

// Metadata
Console.WriteLine($"Source: {source} v{version}");
Console.WriteLine($"Converter: {converter}");
Console.WriteLine($"Created: {created:yyyy-MM-dd HH:mm:ss}");
Console.WriteLine($"Processing time: {processingTimeSeconds:F2}s");

// Temporal coverage
Console.WriteLine($"Coverage: {startYear}-{endYear} ({totalDays} days)");
Console.WriteLine($"Chunks: {totalChunks} × {chunkSizeDays} days each");

// Spatial grid
Console.WriteLine($"Grid: {numLonCells} × {numLatCells} = {numCoordinates} coords");
Console.WriteLine($"Resolution: {gridResolution}°");
Console.WriteLine($"Bounds: Lon [{minLongitude}, {maxLongitude}], Lat [{minLatitude}, {maxLatitude}]");

// Technical details
Console.WriteLine($"Coordinate ordering: {coordinateOrdering}");
Console.WriteLine($"Data type: {dataType}, missing values: {missingValueEncoding}");
Console.WriteLine($"Lookup table: {lookupTableFile}");
Console.WriteLine($"Available attributes: {string.Join(", ", attributes.Keys)}");
```

---

## vvvv Integration Pattern

In vvvv gamma, the `Split()` method with `out` parameters automatically creates multiple output pins grouped semantically:

```
┌─────────────────────────────┐
│  ChunkInfo                  │
└─────────────┬───────────────┘
              │
              ↓ .Split()
┌─────────────────────────────┐
│ Split (ChunkInfo)           │
├─────────────────────────────┤
│ [File Info]                 │
│ ○ File                      │──→ string
│ ○ Size Bytes                │──→ long
├─────────────────────────────┤
│ [Temporal]                  │
│ ○ Start Year                │──→ int
│ ○ Start Day Of Year         │──→ int
│ ○ Num Days                  │──→ int
├─────────────────────────────┤
│ [Spatial]                   │
│ ○ Num Coordinates           │──→ int
├─────────────────────────────┤
│ [Statistics]                │
│ ○ Stat Min                  │──→ float
│ ○ Stat Max                  │──→ float
│ ○ Stat Mean                 │──→ float
│ ○ Missing Data Percent      │──→ float
└─────────────────────────────┘
```

### Semantic Grouping Benefits

Parameters are organized by their meaning, making it easy to:
- **Connect related outputs together** (e.g., all spatial parameters to a grid calculator)
- **Understand data flow visually** (groups are logically related)
- **Find the outputs you need quickly** (predictable ordering)

**Example: ChunkData.Split() groups:**
1. **Identification** → attribute name
2. **Data** → the actual float array
3. **Dimensions** → sizes for array indexing
4. **Metadata** → additional info object

**Example: WeatherDataSpecification.Split() groups:**
1. **Metadata** → version, source, creation info
2. **Temporal Coverage** → years, days, chunks
3. **Spatial Grid** → lon/lat bounds, resolution, cell counts
4. **Data Organization** → ordering, lookup table
5. **Technical** → data types, attributes dictionary

---

## Nested Splitting

You can chain splits for nested objects:

```csharp
// Load chunk data
var chunkData = await reader.LoadChunkAsync("TX", 0);

// Split outer object
chunkData.Split(
    out string attributeName,
    out float[] data,
    out int numCoords,
    out int numDays,
    out ChunkInfo info);

// Split nested ChunkInfo
info.Split(
    out string file,
    out long sizeBytes,
    out int startYear,
    out int startDayOfYear,
    out _,
    out _,
    out float statMin,
    out float statMax,
    out float statMean,
    out float missingPercent);

// Now you have direct access to all properties
Console.WriteLine($"Attribute: {attributeName}");
Console.WriteLine($"File: {file} ({sizeBytes / 1_000_000} MB)");
Console.WriteLine($"Year: {startYear}, Day: {startDayOfYear}");
Console.WriteLine($"Stats: [{statMin}, {statMax}], mean={statMean}");
Console.WriteLine($"Data: {data.Length} values ({numCoords} × {numDays})");
```

---

## Performance Notes

- Split operations use `out` parameters which pass references (no copying for reference types)
- Arrays and collections are returned as references, not copied
- Use discards (`_`) for unwanted output parameters:

```csharp
// Only extract specific properties from the grouped outputs
chunkInfo.Split(
    out string file,
    out long sizeBytes,
    out _,  // startYear discarded
    out _,  // startDayOfYear discarded
    out _,  // numDays discarded
    out _,  // numCoordinates discarded
    out float statMin,
    out float statMax,
    out _,  // statMean discarded
    out _); // missingDataPercent discarded
```

- In vvvv, unconnected output pins have negligible overhead
- Semantic grouping makes it easier to connect related parameters together

---

## See Also

- [WeatherChunkReader_USAGE.md](WeatherChunkReader_USAGE.md) - Full usage guide
- [WeatherChunkReader.cs](WeatherChunkReader.cs) - Source code
