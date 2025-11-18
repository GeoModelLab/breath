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
  
  <!-- Output folder for processed data -->
  <OutputFolder>./weather_data_chunks</OutputFolder>
  
  <!-- Input folder containing E-OBS NetCDF files -->
  <InputFolder>./eobs_data</InputFolder>
</Configuration>
```

### Parameters

- **StartYear / EndYear**: Time range to extract (default: 1980-2024)
- **ChunkDays**: Number of days per chunk file (default: 730 = ~2 years)
- **MinLongitude / MaxLongitude**: Longitude bounds in degrees (default: -25 to 45 for Europe)
- **MinLatitude / MaxLatitude**: Latitude bounds in degrees (default: 34 to 72 for Europe)
- **OutputFolder**: Where to write processed files
- **InputFolder**: Where to find E-OBS NetCDF files

## Usage

1. Place E-OBS NetCDF files in the input folder
2. Edit `config.xml` with desired parameters
3. Run the script in RStudio:

```r
source("eobs_chunk_exporter.R")
```

Or from command line:
```bash
Rscript eobs_chunk_exporter.R
```

## Output Structure

```
weather_data_chunks/
├── specification.xml           # Metadata for all chunks
├── coords_index.bin            # Coordinate index
├── TN/
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

**Access pattern:** `value = data[coordIdx * numDays + dayIdx]`

**Size:** `numCoordinates × numDays × 4 bytes`

### Coordinate Index (`coords_index.bin`)

**Header (36 bytes):**
```
Offset | Type    | Field
-------|---------|------------------
0      | char[4] | Magic ("CORD")
4      | int32   | Version (1)
8      | int32   | NumCoordinates
12     | float32 | MinLongitude
16     | float32 | MaxLongitude
20     | float32 | MinLatitude
24     | float32 | MaxLatitude
28     | int32   | Reserved1
32     | int32   | Reserved2
```

**Entries (20 bytes each):**
```
Offset | Type    | Field
-------|---------|------------------
0      | float32 | Latitude
4      | float32 | Longitude
8      | int32   | OriginalGridX
12     | int32   | OriginalGridY
16     | int32   | OriginalFileIndex
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
- Check `InputFolder` path in config.xml
- Ensure NetCDF files are named correctly

**Missing data warning**
- Some regions/dates may have missing observations
- These are encoded as `NaN` in output files
- Check `missingDataPercent` in specification.xml

## Reading Chunk Files

### In R:
```r
# Read binary chunk
read_chunk <- function(path, num_coords, num_days) {
  con <- file(path, "rb")
  data <- readBin(con, numeric(), n = num_coords * num_days, size = 4, endian = "little")
  close(con)
  
  # Reshape to matrix [num_coords, num_days]
  matrix(data, nrow = num_coords, ncol = num_days, byrow = TRUE)
}

# Get value for specific coordinate and day
get_value <- function(chunk_data, coord_idx, day_idx) {
  chunk_data[coord_idx, day_idx]
}
```

### In C#:
```csharp
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

public float GetValue(float[] chunkData, int numDays, int coordIdx, int dayIdx)
{
    return chunkData[coordIdx * numDays + dayIdx];
}
```

### In Python:
```python
import numpy as np

def read_chunk(path, num_coords, num_days):
    data = np.fromfile(path, dtype=np.float32)
    return data.reshape(num_coords, num_days)

def get_value(chunk_data, coord_idx, day_idx):
    return chunk_data[coord_idx, day_idx]
```

## Notes

- All data is stored as float32 in original units (°C, mm, m/s, %)
- Missing data is encoded as `NaN`
- Coordinate ordering is: latitude first, then longitude (row-major)
- All binary files use little-endian byte order
- Chunk filenames encode start year and day range for easy lookup

## License

This script is provided as-is for scientific research purposes.

## References

E-OBS dataset: https://surfobs.climate.copernicus.eu/
