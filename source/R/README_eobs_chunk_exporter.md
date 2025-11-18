# E-OBS Chunk Exporter

R script to convert E-OBS NetCDF weather data files into temporal chunks for efficient GPU streaming.

## Overview

This script reads E-OBS NetCDF files (which are already split by weather attribute) and exports them as:
- **Binary chunk files**: Raw float32 arrays, one file per attribute per time period
- **Coordinate index**: Binary file mapping array indices to lat/lon coordinates
- **Specification XML**: Human-readable metadata describing all chunks

## Prerequisites

### R Packages
```r
install.packages("ncdf4")
install.packages("XML")
install.packages("rstudioapi")  # for setwd() helper
```

### Input Data
Download E-OBS NetCDF files from: https://surfobs.climate.copernicus.eu/dataaccess/access_eobs.php#datafiles

Required files:
- `tn_ens_mean_0.1deg_reg_v31.0e.nc` (Minimum Temperature)
- `tx_ens_mean_0.1deg_reg_v31.0e.nc` (Maximum Temperature)
- `rr_ens_mean_0.1deg_reg_v31.0e.nc` (Precipitation)
- `fg_ens_mean_0.1deg_reg_v31.0e.nc` (Wind Speed)
- `hu_ens_mean_0.1deg_reg_v31.0e.nc` (Relative Humidity)

## Configuration

Edit `config.xml` to set processing parameters:

```xml
<?xml version="1.0" encoding="utf-8"?>
<Configuration>
  <!-- Year range to process -->
  <StartYear>1980</StartYear>
  <EndYear>2024</EndYear>
  
  <!-- Number of days per binary chunk file -->
  <ChunkDays>730</ChunkDays>
  
  <!-- Geographic bounds (inclusive) -->
  <MinLongitude>-25.0</MinLongitude>
  <MaxLongitude>45.0</MaxLongitude>
  <MinLatitude>34.0</MinLatitude>
  <MaxLatitude>72.0</MaxLatitude>
  
  <!-- Data folder containing E-OBS NetCDF files -->
  <!-- Chunk files will be created in this same folder -->
  <DataFolder>./eobs_data</DataFolder>
</Configuration>
```

### Parameters

- **StartYear / EndYear**: Time range to extract (default: 1980-2024)
- **ChunkDays**: Number of days per chunk file (default: 730 = ~2 years)
- **MinLongitude / MaxLongitude**: Longitude bounds in degrees (default: -25 to 45 for Europe)
- **MinLatitude / MaxLatitude**: Latitude bounds in degrees (default: 34 to 72 for Europe)
- **DataFolder**: Folder containing E-OBS NetCDF files (output will be generated here too)

## Usage

1. Place E-OBS NetCDF files in a data folder (e.g., `./eobs_data`)
2. Edit `config.xml` to point to this folder and set desired parameters
3. Run the script in RStudio:

```r
source("eobs_chunk_exporter.R")
```

Or from command line:
```bash
Rscript eobs_chunk_exporter.R
```

**Note:** The script will create chunk files, specification.xml, and coords_lookup.bin in the **same folder** as your NetCDF files.

## Output Structure

All files are created in the same folder as the E-OBS NetCDF files:

```
eobs_data/                              # Your data folder
├── tn_ens_mean_0.1deg_reg_v31.0e.nc   # Original NetCDF files
├── tx_ens_mean_0.1deg_reg_v31.0e.nc
├── rr_ens_mean_0.1deg_reg_v31.0e.nc
├── fg_ens_mean_0.1deg_reg_v31.0e.nc
├── hu_ens_mean_0.1deg_reg_v31.0e.nc
├── specification.xml                   # Generated metadata
├── coords_lookup.bin                   # Generated lookup table
├── TN/                                 # Generated chunk folders
│   ├── chunk_1980_001-730.bin
│   ├── chunk_1980_731-1460.bin
│   └── ...
├── TX/
│   ├── chunk_1980_001-730.bin
│   └── ...
├── RR/
│   └── ...
├── FG/
│   └── ...
└── HU/
    └── ...
```

