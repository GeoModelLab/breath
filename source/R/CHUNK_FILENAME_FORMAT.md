# Chunk Filename Format

## Format Specification

**Pattern:** `chunk_[absStartDay]-[absEndDay]_[startYear]-[endYear].bin`

**Components:**
1. **Prefix**: `chunk_`
2. **Absolute day range**: `[absStartDay]-[absEndDay]`
   - 4-digit zero-padded day indices
   - Relative to the start of the entire dataset
   - 1-based indexing (first day = 0001)
3. **Year range**: `[startYear]-[endYear]`
   - Calendar year of the first entry
   - Calendar year of the last entry
   - May span multiple years

## Examples

### Example 1: First chunk (2-year span)
```
chunk_0001-0730_1980-1981.bin
```
- Days 1 to 730 of the dataset
- Starts: January 1, 1980
- Ends: December 30, 1981
- Duration: 730 days (~2 years)

### Example 2: Second chunk (2-year span)
```
chunk_0731-1460_1982-1983.bin
```
- Days 731 to 1460 of the dataset
- Starts: December 31, 1981
- Ends: December 30, 1983
- Duration: 730 days (~2 years)

### Example 3: Partial year chunk
```
chunk_1461-1826_1984-1984.bin
```
- Days 1461 to 1826 of the dataset
- All entries in calendar year 1984
- Duration: 366 days (leap year)

### Example 4: Last chunk (partial)
```
chunk_16071-16436_2024-2024.bin
```
- Days 16071 to 16436 of the dataset
- All entries in calendar year 2024
- Duration: 366 days (partial chunk)

## Use Cases

### 1. Finding chunks by date

To find which chunk contains a specific date:

```r
# Calculate absolute day index from date
target_date <- as.Date("1985-06-15")
start_date <- as.Date("1980-01-01")
abs_day <- as.integer(target_date - start_date) + 1  # 2022

# Find chunk: abs_day 2022 falls in days 1461-2190
# File: chunk_1461-2190_1984-1985.bin
```

### 2. Sequential processing

The filename immediately tells you:
- Where this chunk fits in the sequence (by absolute days)
- What time period it covers (by years)
- Whether it's a full or partial chunk (by day count)

### 3. Validation

You can verify chunk completeness:
```r
# Expected days in chunk
expected_days <- 730

# Parse filename
filename <- "chunk_0001-0730_1980-1981.bin"
parts <- strsplit(filename, "[_-.]")[[1]]
start_day <- as.integer(parts[2])  # 1
end_day <- as.integer(parts[3])    # 730

actual_days <- end_day - start_day + 1  # 730
stopifnot(actual_days == expected_days)  # Validation passes
```

## Comparison with Other Formats

### Old format (relative to year):
```
chunk_1980_001-730.bin
```
- ❌ Confusing: "001-730" looks like day-of-year but isn't
- ❌ Year only applies to start
- ❌ Hard to find chunks spanning year boundaries

### Alternative format (dates):
```
chunk_19800101-19811230.bin
```
- ❌ Long filenames
- ❌ Requires date parsing
- ❌ No indication of sequence position

### Current format (absolute + years):
```
chunk_0001-0730_1980-1981.bin
```
- ✓ Clear sequence position (days 1-730)
- ✓ Easy to parse (both programmatically and visually)
- ✓ Year range shows temporal coverage
- ✓ Compact yet informative

## Implementation Notes

**Absolute day calculation:**
```r
# Day 1 = first day in dataset (e.g., 1980-01-01)
# For chunk starting at index chunk_start_day:
chunk_start_day <- (chunk_idx - 1) * chunk_days + 1
chunk_end_day <- min(chunk_idx * chunk_days, total_days)

# Get actual dates
chunk_start_date <- dates_filtered[chunk_start_day]
chunk_end_date <- dates_filtered[chunk_end_day]

# Extract years
chunk_start_year <- as.integer(format(chunk_start_date, "%Y"))
chunk_end_year <- as.integer(format(chunk_end_date, "%Y"))

# Format filename
sprintf("chunk_%04d-%04d_%d-%d.bin",
        chunk_start_day, chunk_end_day,
        chunk_start_year, chunk_end_year)
```

**Parsing filename:**
```r
parse_chunk_filename <- function(filename) {
  # Remove extension
  name <- sub("\\.bin$", "", filename)
  
  # Split by underscore
  parts <- strsplit(name, "_")[[1]]
  
  # Parse day range
  day_parts <- strsplit(parts[2], "-")[[1]]
  start_day <- as.integer(day_parts[1])
  end_day <- as.integer(day_parts[2])
  
  # Parse year range
  year_parts <- strsplit(parts[3], "-")[[1]]
  start_year <- as.integer(year_parts[1])
  end_year <- as.integer(year_parts[2])
  
  list(
    start_day = start_day,
    end_day = end_day,
    start_year = start_year,
    end_year = end_year,
    num_days = end_day - start_day + 1
  )
}

# Example usage
info <- parse_chunk_filename("chunk_0001-0730_1980-1981.bin")
# info$start_day = 1
# info$end_day = 730
# info$start_year = 1980
# info$end_year = 1981
# info$num_days = 730
```

## Benefits

1. **Self-documenting**: Filename tells you exactly what's inside
2. **Sortable**: Alphabetical sorting = temporal sorting
3. **Predictable**: Easy to generate expected filenames programmatically
4. **Unambiguous**: No confusion about date ranges or indexing
5. **Compact**: Reasonable filename length while maintaining clarity

## Migration

If you have old chunk files with different naming, you can rename them:

```r
# Old format: chunk_1980_001-730.bin
# New format: chunk_0001-0730_1980-1981.bin

old_files <- list.files("TN", pattern = "^chunk_.*\\.bin$", full.names = TRUE)

for (old_file in old_files) {
  # Parse old filename (implementation-specific)
  # Calculate new filename
  # Rename file
  file.rename(old_file, new_file)
}
```

However, it's recommended to **regenerate** chunks with the new script to ensure consistency with the specification.xml metadata.
