import netCDF4 as nc
import numpy as np

# Open the NetCDF file
ncfile = nc.Dataset(r'C:\REPO\breath\data\nc files\tx_ens_mean_0.1deg_reg_v31.0e.nc', 'r')

print("=" * 80)
print("E-OBS NetCDF File Diagnostic")
print("=" * 80)

# Print basic info
print("\nFile information:")
print(f"  Format: {ncfile.data_model}")
print(f"  Dimensions: {list(ncfile.dimensions.keys())}")

# Print dimension sizes
print("\nDimension sizes:")
for dim_name, dim in ncfile.dimensions.items():
    print(f"  {dim_name}: {len(dim)}")

# Get the TX variable
tx = ncfile.variables['tx']
print(f"\nVariable 'tx' information:")
print(f"  Shape: {tx.shape}")
print(f"  Data type: {tx.dtype}")
print(f"  Dimensions: {tx.dimensions}")

# Check for _FillValue attribute
if hasattr(tx, '_FillValue'):
    fill_value = tx._FillValue
    print(f"  _FillValue: {fill_value}")
else:
    print("  _FillValue: Not set")

# Check other relevant attributes
if hasattr(tx, 'scale_factor'):
    print(f"  scale_factor: {tx.scale_factor}")
if hasattr(tx, 'add_offset'):
    print(f"  add_offset: {tx.add_offset}")
if hasattr(tx, 'units'):
    print(f"  units: {tx.units}")

# Sample first time slice to check missing data
print("\n" + "=" * 80)
print("Analyzing first time slice (day 0)...")
print("=" * 80)

# Read first time slice
first_slice = tx[0, :, :]
print(f"\nFirst slice shape: {first_slice.shape}")

# Count missing values
if hasattr(tx, '_FillValue'):
    # Check for fill value
    missing_count = np.sum(first_slice == fill_value)
else:
    # Check for NaN
    missing_count = np.sum(np.isnan(first_slice))

total_cells = first_slice.size
valid_count = total_cells - missing_count
missing_percent = (missing_count / total_cells) * 100

print(f"\nMissing data statistics:")
print(f"  Total cells: {total_cells:,}")
print(f"  Valid values: {valid_count:,}")
print(f"  Missing values: {missing_count:,}")
print(f"  Missing percentage: {missing_percent:.2f}%")

# Get statistics for valid data
valid_data = first_slice[~np.isnan(first_slice)]
if len(valid_data) > 0:
    print(f"\nValid data statistics:")
    print(f"  Min: {np.min(valid_data):.2f}")
    print(f"  Max: {np.max(valid_data):.2f}")
    print(f"  Mean: {np.mean(valid_data):.2f}")
    print(f"  Std: {np.std(valid_data):.2f}")

# Sample first 20 values from first row
print(f"\nFirst 20 values from first row:")
first_row = first_slice[0, :20]
print([f'{v:.2f}' if not np.isnan(v) else 'NaN' for v in first_row])

# Check a middle slice for comparison
middle_idx = tx.shape[0] // 2
print(f"\n" + "=" * 80)
print(f"Analyzing middle time slice (day {middle_idx})...")
print("=" * 80)

middle_slice = tx[middle_idx, :, :]
if hasattr(tx, '_FillValue'):
    missing_count_mid = np.sum(middle_slice == fill_value)
else:
    missing_count_mid = np.sum(np.isnan(middle_slice))

missing_percent_mid = (missing_count_mid / total_cells) * 100
print(f"\nMissing percentage: {missing_percent_mid:.2f}%")

# Sample specific coordinates that should have data (e.g., central Europe)
print("\n" + "=" * 80)
print("Sampling specific locations (should have valid data)...")
print("=" * 80)

# Get lat/lon arrays
lat = ncfile.variables['latitude'][:]
lon = ncfile.variables['longitude'][:]

print(f"\nLatitude range: {lat.min():.2f} to {lat.max():.2f}")
print(f"Longitude range: {lon.min():.2f} to {lon.max():.2f}")

# Find index for a central European location (e.g., 50°N, 10°E)
target_lat = 50.0
target_lon = 10.0

lat_idx = np.argmin(np.abs(lat - target_lat))
lon_idx = np.argmin(np.abs(lon - target_lon))

print(f"\nSampling location near {target_lat}°N, {target_lon}°E:")
print(f"  Actual coordinates: {lat[lat_idx]:.2f}°N, {lon[lon_idx]:.2f}°E")
print(f"  Array indices: lat_idx={lat_idx}, lon_idx={lon_idx}")

# Get time series for this location
location_timeseries = tx[:, lat_idx, lon_idx]
valid_ts = location_timeseries[~np.isnan(location_timeseries)]
missing_ts = np.sum(np.isnan(location_timeseries))
missing_ts_percent = (missing_ts / len(location_timeseries)) * 100

print(f"\nTime series at this location:")
print(f"  Total days: {len(location_timeseries)}")
print(f"  Valid days: {len(valid_ts)}")
print(f"  Missing days: {missing_ts}")
print(f"  Missing percentage: {missing_ts_percent:.2f}%")
print(f"\nFirst 10 days: {[f'{v:.2f}' if not np.isnan(v) else 'NaN' for v in location_timeseries[:10]]}")

ncfile.close()

print("\n" + "=" * 80)
print("Diagnostic complete!")
print("=" * 80)
