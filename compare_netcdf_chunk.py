import netCDF4 as nc
import numpy as np
import struct

print("=" * 80)
print("Comparing NetCDF source vs Binary chunk export")
print("=" * 80)

# 1. Open NetCDF and get data for first 730 days (1980-1981)
ncfile = nc.Dataset(r'C:\REPO\breath\data\nc files\tx_ens_mean_0.1deg_reg_v31.0e.nc', 'r')
tx = ncfile.variables['tx']

print(f"\nNetCDF info:")
print(f"  Shape: {tx.shape}")
print(f"  _FillValue: {tx._FillValue}")
print(f"  scale_factor: {tx.scale_factor}")
print(f"  add_offset: {tx.add_offset}")

# Get first 730 days
first_730_days = tx[0:730, :, :]
print(f"\nFirst 730 days shape: {first_730_days.shape}")

# 2. Open the binary chunk file
chunk_file = r'C:\REPO\breath\data\nc files\TX\chunk_0001-0730_1980-1981.bin'
with open(chunk_file, 'rb') as f:
    chunk_data = f.read()

# Calculate expected size
num_coords = 465 * 705  # lat * lon
num_days = 730
expected_size = num_coords * num_days * 4  # float32

print(f"\nBinary chunk info:")
print(f"  File size: {len(chunk_data):,} bytes")
print(f"  Expected size: {expected_size:,} bytes")
print(f"  Match: {len(chunk_data) == expected_size}")

# Unpack first 1000 values from chunk
chunk_values = struct.unpack('<1000f', chunk_data[:1000*4])

# Count missing values in chunk
missing_chunk = sum(1 for v in chunk_values if abs(v + 99.99) < 0.01)
print(f"\nFirst 1000 values from chunk:")
print(f"  Missing (-99.99): {missing_chunk}")
print(f"  Valid: {1000 - missing_chunk}")

# 3. Check what the NetCDF actually contains at coordinate 0
print("\n" + "=" * 80)
print("Checking coordinate 0 (first lat, first lon)...")
print("=" * 80)

lat_idx = 0
lon_idx = 0

# Get time series from NetCDF
netcdf_timeseries = tx[0:730, lat_idx, lon_idx]
print(f"\nNetCDF time series at coord (0,0):")
print(f"  First 20 values: {[f'{v:.2f}' if not isinstance(v, np.ma.core.MaskedConstant) else 'MASKED' for v in netcdf_timeseries[:20]]}")

# Check if masked
if isinstance(netcdf_timeseries, np.ma.MaskedArray):
    masked_count = netcdf_timeseries.mask.sum()
    print(f"  Masked values: {masked_count} / {len(netcdf_timeseries)}")
    print(f"  Masked percentage: {masked_count/len(netcdf_timeseries)*100:.2f}%")

# 4. Check a coordinate that should have data (central Europe)
print("\n" + "=" * 80)
print("Checking central Europe location...")
print("=" * 80)

lat = ncfile.variables['latitude'][:]
lon = ncfile.variables['longitude'][:]

target_lat = 50.0
target_lon = 10.0
lat_idx = np.argmin(np.abs(lat - target_lat))
lon_idx = np.argmin(np.abs(lon - target_lon))

print(f"\nLocation: {lat[lat_idx]:.2f}°N, {lon[lon_idx]:.2f}°E")
print(f"  Indices: lat={lat_idx}, lon={lon_idx}")

# Calculate coordinate index in flattened array (coordinate-major, time-minor)
coord_index = lat_idx * len(lon) + lon_idx
print(f"  Flattened coord index: {coord_index}")

# Get NetCDF time series
netcdf_ts = tx[0:730, lat_idx, lon_idx]
print(f"\nNetCDF time series:")
print(f"  First 10 days: {[f'{v:.2f}' if not isinstance(v, np.ma.core.MaskedConstant) else 'MASKED' for v in netcdf_ts[:10]]}")

if isinstance(netcdf_ts, np.ma.MaskedArray):
    masked = netcdf_ts.mask.sum()
    print(f"  Masked: {masked} / {len(netcdf_ts)}")

# Get corresponding data from chunk file
# Layout is coordinate-major, time-minor: data[coord_idx * num_days + day_idx]
chunk_offset = coord_index * num_days * 4  # 4 bytes per float32
chunk_ts_data = chunk_data[chunk_offset:chunk_offset + num_days * 4]
chunk_ts = struct.unpack(f'<{num_days}f', chunk_ts_data)

print(f"\nChunk file time series:")
print(f"  First 10 days: {[f'{v:.2f}' if abs(v + 99.99) >= 0.01 else 'MISSING' for v in chunk_ts[:10]]}")

missing_in_chunk_ts = sum(1 for v in chunk_ts if abs(v + 99.99) < 0.01)
print(f"  Missing: {missing_in_chunk_ts} / {len(chunk_ts)}")

# 5. Overall statistics
print("\n" + "=" * 80)
print("Overall dataset statistics...")
print("=" * 80)

# Count all missing values in chunk file
all_chunk_values = struct.unpack(f'<{len(chunk_data)//4}f', chunk_data)
total_missing = sum(1 for v in all_chunk_values if abs(v + 99.99) < 0.01)
total_valid = len(all_chunk_values) - total_missing

print(f"\nChunk file totals:")
print(f"  Total values: {len(all_chunk_values):,}")
print(f"  Missing (-99.99): {total_missing:,}")
print(f"  Valid: {total_valid:,}")
print(f"  Missing percentage: {total_missing/len(all_chunk_values)*100:.2f}%")

# Count masked in NetCDF
if isinstance(first_730_days, np.ma.MaskedArray):
    total_masked_nc = first_730_days.mask.sum()
    total_values_nc = first_730_days.size
    print(f"\nNetCDF totals:")
    print(f"  Total values: {total_values_nc:,}")
    print(f"  Masked: {total_masked_nc:,}")
    print(f"  Valid: {total_values_nc - total_masked_nc:,}")
    print(f"  Masked percentage: {total_masked_nc/total_values_nc*100:.2f}%")

ncfile.close()

print("\n" + "=" * 80)
print("Analysis complete!")
print("=" * 80)