## Binary File Formats

### Chunk Files (e.g., `TN/chunk_1980_001-730.bin`)

**Format:** Raw float32 array, little-endian, no header

**Layout:** `float[numCoordinates * numDays]`

**Storage:** Only valid coordinates (land areas), no gaps for ocean/filtered areas

**Access pattern:** `value = data[coordIdx * numDays + dayIdx]`
- Where `coordIdx` comes from the lookup table (not direct lat/lon)

**Size:** `numCoordinates × numDays × 4 bytes`

### Coordinate Lookup Table (`coords_lookup.bin`)

This is a **sparse lookup table** that maps 2D geographic positions to coordinate indices in chunk files.

**Why this structure?**
- Regular 0.1° grid covering the entire lat/lon bounds
- Ocean/invalid areas marked as -1 (no storage in chunk files)
- Valid land areas get sequential indices (0, 1, 2, ...)
- Enables O(1) lookup: lat/lon → grid position → coordinate index → data

**Header (48 bytes):**
```
Offset | Type    | Field
-------|---------|---------------------------
0      | char[4] | Magic ("LOOK")
4      | int32   | Version (1)
8      | int32   | NumLonCells (grid width)
12     | int32   | NumLatCells (grid height)
16     | float32 | MinLongitude
20     | float32 | MaxLongitude
24     | float32 | MinLatitude
28     | float32 | MaxLatitude
32     | float32 | GridResolution (0.1)
36     | int32   | NumValidCoordinates
40     | int32   | Reserved1
44     | int32   | Reserved2
```

**Lookup Data (int32 array):**
```
Size: NumLonCells × NumLatCells × 4 bytes
Layout: Row-major (latitude-major, then longitude)
Values: 
  - -1 = No data (ocean or filtered out)
  - 0+ = Coordinate index in chunk files
```

**Access pattern:**
```
1. Convert lat/lon to grid indices:
   gridX = floor((lon - minLongitude) / 0.1)
   gridY = floor((lat - minLatitude) / 0.1)

2. Calculate lookup table index:
   lookupIdx = gridY * numLonCells + gridX

3. Read coordinate index from lookup table:
   coordIdx = lookupTable[lookupIdx]

4. If coordIdx == -1: no data (ocean)
   If coordIdx >= 0: read from chunk file:
      offset = coordIdx * numDays + dayIdx
      value = chunkData[offset]
```

### Specification XML (`specification.xml`)

Human-readable XML file containing:
- Global metadata (bounds, date ranges, chunk configuration)
- Per-attribute metadata (name, unit, range)
- Per-chunk metadata (file path, dates, statistics)

See example in output folder after running script.

## Memory Usage

The script processes data in chunks to minimize memory usage:
- Reads one time chunk at a time per attribute
- Memory usage: ~`numFilteredCoords × chunkDays × 4 bytes × 2` (input + output buffers)
- For European region (120k coords) with 730-day chunks: ~700 MB per attribute

## Processing Time

Approximate processing times (depends on disk I/O):
- Full European region (1980-2024): 30-60 minutes per attribute
- Smaller regions or time ranges: proportionally faster

## Troubleshooting

**Error: "Config file not found"**
- Ensure `config.xml` is in the same directory as the R script

**Error: "Cannot identify lon/lat dimensions"**
- Check that NetCDF files are from E-OBS dataset
- Verify files are not corrupted

**Error: "File not found: [netcdf file]"**
- Check `DataFolder` path in config.xml
- Ensure NetCDF files are in the specified folder
- Ensure NetCDF files are named correctly

**Missing data warning**
- Some regions/dates may have missing observations
- These are encoded as `NaN` in output files
- Check `missingDataPercent` in specification.xml

## Reading Chunk Files

