import netCDF4 as nc
import numpy as np
import struct

print("=" * 80)
print("Verifying coordinate mapping between NetCDF and binary chunks")
print("=" * 80)

# Open NetCDF
ncfile = nc.Dataset(r'C:\REPO\breath\data\nc files\tx_ens_mean_0.1deg_reg_v31.0e.nc', 'r')
tx = ncfile.variables['tx']
lat_full = ncfile.variables['latitude'][:]
lon_full = ncfile.variables['longitude'][:]

print(f"\nFull NetCDF grid:")
print(f"  Longitude: {len(lon_full)} cells from {lon_full.min():.2f} to {lon_full.max():.2f}")
print(f"  Latitude: {len(lat_full)} cells from {lat_full.min():.2f} to {lat_full.max():.2f}")
print(f"  Total cells: {len(lon_full) * len(lat_full):,}")

# Apply filtering as per specification.xml
min_lon, max_lon = -25.0, 45.0
min_lat, max_lat = 34.0, 72.0

lon_mask = (lon_full >= min_lon) & (lon_full <= max_lon)
lat_mask = (lat_full >= min_lat) & (lat_full <= max_lat)

lon_indices = np.where(lon_mask)[0]
lat_indices = np.where(lat_mask)[0]

lon_filtered = lon_full[lon_indices]
lat_filtered = lat_full[lat_indices]

print(f"\nFiltered grid (as per specification.xml):")
print(f"  Longitude: {len(lon_filtered)} cells from {lon_filtered.min():.2f} to {lon_filtered.max():.2f}")
print(f"  Latitude: {len(lat_filtered)} cells from {lat_filtered.min():.2f} to {lat_filtered.max():.2f}")
print(f"  Total cells: {len(lon_filtered) * len(lat_filtered):,}")
print(f"  Expected by spec: 701 × 381 = 267,081")
print(f"  Match: {len(lon_filtered) * len(lat_filtered) == 267081}")

# Test a specific location: Berlin (52.5°N, 13.4°E)
test_lat, test_lon = 52.5, 13.4

# Find indices in full grid
full_lat_idx = np.argmin(np.abs(lat_full - test_lat))
full_lon_idx = np.argmin(np.abs(lon_full - test_lon))

print(f"\n" + "=" * 80)
print(f"Test location: Berlin ({test_lat}°N, {test_lon}°E)")
print("=" * 80)

print(f"\nFull grid indices:")
print(f"  lat_idx = {full_lat_idx}, lon_idx = {full_lon_idx}")
print(f"  Actual coordinates: {lat_full[full_lat_idx]:.2f}°N, {lon_full[full_lon_idx]:.2f}°E")

# Get NetCDF data for Berlin
berlin_nc = tx[0:730, full_lat_idx, full_lon_idx]
print(f"\nNetCDF data (first 10 days):")
print(f"  {[f'{v:.2f}' if not isinstance(v, np.ma.core.MaskedConstant) else 'MASKED' for v in berlin_nc[:10]]}")

if isinstance(berlin_nc, np.ma.MaskedArray):
    masked_count = berlin_nc.mask.sum()
    print(f"  Masked: {masked_count} / {len(berlin_nc)}")

# Find indices in filtered grid
filtered_lat_idx = np.argmin(np.abs(lat_filtered - test_lat))
filtered_lon_idx = np.argmin(np.abs(lon_filtered - test_lon))

print(f"\nFiltered grid indices:")
print(f"  lat_idx = {filtered_lat_idx}, lon_idx = {filtered_lon_idx}")
print(f"  Actual coordinates: {lat_filtered[filtered_lat_idx]:.2f}°N, {lon_filtered[filtered_lon_idx]:.2f}°E")

# Calculate flattened coordinate index
# R script uses: for (j in 1:nlat) { for (i in 1:nlon) { coord_idx++ } }
# This is LAT-MAJOR ordering: coord_idx = lat_idx * nlon + lon_idx
coord_idx_calculated = filtered_lat_idx * len(lon_filtered) + filtered_lon_idx

print(f"\nCalculated coordinate index (lat-major):")
print(f"  coord_idx = {filtered_lat_idx} * {len(lon_filtered)} + {filtered_lon_idx} = {coord_idx_calculated}")

# Read from chunk file
chunk_file = r'C:\REPO\breath\data\nc files\TX\chunk_0001-0730_1980-1981.bin'
with open(chunk_file, 'rb') as f:
    chunk_data = f.read()

# Layout: coordinate-major, time-minor [coord_idx * num_days + day_idx]
num_days = 730
offset = coord_idx_calculated * num_days * 4
chunk_ts_data = chunk_data[offset:offset + num_days * 4]
chunk_ts = struct.unpack(f'<{num_days}f', chunk_ts_data)

print(f"\nBinary chunk data (first 10 days):")
print(f"  {[f'{v:.2f}' if abs(v + 99.99) >= 0.01 else 'MISSING' for v in chunk_ts[:10]]}")

missing_count = sum(1 for v in chunk_ts if abs(v + 99.99) < 0.01)
print(f"  Missing: {missing_count} / {len(chunk_ts)}")

# Compare
print(f"\n" + "=" * 80)
print("Data comparison:")
print("=" * 80)

if missing_count == 0:
    # Convert NetCDF masked array to regular values
    nc_values = [float(v) if not isinstance(v, np.ma.core.MaskedConstant) else -99.99 for v in berlin_nc[:10]]
    chunk_values = chunk_ts[:10]

    print("\nFirst 10 days side-by-side:")
    print("Day | NetCDF | Chunk  | Match?")
    print("----+--------+--------+-------")
    for i in range(10):
        match = "✓" if abs(nc_values[i] - chunk_values[i]) < 0.1 else "✗"
        print(f"{i:3d} | {nc_values[i]:6.2f} | {chunk_values[i]:6.2f} | {match}")

ncfile.close()

print("\n" + "=" * 80)
print("Analysis complete!")
print("=" * 80)
