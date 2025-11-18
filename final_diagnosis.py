import netCDF4 as nc
import numpy as np
import struct

print("=" * 80)
print("FINAL DIAGNOSIS: Why values don't match")
print("=" * 80)

# Open NetCDF
ncfile = nc.Dataset(r'C:\REPO\breath\data\nc files\tx_ens_mean_0.1deg_reg_v31.0e.nc', 'r')
tx = ncfile.variables['tx']
lat_full = ncfile.variables['latitude'][:]
lon_full = ncfile.variables['longitude'][:]

# Apply filtering
min_lon, max_lon = -25.0, 45.0
min_lat, max_lat = 34.0, 72.0

lon_mask = (lon_full >= min_lon) & (lon_full <= max_lon)
lat_mask = (lat_full >= min_lat) & (lat_full <= max_lat)

lon_indices_np = np.where(lon_mask)[0]
lat_indices_np = np.where(lat_mask)[0]

print(f"\nGrid dimensions:")
print(f"  Full: {len(lat_full)} lat × {len(lon_full)} lon")
print(f"  Filtered: {len(lat_indices_np)} lat indices × {len(lon_indices_np)} lon indices")
print(f"  Cells: {len(lat_indices_np) * len(lon_indices_np):,}")

# Read the EXACT SAME WAY the R script does
# R uses 1-indexed, Python uses 0-indexed
# But R's ncvar_get with start/count should handle this

# Extract the filtered block for first 730 days
time_start = 0  # Python 0-indexed
time_count = 730

# Get the subsetted data [time, lat, lon] for the filtered region
lat_start, lat_end = lat_indices_np[0], lat_indices_np[-1] + 1
lon_start, lon_end = lon_indices_np[0], lon_indices_np[-1] + 1

print(f"\nExtracting data block:")
print(f"  Time: 0 to 730")
print(f"  Lat indices: {lat_start} to {lat_end} ({lat_end - lat_start} values)")
print(f"  Lon indices: {lon_start} to {lon_end} ({lon_end - lon_start} values)")

# Extract data - NetCDF is [time, lat, lon]
data_nc = tx[0:730, lat_start:lat_end, lon_start:lon_end]

print(f"\nExtracted array shape: {data_nc.shape}")
print(f"  Expected: (730, {len(lat_indices_np)}, {len(lon_indices_np)})")

# Transpose to [lon, lat, time] as R script does (line 139)
data_transposed = np.transpose(data_nc, (2, 1, 0))
print(f"\nTransposed to [lon, lat, time]: {data_transposed.shape}")

# Flatten using SAME loop structure as R (lines 258-266)
# for (j in 1:nlat) {
#   for (i in 1:nlon) {
#     for (t in 1:ntime) {
#       flat_idx <- coord_idx * ntime + t
#       flat_data[flat_idx] <- data[i, j, t]  # R is 1-indexed
#     }
#     coord_idx <- coord_idx + 1
#   }
# }

nlon, nlat, ntime = data_transposed.shape
num_coords = nlon * nlat

print(f"\nFlattening with R's loop structure:")
print(f"  nlon={nlon}, nlat={nlat}, ntime={ntime}")
print(f"  num_coords={num_coords}")

flat_data_python = np.zeros(num_coords * ntime, dtype=np.float32)

coord_idx = 0
for j in range(nlat):  # Python 0-indexed
    for i in range(nlon):
        for t in range(ntime):
            flat_idx = coord_idx * ntime + t
            # data_transposed is [lon, lat, time], so data_transposed[i, j, t]
            flat_data_python[flat_idx] = data_transposed[i, j, t]
        coord_idx += 1

print(f"  Flattened {flat_data_python.shape[0]:,} values")

# Replace masked values with -99.99 (simulating R's NA -> -99.99 conversion)
# Count how many are masked
masked_count = np.sum(np.isnan(flat_data_python))
print(f"  Masked (NaN) values: {masked_count:,} ({masked_count/len(flat_data_python)*100:.2f}%)")

# Now compare with actual chunk file
chunk_file = r'C:\REPO\breath\data\nc files\TX\chunk_0001-0730_1980-1981.bin'
with open(chunk_file, 'rb') as f:
    chunk_data = f.read()

all_chunk_values = np.array(struct.unpack(f'<{len(chunk_data)//4}f', chunk_data), dtype=np.float32)

print(f"\nChunk file:")
print(f"  Total values: {len(all_chunk_values):,}")
print(f"  Size match: {len(all_chunk_values) == len(flat_data_python)}")

# Test Berlin again: 52.5°N, 13.4°E
test_lat, test_lon = 52.5, 13.4

lat_filtered = lat_full[lat_indices_np]
lon_filtered = lon_full[lon_indices_np]

# Find in filtered arrays
filtered_lat_idx = np.argmin(np.abs(lat_filtered - test_lat))
filtered_lon_idx = np.argmin(np.abs(lon_filtered - test_lon))

print(f"\n" + "=" * 80)
print(f"Testing Berlin ({test_lat}°N, {test_lon}°E)")
print("=" * 80)
print(f"  Filtered indices: lat={filtered_lat_idx}, lon={filtered_lon_idx}")

# Coordinate index using R's loop structure (lat-major)
coord_idx_berlin = filtered_lat_idx * nlon + filtered_lon_idx
print(f"  Coordinate index: {filtered_lat_idx} * {nlon} + {filtered_lon_idx} = {coord_idx_berlin}")

# Extract time series from Python-flattened data
python_ts = flat_data_python[coord_idx_berlin * ntime:(coord_idx_berlin + 1) * ntime]
chunk_ts = all_chunk_values[coord_idx_berlin * ntime:(coord_idx_berlin + 1) * ntime]

print(f"\nFirst 10 days comparison:")
print("Day | Python-flattened | Chunk file | Match?")
print("----+------------------+------------+-------")
for i in range(10):
    p_val = python_ts[i] if not np.isnan(python_ts[i]) else -99.99
    c_val = chunk_ts[i]
    match = "YES" if abs(p_val - c_val) < 0.01 else "NO"
    print(f"{i:3d} | {p_val:16.2f} | {c_val:10.2f} | {match}")

# Overall comparison
python_valid = flat_data_python[~np.isnan(flat_data_python)]
chunk_valid = all_chunk_values[np.abs(all_chunk_values + 99.99) >= 0.01]

print(f"\n" + "=" * 80)
print("Overall statistics comparison:")
print("=" * 80)
print(f"\nPython-flattened:")
print(f"  Valid values: {len(python_valid):,}")
print(f"  Min: {np.min(python_valid):.2f}")
print(f"  Max: {np.max(python_valid):.2f}")
print(f"  Mean: {np.mean(python_valid):.2f}")

print(f"\nChunk file:")
print(f"  Valid values: {len(chunk_valid):,}")
print(f"  Min: {np.min(chunk_valid):.2f}")
print(f"  Max: {np.max(chunk_valid):.2f}")
print(f"  Mean: {np.mean(chunk_valid):.2f}")

ncfile.close()

print("\n" + "=" * 80)
print("Diagnosis complete!")
print("=" * 80)