### In R:
```r
# Read lookup table
read_lookup_table <- function(path) {
  con <- file(path, "rb")
  
  # Read header
  magic <- readBin(con, character(), n = 1, size = 4)
  version <- readBin(con, integer(), n = 1, size = 4)
  num_lon_cells <- readBin(con, integer(), n = 1, size = 4)
  num_lat_cells <- readBin(con, integer(), n = 1, size = 4)
  min_lon <- readBin(con, numeric(), n = 1, size = 4)
  max_lon <- readBin(con, numeric(), n = 1, size = 4)
  min_lat <- readBin(con, numeric(), n = 1, size = 4)
  max_lat <- readBin(con, numeric(), n = 1, size = 4)
  resolution <- readBin(con, numeric(), n = 1, size = 4)
  num_valid <- readBin(con, integer(), n = 1, size = 4)
  readBin(con, integer(), n = 2, size = 4)  # reserved
  
  # Read lookup data
  lookup_data <- readBin(con, integer(), n = num_lon_cells * num_lat_cells, size = 4)
  
  close(con)
  
  list(
    num_lon_cells = num_lon_cells,
    num_lat_cells = num_lat_cells,
    min_lon = min_lon,
    max_lon = max_lon,
    min_lat = min_lat,
    max_lat = max_lat,
    resolution = resolution,
    num_valid = num_valid,
    data = lookup_data
  )
}

# Read binary chunk
read_chunk <- function(path, num_coords, num_days) {
  con <- file(path, "rb")
  data <- readBin(con, numeric(), n = num_coords * num_days, size = 4, endian = "little")
  close(con)
  
  # Reshape to matrix [num_coords, num_days]
  matrix(data, nrow = num_coords, ncol = num_days, byrow = TRUE)
}

# Get value for specific lat/lon and day
get_value_at_location <- function(lookup, chunk_data, lat, lon, day_idx) {
  # Convert to grid indices
  gridX <- floor((lon - lookup$min_lon) / lookup$resolution)
  gridY <- floor((lat - lookup$min_lat) / lookup$resolution)
  
  # Check bounds
  if (gridX < 0 || gridX >= lookup$num_lon_cells || 
      gridY < 0 || gridY >= lookup$num_lat_cells) {
    return(NA)
  }
  
  # Lookup coordinate index
  lookup_idx <- gridY * lookup$num_lon_cells + gridX + 1  # +1 for R indexing
  coord_idx <- lookup$data[lookup_idx]
  
  if (coord_idx < 0) {
    return(NA)  # No data (ocean)
  }
  
  # Get value from chunk
  chunk_data[coord_idx + 1, day_idx]  # +1 for R indexing
}
```

### In C#:
```csharp
public class LookupTable
{
    public int NumLonCells { get; set; }
    public int NumLatCells { get; set; }
    public float MinLon { get; set; }
    public float MaxLon { get; set; }
    public float MinLat { get; set; }
    public float MaxLat { get; set; }
    public float Resolution { get; set; }
    public int[] Data { get; set; }
    
    public static LookupTable Load(string path)
    {
        using (var stream = File.OpenRead(path))
        using (var reader = new BinaryReader(stream))
        {
            var magic = new string(reader.ReadChars(4));
            var version = reader.ReadInt32();
            var numLonCells = reader.ReadInt32();
            var numLatCells = reader.ReadInt32();
            var minLon = reader.ReadSingle();
            var maxLon = reader.ReadSingle();
            var minLat = reader.ReadSingle();
            var maxLat = reader.ReadSingle();
            var resolution = reader.ReadSingle();
            var numValid = reader.ReadInt32();
            reader.ReadInt32();  // reserved1
            reader.ReadInt32();  // reserved2
            
            int[] data = new int[numLonCells * numLatCells];
            for (int i = 0; i < data.Length; i++)
            {
                data[i] = reader.ReadInt32();
            }
            
            return new LookupTable
            {
                NumLonCells = numLonCells,
                NumLatCells = numLatCells,
                MinLon = minLon,
                MaxLon = maxLon,
                MinLat = minLat,
                MaxLat = maxLat,
                Resolution = resolution,
                Data = data
            };
        }
    }
    
    public int GetCoordIndex(float lat, float lon)
    {
        int gridX = (int)Math.Floor((lon - MinLon) / Resolution);
        int gridY = (int)Math.Floor((lat - MinLat) / Resolution);
        
        if (gridX < 0 || gridX >= NumLonCells || gridY < 0 || gridY >= NumLatCells)
            return -1;
        
        int lookupIdx = gridY * NumLonCells + gridX;
        return Data[lookupIdx];
    }
}

public float[] LoadChunk(string path, int numCoords, int numDays)
{
    using (var stream = File.OpenRead(path))
    using (var reader = new BinaryReader(stream))
    {
        int totalValues = numCoords * numDays;
        float[] data = new float[totalValues];
        
        for (int i = 0; i < totalValues; i++)
        {
            data[i] = reader.ReadSingle();
        }
        
        return data;
    }
}

public float GetValue(LookupTable lookup, float[] chunkData, int numDays, 
                      float lat, float lon, int dayIdx)
{
    int coordIdx = lookup.GetCoordIndex(lat, lon);
    
    if (coordIdx < 0)
        return float.NaN;  // No data
    
    int offset = coordIdx * numDays + dayIdx;
    return chunkData[offset];
}
```

### In Python:
```python
import numpy as np
import struct

def read_lookup_table(path):
    with open(path, 'rb') as f:
        magic = f.read(4).decode('ascii')
        version = struct.unpack('i', f.read(4))[0]
        num_lon_cells = struct.unpack('i', f.read(4))[0]
        num_lat_cells = struct.unpack('i', f.read(4))[0]
        min_lon = struct.unpack('f', f.read(4))[0]
        max_lon = struct.unpack('f', f.read(4))[0]
        min_lat = struct.unpack('f', f.read(4))[0]
        max_lat = struct.unpack('f', f.read(4))[0]
        resolution = struct.unpack('f', f.read(4))[0]
        num_valid = struct.unpack('i', f.read(4))[0]
        f.read(8)  # reserved
        
        data = np.fromfile(f, dtype=np.int32, count=num_lon_cells * num_lat_cells)
        
    return {
        'num_lon_cells': num_lon_cells,
        'num_lat_cells': num_lat_cells,
        'min_lon': min_lon,
        'max_lon': max_lon,
        'min_lat': min_lat,
        'max_lat': max_lat,
        'resolution': resolution,
        'num_valid': num_valid,
        'data': data
    }

def read_chunk(path, num_coords, num_days):
    data = np.fromfile(path, dtype=np.float32)
    return data.reshape(num_coords, num_days)

def get_value_at_location(lookup, chunk_data, lat, lon, day_idx):
    gridX = int((lon - lookup['min_lon']) / lookup['resolution'])
    gridY = int((lat - lookup['min_lat']) / lookup['resolution'])
    
    if (gridX < 0 or gridX >= lookup['num_lon_cells'] or 
        gridY < 0 or gridY >= lookup['num_lat_cells']):
        return np.nan
    
    lookup_idx = gridY * lookup['num_lon_cells'] + gridX
    coord_idx = lookup['data'][lookup_idx]
    
    if coord_idx < 0:
        return np.nan  # No data
    
    return chunk_data[coord_idx, day_idx]
```

## Notes

- All data is stored as float32 in original units (°C, mm, m/s, %)
- Missing data is encoded as `NaN`
- **Sparse storage**: Only valid land coordinates are stored in chunk files (no ocean data)
- **Lookup table**: Regular 0.1° grid with -1 for invalid/ocean cells, >=0 for valid coordinate indices
- Coordinate ordering in chunk files: determined by filtering order (latitude-major, then longitude)
- All binary files use little-endian byte order
- Chunk filenames encode start year and day range for easy lookup
- **Access pattern**: lat/lon → grid indices → lookup table → coordinate index → chunk offset

## Benefits of this structure

✓ **Memory efficient**: Ocean areas take no space in chunk files (only 4 bytes per cell in lookup table)  
✓ **Fast access**: O(1) lookup from any lat/lon position  
✓ **GPU-friendly**: Contiguous float arrays, no gaps  
✓ **Flexible**: Can filter any geographic region without changing the structure

## License

This script is provided as-is for scientific research purposes.

## References

E-OBS dataset: https://surfobs.climate.copernicus.eu/
